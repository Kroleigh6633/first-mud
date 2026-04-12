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

        var session = autoFarmService.StartSession(cmd.PlayerId);

        await notificationService.SendMessageAsync(
            cmd.PlayerId,
            "system",
            "Auto-farm started. Runs until stopped. Press [F] again to stop.",
            ct);

        await notificationService.SendEventAsync(cmd.PlayerId, "AutoFarmStatus",
            new
            {
                active = true,
                state = "idle",
                kills = 0,
                items = 0,
                salvaged = 0,
                deposited = 0
            }, ct);

        _ = Task.Run(async () =>
        {
            await RunAutoFarmLoopAsync(cmd.PlayerId, session, ct);
        }, CancellationToken.None);

        return new CommandResult(true, "Auto-farm started.");
    }

    // -------------------------------------------------------------------------
    // Clockwise spiral step generator
    // -------------------------------------------------------------------------
    private static IEnumerable<(int dx, int dy)> SpiralSteps()
    {
        (int dx, int dy)[] dirs = [(1, 0), (0, 1), (-1, 0), (0, -1)];
        int steps = 1;
        int dirIdx = 0;

        while (true)
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                for (int i = 0; i < steps; i++)
                    yield return dirs[dirIdx % 4];
                dirIdx++;
            }
            steps++;
        }
    }

    // -------------------------------------------------------------------------
    // Consumable helpers
    // -------------------------------------------------------------------------

    private sealed record ConsumableEffect(
        string EffectType,
        int Amount,
        string? BuffKey = null,
        float BuffValue = 0f);

    private static readonly string[] HealingPriority =
    [
        "minor healing draught",
        "healing potion",
        "greater healing elixir"
    ];

    private static readonly string[] WeavePriority =
    [
        "weave tincture",
        "weave elixir"
    ];

    private static readonly string[] BuffNames =
    [
        "fortitude brew",
        "speed draught",
        "strength tonic"
    ];

    private static ConsumableEffect? ResolveEffect(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("minor healing draught"))  return new ConsumableEffect("Heal", 30);
        if (n.Contains("healing potion"))         return new ConsumableEffect("Heal", 60);
        if (n.Contains("greater healing elixir")) return new ConsumableEffect("Heal", 100);
        if (n.Contains("weave tincture"))         return new ConsumableEffect("RestoreWeave", 20);
        if (n.Contains("weave elixir"))           return new ConsumableEffect("RestoreWeave", 50);
        if (n.Contains("fortitude brew"))         return new ConsumableEffect("Buff", 0, "MaxHpBonus", 0.10f);
        if (n.Contains("speed draught"))          return new ConsumableEffect("Buff", 0, "SpeedBonus", 0.20f);
        if (n.Contains("strength tonic"))         return new ConsumableEffect("Buff", 0, "StrikeDamageBonus", 0.15f);
        return null;
    }

    private static async Task<string?> ApplyAndConsumeAsync(
        Player player,
        Item item,
        IPlayerRepository playerRepo,
        IItemRepository itemRepo,
        CancellationToken ct)
    {
        var effect = ResolveEffect(item.Name);
        if (effect is null) return null;

        string message;
        switch (effect.EffectType)
        {
            case "Heal":
            {
                var before  = player.CurrentHp;
                player.HealHp(effect.Amount);
                var healed  = player.CurrentHp - before;
                message = $"Auto-farm: used {item.Name}, restored {healed} HP. ({player.CurrentHp}/{player.MaxHp} HP)";
                break;
            }
            case "RestoreWeave":
            {
                player.RestoreWeave(effect.Amount);
                message = $"Auto-farm: used {item.Name}, restored {effect.Amount} Weave. ({player.Weave.VisibleState})";
                break;
            }
            case "Buff":
            {
                var label = effect.BuffKey switch
                {
                    "MaxHpBonus"        => $"+{(int)(effect.BuffValue * 100)}% max HP",
                    "SpeedBonus"        => $"+{(int)(effect.BuffValue * 100)}% speed",
                    "StrikeDamageBonus" => $"+{(int)(effect.BuffValue * 100)}% strike damage",
                    _                   => "a bonus"
                };
                message = $"Auto-farm: used {item.Name} ({label}) before dangerous fight.";
                break;
            }
            default:
                return null;
        }

        await playerRepo.UpdateAsync(player, ct);

        if (item.IsStackable && item.Quantity > 1)
        {
            item.TryRemoveQuantity(1, out _);
            await itemRepo.UpdateAsync(item, ct);
        }
        else
        {
            await itemRepo.DeleteAsync(item.Id, ct);
        }

        return message;
    }

    private static Item? FindBestConsumable(IReadOnlyList<Item> inventory, string[] priorityNames)
    {
        Item? best = null;
        int bestPriority = -1;

        foreach (var item in inventory)
        {
            if (item.Category != ItemCategory.Consumable) continue;
            var lower = item.Name.ToLowerInvariant();
            for (int i = 0; i < priorityNames.Length; i++)
            {
                if (lower.Contains(priorityNames[i]) && i > bestPriority)
                {
                    best = item;
                    bestPriority = i;
                }
            }
        }

        return best;
    }

    // -------------------------------------------------------------------------
    // Portal-home-to-heal helper
    // -------------------------------------------------------------------------

    private async Task PortalHomeToHealAsync(
        Guid playerId,
        Position returnPos,
        IPlayerRepository playerRepo,
        CancellationToken ct)
    {
        var healPlayer = await playerRepo.GetByIdAsync(playerId, ct);
        if (healPlayer is null) return;

        var homePos = new Position(healPlayer.Position.World, 0, -100, -100);
        healPlayer.PortalHome(homePos);
        await playerRepo.UpdateAsync(healPlayer, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("PlayerMoved", new
            {
                healPlayer.Id,
                X = homePos.X,
                Y = homePos.Y,
                ZoneId = homePos.ZoneId,
                World = homePos.World.ToString()
            }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "system",
                text      = "Auto-farm: no healing consumables and HP critical — portalling home to recover."
            }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("AtHomestead", new { playerId }, ct);

        await Task.Delay(5_000, ct);

        var recoveredPlayer = await playerRepo.GetByIdAsync(playerId, ct);
        if (recoveredPlayer is not null)
        {
            recoveredPlayer.HealHp(50);
            recoveredPlayer.Move(returnPos);
            await playerRepo.UpdateAsync(recoveredPlayer, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("PlayerMoved", new
                {
                    recoveredPlayer.Id,
                    X = returnPos.X,
                    Y = returnPos.Y,
                    ZoneId = returnPos.ZoneId,
                    World = returnPos.World.ToString()
                }, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category  = "system",
                    text      = $"Auto-farm: recovered at homestead (+50 HP). Resuming at ({returnPos.X}, {returnPos.Y})."
                }, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Inventory-full auto-deposit helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// When inventory is full: portal home, batch-deposit all non-equipped non-locked
    /// items into homestead storage, wait 2s, portal back to farming position.
    /// </summary>
    private async Task AutoDepositAndReturnAsync(
        Guid playerId,
        Position returnPos,
        IPlayerRepository playerRepo,
        IItemRepository itemRepo,
        IHomesteadRepository homesteadRepo,
        AutoFarmSession session,
        CancellationToken ct)
    {
        var p = await playerRepo.GetByIdAsync(playerId, ct);
        if (p is null) return;

        var homePos = new Position(p.Position.World, 0, -100, -100);
        p.PortalHome(homePos);
        await playerRepo.UpdateAsync(p, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("PlayerMoved", new
            {
                p.Id,
                X = homePos.X,
                Y = homePos.Y,
                ZoneId = homePos.ZoneId,
                World = homePos.World.ToString()
            }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("AtHomestead", new { playerId }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "system",
                text      = "Auto-farm: inventory full, depositing at homestead..."
            }, ct);

        // Gather eligible items: not equipped, not locked
        var equippedIds = new HashSet<Guid>(p.EquippedItems.Values);
        var allItems = await itemRepo.GetByOwnerAsync(playerId, ct);
        var homestead = await homesteadRepo.GetByPlayerIdAsync(playerId, ct);

        int deposited = 0;
        if (homestead is not null)
        {
            var storageItems = await homesteadRepo.GetStorageItemsAsync(homestead.Id, ct);
            int freeSlots = homestead.StorageSlots - storageItems.Count;

            foreach (var item in allItems)
            {
                if (freeSlots <= 0) break;
                if (equippedIds.Contains(item.Id)) continue;
                if (item.IsLocked) continue;

                item.SetOwner(null);
                await itemRepo.UpdateAsync(item, ct);

                var storageItem = HomesteadStorageItem.Create(homestead.Id, item.Id);
                await homesteadRepo.AddStorageItemAsync(storageItem, ct);

                deposited++;
                freeSlots--;
            }
        }

        autoFarmService.RecordDeposit(playerId, deposited);
        session.ItemsDeposited += deposited;

        await Task.Delay(2_000, ct);

        var returnPlayer = await playerRepo.GetByIdAsync(playerId, ct);
        if (returnPlayer is not null)
        {
            returnPlayer.Move(returnPos);
            await playerRepo.UpdateAsync(returnPlayer, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("PlayerMoved", new
                {
                    returnPlayer.Id,
                    X = returnPos.X,
                    Y = returnPos.Y,
                    ZoneId = returnPos.ZoneId,
                    World = returnPos.World.ToString()
                }, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category  = "system",
                    text      = $"Auto-farm: deposited {deposited} item(s), resuming..."
                }, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Status broadcast helper
    // -------------------------------------------------------------------------

    private async Task BroadcastStatusAsync(
        Guid playerId,
        AutoFarmSession session,
        string state,
        string biome,
        int dangerLevel,
        CancellationToken ct)
    {
        autoFarmService.SetState(playerId, state);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("AutoFarmStatus", new
            {
                active    = true,
                state,
                kills     = session.Kills,
                items     = session.ItemsFound,
                salvaged  = session.ItemsAutoSalvaged,
                deposited = session.ItemsDeposited,
                biome,
                dangerLevel
            }, ct);
    }

    // -------------------------------------------------------------------------
    // Main loop
    // -------------------------------------------------------------------------

    private async Task RunAutoFarmLoopAsync(
        Guid playerId, AutoFarmSession session, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, session.Cts.Token);
        var farmCt = linked.Token;

        const int stepIntervalMs = 3000;

        int originX = 0, originY = 0;
        bool originSet = false;
        int stepCount = 0;

        var spiralEnumerator = SpiralSteps().GetEnumerator();

        try
        {
            while (!farmCt.IsCancellationRequested)
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
                var homesteadRepo    = scope.ServiceProvider.GetRequiredService<IHomesteadRepository>();

                var player = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (player is null) break;

                if (!originSet)
                {
                    originX = player.Position.X;
                    originY = player.Position.Y;
                    originSet = true;
                }

                // --- WALK ONE STEP in the spiral (adaptive danger-aware) ---
                autoFarmService.SetState(playerId, "walking");

                spiralEnumerator.MoveNext();
                var (dx, dy) = spiralEnumerator.Current;

                // Collect all four cardinal directions so we can try alternates
                // when the spiral step lands in a danger zone.
                (int dx, int dy)[] cardinals = [(1, 0), (-1, 0), (0, 1), (0, -1)];
                var zonesForDanger = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);

                int safeCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);

                // Helper: compute danger for a candidate position
                int DangerAt(int cx, int cy)
                {
                    var cZone = ZoneProximity.FindNearby(zonesForDanger, cx, cy);
                    return cZone?.DangerLevel
                        ?? CombatHelpers.GetWildernessDanger(cx, cy,
                            CombatHelpers.GuessWildernessBiome(cx, cy));
                }

                // Build candidate list: spiral step first, then other cardinals,
                // preferring unvisited tiles within the safe cap.
                var candidates = new List<(int ddx, int ddy)> { (dx, dy) };
                foreach (var (cdx, cdy) in cardinals)
                    if ((cdx, cdy) != (dx, dy))
                        candidates.Add((cdx, cdy));

                // Sort: unvisited + safe → visited + safe → anything safe → none
                var farmSession = autoFarmService.GetSession(playerId);
                candidates.Sort((a, b) =>
                {
                    int ax = player.Position.X + a.ddx, ay = player.Position.Y + a.ddy;
                    int bx = player.Position.X + b.ddx, by = player.Position.Y + b.ddy;
                    bool aSafe = DangerAt(ax, ay) <= safeCap;
                    bool bSafe = DangerAt(bx, by) <= safeCap;
                    if (aSafe != bSafe) return aSafe ? -1 : 1;
                    bool aVisited = farmSession?.VisitedTiles.Contains((ax, ay)) ?? false;
                    bool bVisited = farmSession?.VisitedTiles.Contains((bx, by)) ?? false;
                    if (aVisited != bVisited) return aVisited ? 1 : -1; // unvisited first
                    return 0;
                });

                // Pick best candidate
                var chosen = candidates[0];
                int chosenX = player.Position.X + chosen.ddx;
                int chosenY = player.Position.Y + chosen.ddy;
                int chosenDanger = DangerAt(chosenX, chosenY);

                Position newPos;
                if (chosenDanger > safeCap)
                {
                    // All directions are too dangerous — stay on current tile and farm it
                    newPos = player.Position;
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category  = "system",
                            text      = $"Auto-farm: avoiding dangerous terrain (Danger {chosenDanger}) — farming current tile."
                        }, farmCt);
                }
                else
                {
                    // Warn if we had to deviate from the spiral direction
                    if (chosen != (dx, dy) && DangerAt(player.Position.X + dx, player.Position.Y + dy) > safeCap)
                    {
                        int skippedDanger = DangerAt(player.Position.X + dx, player.Position.Y + dy);
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category  = "system",
                                text      = $"Auto-farm: avoiding dangerous terrain (Danger {skippedDanger}) — taking alternate route."
                            }, farmCt);
                    }

                    newPos = new Position(
                        player.Position.World,
                        player.Position.ZoneId,
                        chosenX,
                        chosenY);
                }

                // Mark tile as visited
                farmSession?.VisitedTiles.Add((newPos.X, newPos.Y));

                player.Move(newPos);
                await playerRepo.UpdateAsync(player, farmCt);

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

                // Active companions gain 1 usage point every 10 steps while travelling
                stepCount++;
                if (stepCount % 10 == 0)
                {
                    await using var walkScope = scopeFactory.CreateAsyncScope();
                    var walkHelpers = walkScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                    await walkHelpers.UpdateCompanionUsageAsync(playerId, 1, farmCt);
                }

                // --- HARVEST resource nodes ---
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

                                    // Harvest XP: 1 in auto-farm (vs 5 manual)
                                    await hubContext.Clients
                                        .Group(playerId.ToString())
                                        .SendAsync("GameMessage", new
                                        {
                                            timestamp = DateTime.UtcNow.ToString("O"),
                                            category = "loot-common",
                                            text = $"[Auto-farm] Harvested {actual}x {itemName}. (+1 harvest XP)"
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

                await BroadcastStatusAsync(playerId, session, "walking", biome, dangerLevel, farmCt);

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

                // --- PRE-FIGHT: skip if HP too low to start a new encounter ---
                if (freshPlayer.CurrentHp < freshPlayer.MaxHp * 0.5)
                {
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category  = "system",
                            text      = "Auto-farm: resting before next fight (HP too low)."
                        }, farmCt);
                    continue;
                }

                // --- PRE-FIGHT: buff consumables in danger 7+ zones ---
                if (dangerLevel >= 7)
                {
                    var preInventory = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    foreach (var buffName in BuffNames)
                    {
                        var buff = FindBestConsumable(preInventory, [buffName]);
                        if (buff is not null)
                        {
                            var msg = await ApplyAndConsumeAsync(freshPlayer, buff, playerRepo, itemRepo, farmCt);
                            if (msg is not null)
                                await hubContext.Clients
                                    .Group(playerId.ToString())
                                    .SendAsync("GameMessage", new
                                    {
                                        timestamp = DateTime.UtcNow.ToString("O"),
                                        category  = "system",
                                        text      = msg
                                    }, farmCt);
                        }
                    }
                }

                // --- FIGHT ---
                autoFarmService.SetState(playerId, "fighting");
                await BroadcastStatusAsync(playerId, session, "fighting", biome, dangerLevel, farmCt);

                var encounter = await combatSvc.StartEncounterAsync(
                    playerId, nearbyZone?.Id ?? Guid.NewGuid(), freshPlayer, [], monsters, ct: farmCt);

                var encounterCategory = CombatHelpers.GetCombatDifficultyCategory(avgMonsterLevel, freshPlayer.Level);
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = encounterCategory,
                        text = $"[Auto-farm] Encounter! {string.Join(", ", monsters.Select(m => m.Name))}."
                    }, farmCt);

                // Track weave spent locally (Combatant has no Weave field; freshPlayer.Weave tracks it)
                int currentWeave = freshPlayer.Weave.Current;

                // --- PRE-FIGHT flee check: estimate if we can survive ---
                {
                    var preEnemies = encounter.Combatants.Where(c => !c.IsPlayerSide && !c.IsDefeated).ToList();
                    var prePc = encounter.Combatants
                        .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);
                    if (prePc is not null && preEnemies.Count > 0)
                    {
                        int totalEnemyHp        = preEnemies.Sum(e => e.CurrentHp);
                        int playerDamagePerRound = 20; // conservative Strike estimate
                        int enemyDamagePerRound  = preEnemies.Count * 10;
                        int roundsToKill         = Math.Max(1, totalEnemyHp / playerDamagePerRound);
                        int playerHpAfterFight   = prePc.CurrentHp - roundsToKill * enemyDamagePerRound;

                        if (playerHpAfterFight < 0)
                        {
                            var (fleeSuc, _, fleeEnc) = await combatSvc.FleeAsync(encounter.Id, farmCt);
                            if (fleeSuc && fleeEnc is not null) encounter = fleeEnc;
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = "Auto-farm: fleeing — fight looks unwinnable."
                                }, farmCt);
                        }
                    }
                }

                var safety = 0;
                while (encounter.State == EncounterState.InProgress && safety++ < 50)
                {
                    var actor = encounter.CurrentActor;
                    if (actor is null) break;

                    if (actor.IsPlayerSide)
                    {
                        var livingEnemies = encounter.Combatants
                            .Where(c => !c.IsPlayerSide && !c.IsDefeated)
                            .ToList();
                        if (livingEnemies.Count == 0) break;

                        var playerCombatant = encounter.Combatants
                            .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);

                        // 1. Heal check: use Restore if HP < 40% and it's the player's turn
                        if (actor.CombatantType == CombatantType.Player && playerCombatant is not null)
                        {
                            float hpPct = (float)playerCombatant.CurrentHp / playerCombatant.MaxHp;
                            var healAbility = actor.Abilities
                                .FirstOrDefault(a => a.Category == AbilityCategory.Heal);

                            if (hpPct < 0.4f && healAbility is not null && currentWeave >= healAbility.WeaveCost)
                            {
                                currentWeave -= healAbility.WeaveCost;
                                await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, healAbility.Name, actor.Id, farmCt);
                                encounter = combatSvc.GetEncounter(encounter.Id) ?? encounter;
                                continue;
                            }
                        }

                        // 2. Target priority: kill weakest enemy first
                        var target = livingEnemies.OrderBy(e => e.CurrentHp).First();

                        // 3. Best attack ability: prefer element advantage × BasePower; skip if not enough weave
                        var attackAbilities = actor.Abilities
                            .Where(a => a.Category == AbilityCategory.Attack && currentWeave >= a.WeaveCost)
                            .ToList();

                        CombatAbility chosenAbility;
                        if (attackAbilities.Count == 0)
                        {
                            // No weave-affordable ability found — fall back to Strike (0 cost)
                            chosenAbility = actor.Abilities.FirstOrDefault(a => a.Name == "Strike")
                                ?? actor.Abilities.First(a => a.Category == AbilityCategory.Attack);
                        }
                        else
                        {
                            chosenAbility = attackAbilities
                                .OrderByDescending(a => ElementMatchup.GetMultiplier(a.Element, target.Element) * a.BasePower)
                                .First();
                        }

                        currentWeave -= chosenAbility.WeaveCost;
                        await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, chosenAbility.Name, target.Id, farmCt);
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
                    // Record defeat: lower the safe danger cap to currentDanger - 1
                    autoFarmService.RecordEncounterOutcome(playerId, FarmEncounterOutcome.Defeat, dangerLevel);

                    // Companions still gain usage for the fight even on defeat
                    await using (var defeatCompScope = scopeFactory.CreateAsyncScope())
                    {
                        var defeatCompHelpers = defeatCompScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                        await defeatCompHelpers.UpdateCompanionUsageAsync(playerId, 10, farmCt);
                    }

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
                            category = encounterCategory,
                            text = "Auto-farm ended: you were defeated. You wake at your homestead..."
                        }, serverCt);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("AutoFarmStatus", new
                        {
                            active    = false,
                            reason    = "defeat",
                            kills     = session.Kills,
                            items     = session.ItemsFound,
                            salvaged  = session.ItemsAutoSalvaged,
                            deposited = session.ItemsDeposited
                        }, serverCt);
                    return;
                }

                if (encounter.State == EncounterState.Fled)
                {
                    // Record flee: if 2+ in last 5, the cap will be reduced
                    autoFarmService.RecordEncounterOutcome(playerId, FarmEncounterOutcome.Fled, dangerLevel);
                    int newCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);

                    // Companions gain usage even when fleeing
                    await using (var fledCompScope = scopeFactory.CreateAsyncScope())
                    {
                        var fledCompHelpers = fledCompScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                        await fledCompHelpers.UpdateCompanionUsageAsync(playerId, 10, farmCt);
                    }

                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category  = encounterCategory,
                            text      = $"Auto-farm: fled encounter (Danger {dangerLevel}). Safe cap now {newCap}."
                        }, farmCt);
                }

                if (encounter.State == EncounterState.Victory)
                {
                    // Record victory: consistent wins will raise the safe danger cap
                    autoFarmService.RecordEncounterOutcome(playerId, FarmEncounterOutcome.Victory, dangerLevel);
                    int newCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);
                    if (newCap > player.Level + 2)
                    {
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category  = "system",
                                text      = $"Auto-farm: winning streak — exploring up to Danger {newCap}."
                            }, farmCt);
                    }

                    // Sync HP after combat
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

                        // --- POST-COMBAT CONSUMABLE CHECKS ---
                        var postInventory = await itemRepo.GetByOwnerAsync(playerId, farmCt);

                        if (winPlayer.CurrentHp < winPlayer.MaxHp / 2)
                        {
                            var healer = FindBestConsumable(postInventory, HealingPriority);
                            if (healer is not null)
                            {
                                var msg = await ApplyAndConsumeAsync(winPlayer, healer, playerRepo, itemRepo, farmCt);
                                if (msg is not null)
                                    await hubContext.Clients
                                        .Group(playerId.ToString())
                                        .SendAsync("GameMessage", new
                                        {
                                            timestamp = DateTime.UtcNow.ToString("O"),
                                            category  = "system",
                                            text      = msg
                                        }, farmCt);
                            }
                            else if (winPlayer.CurrentHp < winPlayer.MaxHp * 3 / 10)
                            {
                                await PortalHomeToHealAsync(playerId, winPlayer.Position, playerRepo, farmCt);
                                winPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                            }
                        }

                        if (winPlayer is not null && winPlayer.Weave.Percentage < 20)
                        {
                            var postInventory2 = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                            var weaveCons = FindBestConsumable(postInventory2, WeavePriority);
                            if (weaveCons is not null)
                            {
                                var msg = await ApplyAndConsumeAsync(winPlayer, weaveCons, playerRepo, itemRepo, farmCt);
                                if (msg is not null)
                                    await hubContext.Clients
                                        .Group(playerId.ToString())
                                        .SendAsync("GameMessage", new
                                        {
                                            timestamp = DateTime.UtcNow.ToString("O"),
                                            category  = "system",
                                            text      = msg
                                        }, farmCt);
                            }
                        }
                    }

                    autoFarmService.RecordKill(playerId);
                    session.Kills = autoFarmService.GetSession(playerId)?.Kills ?? session.Kills;

                    // XP award — degraded in auto-farm
                    await using var xpScope = scopeFactory.CreateAsyncScope();
                    var combatHelpers = xpScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                    await combatHelpers.AwardCombatXpAsync(playerId, encounter, farmCt, isAutoFarm: true);

                    // Companion usage — 10 points per combat (same as manual)
                    await using var compScope = scopeFactory.CreateAsyncScope();
                    var compHelpers = compScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                    await compHelpers.UpdateCompanionUsageAsync(playerId, 10, farmCt);

                    // Loot — check inventory full before rolling
                    var currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    var lootPlayer = winPlayer ?? freshPlayer;

                    if (lootPlayer is not null && currentItems.Count >= lootPlayer.MaxInventorySlots)
                    {
                        // Auto-deposit before rolling loot
                        autoFarmService.SetState(playerId, "depositing");
                        await BroadcastStatusAsync(playerId, session, "depositing", biome, dangerLevel, farmCt);
                        await AutoDepositAndReturnAsync(playerId, newPos, playerRepo, itemRepo, homesteadRepo, session, farmCt);
                        // Refresh after deposit
                        currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                        lootPlayer = await playerRepo.GetByIdAsync(playerId, farmCt) ?? lootPlayer;
                    }

                    if (lootPlayer is null) continue;

                    var lootResult = await lootSvc.RollLootDropAsync(
                        dangerLevel, playerId, lootPlayer.Position.World,
                        currentItems.Count, lootPlayer.MaxInventorySlots, farmCt,
                        lootPlayer,
                        nearbyZone?.Name,
                        isAutoFarm: true);

                    if (lootResult.Dropped)
                    {
                        if (lootResult.AutoSalvaged)
                        {
                            autoFarmService.RecordAutoSalvage(playerId);
                            session.ItemsAutoSalvaged = autoFarmService.GetSession(playerId)?.ItemsAutoSalvaged ?? session.ItemsAutoSalvaged;
                        }
                        else
                        {
                            autoFarmService.RecordItem(playerId);
                            session.ItemsFound = autoFarmService.GetSession(playerId)?.ItemsFound ?? session.ItemsFound;
                        }

                        var farmMsgCategory = lootResult.AutoSalvaged
                            ? CombatHelpers.GetSalvageCategory(lootResult.Item?.Workmanship.Value ?? 1)
                            : (lootResult.Item is not null
                                ? CombatHelpers.GetLootCategory(lootResult.Item)
                                : "loot");

                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category = farmMsgCategory,
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

                    // --- REST between fights ---
                    var restPlayer = winPlayer ?? freshPlayer;
                    autoFarmService.SetState(playerId, "resting");
                    await BroadcastStatusAsync(playerId, session, "resting", biome, dangerLevel, farmCt);

                    // Natural HP regen: +5 HP during rest
                    if (restPlayer is not null)
                    {
                        var restedPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                        if (restedPlayer is not null)
                        {
                            restedPlayer.HealHp(5);
                            await playerRepo.UpdateAsync(restedPlayer, farmCt);

                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category = "system",
                                    text = restedPlayer.CurrentHp < restedPlayer.MaxHp
                                        ? $"Resting... (HP: {restedPlayer.CurrentHp}/{restedPlayer.MaxHp})"
                                        : "Resting... (HP full)"
                                }, farmCt);
                        }
                    }

                    // Running summary
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "system",
                            text = $"Auto-farm: {session.Kills} kill(s), {session.ItemsFound} item(s) kept, {session.ItemsAutoSalvaged} auto-salvaged, {session.ItemsDeposited} deposited."
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
            var kills     = finalSession?.Kills             ?? session.Kills;
            var items     = finalSession?.ItemsFound        ?? session.ItemsFound;
            var salvaged  = finalSession?.ItemsAutoSalvaged ?? session.ItemsAutoSalvaged;
            var deposited = finalSession?.ItemsDeposited    ?? session.ItemsDeposited;
            autoFarmService.EndSession(playerId);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = $"Auto-farm stopped: {kills} kill(s), {items} item(s) kept, {salvaged} auto-salvaged, {deposited} deposited."
                }, serverCt);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("AutoFarmStatus", new
                {
                    active    = false,
                    kills,
                    items,
                    salvaged,
                    deposited
                }, serverCt);
        }
    }
}
