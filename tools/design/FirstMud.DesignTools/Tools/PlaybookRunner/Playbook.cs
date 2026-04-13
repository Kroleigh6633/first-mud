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
    [JsonPropertyName("expectedMetrics")]   public ExpectedMetrics? ExpectedMetrics { get; set; }
}

/// <summary>
/// Axis values may be strings (e.g. zoneIds) for the flow evaluator, not just ints.
/// For combat playbooks we still expose <see cref="Axis"/> with int values; for flow
/// playbooks use <see cref="StringAxis"/> instead. The deserializer auto-picks based
/// on JSON value type; see PlaybookEngine.Cartesian for enumeration.
/// </summary>
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
