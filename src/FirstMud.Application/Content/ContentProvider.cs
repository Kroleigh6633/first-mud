using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly string _contentRoot;
    private readonly ILogger<ContentProvider>? _logger;

    private IReadOnlyList<ConsumableDefinition> _consumables = Array.Empty<ConsumableDefinition>();

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

    public void Reload()
    {
        _consumables = LoadConsumables();
        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables from {Root}",
            _consumables.Count, _contentRoot);
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
}
