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
    /// <summary>"combat" (default), "crafting", "capture", or "flow". Dispatches to the matching cell evaluator.</summary>
    [JsonPropertyName("kind")]          public string Kind          { get; set; } = "combat";
    [JsonPropertyName("displayName")]   public string DisplayName   { get; set; } = "";
    [JsonPropertyName("description")]   public string Description   { get; set; } = "";
    [JsonPropertyName("holdouts")]      public Holdouts Holdouts    { get; set; } = new();
    [JsonPropertyName("axes")]          public List<Axis> Axes      { get; set; } = new();
    [JsonPropertyName("rolls")]         public int Rolls            { get; set; } = 200;
    [JsonPropertyName("seed")]          public int Seed             { get; set; } = 42;
    [JsonPropertyName("simulatedMinutes")] public int SimulatedMinutes { get; set; } = 30;
    [JsonPropertyName("expectedViability")] public ExpectedViability ExpectedViability { get; set; } = new();
    [JsonPropertyName("toleranceBands")]    public Dictionary<string, double[]> ToleranceBands { get; set; } = new();
    /// <summary>Crafting-kind only: per-cell expected outcome distribution bands (percentages 0..100).</summary>
    [JsonPropertyName("expectedDistribution")] public ExpectedDistribution ExpectedDistribution { get; set; } = new();
    /// <summary>Crafting-kind only: per-cell holdouts (base ingredient quantity, player seed, display rounding).</summary>
    [JsonPropertyName("crafting")] public CraftingHoldouts Crafting { get; set; } = new();
    /// <summary>Flow-kind only: per-cell expected metric ranges.</summary>
    [JsonPropertyName("expectedMetrics")]   public ExpectedMetrics? ExpectedMetrics { get; set; }
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

/// <summary>Flow-kind only: per-cell expected metric ranges container.</summary>
public sealed class ExpectedMetrics
{
    [JsonPropertyName("cells")] public List<ExpectedMetricCell> Cells { get; set; } = new();
}

/// <summary>Free-form cell: axis keys + expected metric ranges for flow runs.</summary>
public sealed class ExpectedMetricCell : Dictionary<string, JsonElement>
{
    public string? String(string key) => TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public int? Int(string key)
        => TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    public double[]? Range(string key)
    {
        if (!TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var arr = new List<double>();
        foreach (var el in v.EnumerateArray())
            if (el.ValueKind == JsonValueKind.Number) arr.Add(el.GetDouble());
        return arr.Count == 2 ? arr.ToArray() : null;
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
    [JsonPropertyName("values")] [JsonConverter(typeof(AxisValuesConverter))]
    public AxisValues Values { get; set; } = new();
}

/// <summary>Axis values — either integer (combat playbooks) or string (flow playbooks).</summary>
public sealed class AxisValues
{
    public int[]    Ints    { get; init; } = Array.Empty<int>();
    public string[] Strings { get; init; } = Array.Empty<string>();
    public int Length => Ints.Length > 0 ? Ints.Length : Strings.Length;
    public bool IsString => Strings.Length > 0;
    public object Get(int i) => IsString ? Strings[i] : Ints[i];
    public static implicit operator AxisValues(int[] ints) => new() { Ints = ints };
    public static implicit operator AxisValues(string[] strs) => new() { Strings = strs };
}

public sealed class AxisValuesConverter : JsonConverter<AxisValues>
{
    public override AxisValues Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) throw new JsonException("axis values must be an array");
        var ints = new List<int>();
        var strs = new List<string>();
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) ints.Add(n);
            else if (el.ValueKind == JsonValueKind.String) strs.Add(el.GetString() ?? "");
        }
        if (strs.Count > 0 && ints.Count > 0)
            throw new JsonException("axis values must be all-int or all-string");
        return new AxisValues { Ints = ints.ToArray(), Strings = strs.ToArray() };
    }
    public override void Write(Utf8JsonWriter writer, AxisValues value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (value.IsString) foreach (var s in value.Strings) writer.WriteStringValue(s);
        else foreach (var i in value.Ints) writer.WriteNumberValue(i);
        writer.WriteEndArray();
    }
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
