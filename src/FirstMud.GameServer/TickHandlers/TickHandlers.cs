using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.Engine.Tick;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.TickHandlers;

// =====================================================================
// Each class below is ONE periodic job the original GameLoopService
// used to run as a hard-coded branch inside ProcessTickAsync. They have
// been mechanically extracted as ITickHandler implementations so the
// engine's GameLoopService doesn't need to know about any of them.
//
// No behaviour has changed — intervals and logic are identical to the
// pre-extraction implementation.
// =====================================================================

/// <summary>Drives <see cref="AiPlayerService.TickAsync"/> every 60 seconds.</summary>
public class AiPlayerTickHandler(AiPlayerService aiPlayerService, ILogger<AiPlayerTickHandler> logger) : ITickHandler
{
    public string Name => "AI Player";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);
    public bool RunOnFirstTick => true;

    public async Task HandleTickAsync(CancellationToken ct)
    {
        logger.LogDebug("Running AI player tick.");
        await aiPlayerService.TickAsync(ct);
    }
}

/// <summary>Periodic sweep: auto-assign idle companions to city duties. Every 300s.</summary>
public class AutomationSweepTickHandler(IServiceScopeFactory scopeFactory, ILogger<AutomationSweepTickHandler> logger) : ITickHandler
{
    public string Name => "Automation Sweep";
    public TimeSpan Interval => TimeSpan.FromSeconds(300);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        logger.LogDebug("Running automation upkeep tick.");
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var buildingService = scope.ServiceProvider.GetRequiredService<BuildingService>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                await buildingService.AutoAssignIdleCompanionsAsync(player.Id, player.ActiveCompanionIds, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in automation companion sweep.");
        }
    }
}

/// <summary>Heals homestead residents (10 HP + 5 Weave per second).</summary>
public class HomesteadHealTickHandler(IServiceScopeFactory scopeFactory, ILogger<HomesteadHealTickHandler> logger) : ITickHandler
{
    public string Name => "Homestead Heal";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                if (player.Position.X != -100 || player.Position.Y != -100) continue;

                var changed = false;
                var hpBefore = player.CurrentHp;

                if (player.CurrentHp < player.MaxHp)
                {
                    player.HealHp(10);
                    changed = true;
                }

                if (player.Weave.Current < player.Weave.Maximum)
                {
                    player.RestoreWeave(5);
                    changed = true;
                }

                if (!changed) continue;

                logger.LogInformation(
                    "Homestead heal tick: player {Name} at ({X},{Y}), HP {Before} → {After}",
                    player.Name, player.Position.X, player.Position.Y, hpBefore, player.CurrentHp);

                await playerRepo.UpdateAsync(player, ct);

                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = $"Homestead sanctuary heals you. HP: {player.CurrentHp}/{player.MaxHp} | Weave: {player.Weave.Current}/{player.Weave.Maximum}."
                    }, ct);

                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("PlayerHealed", new
                    {
                        player.Id,
                        player.CurrentHp,
                        player.MaxHp,
                        WeavePercent = player.Weave.Percentage,
                        WeaveState = player.Weave.VisibleState.ToString()
                    }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in homestead heal tick.");
        }
    }
}

/// <summary>Regenerates 1 Weave per 10s for players out of combat, not at homestead.</summary>
public class WeaveRegenTickHandler(IServiceScopeFactory scopeFactory, ILogger<WeaveRegenTickHandler> logger) : ITickHandler
{
    public string Name => "Weave Regen";
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                if (player.Position.X == -100 && player.Position.Y == -100) continue;
                if (player.Weave.Current >= player.Weave.Maximum) continue;

                player.RestoreWeave(1);
                await playerRepo.UpdateAsync(player, ct);

                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("PlayerHealed", new
                    {
                        player.Id,
                        player.CurrentHp,
                        player.MaxHp,
                        WeavePercent = player.Weave.Percentage,
                        WeaveState = player.Weave.VisibleState.ToString()
                    }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in Weave regen tick.");
        }
    }
}

/// <summary>Runs homestead companion duties (harvest/craft/salvage/guard) every 60s.</summary>
public class HomesteadCompanionTickHandler(IServiceScopeFactory scopeFactory, ILogger<HomesteadCompanionTickHandler> logger) : ITickHandler
{
    public string Name => "Homestead Companions";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<HomesteadCompanionService>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var results = await service.ProcessAllDutiesAsync(ct);

            if (results.Count > 0)
            {
                var byPlayer = results.GroupBy(r => r.PlayerId);
                foreach (var group in byPlayer)
                {
                    var playerId = group.Key;
                    var msgs = group.ToList();

                    var harvested = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    int crafted = 0, salvaged = 0, otherCount = 0;
                    var otherMsgs = new List<string>();

                    foreach (var m in msgs)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            m.Message, @"harvested (\w[\w\s]*?) x(\d+)");
                        if (match.Success)
                        {
                            var mat = match.Groups[1].Value.Trim();
                            var qty = int.Parse(match.Groups[2].Value);
                            harvested[mat] = harvested.GetValueOrDefault(mat) + qty;
                        }
                        else if (m.Message.Contains("crafted", StringComparison.OrdinalIgnoreCase))
                            crafted++;
                        else if (m.Message.Contains("salvaged", StringComparison.OrdinalIgnoreCase))
                            salvaged++;
                        else
                        {
                            otherMsgs.Add(m.Message);
                            otherCount++;
                        }
                    }

