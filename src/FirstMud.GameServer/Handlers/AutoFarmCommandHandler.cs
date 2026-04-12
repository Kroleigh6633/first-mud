using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

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

    private async Task RunAutoFarmLoopAsync(
        Guid playerId, AutoFarmSession session, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, session.Cts.Token);
        var farmCt = linked.Token;

        var endTime = session.StartedAt.AddSeconds(session.DurationSeconds);
        const int intervalMs = 3000;

        try
        {
            while (!farmCt.IsCancellationRequested && DateTimeOffset.UtcNow < endTime)
            {
                await Task.Delay(intervalMs, farmCt);
                if (farmCt.IsCancellationRequested) break;

                await using var scope = scopeFactory.CreateAsyncScope();
                var playerRepo  = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
                var itemRepo    = scope.ServiceProvider.GetRequiredService<IItemRepository>();
                var zoneRepo    = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
                var lootSvc     = scope.ServiceProvider.GetRequiredService<LootService>();
                var combatSvc   = scope.ServiceProvider.GetRequiredService<CombatService>();

                var player = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (player is null) break;

                var elapsed   = DateTimeOffset.UtcNow - session.StartedAt;
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

                // Find nearby zone for encounter roll
                var zones = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);
                var nearbyZone = ZoneProximity.FindNearby(zones, player.Position.X, player.Position.Y);

                var dangerLevel = nearbyZone?.DangerLevel ?? 2;
                var encounterChance = Math.Min(dangerLevel * 10, 70);
                if (Random.Shared.Next(100) >= encounterChance)
                    continue;

                // Start and auto-fight the encounter
                var monsters = CombatHelpers.BuildMonsterPack(dangerLevel, player.Level);

                // Skip grey encounters — monsters too weak to bother fighting
                var avgMonsterLevel = monsters.Count > 0
                    ? (int)Math.Round(monsters.Average(m => (double)m.Level))
                    : 1;
                var levelGap = player.Level - avgMonsterLevel;

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
                    playerId, nearbyZone?.Id ?? Guid.NewGuid(), player, [], monsters, ct: farmCt);

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
                    autoFarmService.EndSession(playerId);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "combat",
                            text = "Auto-farm ended: you were defeated."
                        }, serverCt);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("AutoFarmStatus", new { active = false, reason = "defeat" }, serverCt);
                    return;
                }

                if (encounter.State == EncounterState.Victory)
                {
                    autoFarmService.RecordKill(playerId);

                    var currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    var lootResult = await lootSvc.RollLootDropAsync(
                        dangerLevel, playerId, player.Position.World,
                        currentItems.Count, player.MaxInventorySlots, farmCt,
                        player);

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
