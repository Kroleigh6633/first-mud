using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.ScenarioPlayer;

public static class ScenarioPlayerCommand
{
    public static int Run(string[] args)
    {
        var fixture = Args.Value(args, "--fixture")
                      ?? Path.Combine(RepoRoot.FixturesDir(), "quest-sample.json");
        var chooseCsv = Args.Value(args, "--choose");
        var forced = string.IsNullOrWhiteSpace(chooseCsv)
            ? null
            : chooseCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        ConsolePretty.Header($"scenario-player: {Path.GetFileName(fixture)}");
        var spec = FixtureLoader.Load<QuestSpec>(fixture);

        ConsolePretty.Info($"Quest: {spec.Id} — {spec.Title}");
        if (!string.IsNullOrWhiteSpace(spec.Description))
            ConsolePretty.Info(spec.Description);
        ConsolePretty.Info($"Beats: {spec.Beats.Count}, root: {spec.Root}");
        Console.WriteLine();

        var runner = new ScenarioRunner();
        var result = runner.Run(spec, forced);

        foreach (var step in result.Steps)
        {
            ConsolePretty.Beat($"{step.BeatId}: {step.Description}");
            foreach (var a in step.AvailableChoices) ConsolePretty.Choice(a);
            if (step.ChosenChoiceId is not null)
                ConsolePretty.Delta($"chose -> {step.ChosenChoiceId}");
            foreach (var d in step.Deltas) ConsolePretty.Delta(d);
            foreach (var w in step.RequirementWarnings) ConsolePretty.Warn(w);
            if (step.Terminal) ConsolePretty.Good($"terminal: {step.Outcome}");
        }
        foreach (var e in result.Errors) ConsolePretty.Error(e);

        Console.WriteLine();
        ConsolePretty.Info($"Final outcome: {result.FinalOutcome}");

        var log = new SimLog("scenario-player");
        var jsonPath = log.WriteJson(result);
        var mdPath = log.AppendMarkdown(spec.Title, result.ToMarkdown());
        ConsolePretty.Info($"json -> {jsonPath}");
        ConsolePretty.Info($"md   -> {mdPath}");

        return result.Errors.Count == 0 ? 0 : 1;
    }
}
