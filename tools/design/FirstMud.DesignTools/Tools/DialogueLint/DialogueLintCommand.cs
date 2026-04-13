using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.DialogueLint;

public static class DialogueLintCommand
{
    public static int Run(string[] args)
    {
        var fixture = Args.Value(args, "--fixture")
                      ?? Path.Combine(RepoRoot.FixturesDir(), "dialogue-sample.json");

        ConsolePretty.Header($"dialogue-lint: {Path.GetFileName(fixture)}");
        var tree = FixtureLoader.Load<DialogueTree>(fixture);
        var report = new DialogueLinter().Lint(tree);

        ConsolePretty.Info($"NPC: {report.NpcId}");
        ConsolePretty.Info($"Nodes: {report.NodeCount} (reachable: {report.ReachableCount})");
        foreach (var e in report.Errors) ConsolePretty.Error(e);
        foreach (var w in report.Warnings) ConsolePretty.Warn(w);
        if (report.Errors.Count == 0 && report.Warnings.Count == 0)
            ConsolePretty.Good("No issues found.");

        var log = new SimLog("dialogue-lint");
        var jsonPath = log.WriteJson(report);
        var mdPath = log.AppendMarkdown($"NPC {report.NpcId}", report.ToMarkdown());
        ConsolePretty.Info($"json -> {jsonPath}");
        ConsolePretty.Info($"md   -> {mdPath}");

        return report.Errors.Count == 0 ? 0 : 1;
    }
}
