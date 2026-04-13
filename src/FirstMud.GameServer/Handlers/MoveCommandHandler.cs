using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class MoveCommandHandler(
    IPlayerRepository playerRepository,
    IZoneRepository zoneRepository,
    IItemRepository itemRepository,
    ICompanionRepository companionRepository,
    CombatService combatService,
    CombatHelpers combatHelpers,
    LootService lootService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<MoveCommand>
{
    // In-memory set of treasure tiles already claimed this session (playerId → set of "x,y")
    // Prevents the same tile rewarding a player multiple times per server process.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, HashSet<string>>
        _claimedTreasures = new();

    public async Task<CommandResult> HandleAsync(MoveCommand cmd, CancellationToken ct)
    {
        if (Math.Abs(cmd.DeltaX) > 1 || Math.Abs(cmd.DeltaY) > 1)
            return new CommandResult(false, "Move delta must be within ±1 per axis.");

        if (cmd.DeltaX == 0 && cmd.DeltaY == 0)
            return new CommandResult(false, "Move delta cannot be (0, 0).");

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var current = player.Position;
        var newPosition = new Position(
            current.World,
            current.ZoneId,
            current.X + cmd.DeltaX,
            current.Y + cmd.DeltaY);

        // Award 1 XP per new tile moved (every move counts as exploration)
        var milestone = player.RecordTileDiscovery();
        player.GainExperience(1);

        player.Move(newPosition);
        await playerRepository.UpdateAsync(player, ct);

        var positionPayload = new
        {
            player.Id,
            newPosition.X,
            newPosition.Y,
            newPosition.ZoneId,
            World = newPosition.World.ToString()
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", positionPayload, ct);

        // Broadcast exploration milestone message if one was reached
        if (milestone is not null)
        {
            await notificationService.SendMessageAsync(
                cmd.PlayerId,
                "wyrd",
                $"{milestone.Label} (+{milestone.XpAwarded} XP)",
                ct);
        }

        // Hidden treasure check: 1% of wilderness tiles (deterministic hash of x,y)
        var tx = newPosition.X;
        var ty = newPosition.Y;
        var treasureHash = ((tx * 48271 + ty * 91283) & 0x7FFFFFFF) % 100;
        if (treasureHash == 0)
        {
            await TryAwardHiddenTreasureAsync(cmd.PlayerId, player, tx, ty, ct);
        }

        await TryTriggerEncounterAsync(cmd.PlayerId, newPosition, ct);

        return new CommandResult(true, $"Moved to ({newPosition.X}, {newPosition.Y}).", positionPayload);
    }

    private async Task TryTriggerEncounterAsync(Guid playerId, Position pos, CancellationToken ct)
    {
        var zones = await zoneRepository.GetByWorldAsync(pos.World, ct);
        var nearbyZone = ZoneProximity.FindNearby(zones, pos.X, pos.Y);

        int dangerLevel;
        int chance;
        string biome;

        if (nearbyZone != null)
        {
            // Named zone: use zone's own danger level and biome (existing behaviour).
            if (nearbyZone.DangerLevel <= 0) return;
            dangerLevel = nearbyZone.DangerLevel;
            chance = Math.Min(dangerLevel * 8, 80);
            biome = CombatHelpers.GetBiome(nearbyZone);
        }
        else
        {
            // Wilderness: compute danger from biome + distance from world centre.
            biome = CombatHelpers.GuessWildernessBiome(pos.X, pos.Y);
            dangerLevel = CombatHelpers.GetWildernessDanger(pos.X, pos.Y, biome);

            if (biome == "path")
            {
                // Roads are mostly safe — occasional bandit.
                chance = 2;
            }
            else
            {
                if (dangerLevel <= 0) return;
                chance = Math.Min(dangerLevel * 4, 40);
            }
        }

        if (Random.Shared.Next(100) >= chance) return;

        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var activeCompanions = new List<Companion>();
        foreach (var compId in player.ActiveCompanionIds)
        {
            var comp = await companionRepository.GetByIdAsync(compId, ct);
            if (comp != null && !comp.IsPermanentlyGone)
                activeCompanions.Add(comp);
        }

        int partySize = 1 + activeCompanions.Count;
        var monsters = combatHelpers.BuildMonsterPack(dangerLevel, player.Level, biome, partySize);

        // Aggro check: high-level players in low-level zones don't get bothered
        var avgMonsterLevel = monsters.Count > 0
            ? (int)Math.Round(monsters.Average(m => (double)m.Level))
            : 1;
        var levelGap = player.Level - avgMonsterLevel;

        if (levelGap >= 4)
        {
            await notificationService.SendMessageAsync(
                playerId, "combat",
                "The creatures here sense your power and keep their distance.",
                ct);
            return;
        }

        if (levelGap == 3 && Random.Shared.Next(2) == 0)
        {
            var fleeingName = monsters[0].Name;
            await notificationService.SendMessageAsync(
                playerId, "combat",
                $"A {fleeingName} scurries away at the sight of you.",
                ct);
            return;
        }

        // Survivability gate: if total enemy HP exceeds 3× party HP, the fight is
        // (almost certainly) unwinnable at this stage — skip the encounter entirely.
        //
        // NOTE: monsters.Sum(m => m.Hp) already reflects MonsterFactory.ScaleMonster
        // post-scaling HP (danger + isBoss). Previously this value was multiplied
        // again by (1 + danger*0.4), which double-counted scaling and therefore
        // only fired in extreme cases — contributing to the danger-6 TPK.
        int totalEnemyHp = monsters.Sum(m => m.Hp);
        // Use player HP as the party HP baseline; companions don't expose a CurrentHp
        // field, so scale by party size as a rough proxy.
        int partyHp = player.CurrentHp * partySize;

        if (totalEnemyHp > partyHp * 3)
        {
            await notificationService.SendMessageAsync(
                playerId,
                "system",
                $"Your party senses overwhelming danger and avoids the encounter. (Danger {dangerLevel})",
                ct);
            return;
        }

        // Pre-combat difficulty estimate via deterministic sim. If the party's
        // estimated win rate is dire, warn loudly and skip the encounter so the
        // player isn't dropped into an unwinnable fight with no agency.
        //
        // Threshold: < 20% estimated win rate = auto-avoid; the player will see
        // the warning and can reposition / swap companions before re-engaging.
        // Between 20% and 40% = proceed but surface a "punishing" warning.
        var preCombatWinRate = EstimateWinRate(player, activeCompanions, monsters);
        if (preCombatWinRate < 0.20)
        {
            await notificationService.SendMessageAsync(
                playerId,
                "system",
                $"Your party hesitates — this encounter looks unwinnable (est. {preCombatWinRate:P0} win rate). You back away. (Danger {dangerLevel})",
                ct);
            return;
        }

        if (preCombatWinRate < 0.40)
        {
            await notificationService.SendMessageAsync(
                playerId,
                "combat",
                $"Warning: this encounter looks punishing (est. {preCombatWinRate:P0} win rate). Consider fleeing early. (Danger {dangerLevel})",
                ct);
        }

        var encounter = await combatService.StartEncounterAsync(
            playerId, Guid.NewGuid(), player, activeCompanions, monsters, ct: ct, dangerLevel: dangerLevel);

        var combatCategory = CombatHelpers.GetCombatDifficultyCategory(avgMonsterLevel, player.Level);

        var monsterNames = string.Join(", ", monsters.Select(m => m.Name));
        var narration = CombatHelpers.GetBiomeNarration(biome, monsterNames);
        await notificationService.SendMessageAsync(
            playerId,
            combatCategory,
            narration,
            ct);

        await combatHelpers.ProcessEnemyTurnsAsync(playerId, encounter, ct, dangerLevel);

        var dto = CombatHelpers.BuildCombatUpdateDto(encounter, dangerLevel: dangerLevel);
        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);
    }

    private async Task TryAwardHiddenTreasureAsync(
        Guid playerId,
        Player player,
        int x, int y,
        CancellationToken ct)
    {
        var tileKey = $"{x},{y}";
        var claimed = _claimedTreasures.GetOrAdd(playerId, _ => []);

        lock (claimed)
        {
            if (!claimed.Add(tileKey))
                return; // Already claimed this tile this session
        }

        await notificationService.SendMessageAsync(
            playerId,
            "loot-rare",
            "You discover a hidden cache buried beneath the soil!",
            ct);

        // Drop a guaranteed rare item (W4–W7) as the treasure
        var biome = CombatHelpers.GuessWildernessBiome(x, y);
        var dangerLevel = Math.Max(4, CombatHelpers.GetWildernessDanger(x, y, biome));

        var items = await itemRepository.GetByOwnerAsync(playerId, ct);
        var inventoryCount = items.Count;

        var lootResult = await lootService.RollLootDropAsync(
            dangerLevel: dangerLevel,
            ownerId: playerId,
            originWorld: player.Position.World,
            currentInventoryCount: inventoryCount,
            maxInventorySlots: player.MaxInventorySlots,
            ct: ct,
            player: player,
            zoneName: null,
            isAutoFarm: false);

        if (lootResult.Dropped && lootResult.Item is not null && !lootResult.AutoSalvaged)
        {
            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("LootDropped", new
                {
                    lootResult.Item.Id,
                    Name = lootResult.Item.Name,
                    Description = lootResult.Item.Description,
                    Workmanship = lootResult.Item.Workmanship.Value,
                    Category = lootResult.Item.Category.ToString(),
                    Slot = lootResult.Item.Slot.ToString(),
                }, ct);
        }
    }

    /// <summary>
    /// Quick Monte-Carlo estimate of the party's win chance against the spawned
    /// pack, using the deterministic <see cref="CombatSimulationService"/>. Used
    /// as a pre-combat UX gate: if the estimate is dire, we warn the player and
    /// skip the fight so they don't get TPK'd with no agency.
    ///
    /// Kept small (32 rolls) to keep per-move latency under ~30ms.
    /// </summary>
    private static double EstimateWinRate(
        Player player,
        IReadOnlyList<Companion> activeCompanions,
        IReadOnlyList<FirstMud.Application.MonsterTemplate> monsters)
    {
        const int rolls = 32;
        var element = player.PrimaryElement == default ? FirstMud.Domain.Enums.MagicElement.Aether : player.PrimaryElement;
        var party = activeCompanions
            .Select(c => new CombatSimulationService.PartyMember(c.Type, c.Element, c.CurrentLayer, c.Level))
            .ToList();

        int wins = 0;
        int successfulRolls = 0;
        for (int i = 0; i < rolls; i++)
        {
            try
            {
                var rng = new Random(unchecked(player.Id.GetHashCode() * 1_000_003 + i));
                var sim = new CombatSimulationService(rng);
                var enc = sim.BuildEncounter(element, player.Level, party, monsters);
                var result = sim.Run(enc, maxRounds: 40);
                if (result.Outcome == CombatSimulationService.Outcome.Victory) wins++;
                successfulRolls++;
            }
            catch
            {
                // The pre-combat win-rate preview must never bubble an exception
                // up to the move command — a sim bug would otherwise convert
                // every wilderness move into a "Command failed" on the client
                // and appear as a repeated error storm to the player. Swallow
                // and skip; if *every* roll throws we fall back to a neutral
                // 0.5 estimate so the move proceeds past the <20% auto-avoid
                // gate and the player still gets into combat.
            }
        }
        if (successfulRolls == 0) return 0.5;
        return (double)wins / successfulRolls;
    }
}
