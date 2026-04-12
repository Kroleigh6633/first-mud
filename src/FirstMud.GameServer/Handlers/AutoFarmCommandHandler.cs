using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Handlers;

public class AutoFarmCommandHandler(
    IPlayerRepository playerRepository,
    AutoFarmService autoFarmService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<AutoFarmCommandHandler> logger) : ICommandHandler<AutoFarmCommand>
{
    public async Task<CommandResult> HandleAsync(AutoFarmCommand cmd, CancellationToken ct)
    {
        // Toggle: if already active, cancel the session
        if (autoFarmService.IsActive(cmd.PlayerId))
        {
            autoFarmService.EndSession(cmd.PlayerId);
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", "Auto-farm cancelled.", ct);
            await notificationService.SendEventAsync(cmd.PlayerId, "AutoFarmStatus", new { active = false }, ct);
            return new CommandResult(true, "Auto-farm cancelled.");
        }

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var session = autoFarmService.StartSession(cmd.PlayerId, cmd.DurationSeconds);

        await notificationService.SendMessageAsync(
            cmd.PlayerId,
            "system",
            $"Auto-farm started. Duration: {cmd.DurationSeconds / 60} minute(s). Press [F] again to stop.",
            ct);

        await notificationService.SendEventAsync(cmd.PlayerId, "AutoFarmStatus",
            new { active = true, durationSeconds = cmd.DurationSeconds }, ct);

        _ = Task.Run(async () =>
        {
            await RunAutoFarmLoopAsync(cmd.PlayerId, session, ct);
        }, CancellationToken.None);

        return new CommandResult(true, "Auto-farm started.");
    }

    // -------------------------------------------------------------------------
    // Clockwise spiral step generator
    // Yields (dx, dy) steps in the pattern: R R D D L L L U U U R R R R ...
    // -------------------------------------------------------------------------
    private static IEnumerable<(int dx, int dy)> SpiralSteps()
    {
        // Directions: right, down, left, up
        (int dx, int dy)[] dirs = [(1, 0), (0, 1), (-1, 0), (0, -1)];
        int steps = 1;
        int dirIdx = 0;

        while (true)
        {
            // Each side length is repeated twice before growing
            for (int repeat = 0; repeat < 2; repeat++)
            {
                for (int i = 0; i < steps; i++)
                    yield return dirs[dirIdx % 4];
                dirIdx++;
            }
            steps++;
        }
    }

    private async Task RunAutoFarmLoopAsync(
        Guid playerId, AutoFarmSession session, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, session.Cts.Token);
        var farmCt = linked.Token;

        var endTime = session.StartedAt.AddSeconds(session.DurationSeconds);
        const int stepIntervalMs = 3000;

        // Remember starting position so we can return there at the end
        int originX = 0, originY = 0;
        bool originSet = false;

        var spiralEnumerator = SpiralSteps().GetEnumerator();

        try
        {
            while (!farmCt.IsCancellationRequested && DateTimeOffset.UtcNow < endTime)
            {
                await Task.Delay(stepIntervalMs, farmCt);
                if (farmCt.IsCancellationRequested) break;

                await using var scope = scopeFactory.CreateAsyncScope();
                var playerRepo       = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
                var itemRepo         = scope.ServiceProvider.GetRequiredService<IItemRepository>();
                var zoneRepo         = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
                var lootSvc          = scope.ServiceProvider.GetRequiredService<LootService>();
                var combatSvc        = scope.ServiceProvider.GetRequiredService<CombatService>();
                var resourceNodeRepo = scope.ServiceProvider.GetRequiredService<IResourceNodeRepository>();

                var player = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (player is null) break;

                // Capture origin on first step
                if (!originSet)
                {
                    originX = player.Position.X;
                    originY = player.Position.Y;
                    originSet = true;
                }

                // --- STATUS TICKER ---
                var remaining = endTime - DateTimeOffset.UtcNow;
                var remainingMin = Math.Max(0, (int)remaining.TotalMinutes);
                var remainingSec = Math.Max(0, (int)remaining.TotalSeconds % 60);

                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = $"Auto-farming... {remainingMin}m {remainingSec}s left. Kills: {session.Kills}. Items: {session.ItemsFound}."
                    }, farmCt);

                // --- WALK ONE STEP in the spiral ---
                spiralEnumerator.MoveNext();
                var (dx, dy) = spiralEnumerator.Current;

                var newPos = new Position(
                    player.Position.World,
                    player.Position.ZoneId,
                    player.Position.X + dx,
                    player.Position.Y + dy);

                // Danger check before moving: if 4+ levels above player, turn back
                var zonesForDanger = await zoneRepo.GetByWorldAsync(newPos.World, farmCt);
                var newZone = ZoneProximity.FindNearby(zonesForDanger, newPos.X, newPos.Y);
                var newDanger = newZone?.DangerLevel
                    ?? CombatHelpers.GetWildernessDanger(newPos.X, newPos.Y,
                        CombatHelpers.GuessWildernessBiome(newPos.X, newPos.Y));

                if (newDanger > player.Level + 4)
                {
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "system",
                            text = "Auto-farm: area ahead is too dangerous — turning back."
                        }, farmCt);

                    // Retreat one step toward origin
                    int retX = player.Position.X + Math.Sign(originX - player.Position.X);
                    int retY = player.Position.Y + Math.Sign(originY - player.Position.Y);
                    newPos = new Position(player.Position.World, player.Position.ZoneId, retX, retY);
                }

                // Move the player
                player.Move(newPos);
                await playerRepo.UpdateAsync(player, farmCt);

                // Broadcast position change so the map updates
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("PlayerMoved", new
                    {
                        player.Id,
                        X = newPos.X,
                        Y = newPos.Y,
                        ZoneId = newPos.ZoneId,
                        World = newPos.World.ToString()
                    }, farmCt);

                // --- HARVEST if the current tile has harvestable resource nodes ---
                try
                {
                    var harvestZones = await zoneRepo.GetByWorldAsync(newPos.World, farmCt);
                    var nearestZone = ZoneProximity.FindNearby(harvestZones, newPos.X, newPos.Y);
                    if (nearestZone is not null)
                    {
                        var nodes = await resourceNodeRepo.GetByZoneIdAsync(nearestZone.Id, farmCt);
                        var node = nodes.FirstOrDefault(n => n.RemainingYield > 0);
                        if (node is not null)
                        {
                            var harvestPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                            if (harvestPlayer is not null)
                            {
                                var currentCount = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                                if (harvestPlayer.CanCarryMore(currentCount.Count))
                                {
                                    var amount = Random.Shared.Next(1, 4);
                                    var actual = node.Harvest(amount);
                                    await resourceNodeRepo.UpdateAsync(node, farmCt);

                                    var itemName = node.ResourceType.ToString();
                                    var existingStack = await itemRepo.GetByOwnerAndNameAsync(
                                        playerId, itemName, ItemCategory.Component, farmCt);
                                    if (existingStack is not null)
                                    {
                                        existingStack.AddQuantity(actual);
                                        await itemRepo.UpdateAsync(existingStack, farmCt);
                                    }
                                    else
                                    {
                                        var newItem = Item.Create(
                                            itemName,
                                            $"A resource gathered from the wilds: {itemName.ToLowerInvariant()}.",
                                            ItemCategory.Component,
                                            Workmanship.Of(1),
                                            harvestPlayer.Position.World);
                                        newItem.SetOwner(playerId);
                                        if (actual > 1) newItem.AddQuantity(actual - 1);
                                        await itemRepo.AddAsync(newItem, farmCt);
                                    }

                                    await hubContext.Clients
                                        .Group(playerId.ToString())
                                        .SendAsync("GameMessage", new
                                        {
                                            timestamp = DateTime.UtcNow.ToString("O"),
                                            category = "loot",
                                            text = $"[Auto-farm] Harvested {actual}x {itemName}."
                                        }, farmCt);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Auto-farm harvest skipped at ({X},{Y})", newPos.X, newPos.Y);
                }

                // --- ENCOUNTER ROLL ---
                var allZones = await zoneRepo.GetByWorldAsync(newPos.World, farmCt);
                var nearbyZone = ZoneProximity.FindNearby(allZones, newPos.X, newPos.Y);

                int dangerLevel;
                int encounterChance;
                string biome;

                if (nearbyZone != null)
                {
                    dangerLevel = nearbyZone.DangerLevel;
                    encounterChance = Math.Min(dangerLevel * 10, 70);
                    biome = CombatHelpers.GetBiome(nearbyZone);
                }
                else
                {
                    biome = CombatHelpers.GuessWildernessBiome(newPos.X, newPos.Y);
                    dangerLevel = CombatHelpers.GetWildernessDanger(newPos.X, newPos.Y, biome);
                    encounterChance = biome == "path" ? 2 : Math.Min(dangerLevel * 4, 40);
                }

                if (dangerLevel <= 0 && biome != "path")
                    continue;

                if (Random.Shared.Next(100) >= encounterChance)
                    continue;

                // Build monster pack
                var freshPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (freshPlayer is null) break;

                var monsters = CombatHelpers.BuildMonsterPack(dangerLevel, freshPlayer.Level, biome);

                var avgMonsterLevel = monsters.Count > 0
                    ? (int)Math.Round(monsters.Average(m => (double)m.Level))
                    : 1;
                var levelGap = freshPlayer.Level - avgMonsterLevel;

                if (levelGap >= 4)
                {
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "system",
                            text = "Auto-farm: skipped encounter (enemies too weak)."
                        }, farmCt);
                    continue;
                }

                var encounter = await combatSvc.StartEncounterAsync(
                    playerId, nearbyZone?.Id ?? Guid.NewGuid(), freshPlayer, [], monsters, ct: farmCt);

                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "combat",
                        text = $"[Auto-farm] Encounter! {string.Join(", ", monsters.Select(m => m.Name))}."
                    }, farmCt);

                // Auto-fight to completion
                var safety = 0;
                while (encounter.State == EncounterState.InProgress && safety++ < 50)
                {
                    var actor = encounter.CurrentActor;
                    if (actor is null) break;

                    if (actor.IsPlayerSide)
                    {
                        var enemies = encounter.Combatants
                            .Where(c => !c.IsPlayerSide && !c.IsDefeated)
                            .ToList();
                        if (enemies.Count == 0) break;
                        var target = enemies[Random.Shared.Next(enemies.Count)];
                        await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, "Strike", target.Id, farmCt);
                    }
                    else
                    {
                        var abilities = actor.Abilities.Where(a => a.Category == AbilityCategory.Attack).ToList();
                        if (abilities.Count == 0) break;
                        var ability = abilities[Random.Shared.Next(abilities.Count)];
                        var allies = encounter.Combatants.Where(c => c.IsPlayerSide && !c.IsDefeated).ToList();
                        if (allies.Count == 0) break;
                        var target = allies[Random.Shared.Next(allies.Count)];
                        await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, ability.Name, target.Id, farmCt);
                    }

                    encounter = combatSvc.GetEncounter(encounter.Id) ?? encounter;
                }

                if (encounter.State == EncounterState.Defeat)
                {
                    // Sync HP and portal home on defeat
                    var defPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                    if (defPlayer is not null)
                    {
                        defPlayer.SetCurrentHp(1);
                        var homePos = new Position(defPlayer.Position.World, 0, -100, -100);
                        defPlayer.PortalHome(homePos);
                        await playerRepo.UpdateAsync(defPlayer, farmCt);

                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("PlayerMoved", new
                            {
                                defPlayer.Id,
                                X = homePos.X,
                                Y = homePos.Y,
                                ZoneId = homePos.ZoneId,
                                World = homePos.World.ToString()
                            }, serverCt);
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("AtHomestead", new { playerId }, serverCt);
                    }

                    autoFarmService.EndSession(playerId);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "combat",
                            text = "Auto-farm ended: you were defeated. You wake at your homestead..."
                        }, serverCt);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("AutoFarmStatus", new { active = false, reason = "defeat" }, serverCt);
                    return;
                }

                if (encounter.State == EncounterState.Victory)
                {
                    // Sync HP back to player entity after winning a fight
                    var winPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                    if (winPlayer is not null)
                    {
                        var playerCombatant = encounter.Combatants
                            .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);
                        if (playerCombatant is not null)
                        {
                            winPlayer.SetCurrentHp(Math.Min(playerCombatant.CurrentHp, winPlayer.MaxHp));
                            await playerRepo.UpdateAsync(winPlayer, farmCt);
                        }
                    }

                    autoFarmService.RecordKill(playerId);

                    var currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    var lootPlayer = winPlayer ?? freshPlayer;
                    var lootResult = await lootSvc.RollLootDropAsync(
                        dangerLevel, playerId, lootPlayer.Position.World,
                        currentItems.Count, lootPlayer.MaxInventorySlots, farmCt,
                        lootPlayer,
                        nearbyZone?.Name);

                    if (lootResult.Dropped)
                    {
                        if (lootResult.AutoSalvaged)
                        {
                            autoFarmService.RecordAutoSalvage(playerId);
                        }
                        else
                        {
                            autoFarmService.RecordItem(playerId);
                        }

                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category = lootResult.AutoSalvaged ? "salvage" : "loot",
                                text = $"[Auto-farm] {lootResult.Message}"
                            }, farmCt);

                        if (lootResult.Item is not null && !lootResult.AutoSalvaged)
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("LootDropped", new
                                {
                                    lootResult.Item.Id,
                                    lootResult.Item.Name,
                                    lootResult.Item.Description,
                                    Workmanship = lootResult.Item.Workmanship.Value,
                                    Category = lootResult.Item.Category.ToString()
                                }, farmCt);
                    }

                    // Running summary after each fight
                    var currentSession = autoFarmService.GetSession(playerId);
                    var kills    = currentSession?.Kills             ?? session.Kills;
                    var kept     = currentSession?.ItemsFound        ?? session.ItemsFound;
                    var salvaged = currentSession?.ItemsAutoSalvaged ?? 0;

                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "system",
                            text = $"Auto-farm: {kills} kill(s), {kept} item(s) kept, {salvaged} auto-salvaged."
                        }, farmCt);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation exit
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-farm loop error for player {PlayerId}", playerId);
        }
        finally
        {
            // Return to starting position
            if (originSet)
            {
                try
                {
                    await using var finalScope = scopeFactory.CreateAsyncScope();
                    var finalPlayerRepo = finalScope.ServiceProvider.GetRequiredService<IPlayerRepository>();
                    var returnPlayer = await finalPlayerRepo.GetByIdAsync(playerId, serverCt);
                    if (returnPlayer is not null)
                    {
                        var returnPos = new Position(returnPlayer.Position.World, returnPlayer.Position.ZoneId, originX, originY);
                        returnPlayer.Move(returnPos);
                        await finalPlayerRepo.UpdateAsync(returnPlayer, serverCt);

                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("PlayerMoved", new
                            {
                                returnPlayer.Id,
                                X = returnPos.X,
                                Y = returnPos.Y,
                                ZoneId = returnPos.ZoneId,
                                World = returnPos.World.ToString()
                            }, serverCt);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Auto-farm: failed to return player {PlayerId} to origin", playerId);
                }
            }

            var finalSession = autoFarmService.GetSession(playerId);
            var kills    = finalSession?.Kills             ?? session.Kills;
            var items    = finalSession?.ItemsFound        ?? session.ItemsFound;
            var salvaged = finalSession?.ItemsAutoSalvaged ?? 0;
            autoFarmService.EndSession(playerId);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = $"Auto-farm complete: {kills} kill(s), {items} item(s) kept, {salvaged} auto-salvaged."
                }, serverCt);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("AutoFarmStatus", new { active = false, kills, items, salvaged }, serverCt);
        }
    }
}
