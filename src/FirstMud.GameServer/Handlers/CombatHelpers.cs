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
// BiomeService and MonsterFactory are static helpers extracted from this class.
// All callers continue to use CombatHelpers.GetBiome / CombatHelpers.BuildMonsterPack
// via the forwarding wrappers below — no call-site changes required.

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
    ILogger<CombatHelpers> logger,
    QuestProgressTracker questProgressTracker,
    FirstMud.Domain.Interfaces.IQuestGraphRepository questGraphRepository)
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

        // ── Quest kill tracking ──────────────────────────────────────────────
        // For each active kill quest, record the enemies defeated in this encounter
        // and broadcast progress back to the client.
        await TrackQuestKillsAsync(playerId, defeatedEnemies.Count, ct);
    }

    /// <summary>
    /// Records kills for any accepted kill-type quests and broadcasts progress.
    /// Called after every combat Victory.
    /// </summary>
    private async Task TrackQuestKillsAsync(Guid playerId, int killCount, CancellationToken ct)
    {
        if (killCount <= 0) return;

        // Load in-progress quests for this player
        var inProgressQuests = await questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
        var killQuests = inProgressQuests
            .Where(q => q.IsTaken && IsKillQuest(q.Title))
            .ToList();

        foreach (var quest in killQuests)
        {
            questProgressTracker.RecordKill(playerId, quest.QuestId, killCount);
            var totalKills = questProgressTracker.GetKills(playerId, quest.QuestId);

            var required = ParseKillCount(quest.Description);
            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("QuestKillProgress", new
                {
                    QuestId  = quest.QuestId,
                    Kills    = totalKills,
                    Required = required,
                }, ct);
        }
    }

    private static bool IsKillQuest(string title) =>
        title.Contains("Defeat", StringComparison.OrdinalIgnoreCase)
     || title.Contains("Slay",   StringComparison.OrdinalIgnoreCase)
     || title.Contains("Hunt",   StringComparison.OrdinalIgnoreCase);

    private static int ParseKillCount(string description)
    {
        var match = System.Text.RegularExpressions.Regex.Match(description, @"\b(\d+)\b");
        return match.Success && int.TryParse(match.Value, out var n) && n > 0 ? n : 3;
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

        // Diagnostic trace — fires on every loot drop so we can track equip decisions
        var hasSlot = item.Slot != Domain.Enums.EquipmentSlot.None && player.GetEquipped(item.Slot) is null;
        logger.LogInformation(
            "Loot: {Name} Slot={Slot} PlayerHasSlot={HasSlot} AutoSalvaged={AutoSalvaged}",
            item.Name, item.Slot, hasSlot, result.AutoSalvaged);

        // If the item was auto-salvaged, send the message and stop — don't equip or broadcast LootDropped
        if (result.AutoSalvaged)
        {
            var salvageCategory = GetSalvageCategory(item.Workmanship.Value);
            await notificationService.SendMessageAsync(playerId, salvageCategory, result.Message, ct);
            return;
        }

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
                await notificationService.SendMessageAsync(playerId, lootCategory, $"You equip the {item.DisplayName}.", ct);
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
                        $"You swap your {currentEquipped.DisplayName} W{currentEquipped.Workmanship.Value} for {item.DisplayName} W{item.Workmanship.Value}. Much better.", ct);
                }
            }
        }

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("LootDropped", new
            {
                item.Id,
                Name = item.DisplayName,
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
            if (Random.Shared.Next(100) >= 25) continue;

            // Generate a flavourful name for the captured creature
            var companionName = GenerateCapturedName(enemy.Element);

            var companion = Companion.Create(
                playerId,
                companionName,
                CompanionType.CapturedMonster,
                enemy.Element);

            // Captured monsters always start at Layer 1
            await companionRepository.AddAsync(companion, ct);

            // Auto-activate if there's a free slot; otherwise assign to best homestead duty
            var activated = player.TryAddActiveCompanion(companion.Id);
            if (activated)
            {
                companion.SetActive(true);
                await companionRepository.UpdateAsync(companion, ct);
            }
            await playerRepository.UpdateAsync(player, ct);

            string slotNote;
            if (activated)
            {
                slotNote = "It has joined your active party.";
            }
            else
            {
                // All combat slots full — assign to best homestead duty
                var bestDuty = new[] { HomesteadDuty.Harvester, HomesteadDuty.Salvager, HomesteadDuty.Guard }
                    .OrderByDescending(d => companion.GetAptitude(d))
                    .First();

                companion.AssignToHomestead(bestDuty);
                await companionRepository.UpdateAsync(companion, ct);

                await notificationService.SendMessageAsync(playerId, "system",
                    $"{companion.Name} has been assigned to {bestDuty} duty at your homestead ({companion.GetAptitude(bestDuty)} star aptitude).",
                    ct);

                slotNote = $"Your party is full — {companion.Name} is contributing as a {bestDuty} at your homestead.";
            }

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

            // Scale usage points by bond level deficit — low-bond companions catch up faster
            var scaledUsage = companion.CurrentLayer switch
            {
                1 => 25,  // Bond 1: 2.5× fast catch-up
                2 => 20,
                3 => 15,
                4 => 12,
                5 => 10,
                _ => 10   // Bond 6 MAX: standard (but won't level further)
            };

            var layerBefore = companion.CurrentLayer;
            companion.RecordUsage(scaledUsage);
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
                companion.Name, companion.Id, scaledUsage,
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
    // Companion auto-rotation
    // -------------------------------------------------------------------------

    /// <summary>
    /// After every 5th combat victory (or when called explicitly from an auto-farm
    /// deposit cycle), checks whether any active companions are at Bond MAX (Layer 6)
    /// while lower-bond companions sit idle.  Swaps them: maxed companion → best
    /// homestead duty, lowest-bond idle companion → active party.
    ///
    /// No-ops when <see cref="Player.AutoRotateMaxedCompanions"/> is false or
    /// there is nothing to swap.
    /// </summary>
    public async Task TryRotateMaxedCompanionsAsync(Guid playerId, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;
        if (!player.AutoRotateMaxedCompanions)
        {
            logger.LogDebug("Rotation check skipped: AutoRotateMaxedCompanions is OFF for player {PlayerId}", playerId);
            return;
        }

        var allCompanions = await companionRepository.GetByOwnerAsync(playerId, ct);

        // Active companions that have hit the bond cap — use player.ActiveCompanionIds as ground truth
        var maxedActive = allCompanions
            .Where(c => !c.IsPermanentlyGone
                     && player.ActiveCompanionIds.Contains(c.Id)
                     && c.CurrentLayer >= 6)
            .ToList();

        if (maxedActive.Count == 0)
        {
            logger.LogDebug(
                "Rotation check: no maxed active companions for player {PlayerId}. Active slots: [{Ids}]",
                playerId,
                string.Join(", ", player.ActiveCompanionIds));
            return;
        }

        // Growable candidates: not active, not permanently gone, below bond cap.
        // Include companions assigned to homestead duty — they will be recalled.
        var growableInactive = allCompanions
            .Where(c => !c.IsPermanentlyGone
                     && !player.ActiveCompanionIds.Contains(c.Id)
                     && c.CurrentLayer < 6)
            .OrderBy(c => c.CurrentLayer)   // lowest bond first — most benefit from XP
            .ThenBy(c => c.UsageCounter)    // least-progressed within same layer
            .ToList();

        if (growableInactive.Count == 0)
        {
            logger.LogInformation(
                "Rotation check: {Count} maxed companion(s) active but NO growable replacements available for player {PlayerId}. " +
                "Maxed: [{Names}]",
                maxedActive.Count,
                playerId,
                string.Join(", ", maxedActive.Select(c => $"{c.Name} L{c.CurrentLayer}")));
            return;
        }

        logger.LogInformation(
            "Rotation check: {MaxedCount} maxed active, {GrowCount} growable candidates for player {PlayerId}. " +
            "Maxed: [{Maxed}] Candidates: [{Grow}]",
            maxedActive.Count,
            growableInactive.Count,
            playerId,
            string.Join(", ", maxedActive.Select(c => $"{c.Name} L{c.CurrentLayer}")),
            string.Join(", ", growableInactive.Take(5).Select(c => $"{c.Name} L{c.CurrentLayer}")));

        var rotated = false;

        foreach (var maxed in maxedActive)
        {
            if (growableInactive.Count == 0) break;

            var replacement = growableInactive[0];
            growableInactive.RemoveAt(0);

            // Deactivate the maxed companion
            player.RemoveActiveCompanion(maxed.Id);
            maxed.SetActive(false);

            // Assign maxed companion to its best homestead duty
            var bestDuty = new[] { Domain.Enums.HomesteadDuty.Guard, Domain.Enums.HomesteadDuty.Harvester, Domain.Enums.HomesteadDuty.Salvager, Domain.Enums.HomesteadDuty.Crafter }
                .OrderByDescending(d => maxed.GetAptitude(d))
                .First();

            maxed.AssignToHomestead(bestDuty);
            await companionRepository.UpdateAsync(maxed, ct);

            // Activate the replacement (may need to recall from homestead first)
            if (replacement.AssignedDuty.HasValue && replacement.AssignedDuty != Domain.Enums.HomesteadDuty.None)
                replacement.RecallFromHomestead();

            player.TryAddActiveCompanion(replacement.Id);
            replacement.SetActive(true);
            await companionRepository.UpdateAsync(replacement, ct);

            await notificationService.SendMessageAsync(playerId, "system",
                $"{maxed.Name} (Bond MAX) has been rotated to {bestDuty} duty at your homestead. " +
                $"{replacement.Name} (Bond {replacement.CurrentLayer}) joins your party to train.",
                ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("CompanionRotated", new
                {
                    RotatedOutId   = maxed.Id,
                    RotatedOutName = maxed.Name,
                    Duty           = bestDuty.ToString(),
                    RotatedInId    = replacement.Id,
                    RotatedInName  = replacement.Name,
                    ReplacementLayer = replacement.CurrentLayer,
                }, ct);

            logger.LogInformation(
                "Auto-rotated companion {MaxedName} (Layer {Layer}) → {Duty}; {RepName} (Layer {RepLayer}) → active for player {PlayerId}",
                maxed.Name, maxed.CurrentLayer, bestDuty, replacement.Name, replacement.CurrentLayer, playerId);

            rotated = true;
        }

        if (rotated)
        {
            await playerRepository.UpdateAsync(player, ct);
            await CompanionDtoHelpers.BroadcastCompanionListAsync(playerId, companionRepository, hubContext, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Biome detection — forwarding wrappers → BiomeService
    // Kept here so callers don't need to change import namespaces.
    // -------------------------------------------------------------------------

    public static string GetBiome(Zone? zone)                               => BiomeService.GetBiome(zone);
    public static string GetBiomeForPosition(Zone? nearbyZone, int x, int y) => BiomeService.GetBiomeForPosition(nearbyZone, x, y);
    public static string GuessWildernessBiome(int x, int y)                 => BiomeService.GuessWildernessBiome(x, y);
    public static int    GetWildernessDanger(int x, int y, string biome)    => BiomeService.GetWildernessDanger(x, y, biome);
    public static string GetBiomeNarration(string biome, string monsterNames) => BiomeService.GetBiomeNarration(biome, monsterNames);

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
    // Monster pack builder — forwarding wrapper → MonsterFactory
    // -------------------------------------------------------------------------

    public static List<MonsterTemplate> BuildMonsterPack(int dangerLevel, int playerLevel = 1, string biome = "plains")
        => MonsterFactory.BuildMonsterPack(dangerLevel, playerLevel, biome);

    // -------------------------------------------------------------------------
    // DTO builder
    // -------------------------------------------------------------------------

    public static CombatUpdateDto BuildCombatUpdateDto(Encounter encounter, string? lastActionText = null, int dangerLevel = 0)
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
            lastActionText,
            dangerLevel);
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
