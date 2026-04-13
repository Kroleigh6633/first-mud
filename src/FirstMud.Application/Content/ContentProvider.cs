using System.Text.Json;
using System.Text.Json.Serialization;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Content;

/// <summary>
/// JSON-backed implementation of <see cref="IContentProvider"/>.
///
/// Loads and validates content files at construction time. Validation is
/// strict-enough-to-catch-typos: required fields, enum values, and Buff
/// effects must have a buffKey. Anything invalid throws on startup so
/// misconfigured content cannot reach production silently.
/// </summary>
public sealed class ContentProvider : IContentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> ValidEffectTypes =
        new(StringComparer.Ordinal) { "Heal", "RestoreWeave", "Buff" };

    private static readonly HashSet<string> ValidBuffKeys =
        new(StringComparer.Ordinal) { "MaxHpBonus", "SpeedBonus", "StrikeDamageBonus" };

    private static readonly HashSet<string> ValidBiomes =
        new(StringComparer.Ordinal)
        { "mountain", "forest", "desert", "water", "swamp", "plains", "wyrd" };

    private readonly string _contentRoot;
    private readonly ILogger<ContentProvider>? _logger;

    private IReadOnlyList<ConsumableDefinition> _consumables = Array.Empty<ConsumableDefinition>();
    private IReadOnlyList<MonsterDefinition> _monsters = Array.Empty<MonsterDefinition>();
    private Dictionary<string, MonsterDefinition> _monstersById = new(StringComparer.Ordinal);

    public ContentProvider(string contentRoot, ILogger<ContentProvider>? logger = null)
    {
        _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<ConsumableDefinition> Consumables => _consumables;

    public ConsumableDefinition? ResolveConsumable(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        var lower = itemName.ToLowerInvariant();
        foreach (var def in _consumables)
        {
            if (lower.Contains(def.MatchToken))
                return def;
        }
        return null;
    }

    public IReadOnlyList<ConsumableDefinition> ConsumablesByGroup(string priorityGroup)
    {
        return _consumables
            .Where(c => string.Equals(c.PriorityGroup, priorityGroup, StringComparison.Ordinal))
            .OrderBy(c => c.PriorityRank)
            .ToList();
    }

    public IReadOnlyList<MonsterDefinition> AllMonsters() => _monsters;

    public MonsterDefinition? GetMonster(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _monstersById.TryGetValue(id, out var def) ? def : null;
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiome(string biome)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiomeAndTier(string biome, int tier)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            m.Tier == tier &&
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
    }

    public void Reload()
    {
        _consumables = LoadConsumables();
        _monsters = LoadMonsters();
        _monstersById = _monsters.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables, {MonsterCount} monsters from {Root}",
            _consumables.Count, _monsters.Count, _contentRoot);
    }

    private IReadOnlyList<ConsumableDefinition> LoadConsumables()
    {
        var path = Path.Combine(_contentRoot, "consumables.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ConsumablesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Consumables is null || doc.Consumables.Count == 0)
            throw new InvalidDataException($"{path}: no consumables defined.");

        var list = new List<ConsumableDefinition>(doc.Consumables.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Consumables)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: consumable missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate consumable id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.MatchToken))
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' missing matchToken.");
            if (raw.MatchToken != raw.MatchToken.ToLowerInvariant())
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' matchToken must be lowercase.");
            if (string.IsNullOrWhiteSpace(raw.EffectType) || !ValidEffectTypes.Contains(raw.EffectType))
                throw new InvalidDataException(
                    $"{path}: consumable '{raw.Id}' has invalid effectType '{raw.EffectType}'. " +
                    $"Must be one of: {string.Join(", ", ValidEffectTypes)}.");

            if (raw.EffectType == "Buff")
            {
                if (string.IsNullOrWhiteSpace(raw.BuffKey) || !ValidBuffKeys.Contains(raw.BuffKey))
                    throw new InvalidDataException(
                        $"{path}: Buff consumable '{raw.Id}' has invalid buffKey '{raw.BuffKey}'.");
            }

            list.Add(new ConsumableDefinition(
                Id: raw.Id,
                MatchToken: raw.MatchToken,
                EffectType: raw.EffectType,
                Amount: raw.Amount,
                BuffKey: raw.BuffKey,
                BuffValue: raw.BuffValue,
                PriorityGroup: raw.PriorityGroup,
                PriorityRank: raw.PriorityRank));
        }

        return list;
    }

    // ─── JSON DTOs (private; external callers use ConsumableDefinition) ─────

    private sealed class ConsumablesFile
    {
        [JsonPropertyName("consumables")]
        public List<RawConsumable>? Consumables { get; set; }
    }

    private sealed class RawConsumable
    {
        public string Id { get; set; } = "";
        public string MatchToken { get; set; } = "";
        public string EffectType { get; set; } = "";
        public int Amount { get; set; }
        public string? BuffKey { get; set; }
        public float BuffValue { get; set; }
        public string? PriorityGroup { get; set; }
        public int PriorityRank { get; set; }
    }

    // ─── Monsters ────────────────────────────────────────────────────────────

    private IReadOnlyList<MonsterDefinition> LoadMonsters()
    {
        var path = Path.Combine(_contentRoot, "monsters.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<MonstersFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Abilities is null || doc.Abilities.Count == 0)
            throw new InvalidDataException($"{path}: no abilities defined.");
        if (doc.Monsters is null || doc.Monsters.Count == 0)
            throw new InvalidDataException($"{path}: no monsters defined.");

        // Build ability table first — monsters reference abilities by id.
        var abilityTable = new Dictionary<string, CombatAbility>(StringComparer.Ordinal);
        foreach (var raw in doc.Abilities)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: ability missing id.");
            if (abilityTable.ContainsKey(raw.Id))
                throw new InvalidDataException($"{path}: duplicate ability id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: ability '{raw.Id}' missing name.");
            if (raw.BasePower < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative basePower.");
            if (raw.WeaveCost < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative weaveCost.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid element '{raw.Element}'.");
            if (!Enum.TryParse<AbilityTargetType>(raw.TargetType, ignoreCase: false, out var target))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid targetType '{raw.TargetType}'.");
            if (!Enum.TryParse<AbilityCategory>(raw.Category, ignoreCase: false, out var category))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid category '{raw.Category}'.");

            abilityTable[raw.Id] = new CombatAbility(
                raw.Name, raw.BasePower, raw.WeaveCost,
                element, target, category,
                raw.LifestealPower, raw.WyrdProcChance);
        }

        var list = new List<MonsterDefinition>(doc.Monsters.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Monsters)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: monster missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate monster id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: monster '{raw.Id}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.Biome) || !ValidBiomes.Contains(raw.Biome))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid biome '{raw.Biome}'. " +
                    $"Must be one of: {string.Join(", ", ValidBiomes)}.");
            if (raw.Tier < 0 || raw.Tier > 3)
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has tier {raw.Tier} outside range 0-3.");
            if (raw.Hp < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative hp.");
            if (raw.Speed < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative speed.");
            if (raw.Level < 1)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has level < 1.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid element '{raw.Element}'.");
            if (raw.Abilities is null || raw.Abilities.Count == 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has no abilities.");

            var abilities = new List<CombatAbility>(raw.Abilities.Count);
            foreach (var abilityId in raw.Abilities)
            {
                if (!abilityTable.TryGetValue(abilityId, out var ability))
                    throw new InvalidDataException(
                        $"{path}: monster '{raw.Id}' references unknown ability '{abilityId}'.");
                abilities.Add(ability);
            }

            list.Add(new MonsterDefinition(
                Id: raw.Id,
                Name: raw.Name,
                Biome: raw.Biome,
                Tier: raw.Tier,
                Hp: raw.Hp,
                Speed: raw.Speed,
                Level: raw.Level,
                Element: element,
                Abilities: abilities));
        }

        return list;
    }

    private sealed class MonstersFile
    {
        [JsonPropertyName("abilities")]
        public List<RawAbility>? Abilities { get; set; }

        [JsonPropertyName("monsters")]
        public List<RawMonster>? Monsters { get; set; }
    }

    private sealed class RawAbility
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int BasePower { get; set; }
        public int WeaveCost { get; set; }
        public string Element { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string Category { get; set; } = "";
        public float LifestealPower { get; set; }
        public float WyrdProcChance { get; set; }
    }

    private sealed class RawMonster
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Biome { get; set; } = "";
        public int Tier { get; set; }
        public int Hp { get; set; }
        public int Speed { get; set; }
        public int Level { get; set; }
        public string Element { get; set; } = "";
        public List<string>? Abilities { get; set; }
    }
}
