using FirstMud.Application;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.Services;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Dtos;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Shared combat utility methods extracted from CommandDispatcher.
/// Injected as a scoped service so all combat/loot/XP handlers share one copy.
/// </summary>
public class CombatHelpers(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    ICompanionRepository companionRepository,
    IZoneRepository zoneRepository,
    CombatService combatService,
    LootService lootService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext,
    ILogger<CombatHelpers> logger)
{
    // -------------------------------------------------------------------------
    // Enemy auto-turn processing
    // -------------------------------------------------------------------------

    // Tracks which companion IDs have already used their one-time buff this encounter.
    // Key = encounterId, Value = set of companionSourceEntityIds that have buffed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, HashSet<Guid>> _companionBuffUsed = new();

    /// <summary>
    /// Automatically runs any pending enemy AND companion turns until the human player
    /// is next or the encounter ends.
    /// Enemies attack randomly. Companions use smart AI: heal low-HP allies first,
    /// then buff once per encounter, then use their strongest attack.
    /// </summary>
    public async Task ProcessEnemyTurnsAsync(Guid playerId, Encounter encounter, CancellationToken ct)
    {
        var buffUsed = _companionBuffUsed.GetOrAdd(encounter.Id, _ => []);

        var safety = 0;
        while (encounter.State == EncounterState.InProgress && safety++ < 30)
        {
            var actor = encounter.CurrentActor;
            if (actor is null) break;

            // Stop when it's the human player's turn
            if (actor.IsPlayerSide && actor.CombatantType == CombatantType.Player) break;

            // ---- COMPANION TURN (auto-AI) ----
            if (actor.IsPlayerSide && actor.CombatantType == CombatantType.Companion)
            {
                var companionSnapshot = encounter.Combatants
                    .Select(c => (c.Id, c.CurrentHp, c.MaxHp, c.IsPlayerSide))
                    .ToList();

                var hasBuffed = buffUsed.Contains(actor.SourceEntityId);
                var (chosenAbility, targetAlly) = CompanionAbilityFactory.SelectBestAction(
                    actor.Abilities, companionSnapshot, hasBuffed);

                if (chosenAbility is null)
                {
                    // No valid ability — skip turn gracefully
                    var (_, _, _) = await combatService.ExecuteActionAsync(
                        encounter.Id, actor.Id,
                        actor.Abilities.FirstOrDefault()?.Name ?? "Strike",
                        null, ct);
                    continue;
                }

                if (chosenAbility.Category == AbilityCategory.Buff)
                    buffUsed.Add(actor.SourceEntityId);

                Guid? targetId = null;
                string targetName;

                if (targetAlly)
                {
                    // Pick the most damaged ally (lowest HP%)
                    var ally = encounter.Combatants
                        .Where(c => c.IsPlayerSide && !c.IsDefeated)
                        .OrderBy(c => (float)c.CurrentHp / Math.Max(c.MaxHp, 1))
                        .FirstOrDefault();

                    targetId = ally?.Id;
                    targetName = ally?.Name ?? "ally";
                }
                else
                {
                    // Pick first living enemy
                    var enemy = encounter.Combatants
                        .Where(c => !c.IsPlayerSide && !c.IsDefeated)
                        .FirstOrDefault();

                    targetId = enemy?.Id;
                    targetName = enemy?.Name ?? "enemy";
                }

                var (cSuccess, cNarration, _) = await combatService.ExecuteActionAsync(
                    encounter.Id, actor.Id, chosenAbility.Name, targetId, ct);
                if (!cSuccess) break;

                string narrative;
                if (chosenAbility.Category == AbilityCategory.Buff)
                {
                    narrative = $"{actor.Name} uses {chosenAbility.Name}! The party is bolstered.";
                }
                else if (chosenAbility.Category == AbilityCategory.Debuff)
                {
                    narrative = $"{actor.Name} uses {chosenAbility.Name} on {targetName}! They falter.";
                }
                else if (!string.IsNullOrEmpty(cNarration))
                {
                    // Use the narration returned by ExecuteActionAsync (hit/miss/dodge/crit/heal)
                    narrative = cNarration;
                }
                else
                {
                    narrative = $"{actor.Name} acts.";
                }

                await notificationService.SendMessageAsync(playerId, "combat", narrative, ct);
                continue;
            }

            // ---- ENEMY TURN ----
            var enemyAbilities = actor.Abilities.Where(a => a.Category == AbilityCategory.Attack).ToList();
            if (enemyAbilities.Count == 0) break;
            var enemyAbility = enemyAbilities[Random.Shared.Next(enemyAbilities.Count)];

            var targets = encounter.Combatants
                .Where(c => c.IsPlayerSide && !c.IsDefeated)
                .ToList();
            if (targets.Count == 0) break;
            var target = targets[Random.Shared.Next(targets.Count)];

            var (eSuccess, eNarration, _) = await combatService.ExecuteActionAsync(
                encounter.Id, actor.Id, enemyAbility.Name, target.Id, ct);
            if (!eSuccess) break;

            var eNarrText = !string.IsNullOrEmpty(eNarration)
                ? eNarration
                : $"{actor.Name} uses {enemyAbility.Name} on {target.Name}.";

            await notificationService.SendMessageAsync(playerId, "combat", eNarrText, ct);
        }

        // Clean up buff tracking when encounter ends
        if (encounter.State != EncounterState.InProgress)
            _companionBuffUsed.TryRemove(encounter.Id, out _);
    }

    // -------------------------------------------------------------------------
    // XP + level-up helpers
    // -------------------------------------------------------------------------

    public async Task AwardCombatXpAsync(Guid playerId, Encounter encounter, CancellationToken ct, bool isAutoFarm = false)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var defeatedEnemies = encounter.Combatants
            .Where(c => !c.IsPlayerSide && c.IsDefeated)
            .ToList();

        if (defeatedEnemies.Count == 0) return;

        // Determine difficulty category from avg enemy level vs player level
        var avgMonsterLevel = defeatedEnemies.Count > 0
            ? (int)Math.Round(defeatedEnemies.Average(e => (double)e.Level))
            : 1;
        var combatCategory = GetCombatDifficultyCategory(avgMonsterLevel, player.Level);

        var breakdown = new List<string>();
        var totalXp = 0;

        foreach (var enemy in defeatedEnemies)
        {
            var levelDiff = enemy.Level - player.Level;
            var multiplier = levelDiff switch
            {
                >= 2  => 1.00f,
                1     => 0.90f,
                0     => 0.75f,
                -1    => 0.50f,
                -2    => 0.25f,
                -3    => 0.10f,
                _     => 0.00f,   // <= -4: grey mob
            };

            const int baseXp = 20;
            var xpGained = (int)(baseXp * multiplier);
            totalXp += xpGained;

            var label = multiplier == 0f ? $"{enemy.Name}: 0 XP [grey]" : $"{enemy.Name}: {xpGained} XP";
            breakdown.Add(label);
        }

        // Auto-farm degrades XP to 25% of manual rate
        string autoFarmSuffix = string.Empty;
        if (isAutoFarm && totalXp > 0)
        {
            totalXp = Math.Max(1, (int)(totalXp * 0.25f));
            autoFarmSuffix = " (auto-farm: 25% rate)";
        }

        if (totalXp > 0)
        {
            player.GainExperience(totalXp);
            await playerRepository.UpdateAsync(player, ct);
            logger.LogDebug("Awarded {Xp} XP to player {PlayerId} (autoFarm={IsAutoFarm})", totalXp, playerId, isAutoFarm);
        }

        var detail = string.Join(", ", breakdown);
        await notificationService.SendMessageAsync(
            playerId, combatCategory,
            $"You gained {totalXp} experience{autoFarmSuffix}! ({detail})",
            ct);

        await BroadcastLevelUpEventsAsync(playerId, player, ct);
        player.ClearDomainEvents();
    }

    public async Task BroadcastLevelUpEventsAsync(Guid playerId, Player player, CancellationToken ct)
    {
        foreach (var domainEvent in player.DomainEvents)
        {
            if (domainEvent is PlayerLeveledUpEvent levelEvent)
            {
                await hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("PlayerLeveledUp", new
                    {
                        PlayerId = levelEvent.PlayerId,
                        NewLevel = levelEvent.NewLevel,
                        Message = $"You reached level {levelEvent.NewLevel}!"
                    }, ct);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Loot rarity helpers
    // -------------------------------------------------------------------------

    public static string GetLootCategory(Item item)
    {
        var category = item.Workmanship.Value switch
        {
            <= 2 => "loot-common",
            <= 3 => "loot-uncommon",
            <= 5 => "loot-rare",
            <= 7 => "loot-epic",
            _    => "loot-legendary"
        };

        if (item.Imbues.Count > 0)
            category = BumpLootRarity(category);

        return category;
    }

    private static string BumpLootRarity(string category) => category switch
    {
        "loot-common"    => "loot-uncommon",
        "loot-uncommon"  => "loot-rare",
        "loot-rare"      => "loot-epic",
        "loot-epic"      => "loot-legendary",
        _                => category
    };

    public static string GetSalvageCategory(int workmanship) => workmanship switch
    {
        <= 2 => "salvage-common",
        <= 4 => "salvage-uncommon",
        _    => "salvage-rare"
    };

    // -------------------------------------------------------------------------
    // Loot rolling
    // -------------------------------------------------------------------------

    public async Task TryRollLootAsync(Guid playerId, Guid zoneId, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        // Determine danger level from zone lookup, fall back to default
        var zones = await zoneRepository.GetByWorldAsync(Domain.Enums.WorldId.Aeldran, ct);
        var zone = zones.FirstOrDefault(z => z.Id == zoneId);
        var dangerLevel = zone?.DangerLevel ?? 2;

        var ownedItems = await itemRepository.GetByOwnerAsync(playerId, ct);

        var result = await lootService.RollLootDropAsync(
            dangerLevel,
            playerId,
            player.Position.World,
            ownedItems.Count,
            player.MaxInventorySlots,
            ct,
            player,
            zone?.Name);

        if (!result.Dropped)
        {
            if (!string.IsNullOrEmpty(result.Message))
                await notificationService.SendMessageAsync(playerId, "loot", result.Message, ct);
            return;
        }

        var item = result.Item!;
        var lootCategory = GetLootCategory(item);
        await notificationService.SendMessageAsync(playerId, lootCategory, result.Message, ct);

        // Auto-equip logic: only for items with a real slot
        if (item.Slot != Domain.Enums.EquipmentSlot.None)
        {
            var slot = item.Slot;
            var currentEquippedId = player.GetEquipped(slot);

            if (currentEquippedId is null)
            {
                // Slot is empty — auto-equip
                player.Equip(slot, item.Id);
                await playerRepository.UpdateAsync(player, ct);
                await notificationService.SendMessageAsync(playerId, lootCategory, $"You equip the {item.Name}.", ct);
            }
            else
            {
                // Compare workmanship
                var currentEquipped = await itemRepository.GetByIdAsync(currentEquippedId.Value, ct);
                if (currentEquipped is not null
                    && item.Workmanship.Value > currentEquipped.Workmanship.Value
                    && !currentEquipped.IsLocked)
                {
                    // Swap: new item is better and old item is not locked
                    player.Equip(slot, item.Id);
                    await playerRepository.UpdateAsync(player, ct);
                    await notificationService.SendMessageAsync(playerId, lootCategory,
                        $"You swap your {currentEquipped.Name} W{currentEquipped.Workmanship.Value} for {item.Name} W{item.Workmanship.Value}. Much better.", ct);
                }
            }
        }

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("LootDropped", new
            {
                item.Id,
                item.Name,
                item.Description,
                Workmanship = item.Workmanship.Value,
                Category = item.Category.ToString(),
                Slot = item.Slot.ToString()
            }, ct);
    }

    // -------------------------------------------------------------------------
    // Companion capture
    // -------------------------------------------------------------------------

    public async Task TryCaptureCompanionAsync(Guid playerId, Encounter encounter, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var defeatedEnemies = encounter.Combatants
            .Where(c => !c.IsPlayerSide && c.IsDefeated)
            .ToList();

        foreach (var enemy in defeatedEnemies)
        {
            if (Random.Shared.Next(100) >= 15) continue;

            // Generate a flavourful name for the captured creature
            var companionName = GenerateCapturedName(enemy.Element);

            var companion = Companion.Create(
                playerId,
                companionName,
                CompanionType.CapturedMonster,
                enemy.Element);

            // Captured monsters always start at Layer 1
            await companionRepository.AddAsync(companion, ct);

            // Auto-activate if there's a free slot; otherwise just add to roster
            var activated = player.TryAddActiveCompanion(companion.Id);
            if (activated)
            {
                companion.SetActive(true);
                await companionRepository.UpdateAsync(companion, ct);
            }
            await playerRepository.UpdateAsync(player, ct);

            var slotNote = activated
                ? "It has joined your active party."
                : "Your party is full — it waits in your roster. Press [B] to manage companions.";

            await notificationService.SendMessageAsync(playerId, "system",
                $"You captured a {enemy.Name}! Named it '{companionName}'. " +
                $"[Layer 1 {enemy.Element} CapturedMonster] {slotNote}", ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("CompanionCaptured", new
                {
                    CompanionId = companion.Id,
                    Name = companionName,
                    OriginalMonsterName = enemy.Name,
                    Element = companion.Element.ToString(),
                    Type = companion.Type.ToString(),
                    Layer = companion.CurrentLayer,
                    IsActive = companion.IsActive
                }, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Companion usage / leveling after combat
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records usage points for every active companion that participated in the encounter,
    /// persists the update, and broadcasts a layer-up message if TryAdvanceLayer fires.
    /// Call this after every combat resolution (Victory, Fled, or Defeat).
    /// </summary>
    public async Task UpdateCompanionUsageAsync(Guid playerId, int usagePoints, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        foreach (var companionId in player.ActiveCompanionIds)
        {
            var companion = await companionRepository.GetByIdAsync(companionId, ct);
            if (companion is null || companion.IsPermanentlyGone) continue;

            var layerBefore = companion.CurrentLayer;
            companion.RecordUsage(usagePoints);
            await companionRepository.UpdateAsync(companion, ct);

            // Broadcast layer-up events raised by RecordUsage → TryAdvanceLayer
            foreach (var evt in companion.DomainEvents)
            {
                if (evt is CompanionLayerUnlockedEvent layerEvt)
                {
                    await notificationService.SendMessageAsync(playerId, "system",
                        $"{companion.Name} has advanced to Layer {layerEvt.NewLayer}! New abilities unlocked.",
                        ct);

                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("CompanionLayerUp", new
                        {
                            CompanionId = companion.Id,
                            companion.Name,
                            NewLayer    = layerEvt.NewLayer,
                        }, ct);
                }
            }
            companion.ClearDomainEvents();

            logger.LogDebug(
                "Companion {Name} ({Id}) usage +{Points} → {Counter}/{Threshold} (Layer {Layer})",
                companion.Name, companion.Id, usagePoints,
                companion.UsageCounter, companion.NextLayerThreshold, companion.CurrentLayer);
        }

        // Refresh the companion panel on the client
        await CompanionDtoHelpers.BroadcastCompanionListAsync(playerId, companionRepository, hubContext, ct);
    }

    /// <summary>
    /// Generates a flavourful name for a newly captured monster using element-themed syllables.
    /// </summary>
    private static string GenerateCapturedName(MagicElement element)
    {
        var prefixes = element switch
        {
            MagicElement.Fire   => new[] { "Blaze", "Cinder", "Ash", "Ember", "Scorch", "Smolder", "Char" },
            MagicElement.Water  => new[] { "Tide", "Mist", "Foam", "Eddy", "Brook", "Ripple", "Surge" },
            MagicElement.Earth  => new[] { "Stone", "Grit", "Dust", "Slab", "Mire", "Shale", "Pebble" },
            MagicElement.Air    => new[] { "Gust", "Drift", "Breeze", "Squall", "Wisp", "Zephyr", "Gale" },
            MagicElement.Aether => new[] { "Echo", "Shade", "Pale", "Veil", "Rune", "Omen", "Wyrd" },
            _                   => new[] { "Wild", "Feral", "Roam", "Stray", "Lost", "Spare", "Odd" },
        };

        var suffixes = new[] { "claw", "fang", "hide", "mane", "spike", "eye", "paw", "scale", "tail", "thorn" };

        var prefix = prefixes[Random.Shared.Next(prefixes.Length)];
        var suffix = suffixes[Random.Shared.Next(suffixes.Length)];
        return prefix + suffix;
    }

    // -------------------------------------------------------------------------
    // Biome detection
    // -------------------------------------------------------------------------

    public static string GetBiome(Zone? zone) => zone?.Name switch
    {
        "Caervorn Highlands" => "mountain",
        "The Thornwood"      => "forest",
        "Portmere (Compact)" => "plains",
        "Gravenmarsh"        => "swamp",
        "The Drowned Coast"  => "water",
        "The Ashen Reach"    => "desert",
        "Starting Road"      => "plains",
        "Gravenhold"         => "mountain",
        "The Maw Borderlands"=> "wyrd",
        _                    => "plains",
    };

    /// <summary>
    /// Returns the biome string for a given position.
    /// If a named zone is nearby, defers to GetBiome(zone).
    /// Otherwise approximates from proximity to known zone centres.
    /// </summary>
    public static string GetBiomeForPosition(Zone? nearbyZone, int x, int y)
    {
        if (nearbyZone != null) return GetBiome(nearbyZone);
        return GuessWildernessBiome(x, y);
    }

    /// <summary>
    /// Approximates a biome for a wilderness tile based on proximity to named zone centres.
    /// Uses Manhattan distance to the nearest thematic zone; falls back to plains.
    /// Zone centres match ZoneGridLayout.cs.
    /// </summary>
    public static string GuessWildernessBiome(int x, int y)
    {
        // (zoneCentreX, zoneCentreY, biome, influenceRadius)
        (int cx, int cy, string biome, int radius)[] zoneThemes =
        [
            (8,  3,  "mountain", 10),   // Caervorn Highlands
            (13, 5,  "forest",   10),   // The Thornwood
            (24, 13, "plains",    8),   // Portmere Compact
            (28, 9,  "swamp",    10),   // Gravenmarsh
            (32, 15, "water",    10),   // The Drowned Coast
            (34, 4,  "desert",   10),   // The Ashen Reach
            (20, 10, "plains",    6),   // Starting Road
            (26, 7,  "mountain",  8),   // Gravenhold
            (36, 18, "wyrd",     10),   // The Maw Borderlands
        ];

        string closestBiome = "plains";
        int closestDist = int.MaxValue;

        foreach (var (cx, cy, biome, radius) in zoneThemes)
        {
            int dist = Math.Abs(x - cx) + Math.Abs(y - cy);
            if (dist < radius && dist < closestDist)
            {
                closestDist = dist;
                closestBiome = biome;
            }
        }

        return closestBiome;
    }

    /// <summary>
    /// Computes the danger level for a wilderness tile (no named zone nearby).
    /// Combines Manhattan distance from the world centre (20, 10) with a biome modifier.
    /// Returns a value in [0, 10].
    /// </summary>
    public static int GetWildernessDanger(int x, int y, string biome)
    {
        const int worldCentreX = 20;
        const int worldCentreY = 10;
        int distFromCenter = Math.Abs(x - worldCentreX) + Math.Abs(y - worldCentreY);
        int wildernessBaseDanger = distFromCenter / 5;

        int biomeDanger = biome switch
        {
            "forest"      => 2,
            "denseForest" => 3,
            "mountain"    => 3,
            "snowMountain"=> 5,
            "swamp"       => 3,
            "water"       => 2,
            "desert"      => 3,
            "wyrd"        => 5,
            "plains" or "grassland" => 1,
            "path"        => 0,
            _             => 1,
        };

        return Math.Clamp(wildernessBaseDanger + biomeDanger, 0, 10);
    }

    public static string GetBiomeNarration(string biome, string monsterNames) => biome switch
    {
        "mountain" => $"A {monsterNames} emerges from behind a boulder!",
        "forest"   => $"{monsterNames} burst from the undergrowth!",
        "water"    => $"Something rises from the depths... {monsterNames}!",
        "desert"   => $"A {monsterNames} scuttles from beneath the dunes!",
        "swamp"    => $"A {monsterNames} materializes from the mist!",
        "wyrd"     => $"Reality tears open. A {monsterNames} steps through!",
        _          => $"Hostile creatures emerge! You face: {monsterNames}.",
    };

    /// <summary>
    /// Returns a message category string based on how hard the encounter is
    /// relative to the player. Used to colour combat messages in the client.
    /// </summary>
    public static string GetCombatDifficultyCategory(int averageMonsterLevel, int playerLevel) =>
        (averageMonsterLevel - playerLevel) switch
        {
            <= -4 => "combat-trivial",   // grey mob
            <= -2 => "combat-easy",      // green
            <= 0  => "combat-normal",    // yellow
            <= 2  => "combat-hard",      // orange
            _     => "combat-deadly",    // red
        };

    // -------------------------------------------------------------------------
    // Monster pack builder
    // -------------------------------------------------------------------------

    public static List<MonsterTemplate> BuildMonsterPack(int dangerLevel, int playerLevel = 1, string biome = "plains")
    {
        // ---- Shared ability definitions ----
        // Generic
        var claw      = new CombatAbility("Claw",          6,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var bite      = new CombatAbility("Bite",          8,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        // Mountain abilities
        var headbutt       = new CombatAbility("Headbutt",         7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var rockThrow      = new CombatAbility("Rock Throw",       9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var boulderThrow   = new CombatAbility("Boulder Throw",   14,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var regenerate     = new CombatAbility("Regenerate",      12,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Heal);
        var diveAttack     = new CombatAbility("Dive Attack",     16,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var windGust       = new CombatAbility("Wind Gust",        8,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var breathWeapon   = new CombatAbility("Breath Weapon",   14,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var frostSlam      = new CombatAbility("Frost Slam",      16,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var glacialRoar    = new CombatAbility("Glacial Roar",     8,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var earthShatter   = new CombatAbility("Earth Shatter",   12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);

        // Forest abilities
        var pounce         = new CombatAbility("Pounce",           9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var charge         = new CombatAbility("Charge",          10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var web            = new CombatAbility("Web",              5,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var venomBite      = new CombatAbility("Venom Bite",      10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var quickSlash     = new CombatAbility("Quick Slash",      9,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var maul           = new CombatAbility("Maul",            15,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var roar           = new CombatAbility("Roar",             5,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var branchSwipe    = new CombatAbility("Branch Swipe",    14,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var rootStrike     = new CombatAbility("Root Strike",     10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var wyldGore       = new CombatAbility("Wyld Gore",       18,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var thornBarrage   = new CombatAbility("Thorn Barrage",   14,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);

        // Desert abilities
        var stingStrike    = new CombatAbility("Sting Strike",     8,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var acidSpit       = new CombatAbility("Acid Spit",        9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var fireLash       = new CombatAbility("Fire Lash",       10,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var constrict      = new CombatAbility("Constrict",        9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var burrow         = new CombatAbility("Burrow",           5,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Buff);
        var eruption       = new CombatAbility("Eruption",        12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var embersweep     = new CombatAbility("Ember Sweep",     12,  0, MagicElement.Fire,  AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var ashSurge       = new CombatAbility("Ash Surge",        9,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var reviveFlame    = new CombatAbility("Revive Flame",    14,  0, MagicElement.Fire,  AbilityTargetType.Self,        AbilityCategory.Heal);
        var infernoBreath  = new CombatAbility("Inferno Breath",  16,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        // Water abilities
        var pinch          = new CombatAbility("Pinch",            7,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var waterJet       = new CombatAbility("Water Jet",       10,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var tidalSurge     = new CombatAbility("Tidal Surge",     11,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var snapJaw        = new CombatAbility("Snap Jaw",        12,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var tentacleLash   = new CombatAbility("Tentacle Lash",   13,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var inkCloud       = new CombatAbility("Ink Cloud",        6,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var crushingDepths = new CombatAbility("Crushing Depths",  18, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var drainTouch     = new CombatAbility("Drain Touch",     12,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);
        var voidPulse      = new CombatAbility("Void Pulse",      15,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        // Swamp abilities
        var gnaw           = new CombatAbility("Gnaw",             7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var leechDrain     = new CombatAbility("Leech Drain",      8,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);
        var spectralTouch  = new CombatAbility("Spectral Touch",  10,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var mistVeil       = new CombatAbility("Mist Veil",        5,  0, MagicElement.Water, AbilityTargetType.Self,        AbilityCategory.Buff);
        var mudSlap        = new CombatAbility("Mud Slap",         9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var shadowStep     = new CombatAbility("Shadow Step",     11,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var phantomStrike  = new CombatAbility("Phantom Strike",   9,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var toxicSpray     = new CombatAbility("Toxic Spray",     10,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var debilitatingCroak = new CombatAbility("Debilitating Croak", 6, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var multiStrike    = new CombatAbility("Multi Strike",    12,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var graveDrain     = new CombatAbility("Grave Drain",     14,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);

        // Plains abilities
        var scratchScrape  = new CombatAbility("Scratch",          6,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var snarl          = new CombatAbility("Snarl",            7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var dirtyBlow      = new CombatAbility("Dirty Blow",       9,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var trickSlash     = new CombatAbility("Trick Slash",      8,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var kickHooves     = new CombatAbility("Hoof Kick",       10,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var stampede       = new CombatAbility("Stampede",        11,  0, MagicElement.Air,   AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var shieldBash     = new CombatAbility("Shield Bash",     12,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var armorBreak     = new CombatAbility("Armor Break",      8,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var howl           = new CombatAbility("Howl",             6,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Buff);
        var packHunter     = new CombatAbility("Pack Hunter",     10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var brutalSmash    = new CombatAbility("Brutal Smash",    16,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var groundPound    = new CombatAbility("Ground Pound",    12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var cavalryCharge  = new CombatAbility("Cavalry Charge",  14,  0, MagicElement.Air,   AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var swiftBlow      = new CombatAbility("Swift Blow",      10,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        // Wyrd abilities
        var wyrdNip        = new CombatAbility("Wyrd Nip",         7,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var shadowPounce   = new CombatAbility("Shadow Pounce",    9,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var phaseStrike    = new CombatAbility("Phase Strike",    11,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var blink          = new CombatAbility("Blink",            5,  0, MagicElement.Aether,AbilityTargetType.Self,        AbilityCategory.Buff);
        var wraitheTouch   = new CombatAbility("Wraith Touch",    10,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var nullField      = new CombatAbility("Null Field",       8,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
        var voidTear       = new CombatAbility("Void Tear",       13,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var realityShred   = new CombatAbility("Reality Shred",   14,  0, MagicElement.Aether,AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var wyrdBurst      = new CombatAbility("Wyrd Burst",      18,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var weaveRend      = new CombatAbility("Weave Rend",      15,  0, MagicElement.Aether,AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
        var aetherDrain    = new CombatAbility("Aether Drain",    12,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);

        // ---- Per-biome tier pools ----
        // Tiers 0-3 correspond to dangerLevel ranges: <=2, <=4, <=7, 8+
        MonsterTemplate[][] mountainPools =
        [
            [
                new("Mountain Goat",  22, 7, 1, MagicElement.Earth, [headbutt, charge]),
                new("Rock Beetle",    26, 4, 1, MagicElement.Earth, [claw, rockThrow]),
            ],
            [
                new("Stone Troll",    55, 4, 2, MagicElement.Earth, [boulderThrow, regenerate, earthShatter]),
                new("Mountain Lion",  38, 9, 2, MagicElement.Air,   [pounce, quickSlash]),
            ],
            [
                new("Wyvern",         60, 8, 3, MagicElement.Air,   [diveAttack, windGust, rockThrow]),
                new("Rock Golem",     80, 3, 3, MagicElement.Earth, [boulderThrow, earthShatter, regenerate]),
            ],
            [
                new("Dragon Whelp",   85, 7, 4, MagicElement.Fire,  [breathWeapon, diveAttack, embersweep]),
                new("Frost Giant",    90, 3, 4, MagicElement.Water,  [frostSlam, glacialRoar, earthShatter]),
            ],
        ];

        MonsterTemplate[][] forestPools =
        [
            [
                new("Timber Wolf",    28, 8, 1, MagicElement.Earth, [bite, pounce]),
                new("Wild Boar",      32, 6, 1, MagicElement.Earth, [charge, headbutt]),
            ],
            [
                new("Thornweaver Spider", 35, 7, 2, MagicElement.Earth, [web, venomBite, claw]),
                new("Forest Bandit",      30, 9, 2, MagicElement.Air,   [quickSlash, trickSlash]),
            ],
            [
                new("Dire Bear",      65, 5, 3, MagicElement.Earth, [maul, roar, charge]),
                new("Treant",         75, 3, 3, MagicElement.Earth, [branchSwipe, rootStrike, earthShatter]),
            ],
            [
                new("Elder Stag",     75, 7, 4, MagicElement.Aether, [wyldGore, shadowStep, diveAttack]),
                new("Thornwood Guardian", 95, 4, 4, MagicElement.Earth, [thornBarrage, maul, regenerate]),
            ],
        ];

        MonsterTemplate[][] desertPools =
        [
            [
                new("Sand Scorpion",  24, 7, 1, MagicElement.Fire,  [stingStrike, claw]),
                new("Dust Viper",     22, 8, 1, MagicElement.Earth, [bite, constrict]),
            ],
            [
                new("Fire Lizard",    36, 7, 2, MagicElement.Fire,  [fireLash, acidSpit]),
                new("Giant Centipede",34, 6, 2, MagicElement.Earth, [constrict, bite, claw]),
            ],
            [
                new("Sand Wurm",      65, 4, 3, MagicElement.Earth, [burrow, eruption, bite]),
                new("Ash Golem",      70, 3, 3, MagicElement.Fire,  [ashSurge, embersweep, regenerate]),
            ],
            [
                new("Phoenix Hatchling", 70, 8, 4, MagicElement.Fire, [infernoBreath, reviveFlame, diveAttack]),
                new("Ember Drake",    85, 7, 4, MagicElement.Fire,  [infernoBreath, embersweep, breathWeapon]),
            ],
        ];

        MonsterTemplate[][] waterPools =
        [
            [
                new("Giant Crab",     26, 5, 1, MagicElement.Water, [pinch, claw]),
                new("Mud Skipper",    22, 8, 1, MagicElement.Water, [waterJet, bite]),
            ],
            [
                new("Tide Lurker",    35, 7, 2, MagicElement.Water, [tidalSurge, waterJet]),
                new("Reef Shark",     32, 9, 2, MagicElement.Water, [snapJaw, charge]),
            ],
            [
                new("Sea Serpent",    60, 6, 3, MagicElement.Water, [tentacleLash, tidalSurge, bite]),
                new("Kraken Spawn",   65, 5, 3, MagicElement.Water, [tentacleLash, inkCloud, crushingDepths]),
            ],
            [
                new("Deep Horror",    80, 5, 4, MagicElement.Aether, [voidPulse, crushingDepths, drainTouch]),
                new("Drowned Revenant",75, 6, 4, MagicElement.Water, [drainTouch, tidalSurge, voidPulse]),
            ],
        ];

        MonsterTemplate[][] swampPools =
        [
            [
                new("Swamp Rat",      22, 7, 1, MagicElement.Earth, [gnaw, scratchScrape]),
                new("Leech Swarm",    20, 6, 1, MagicElement.Water, [leechDrain, claw]),
            ],
            [
                new("Bog Wraith",     32, 7, 2, MagicElement.Water, [spectralTouch, mistVeil, waterJet]),
                new("Marsh Crawler",  36, 6, 2, MagicElement.Earth, [mudSlap, claw, constrict]),
            ],
            [
                new("Moor Stalker",   55, 8, 3, MagicElement.Aether, [shadowStep, phantomStrike, nullField]),
                new("Poison Toad",    52, 5, 3, MagicElement.Water,  [toxicSpray, debilitatingCroak, bite]),
            ],
            [
                new("Swamp Hydra",    90, 5, 4, MagicElement.Water,  [multiStrike, tidalSurge, tentacleLash]),
                new("Grave Wight",    78, 6, 4, MagicElement.Aether, [graveDrain, phantomStrike, nullField]),
            ],
        ];

        MonsterTemplate[][] plainsPools =
        [
            [
                new("Cave Rat",       24, 7, 1, MagicElement.Earth, [scratchScrape, gnaw]),
                new("Stray Dog",      26, 8, 1, MagicElement.Earth, [snarl, bite]),
            ],
            [
                new("Highway Bandit", 34, 8, 2, MagicElement.Air,   [dirtyBlow, trickSlash]),
                new("Wild Horse",     36, 9, 2, MagicElement.Air,   [kickHooves, stampede]),
            ],
            [
                new("Rogue Knight",   60, 6, 3, MagicElement.Earth, [shieldBash, armorBreak, brutalSmash]),
                new("Pack Alpha Wolf",55, 7, 3, MagicElement.Earth, [howl, packHunter, maul]),
            ],
            [
                new("Wandering Ogre", 88, 4, 4, MagicElement.Earth, [brutalSmash, groundPound, roar]),
                new("Mounted Raider", 75, 9, 4, MagicElement.Air,   [cavalryCharge, swiftBlow, trickSlash]),
            ],
        ];

        MonsterTemplate[][] wyrdPools =
        [
            [
                new("Wyrd Hound",     25, 8, 1, MagicElement.Aether, [wyrdNip, phantomStrike]),
                new("Shadow Cat",     22, 9, 1, MagicElement.Aether, [shadowPounce, claw]),
            ],
            [
                new("Phase Spider",   32, 8, 2, MagicElement.Aether, [phaseStrike, blink, web]),
                new("Wyrd Wraith",    30, 7, 2, MagicElement.Aether, [wraitheTouch, nullField]),
            ],
            [
                new("Void Stalker",   58, 7, 3, MagicElement.Aether, [voidTear, shadowStep, nullField]),
                new("Reality Shredder",55, 6, 3, MagicElement.Aether, [realityShred, phaseStrike, voidPulse]),
            ],
            [
                new("Wyrd Abomination", 92, 5, 4, MagicElement.Aether, [wyrdBurst, realityShred, aetherDrain]),
                new("Tear in the Weave", 80, 6, 4, MagicElement.Aether, [weaveRend, voidPulse, nullField]),
            ],
        ];

        var biomePools = biome switch
        {
            "mountain" => mountainPools,
            "forest"   => forestPools,
            "desert"   => desertPools,
            "water"    => waterPools,
            "swamp"    => swampPools,
            "wyrd"     => wyrdPools,
            _          => plainsPools,
        };

        var tierIndex = dangerLevel switch
        {
            <= 2 => 0,
            <= 4 => 1,
            <= 7 => 2,
            _    => 3,
        };

        var pool = biomePools[tierIndex];
        var pack = new List<MonsterTemplate>();

        var primary = pool[Random.Shared.Next(pool.Length)];
        pack.Add(ScaleMonster(primary, dangerLevel, playerLevel));

        if (dangerLevel >= 3 && Random.Shared.Next(2) == 0)
        {
            var weakPool = biomePools[Math.Max(0, tierIndex - 1)];
            var extra = weakPool[Random.Shared.Next(weakPool.Length)];
            pack.Add(ScaleMonster(extra, dangerLevel, playerLevel));
        }

        if (dangerLevel >= 7 && pack.Count == 1)
        {
            var midPool = biomePools[Math.Max(0, tierIndex - 1)];
            var extra = midPool[Random.Shared.Next(midPool.Length)];
            pack.Add(ScaleMonster(extra, dangerLevel, playerLevel));
        }

        return pack;
    }

    private static MonsterTemplate ScaleMonster(MonsterTemplate template, int dangerLevel, int playerLevel)
    {
        // Base level: danger level +/- 1 for some variance
        var variance = Random.Shared.Next(-1, 2); // -1, 0, or 1
        var monsterLevel = Math.Max(1, dangerLevel + variance);

        // Scale up if the player has significantly out-levelled the zone
        if (playerLevel > dangerLevel * 2)
            monsterLevel = Math.Max(monsterLevel, playerLevel - 2);

        return template with { Level = monsterLevel };
    }

    // -------------------------------------------------------------------------
    // DTO builder
    // -------------------------------------------------------------------------

    public static CombatUpdateDto BuildCombatUpdateDto(Encounter encounter, string? lastActionText = null)
    {
        var combatantDtos = encounter.Combatants
            .Select(c => new CombatantDto(
                c.Id,
                c.Name,
                c.CombatantType.ToString(),
                c.CurrentHp,
                c.MaxHp,
                c.Speed,
                c.Element.ToString(),
                c.IsPlayerSide,
                c.IsDefeated,
                c.Abilities.Select(a => new AbilityDto(
                    a.Name,
                    a.BasePower,
                    a.WeaveCost,
                    a.Element.ToString(),
                    a.Category.ToString())).ToList()))
            .ToList();

        return new CombatUpdateDto(
            encounter.Id,
            encounter.State.ToString(),
            combatantDtos,
            encounter.CurrentActor?.Id ?? Guid.Empty,
            encounter.RoundNumber,
            lastActionText);
    }

    // -------------------------------------------------------------------------
    // HP sync helpers (Issue 2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// After combat ends (Victory or Fled), syncs the player combatant's remaining HP
    /// back to the Player entity so damage persists between encounters.
    /// </summary>
    public async Task SyncPlayerHpAfterCombatAsync(Guid playerId, Encounter encounter, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var playerCombatant = encounter.Combatants
            .FirstOrDefault(c => c.IsPlayerSide && c.CombatantType == CombatantType.Player);

        if (playerCombatant is null) return;

        // Cap at the player's actual MaxHp — combat MaxHp can be inflated by armor bonuses
        var syncedHp = Math.Min(playerCombatant.CurrentHp, player.MaxHp);
        player.SetCurrentHp(syncedHp);
        await playerRepository.UpdateAsync(player, ct);

        // TODO: sync Weave cost from Weave Bolt usage once Weave deduction is implemented in combat

        logger.LogDebug("Synced player {PlayerId} HP to {Hp} after combat", playerId, syncedHp);
    }

    /// <summary>
    /// Handles player defeat: sets HP to 1, portals them home, broadcasts the narrative.
    /// </summary>
    public async Task HandlePlayerDefeatAsync(Guid playerId, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        player.SetCurrentHp(1);

        // Mirror the same homestead position used by PortalHomeCommandHandler
        var homesteadPosition = new Domain.ValueObjects.Position(player.Position.World, 0, -100, -100);
        player.PortalHome(homesteadPosition);

        await playerRepository.UpdateAsync(player, ct);

        await notificationService.SendMessageAsync(playerId, "system",
            "You have been defeated. You wake at your homestead, wounds bound and spirit shaken...", ct);

        // Push updated world state so client reflects new position
        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("PlayerMoved", new
            {
                Id = playerId,
                X = homesteadPosition.X,
                Y = homesteadPosition.Y,
                ZoneId = homesteadPosition.ZoneId,
                World = homesteadPosition.World.ToString()
            }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("AtHomestead", new { playerId }, ct);

        logger.LogInformation("Player {PlayerId} defeated — portaled home", playerId);
    }
}
