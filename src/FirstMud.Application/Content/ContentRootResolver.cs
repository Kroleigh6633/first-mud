namespace FirstMud.Application.Content;

/// <summary>
/// Locates the repo-root <c>content/</c> folder at runtime.
///
/// The folder isn't copied into bin/ — content files live in source control
/// and are read in place. At runtime we start from AppContext.BaseDirectory
/// (the bin folder) and walk upward until we find a sibling <c>content/</c>
/// directory. An explicit <c>FIRSTMUD_CONTENT_ROOT</c> env var wins.
/// </summary>
public static class ContentRootResolver
{
    public const string EnvVar = "FIRSTMUD_CONTENT_ROOT";

    public static string Resolve()
    {
        var envOverride = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(envOverride) && Directory.Exists(envOverride))
            return envOverride;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "consumables.json")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate content/ folder. Set the FIRSTMUD_CONTENT_ROOT " +
            "environment variable or run from within the repo.");
    }
}
