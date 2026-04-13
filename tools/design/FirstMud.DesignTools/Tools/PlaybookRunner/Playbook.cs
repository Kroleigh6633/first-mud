using System.Text.Json;
using System.Text.Json.Serialization;

namespace FirstMud.DesignTools.Tools.PlaybookRunner;

/// <summary>
/// POCO mirror of <c>tools/design/schemas/playbook.schema.json</c>.
/// Deserialized from the playbook JSON files under <c>tools/design/playbooks/</c>.
/// </summary>
public sealed class Playbook
{
    [JsonPropertyName("$schema")]  public string? Schema       { get; set; }
    [JsonPropertyName("id")]            public string Id            { get; set; } = "";
    [JsonPropertyName("displayName")]   public string DisplayName   { get; set; } = "";
    [JsonPropertyName("description")]   public string Description   { get; set; } = "";
    /// <summary>"combat" (default) or "crafting". Dispatches to the matching cell evaluator.</summary>
    [JsonPropertyName("kind")]          public string Kind          { get; set; } = "combat";
    [JsonPropertyName("holdouts")]      public Holdouts Holdouts    { get; set; } = new();
    [JsonPropertyName("axes")]          public List<Axis> Axes      { get; set; } = new();
    [JsonPropertyName("rolls")]         public int Rolls            { get; set; } = 200;
    [JsonPropertyName("seed")]          public int Seed             { get; set; } = 42;
    [JsonPropertyName("expectedViability")] public ExpectedViability ExpectedViability { get; set; } = new();
    [JsonPropertyName("toleranceBands")]    public Dictionary<string, double[]> ToleranceBands { get; set; } = new();
    /// <summary>Crafting-kind only: per-cell expected outcome distribution bands (percentages 0..100).</summary>
    [JsonPropertyName("expectedDistribution")] public ExpectedDistribution ExpectedDistribution { get; set; } = new();
    /// <summary>Crafting-kind only: per-cell holdouts (base ingredient quantity, player seed, display rounding).</summary>
    [JsonPropertyName("crafting")] public CraftingHoldouts Crafting { get; set; } = new();
}

public sealed class CraftingHoldouts
{
    /// <summary>Recipe base quantity for the single-ingredient sim (per ingredient, before seeding).</summary>
    [JsonPropertyName("baseQuantity")] public int BaseQuantity { get; set; } = 10;
    /// <summary>Player CraftingSeed used to compute seeded quantities. Varied-per-cell unless set.</summary>
    [JsonPropertyName("playerSeed")]   public int PlayerSeed   { get; set; } = 42;
    /// <summary>Rounding granularity the UI applies to the displayed quantity (current game: 5).</summary>
    [JsonPropertyName("displayRounding")] public int DisplayRounding { get; set; } = 5;
    /// <summary>How many different player seeds to average across per cell (reduces single-seed lottery).</summary>
    [JsonPropertyName("seedSpread")]   public int SeedSpread   { get; set; } = 16;
}

public sealed class ExpectedDistribution
{
    [JsonPropertyName("cells")] public List<ExpectedDistributionCell> Cells { get; set; } = new();
}

/// <summary>Free-form cell: axis keys at top level + "expected" → { outcomeName: [minPct, maxPct] }.</summary>
public sealed class ExpectedDistributionCell : Dictionary<string, JsonElement>
{
    public Dictionary<string, double[]> Expected()
    {
        var result = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (!TryGetValue("expected", out var v) || v.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in v.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var arr = prop.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetDouble()).ToArray();
            if (arr.Length == 2) result[prop.Name] = arr;
        }
        return result;
    }

    public int? Axis(string axisName)
    {
        if (!TryGetValue(axisName, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}

public sealed class Holdouts
{
    [JsonPropertyName("playerLevel")]   public int?     PlayerLevel   { get; set; }
    [JsonPropertyName("playerElement")] public string?  PlayerElement { get; set; }
    [JsonPropertyName("companions")]    public List<CompanionSpec>? Companions { get; set; }
    [JsonPropertyName("imbueLevel")]    public int?     ImbueLevel    { get; set; }
    [JsonPropertyName("gearTier")]      public int?     GearTier      { get; set; }
    [JsonPropertyName("monsterId")]     public string?  MonsterId     { get; set; }
    [JsonPropertyName("dangerLevel")]   public int?     DangerLevel   { get; set; }
    [JsonPropertyName("companionCount")] public int?    CompanionCount { get; set; }
    [JsonPropertyName("companionLayer")] public int?    CompanionLayer { get; set; }
}

public sealed class CompanionSpec
{
    [JsonPropertyName("layer")]   public int Layer      { get; set; } = 1;
    [JsonPropertyName("element")] public string Element { get; set; } = "Aether";
    [JsonPropertyName("level")]   public int Level      { get; set; } = 1;
    [JsonPropertyName("type")]    public string Type    { get; set; } = "Wildfolk";
}

public sealed class Axis
{
    [JsonPropertyName("name")]   public string Name   { get; set; } = "";
    [JsonPropertyName("values")] public int[]  Values { get; set; } = Array.Empty<int>();
}

public sealed class ExpectedViability
{
    [JsonPropertyName("cells")] public List<ExpectedCell> Cells { get; set; } = new();
}

/// <summary>Free-form cell: axis keys + "expected" band. Axis keys deserialize as JsonElement ints.</summary>
public sealed class ExpectedCell : Dictionary<string, JsonElement>
{
    public string Expected => TryGetValue("expected", out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() ?? ""
        : "";

    /// <summary>Returns the integer axis value for <paramref name="axisName"/>, or null if missing/non-int.</summary>
    public int? Axis(string axisName)
    {
        if (!TryGetValue(axisName, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}
