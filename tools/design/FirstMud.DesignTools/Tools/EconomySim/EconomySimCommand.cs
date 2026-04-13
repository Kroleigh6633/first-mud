using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.EconomySim;

/// <summary>
/// SCAFFOLD. Will simulate N hours of play for a given scenario
/// (farming-heavy, combat-heavy, balanced). For now: loads content and
/// prints what's available.
/// </summary>
public static class EconomySimCommand
{
    public static int Run(string[] args)
    {
        var hours = Args.IntValue(args, "--hours") ?? 24;
        var scenario = Args.Value(args, "--scenario") ?? "balanced";

        ConsolePretty.Header($"economy-sim: --hours {hours} --scenario {scenario}");

        IContentProvider? content = null;
        try
        {
            content = new ContentProvider(RepoRoot.ContentDir());
            ConsolePretty.Good($"ContentProvider loaded: {content.Consumables.Count} consumables.");
        }
        catch (Exception ex)
        {
            ConsolePretty.Warn($"Could not load ContentProvider: {ex.Message}");
        }

        var recipes = Path.Combine(RepoRoot.ContentDir(), "recipes.json");
        var buildings = Path.Combine(RepoRoot.ContentDir(), "buildings.json");
        ConsolePretty.Info($"recipes.json present: {File.Exists(recipes)}");
        ConsolePretty.Info($"buildings.json present: {File.Exists(buildings)}");

        ConsolePretty.Warn($"TODO: simulate {hours} hours of scenario '{scenario}'.");

        var log = new SimLog("economy-sim");
        log.WriteJson(new
        {
            hours, scenario,
            status = "scaffold",
            consumablesLoaded = content?.Consumables.Count ?? 0,
            recipesPresent = File.Exists(recipes),
            buildingsPresent = File.Exists(buildings),
        });
        log.AppendMarkdown($"scaffold {scenario} {hours}h",
            $"Scaffold. hours={hours}, scenario=`{scenario}`. recipes.json: {File.Exists(recipes)}, buildings.json: {File.Exists(buildings)}.");
        return 0;
    }
}
