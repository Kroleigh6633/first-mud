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

                var (cSuccess, _, _) = await combatService.ExecuteActionAsync(
                    encounter.Id, actor.Id, chosenAbility.Name, targetId, ct);
                if (!cSuccess) break;

                string narrative;
                if (chosenAbility.Category is AbilityCategory.Heal or AbilityCategory.Revive)
                {
                    int heal = chosenAbility.BasePower + actor.Level * 2;
                    narrative = $"{actor.Name} uses {chosenAbility.Name} on {targetName}, restoring {heal} HP.";
                }
                else if (chosenAbility.Category == AbilityCategory.Buff)
                {
                    narrative = $"{actor.Name} uses {chosenAbility.Name}! The party is bolstered.";
                }
                else if (chosenAbility.Category == AbilityCategory.Debuff)
                {
                    narrative = $"{actor.Name} uses {chosenAbility.Name} on {targetName}! They falter.";
                }
                else
                {
                    int rawPower = chosenAbility.BasePower + actor.Level * 2;
                    // Get target element for multiplier display
                    var targetCombatant = encounter.Combatants.FirstOrDefault(c => c.Id == targetId);
                    float mult = targetCombatant is not null
                        ? ElementMatchup.GetMultiplier(chosenAbility.Element, targetCombatant.Element)
                        : 1f;
                    int dmg = (int)(rawPower * mult);
                    var multLabel = mult > 1f ? " (super effective!)" : mult < 1f ? " (resisted)" : "";
                    narrative = $"{actor.Name} uses {chosenAbility.Name} on {targetName} for {dmg} damage{multLabel}.";
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

            var (eSuccess, _, _) = await combatService.ExecuteActionAsync(
                encounter.Id, actor.Id, enemyAbility.Name, target.Id, ct);
            if (!eSuccess) break;

            int ePower = enemyAbility.BasePower + actor.Level * 2;
            float eMult = ElementMatchup.GetMultiplier(enemyAbility.Element, target.Element);
            int eDmg = (int)(ePower * eMult);
            var eMultLabel = eMult > 1f ? " (super effective!)" : eMult < 1f ? " (resisted)" : "";

            await notificationService.SendMessageAsync(
                playerId,
                "combat",
                $"{actor.Name} uses {enemyAbility.Name} on {target.Name} for {eDmg} damage{eMultLabel}.",
                ct);
        }

        // Clean up buff tracking when encounter ends
        if (encounter.State != EncounterState.InProgress)
            _companionBuffUsed.TryRemove(encounter.Id, out _);
    }

    // -------------------------------------------------------------------------
    // XP + level-up helpers
    // -------------------------------------------------------------------------

    public async Task AwardCombatXpAsync(Guid playerId, Encounter encounter, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var defeatedEnemies = encounter.Combatants
            .Where(c => !c.IsPlayerSide && c.IsDefeated)
            .ToList();

        if (defeatedEnemies.Count == 0) return;

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

        if (totalXp > 0)
        {
            player.GainExperience(totalXp);
            await playerRepository.UpdateAsync(player, ct);
            logger.LogDebug("Awarded {Xp} XP to player {PlayerId}", totalXp, playerId);
        }

        var detail = string.Join(", ", breakdown);
        await notificationService.SendMessageAsync(
            playerId, "combat",
            $"You gained {totalXp} experience! ({detail})",
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
            player);

        if (!result.Dropped)
        {
            if (!string.IsNullOrEmpty(result.Message))
                await notificationService.SendMessageAsync(playerId, "loot", result.Message, ct);
            return;
        }

        var item = result.Item!;
        await notificationService.SendMessageAsync(playerId, "loot", result.Message, ct);

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
                await notificationService.SendMessageAsync(playerId, "loot", $"You equip the {item.Name}.", ct);
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
                    await notificationService.SendMessageAsync(playerId, "loot",
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
    // Monster pack builder
    // -------------------------------------------------------------------------

    public static List<MonsterTemplate> BuildMonsterPack(int dangerLevel, int playerLevel = 1)
    {
        var claw     = new CombatAbility("Claw",       6, 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var bite     = new CombatAbility("Bite",       8, 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var fireSpit = new CombatAbility("Fire Spit", 10, 0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var waterJet = new CombatAbility("Water Jet", 10, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var airSlash = new CombatAbility("Air Slash",  9, 0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        MonsterTemplate[][] pools =
        [
            [
                new("Cave Rat",      25, 5, 1, MagicElement.Earth, [claw]),
                new("Marsh Bat",     20, 8, 1, MagicElement.Air,   [airSlash]),
                new("Fire Beetle",   28, 4, 1, MagicElement.Fire,  [fireSpit]),
                new("Stream Eel",    22, 7, 1, MagicElement.Water, [waterJet]),
            ],
            [
                new("Thornwood Wolf", 35, 7, 2, MagicElement.Earth, [bite]),
                new("Fire Imp",       30, 6, 2, MagicElement.Fire,  [fireSpit]),
                new("Bog Wraith",     28, 8, 2, MagicElement.Water, [waterJet]),
                new("Wind Sprite",    25, 9, 2, MagicElement.Air,   [airSlash]),
            ],
            [
                new("Grave Stalker",  50, 8, 3, MagicElement.Earth, [bite, claw]),
                new("Ashlands Drake", 55, 7, 3, MagicElement.Fire,  [fireSpit, bite]),
                new("Tide Serpent",   45, 9, 3, MagicElement.Water, [waterJet, bite]),
            ],
            [
                new("Elder Drake",   70, 9,  4, MagicElement.Fire,   [fireSpit, bite]),
                new("Deep Horror",   65, 8,  4, MagicElement.Water,  [waterJet, bite]),
                new("Wyrd Stalker",  60, 10, 4, MagicElement.Aether, [bite, claw]),
            ],
        ];

        var tierIndex = dangerLevel switch
        {
            <= 2 => 0,
            <= 4 => 1,
            <= 7 => 2,
            _    => 3,
        };

        var pool = pools[tierIndex];
        var pack = new List<MonsterTemplate>();

        var primary = pool[Random.Shared.Next(pool.Length)];
        pack.Add(ScaleMonster(primary, dangerLevel, playerLevel));

        if (dangerLevel >= 3 && Random.Shared.Next(2) == 0)
        {
            var weakPool = pools[Math.Max(0, tierIndex - 1)];
            var extra = weakPool[Random.Shared.Next(weakPool.Length)];
            pack.Add(ScaleMonster(extra, dangerLevel, playerLevel));
        }

        if (dangerLevel >= 7 && pack.Count == 1)
        {
            var midPool = pools[Math.Max(0, tierIndex - 1)];
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

    public static CombatUpdateDto BuildCombatUpdateDto(Encounter encounter)
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
            encounter.RoundNumber);
    }
}
