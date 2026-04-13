using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Orchestrates the auto-farm loop: walk → harvest → encounter-roll → fight →
/// loot → rest → deposit → repeat.
///
/// Extracted from AutoFarmCommandHandler (was 1,200 lines) to give each concern
/// a focused home:
///   - AutoFarmCommandHandler : thin toggle (start/stop session)
///   - FarmingOrchestrator    : loop coordination and step sequencing (this class)
///   - ConsumableHelper       : item-use logic (nested static class)
///   - BiomeService           : biome/danger queries
///   - MonsterFactory         : monster pack building
/// </summary>
public class FarmingOrchestrator(
    IHubContext<GameHub> hubContext,
    AutoFarmService autoFarmService,
    IServiceScopeFactory scopeFactory,
    ILogger<FarmingOrchestrator> logger,
    InventoryDepositService inventoryDepositService)
{
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
    // Public entry point — spawns the loop as a fire-and-forget Task
    // -------------------------------------------------------------------------

    public void StartLoop(Guid playerId, AutoFarmSession session, CancellationToken serverCt)
    {
        _ = Task.Run(async () =>
        {
            await RunAutoFarmLoopAsync(playerId, session, serverCt);
        }, CancellationToken.None);
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

    private Task AutoDepositAndReturnAsync(
        Guid playerId,
        Position returnPos,
        IPlayerRepository playerRepo,
        IItemRepository itemRepo,
        IHomesteadRepository homesteadRepo,
        AutoFarmSession session,
        CancellationToken ct)
        => inventoryDepositService.AutoDepositAndReturnAsync(playerId, returnPos, session, ct);

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
                quests    = session.QuestsCompleted,
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
        int navDetourSteps = 0;   // counts detour steps taken while navigating to target zone
        bool navAborted = false;  // true when we gave up on reaching the target zone safely

        var spiralEnumerator = SpiralSteps().GetEnumerator();

        try
        {
            while (!farmCt.IsCancellationRequested)
            {
                await Task.Delay(stepIntervalMs, farmCt);
                if (farmCt.IsCancellationRequested) break;

                await using var scope = scopeFactory.CreateAsyncScope();
                var playerRepo        = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
                var itemRepo          = scope.ServiceProvider.GetRequiredService<IItemRepository>();
                var zoneRepo          = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
                var lootSvc           = scope.ServiceProvider.GetRequiredService<LootService>();
                var combatSvc         = scope.ServiceProvider.GetRequiredService<CombatService>();
                var resourceNodeRepo  = scope.ServiceProvider.GetRequiredService<IResourceNodeRepository>();
                var homesteadRepo     = scope.ServiceProvider.GetRequiredService<IHomesteadRepository>();
                var companionRepo     = scope.ServiceProvider.GetRequiredService<ICompanionRepository>();

                var player = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (player is null) break;

                if (!originSet)
                {
                    originX = player.Position.X;
                    originY = player.Position.Y;
                    originSet = true;

                    // If the player specified a target zone, do a pre-check before navigating
                    if (session.TargetX.HasValue && session.TargetY.HasValue && !navAborted)
                    {
                        int preCheckSafeCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);
                        var preCheckZones   = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);

                        int PreCheckDangerAt(int cx, int cy)
                        {
                            var z = ZoneProximity.FindNearby(preCheckZones, cx, cy);
                            return z?.DangerLevel
                                ?? BiomeService.GetWildernessDanger(cx, cy,
                                    BiomeService.GuessWildernessBiome(cx, cy));
                        }

                        // Sample 6 evenly-spaced points along the straight-line path
                        int tx = session.TargetX.Value, ty = session.TargetY.Value;
                        int maxSampledDanger = 0;
                        const int sampleCount = 6;
                        for (int s = 1; s <= sampleCount; s++)
                        {
                            int sx = player.Position.X + (tx - player.Position.X) * s / sampleCount;
                            int sy = player.Position.Y + (ty - player.Position.Y) * s / sampleCount;
                            int sd = PreCheckDangerAt(sx, sy);
                            if (sd > maxSampledDanger) maxSampledDanger = sd;
                        }

                        if (maxSampledDanger >= preCheckSafeCap + 4)
                        {
                            // Path is extremely dangerous — refuse to navigate at all
                            navAborted = true;
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = $"Auto-farm: route to ({tx}, {ty}) is blocked by danger {maxSampledDanger} territory (your safe limit: {preCheckSafeCap}). Farming current area instead."
                                }, farmCt);
                        }
                        else if (maxSampledDanger > preCheckSafeCap + 2)
                        {
                            // Warn but still attempt (detour logic will handle impassable tiles)
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = $"Auto-farm: route to ({tx}, {ty}) passes through dangerous territory (danger {maxSampledDanger}, your safe limit: {preCheckSafeCap}). Will try to detour around it."
                                }, farmCt);
                        }
                        else
                        {
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = $"Auto-farm: heading to target zone ({tx}, {ty})..."
                                }, farmCt);
                        }
                    }
                }

                // --- TARGET ZONE NAVIGATION: danger-aware step toward target until within 2 tiles ---
                if (session.TargetX.HasValue && session.TargetY.HasValue && !navAborted)
                {
                    int distX = Math.Abs(session.TargetX.Value - player.Position.X);
                    int distY = Math.Abs(session.TargetY.Value - player.Position.Y);
                    if (distX > 2 || distY > 2)
                    {
                        int navSafeCap   = autoFarmService.GetSafeDangerCap(playerId, player.Level);
                        var navZones     = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);
                        int directDist   = distX + distY; // Manhattan distance to target

                        int NavDangerAt(int cx, int cy)
                        {
                            var z = ZoneProximity.FindNearby(navZones, cx, cy);
                            return z?.DangerLevel
                                ?? BiomeService.GetWildernessDanger(cx, cy,
                                    BiomeService.GuessWildernessBiome(cx, cy));
                        }

                        // Maximum detour: 3× the direct distance
                        int maxDetourSteps = directDist * 3;

                        int tdx = Math.Sign(session.TargetX.Value - player.Position.X);
                        int tdy = Math.Sign(session.TargetY.Value - player.Position.Y);

                        // Build candidate steps: direct diagonal first, then axis-aligned
                        // components, then perpendiculars
                        var navCandidates = new List<(int dx, int dy)>();

                        // Direct diagonal (or axis) step toward target
                        if (tdx != 0 && tdy != 0)
                        {
                            navCandidates.Add((tdx, tdy));   // diagonal
                            navCandidates.Add((tdx, 0));     // horizontal component
                            navCandidates.Add((0, tdy));     // vertical component
                        }
                        else if (tdx != 0)
                        {
                            navCandidates.Add((tdx, 0));
                            navCandidates.Add((tdx, 1));
                            navCandidates.Add((tdx, -1));
                        }
                        else
                        {
                            navCandidates.Add((0, tdy));
                            navCandidates.Add((1, tdy));
                            navCandidates.Add((-1, tdy));
                        }

                        // Check detour limit before taking any step
                        if (navDetourSteps > maxDetourSteps)
                        {
                            navAborted = true;
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = $"Auto-farm: route too long — no safe path to ({session.TargetX}, {session.TargetY}). Farming current area instead."
                                }, farmCt);
                            // Fall through to spiral farming
                            goto spiralFarm;
                        }

                        // Evaluate each candidate step
                        (int dx, int dy) chosenNavStep = default;
                        bool isDetour = false;
                        int chosenNavDanger = int.MaxValue;

                        foreach (var (cdx, cdy) in navCandidates)
                        {
                            int nx = player.Position.X + cdx;
                            int ny = player.Position.Y + cdy;
                            int nd = NavDangerAt(nx, ny);
                            if (nd <= navSafeCap)
                            {
                                // Prefer the first (most direct) safe step
                                chosenNavStep  = (cdx, cdy);
                                chosenNavDanger = nd;
                                isDetour = (cdx, cdy) != navCandidates[0];
                                break;
                            }
                        }

                        if (chosenNavStep == default)
                        {
                            // All candidates are dangerous — abort
                            int blockedDanger = NavDangerAt(
                                player.Position.X + navCandidates[0].dx,
                                player.Position.Y + navCandidates[0].dy);
                            navAborted = true;
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = $"Auto-farm: no safe route to ({session.TargetX}, {session.TargetY}). Path blocked by danger {blockedDanger} territory (your safe limit: {navSafeCap}). Farming current area instead."
                                }, farmCt);
                            // Fall through to spiral farming
                            goto spiralFarm;
                        }

                        if (isDetour)
                        {
                            navDetourSteps++;
                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("GameMessage", new
                                {
                                    timestamp = DateTime.UtcNow.ToString("O"),
                                    category  = "system",
                                    text      = "Auto-farm: detouring around dangerous terrain..."
                                }, farmCt);
                        }

                        var navPos = new Position(
                            player.Position.World,
                            player.Position.ZoneId,
                            player.Position.X + chosenNavStep.dx,
                            player.Position.Y + chosenNavStep.dy);
                        player.Move(navPos);
                        await playerRepo.UpdateAsync(player, farmCt);
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("PlayerMoved", new
                            {
                                player.Id,
                                X = navPos.X,
                                Y = navPos.Y,
                                ZoneId = navPos.ZoneId,
                                World = navPos.World.ToString()
                            }, farmCt);
                        await BroadcastStatusAsync(playerId, session, "walking", "path", chosenNavDanger, farmCt);
                        continue;
                    }
                }

                spiralFarm:

                // --- WALK ONE STEP in the spiral (adaptive danger-aware) ---
                autoFarmService.SetState(playerId, "walking");

                spiralEnumerator.MoveNext();
                var (dx, dy) = spiralEnumerator.Current;

                (int dx, int dy)[] cardinals = [(1, 0), (-1, 0), (0, 1), (0, -1)];
                var zonesForDanger = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);

                int safeCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);

                int DangerAt(int cx, int cy)
                {
                    var cZone = ZoneProximity.FindNearby(zonesForDanger, cx, cy);
                    return cZone?.DangerLevel
                        ?? BiomeService.GetWildernessDanger(cx, cy,
                            BiomeService.GuessWildernessBiome(cx, cy));
                }

                var candidates = new List<(int ddx, int ddy)> { (dx, dy) };
                foreach (var (cdx, cdy) in cardinals)
                    if ((cdx, cdy) != (dx, dy))
                        candidates.Add((cdx, cdy));

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
                    if (aVisited != bVisited) return aVisited ? 1 : -1;
                    return 0;
                });

                var chosen = candidates[0];
                int chosenX = player.Position.X + chosen.ddx;
                int chosenY = player.Position.Y + chosen.ddy;
                int chosenDanger = DangerAt(chosenX, chosenY);

                Position newPos;
                if (chosenDanger > safeCap)
                {
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

                // --- AUTO-COMPLETE explore quests after each move step ---
                await using (var exploreQuestScope = scopeFactory.CreateAsyncScope())
                {
                    var exploreQuestSvc = exploreQuestScope.ServiceProvider.GetRequiredService<QuestAutoCompleteService>();
                    var exploreQuestsCompleted = await exploreQuestSvc.TryAutoCompleteQuestsAsync(playerId, farmCt);
                    if (exploreQuestsCompleted > 0)
                    {
                        for (int _q = 0; _q < exploreQuestsCompleted; _q++)
                            autoFarmService.RecordQuestComplete(playerId);
                        session.QuestsCompleted = autoFarmService.GetSession(playerId)?.QuestsCompleted ?? session.QuestsCompleted;
                    }
                }

                // --- HARVEST resource nodes (skipped for combat-only priority) ---
                if (session.Priority != "combat")
                {
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
                } // end harvest priority check

                // --- ENCOUNTER ROLL (skipped for harvest-only priority) ---
                if (session.Priority == "harvest")
                {
                    await BroadcastStatusAsync(playerId, session, "walking", "path", 0, farmCt);
                    continue;
                }
                var allZones = await zoneRepo.GetByWorldAsync(newPos.World, farmCt);
                var nearbyZone = ZoneProximity.FindNearby(allZones, newPos.X, newPos.Y);

                int dangerLevel;
                int encounterChance;
                string biome;

                if (nearbyZone != null)
                {
                    dangerLevel = nearbyZone.DangerLevel;
                    encounterChance = Math.Min(dangerLevel * 10, 70);
                    biome = BiomeService.GetBiome(nearbyZone);
                }
                else
                {
                    biome = BiomeService.GuessWildernessBiome(newPos.X, newPos.Y);
                    dangerLevel = BiomeService.GetWildernessDanger(newPos.X, newPos.Y, biome);
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

                var farmActiveCompanions = new List<Companion>();
                foreach (var compId in freshPlayer.ActiveCompanionIds)
                {
                    var comp = await companionRepo.GetByIdAsync(compId, farmCt);
                    if (comp != null && !comp.IsPermanentlyGone)
                        farmActiveCompanions.Add(comp);
                }

                int partySize = 1 + farmActiveCompanions.Count;
                var monsters = MonsterFactory.BuildMonsterPack(dangerLevel, freshPlayer.Level, biome, partySize);

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
                    foreach (var buffName in ConsumableHelper.BuffNames)
                    {
                        var buff = ConsumableHelper.FindBest(preInventory, [buffName]);
                        if (buff is not null)
                        {
                            var msg = await ConsumableHelper.ApplyAndConsumeAsync(freshPlayer, buff, playerRepo, itemRepo, farmCt);
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
                    playerId, nearbyZone?.Id ?? Guid.NewGuid(), freshPlayer, farmActiveCompanions, monsters, ct: farmCt, dangerLevel: dangerLevel);

                var encounterCategory = CombatHelpers.GetCombatDifficultyCategory(avgMonsterLevel, freshPlayer.Level);
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = encounterCategory,
                        text = $"[Auto-farm] Encounter! {string.Join(", ", monsters.Select(m => m.Name))}."
                    }, farmCt);

                int currentWeave = freshPlayer.Weave.Current;

                // --- PRE-FIGHT flee check ---
                {
                    var preEnemies = encounter.Combatants.Where(c => !c.IsPlayerSide && !c.IsDefeated).ToList();
                    var prePc = encounter.Combatants
                        .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);
                    if (prePc is not null && preEnemies.Count > 0)
                    {
                        int totalEnemyHp        = preEnemies.Sum(e => e.CurrentHp);
                        int playerDamagePerRound = 20;
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

                var initDto = CombatHelpers.BuildCombatUpdateDto(encounter, dangerLevel: dangerLevel);
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("CombatUpdate", initDto, farmCt);

                var safety = 0;
                while (encounter.State == EncounterState.InProgress && safety++ < 50)
                {
                    var actor = encounter.CurrentActor;
                    if (actor is null) break;

                    string? actionText = null;

                    if (actor.IsPlayerSide)
                    {
                        var livingEnemies = encounter.Combatants
                            .Where(c => !c.IsPlayerSide && !c.IsDefeated)
                            .ToList();
                        if (livingEnemies.Count == 0) break;

                        var playerCombatant = encounter.Combatants
                            .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);

                        // 1. Heal check
                        if (actor.CombatantType == CombatantType.Player && playerCombatant is not null)
                        {
                            float hpPct = (float)playerCombatant.CurrentHp / playerCombatant.MaxHp;
                            var healAbility = actor.Abilities
                                .FirstOrDefault(a => a.Category == AbilityCategory.Heal);

                            if (hpPct < 0.4f && healAbility is not null && currentWeave >= healAbility.WeaveCost)
                            {
                                currentWeave -= healAbility.WeaveCost;
                                var (_, healNarr, _) = await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, healAbility.Name, actor.Id, farmCt);
                                actionText = healNarr;
                                encounter = combatSvc.GetEncounter(encounter.Id) ?? encounter;
                                var healDto = CombatHelpers.BuildCombatUpdateDto(encounter, actionText, dangerLevel);
                                await hubContext.Clients.Group(playerId.ToString()).SendAsync("CombatUpdate", healDto, farmCt);
                                await Task.Delay(800, farmCt);
                                continue;
                            }
                        }

                        // 2. Target priority: kill weakest enemy first
                        var target = livingEnemies.OrderBy(e => e.CurrentHp).First();

                        // 3. Best attack ability
                        var attackAbilities = actor.Abilities
                            .Where(a => a.Category == AbilityCategory.Attack && currentWeave >= a.WeaveCost)
                            .ToList();

                        CombatAbility chosenAbility;
                        if (attackAbilities.Count == 0)
                        {
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
                        var (_, atkNarr, _) = await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, chosenAbility.Name, target.Id, farmCt);
                        actionText = atkNarr;
                    }
                    else
                    {
                        var abilities = actor.Abilities.Where(a => a.Category == AbilityCategory.Attack).ToList();
                        if (abilities.Count == 0) break;
                        var ability = abilities[Random.Shared.Next(abilities.Count)];
                        var allies = encounter.Combatants.Where(c => c.IsPlayerSide && !c.IsDefeated).ToList();
                        if (allies.Count == 0) break;
                        var tgt = allies[Random.Shared.Next(allies.Count)];
                        var (_, eNarr, _) = await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, ability.Name, tgt.Id, farmCt);
                        actionText = eNarr;
                    }

                    encounter = combatSvc.GetEncounter(encounter.Id) ?? encounter;

                    var actionDto = CombatHelpers.BuildCombatUpdateDto(encounter, actionText, dangerLevel);
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("CombatUpdate", actionDto, farmCt);
                    await Task.Delay(800, farmCt);
                }

                // Broadcast final encounter state
                var finalDto = CombatHelpers.BuildCombatUpdateDto(encounter, dangerLevel: dangerLevel);
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("CombatUpdate", finalDto, farmCt);

                if (encounter.State == EncounterState.Defeat)
                {
                    autoFarmService.RecordEncounterOutcome(playerId, FarmEncounterOutcome.Defeat, dangerLevel);

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
                            deposited = session.ItemsDeposited,
                            quests    = session.QuestsCompleted
                        }, serverCt);
                    return;
                }

                if (encounter.State == EncounterState.Fled)
                {
                    autoFarmService.RecordEncounterOutcome(playerId, FarmEncounterOutcome.Fled, dangerLevel);
                    int newCap = autoFarmService.GetSafeDangerCap(playerId, player.Level);

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
                            var healer = ConsumableHelper.FindBest(postInventory, ConsumableHelper.HealingPriority);
                            if (healer is not null)
                            {
                                var msg = await ConsumableHelper.ApplyAndConsumeAsync(winPlayer, healer, playerRepo, itemRepo, farmCt);
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
                            var weaveCons = ConsumableHelper.FindBest(postInventory2, ConsumableHelper.WeavePriority);
                            if (weaveCons is not null)
                            {
                                var msg = await ConsumableHelper.ApplyAndConsumeAsync(winPlayer, weaveCons, playerRepo, itemRepo, farmCt);
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

                    // Companion usage — 10 points per combat
                    await using var compScope = scopeFactory.CreateAsyncScope();
                    var compHelpers = compScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                    await compHelpers.UpdateCompanionUsageAsync(playerId, 10, farmCt);

                    // Companion capture — 25% per defeated enemy
                    await using var captureScope = scopeFactory.CreateAsyncScope();
                    var captureHelpers = captureScope.ServiceProvider.GetRequiredService<CombatHelpers>();
                    await captureHelpers.TryCaptureCompanionAsync(playerId, encounter, farmCt);

                    // Loot — check inventory full before rolling
                    var currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    var lootPlayer = winPlayer ?? freshPlayer;

                    if (lootPlayer is not null && currentItems.Count >= lootPlayer.MaxInventorySlots)
                    {
                        autoFarmService.SetState(playerId, "depositing");
                        await BroadcastStatusAsync(playerId, session, "depositing", biome, dangerLevel, farmCt);
                        await AutoDepositAndReturnAsync(playerId, newPos, playerRepo, itemRepo, homesteadRepo, session, farmCt);
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
                        // Diagnostic trace — fires on every auto-farm loot drop
                        if (lootResult.Item is not null)
                        {
                            var hasSlot = lootResult.Item.Slot != Domain.Enums.EquipmentSlot.None
                                       && lootPlayer?.GetEquipped(lootResult.Item.Slot) is null;
                            logger.LogInformation(
                                "Loot: {Name} Slot={Slot} PlayerHasSlot={HasSlot} AutoSalvaged={AutoSalvaged}",
                                lootResult.Item.Name, lootResult.Item.Slot, hasSlot, lootResult.AutoSalvaged);
                        }

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
                        {
                            var droppedItem = lootResult.Item;

                            // Auto-equip: if the slot is empty or new item is an upgrade, equip immediately
                            if (droppedItem.Slot != Domain.Enums.EquipmentSlot.None)
                            {
                                var equipPlayer = await playerRepo.GetByIdAsync(playerId, farmCt);
                                if (equipPlayer is not null)
                                {
                                    var itemSlot = droppedItem.Slot;
                                    var currentEquippedId = equipPlayer.GetEquipped(itemSlot);

                                    if (currentEquippedId is null)
                                    {
                                        logger.LogInformation(
                                            "[Auto-farm] Auto-equip check: {ItemName} W{W} (eff W{EffW}) Slot={Slot} | Current equipped: (empty) | Decision: equip",
                                            droppedItem.DisplayName, droppedItem.Workmanship.Value,
                                            droppedItem.Workmanship.Value + droppedItem.Imbues.Count, itemSlot);

                                        // Slot is empty — auto-equip
                                        equipPlayer.Equip(itemSlot, droppedItem.Id);
                                        await playerRepo.UpdateAsync(equipPlayer, farmCt);
                                        await hubContext.Clients
                                            .Group(playerId.ToString())
                                            .SendAsync("GameMessage", new
                                            {
                                                timestamp = DateTime.UtcNow.ToString("O"),
                                                category  = farmMsgCategory,
                                                text      = $"[Auto-farm] You equip the {droppedItem.DisplayName}."
                                            }, farmCt);
                                    }
                                    else
                                    {
                                        // Compare effective workmanship (raw W + imbue count per item)
                                        var currentEquipped = await itemRepo.GetByIdAsync(currentEquippedId.Value, farmCt);
                                        var newEffW     = droppedItem.Workmanship.Value + droppedItem.Imbues.Count;
                                        var currentEffW = currentEquipped is not null
                                            ? currentEquipped.Workmanship.Value + currentEquipped.Imbues.Count
                                            : 0;

                                        logger.LogInformation(
                                            "[Auto-farm] Auto-equip check: {ItemName} W{W} (eff W{EffW}) Slot={Slot} | Current equipped: {CurrentName} W{CurrentW} (eff W{CurrentEffW}) Locked={Locked} | Decision: {Decision}",
                                            droppedItem.DisplayName, droppedItem.Workmanship.Value, newEffW, itemSlot,
                                            currentEquipped?.DisplayName ?? "unknown", currentEquipped?.Workmanship.Value ?? 0, currentEffW,
                                            currentEquipped?.IsLocked ?? false,
                                            currentEquipped is null ? "skip/no-current"
                                                : currentEquipped.IsLocked ? "skip/locked"
                                                : newEffW > currentEffW ? "swap"
                                                : "skip/not-better");

                                        if (currentEquipped is not null
                                            && newEffW > currentEffW
                                            && !currentEquipped.IsLocked)
                                        {
                                            equipPlayer.Equip(itemSlot, droppedItem.Id);
                                            await playerRepo.UpdateAsync(equipPlayer, farmCt);
                                            await hubContext.Clients
                                                .Group(playerId.ToString())
                                                .SendAsync("GameMessage", new
                                                {
                                                    timestamp = DateTime.UtcNow.ToString("O"),
                                                    category  = farmMsgCategory,
                                                    text      = $"[Auto-farm] You swap your {currentEquipped.DisplayName} W{currentEquipped.Workmanship.Value}{(currentEquipped.Imbues.Count > 0 ? $"+{currentEquipped.Imbues.Count}i" : "")} for {droppedItem.DisplayName} W{droppedItem.Workmanship.Value}{(droppedItem.Imbues.Count > 0 ? $"+{droppedItem.Imbues.Count}i" : "")}. Much better."
                                                }, farmCt);
                                        }
                                    }
                                }
                            }

                            await hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("LootDropped", new
                                {
                                    droppedItem.Id,
                                    Name = droppedItem.DisplayName,
                                    droppedItem.Description,
                                    Workmanship = droppedItem.Workmanship.Value,
                                    Category = droppedItem.Category.ToString(),
                                    Slot = droppedItem.Slot.ToString()
                                }, farmCt);
                        }
                    }

                    // --- AUTO-COMPLETE QUESTS (kill / gather quests after each victory) ---
                    await using (var questCompScope = scopeFactory.CreateAsyncScope())
                    {
                        var questCompSvc = questCompScope.ServiceProvider.GetRequiredService<QuestAutoCompleteService>();
                        var questsCompletedThisFight = await questCompSvc.TryAutoCompleteQuestsAsync(playerId, farmCt);
                        if (questsCompletedThisFight > 0)
                        {
                            for (int _q = 0; _q < questsCompletedThisFight; _q++)
                                autoFarmService.RecordQuestComplete(playerId);
                            session.QuestsCompleted = autoFarmService.GetSession(playerId)?.QuestsCompleted ?? session.QuestsCompleted;
                        }
                    }

                    // --- REST between fights ---
                    autoFarmService.SetState(playerId, "resting");
                    await BroadcastStatusAsync(playerId, session, "resting", biome, dangerLevel, farmCt);

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

                    // Running summary
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "system",
                            text = $"Auto-farm: Kills: {session.Kills}, Items: {session.ItemsFound}, Quests: {session.QuestsCompleted} | salvaged: {session.ItemsAutoSalvaged}, deposited: {session.ItemsDeposited}."
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
            var quests    = finalSession?.QuestsCompleted   ?? session.QuestsCompleted;
            autoFarmService.EndSession(playerId);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = $"Auto-farm stopped: Kills: {kills}, Items: {items}, Quests: {quests} | salvaged: {salvaged}, deposited: {deposited}."
                }, serverCt);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("AutoFarmStatus", new
                {
                    active    = false,
                    kills,
                    items,
                    salvaged,
                    deposited,
                    quests
                }, serverCt);
        }
    }
}
