using System.Text;
using System.Text.Json;

namespace FirstMud.DesignTools.Shared;

/// <summary>
/// Writes tool output as (1) a timestamped JSON file and (2) appended markdown
/// under docs/design/sim-logs/YYYY-MM-DD.md. Callers hand it a tool name, a
/// human summary, and a serializable payload.
/// </summary>
public sealed class SimLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string ToolName { get; }
    public DateTime Timestamp { get; }

    public SimLog(string toolName)
    {
        ToolName = toolName;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>Writes the JSON output file and returns its absolute path.</summary>
    public string WriteJson(object payload)
    {
        var stamp = Timestamp.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(RepoRoot.SimLogsDir(), $"{ToolName}-{stamp}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        return path;
    }

    /// <summary>Appends a markdown section to today's sim-log file and returns its path.</summary>
    public string AppendMarkdown(string title, string body)
    {
        var day = Timestamp.ToString("yyyy-MM-dd");
        var path = Path.Combine(RepoRoot.SimLogsDir(), $"{day}.md");
        var sb = new StringBuilder();
        if (!File.Exists(path))
        {
            sb.AppendLine($"# Design Sim Log — {day}");
            sb.AppendLine();
        }
        sb.AppendLine($"## {Timestamp:HH:mm:ss}Z — {ToolName} — {title}");
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine();
        File.AppendAllText(path, sb.ToString());
        return path;
    }
}