                    var parts = new List<string>();
                    if (harvested.Count > 0)
                    {
                        var total = harvested.Values.Sum();
                        var matList = string.Join(", ", harvested.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
                        parts.Add($"Harvested {total} materials ({matList})");
                    }
                    if (crafted > 0) parts.Add($"Crafted {crafted} item(s)");
                    if (salvaged > 0) parts.Add($"Salvaged {salvaged} item(s)");

                    int workerCount = msgs.Count - otherCount;
                    var summary = parts.Count > 0
                        ? $"Homestead report ({workerCount} workers): {string.Join(" · ", parts)}."
                        : null;

                    if (summary is not null)
                    {
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category = "system",
                                text = summary,
                            }, ct);
                    }

                    foreach (var other in otherMsgs)
                    {
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category = "system",
                                text = other,
                            }, ct);
                    }
                }

                logger.LogDebug("Homestead companion tick complete. {Count} duty action(s) processed.", results.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in homestead companion tick.");
        }
    }
}

/// <summary>Accumulates drift on idle companions every 60s.</summary>
public class CompanionDriftTickHandler(IServiceScopeFactory scopeFactory, ILogger<CompanionDriftTickHandler> logger) : ITickHandler
{
    public string Name => "Companion Drift";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var companionRepo = scope.ServiceProvider.GetRequiredService<ICompanionRepository>();
            var hubContext    = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            var playerRepo    = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var players       = await playerRepo.GetActivePlayersAsync(ct);

            foreach (var player in players)
            {
                var companions = await companionRepo.GetByOwnerAsync(player.Id, ct);
                foreach (var companion in companions)
                {
                    if (companion.IsPermanentlyGone) continue;
                    if (companion.AssignedDuty.HasValue && companion.AssignedDuty != FirstMud.Domain.Enums.HomesteadDuty.None) continue;

                    var layerBefore = companion.CurrentLayer;
                    companion.AccumulateDrift(1f / 60f);
                    await companionRepo.UpdateAsync(companion, ct);

                    if (companion.CurrentLayer < layerBefore)
                    {
                        await hubContext.Clients
                            .Group(player.Id.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category  = "system",
                                text      = $"{companion.Name} has drifted to Layer {companion.CurrentLayer} due to inactivity. Bring them on an adventure!"
                            }, ct);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in companion drift tick.");
        }
    }
}

/// <summary>Advances building construction progress every 60s.</summary>
public class BuildingConstructionTickHandler(IServiceScopeFactory scopeFactory, ILogger<BuildingConstructionTickHandler> logger) : ITickHandler
{
    public string Name => "Building Construction";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var buildingService = scope.ServiceProvider.GetRequiredService<BuildingService>();
            var hubContext      = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            var playerRepo      = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var homesteadRepo   = scope.ServiceProvider.GetRequiredService<IHomesteadRepository>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                var homestead = await homesteadRepo.GetByPlayerIdAsync(player.Id, ct);
                if (homestead is not null)
                    buildingService.RegisterHomesteadOwner(homestead.Id, player.Id);
            }

            var results = await buildingService.TickConstructionAsync(ct);

            foreach (var result in results)
            {
                if (result.PlayerId == Guid.Empty) continue;

                var msg = result.AutoAssignedCompanionName is not null
                    ? $"{result.BuildingType} construction complete! {result.AutoAssignedCompanionName} has been assigned to work there."
                    : $"{result.BuildingType} construction complete! Visit your homestead [P] and open the city panel [G] to assign a worker.";

                await hubContext.Clients
                    .Group(result.PlayerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category  = "system",
                        text      = msg,
                    }, ct);
            }

            if (results.Count > 0)
                logger.LogDebug("Building construction tick complete. {Count} building(s) finished.", results.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in building construction tick.");
        }
    }
}

/// <summary>Regenerates resource-node yield every 60s.</summary>
public class ResourceRegenTickHandler(IServiceScopeFactory scopeFactory, ILogger<ResourceRegenTickHandler> logger) : ITickHandler
{
    public string Name => "Resource Regen";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task HandleTickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var nodeRepo = scope.ServiceProvider.GetRequiredService<IResourceNodeRepository>();

            var nodes = await nodeRepo.GetAllAsync(ct);
            foreach (var node in nodes)
            {
                if (node.RemainingYield >= node.MaxYield) continue;
                node.Regenerate();
                await nodeRepo.UpdateAsync(node, ct);
            }

            logger.LogDebug("Resource node regen tick complete. Nodes updated: {Count}",
                nodes.Count(n => n.RemainingYield < n.MaxYield));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error in resource node regen tick.");
        }
    }
}
