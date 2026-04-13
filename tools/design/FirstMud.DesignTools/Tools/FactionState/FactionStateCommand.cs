using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.FactionState;

/// <summary>
/// SCAFFOLD. Loads faction data from lore/factions.md (or a placeholder JSON)
/// and prints what's loaded. Rendering the actual state at T is a TODO.
///
/// Design: a faction-timeline spec (YAML/JSON) supplies ordered events with
/// delta rules per beat-marker or timestep. This tool will fold those events
/// to produce the faction state at marker T, outputting relationship matrix,
/// active treaties, and ambient tension levels.
/// </summary>
public static class FactionStateCommand
{
    public static int Run(string[] args)
    {
        var at = Args.Value(args, "--at") ?? "T0";
        ConsolePretty.Header($"faction-state: --at {at}");

        var lorePath = Path.Combine(RepoRoot.Find(), "lore", "factions.md");
        if (File.Exists(lorePath))
        {
            var lines = File.ReadAllLines(lorePath).Length;
            ConsolePretty.Info($"Loaded faction lore: {lorePath} ({lines} lines).");
        }
        else
        {
            ConsolePretty.Warn($"No faction lore at {lorePath}; using placeholder set (Wytchwood, Ironlight, Hollow Court).");
        }

        ConsolePretty.Warn($"TODO: render faction state at marker '{at}'.");
        ConsolePretty.Info("See docs/design/sim-logs for output once implemented.");

        var log = new SimLog("faction-state");
        var payload = new
        {
            marker = at,
            status = "scaffold",
            loreLoaded = File.Exists(lorePath),
            notes = "Tool is scaffolded; implementation pending.",
        };
        log.WriteJson(payload);
        log.AppendMarkdown($"scaffold run @{at}", $"Scaffold run. Marker: `{at}`. Lore file present: {File.Exists(lorePath)}.");
        return 0;
    }
}
