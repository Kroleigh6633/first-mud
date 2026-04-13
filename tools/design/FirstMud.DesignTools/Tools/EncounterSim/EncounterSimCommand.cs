using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.EncounterSim;

/// <summary>
/// SCAFFOLD. Will simulate N combat rolls against a monster with a chosen
/// party. For now: loads IContentProvider, probes for monsters content,
/// degrades gracefully if absent.
/// </summary>
public static class EncounterSimCommand
{
    public static int Run(string[] args)
    {
        var monster = Args.Value(args, "--monster") ?? "(unset)";
        var partyCsv = Args.Value(args, "--party") ?? "";
        var rolls = Args.IntValue(args, "--rolls") ?? 100;

        ConsolePretty.Header($"encounter-sim: --monster {monster} --rolls {rolls}");

        IContentProvider? content = null;
        try
        {
            content = new ContentProvider(RepoRoot.ContentDir());
            ConsolePretty.Good($"ContentProvider loaded ({content.Consumables.Count} consumables).");
        }
        catch (Exception ex)
        {
            ConsolePretty.Warn($"Could not load ContentProvider: {ex.Message}");
        }

        var monstersFile = Path.Combine(RepoRoot.ContentDir(), "monsters.json");
        if (File.Exists(monstersFile))
            ConsolePretty.Info($"monsters.json present ({new FileInfo(monstersFile).Length} bytes) — definitions not yet wired to provider.");
        else
            ConsolePretty.Warn("monsters.json not present. Parallel migration in progress.");

        ConsolePretty.Info($"party: [{partyCsv}]");
        ConsolePretty.Warn($"TODO: simulate {rolls} combat rolls against '{monster}'.");

        var log = new SimLog("encounter-sim");
        log.WriteJson(new
        {
            monster, party = partyCsv.Split(',', StringSplitOptions.RemoveEmptyEntries), rolls,
            status = "scaffold",
            monstersJsonExists = File.Exists(monstersFile),
            consumablesLoaded = content?.Consumables.Count ?? 0,
        });
        log.AppendMarkdown($"scaffold run vs {monster}",
            $"Scaffold. Monster=`{monster}`, rolls={rolls}, party=`{partyCsv}`. monsters.json present: {File.Exists(monstersFile)}.");
        return 0;
    }
}
