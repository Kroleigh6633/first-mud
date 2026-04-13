using System.Text.Json;

namespace FirstMud.GameServer.Hubs.Parsers;

/// <summary>
/// Shared JsonElement helpers for command parsers. Keeps the parser classes tiny
/// and centralises tolerance for malformed/missing payload fields (same behaviour
/// the original GameHub switch had: default everything on parse failure).
/// </summary>
internal static class CommandPayloadExtensions
{
    public static int TryGetInt(this JsonElement payload, string key)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(key, out var prop)
            && prop.TryGetInt32(out var val))
            return val;
        return 0;
    }

    public static Guid TryGetGuid(this JsonElement payload, string key)
        => payload.TryGetNullableGuid(key) ?? Guid.Empty;

    public static Guid? TryGetNullableGuid(this JsonElement payload, string key)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(key, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var guid))
            return guid;
        return null;
    }

    public static string? TryGetString(this JsonElement payload, string key)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(key, out var prop)
            && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    public static List<Guid> TryGetGuidList(this JsonElement payload, string key)
    {
        var result = new List<Guid>();
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(key, out var prop)
            || prop.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && Guid.TryParse(item.GetString(), out var guid))
                result.Add(guid);
        }
        return result;
    }

    public static int? TryGetNullableIntFromNested(this JsonElement payload, string outerKey, string innerKey)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(outerKey, out var outer)
            || outer.ValueKind != JsonValueKind.Object
            || !outer.TryGetProperty(innerKey, out var prop)
            || !prop.TryGetInt32(out var val))
            return null;
        return val;
    }
}
