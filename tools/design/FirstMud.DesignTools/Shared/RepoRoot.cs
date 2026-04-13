namespace FirstMud.DesignTools.Shared;

/// <summary>Walks upward from the executing assembly to find the repo root
/// (marker: FirstMud.slnx). Works regardless of CWD.</summary>
public static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FirstMud.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (FirstMud.slnx) from " + AppContext.BaseDirectory);
    }

    public static string ContentDir() => Path.Combine(Find(), "content");
    public static string FixturesDir() => Path.Combine(Find(), "tools", "design", "fixtures");
    public static string SimLogsDir()
    {
        var p = Path.Combine(Find(), "docs", "design", "sim-logs");
        Directory.CreateDirectory(p);
        return p;
    }
}
