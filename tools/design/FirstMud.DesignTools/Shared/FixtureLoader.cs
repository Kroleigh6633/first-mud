using System.Text.Json;

namespace FirstMud.DesignTools.Shared;

/// <summary>Loads JSON fixtures with case-insensitive, comment-tolerant parsing.</summary>
public static class FixtureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Load<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}", path);
        using var stream = File.OpenRead(path);
        var result = JsonSerializer.Deserialize<T>(stream, JsonOptions);
        if (result is null)
            throw new InvalidDataException($"Fixture empty or unparseable: {path}");
        return result;
    }
}
