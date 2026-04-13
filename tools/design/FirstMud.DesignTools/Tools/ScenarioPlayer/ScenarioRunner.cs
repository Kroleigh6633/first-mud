using System.Text;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.ScenarioPlayer;

/// <summary>
/// Steps through a QuestSpec beat-by-beat. Deterministic path via --choose,
/// otherwise picks the first eligible choice at each beat.
/// </summary>
public sealed class ScenarioRunner
{
    public ScenarioResult Run(QuestSpec spec, IReadOnlyList<string>? forcedChoices = null)
    {
        var state = new RunState
        {
            Flags = new HashSet<string>(spec.StartState.Flags),
            Inventory = spec.StartState.Inventory.ToDictionary(i => i.Key, i => i.Amount),
            Reputation = new Dictionary<string, int>(spec.StartState.Reputation),
        };

        var beatMap = spec.Beats.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var result = new ScenarioResult { QuestId = spec.Id, Title = spec.Title };

        if (!beatMap.ContainsKey(spec.Root))
        {
            result.Errors.Add($"Root beat '{spec.Root}' not found.");
            return result;
        }

        var queue = forcedChoices is null ? new Queue<string>() : new Queue<string>(forcedChoices);
        var currentId = spec.Root;
        var visited = new HashSet<string>();
        var guard = 0;

        while (true)
        {
            if (guard++ > 500) { result.Errors.Add("Beat step guard exceeded (possible loop)."); break; }
            if (!beatMap.TryGetValue(currentId, out var beat))
            {
                result.Errors.Add($"Beat reference '{currentId}' not found.");
                break;
            }

            var step = new ScenarioStep { BeatId = beat.Id, Description = beat.Description };
            visited.Add(beat.Id);

            if (beat.Requires is not null && !MeetsRequirements(beat.Requires, state, out var missing))
                step.RequirementWarnings.AddRange(missing);

            if (beat.Terminal || beat.Choices.Count == 0)
            {
                step.Terminal = true;
                step.Outcome = beat.Outcome ?? (beat.Terminal ? "terminal" : "dead-end");
                result.Steps.Add(step);
                result.FinalOutcome = step.Outcome;
                break;
            }

            var chosen = PickChoice(beat, state, queue, step);
            if (chosen is null)
            {
                step.Terminal = true;
                step.Outcome = "no-eligible-choice";
                result.Steps.Add(step);
                result.FinalOutcome = step.Outcome;
                break;
            }

            foreach (var eff in chosen.Effects)
            {
                var delta = ApplyEffect(eff, state);
                step.Deltas.Add(delta);
            }

            step.ChosenChoiceId = chosen.Id;
            step.ChosenChoiceText = chosen.Text;
            result.Steps.Add(step);

            if (string.IsNullOrEmpty(chosen.Next))
            {
                result.FinalOutcome = "choice-had-no-next";
                break;
            }
            currentId = chosen.Next;
        }

        result.FinalState = StateSnapshot(state);
        return result;
    }

    private static QuestChoice? PickChoice(QuestBeat beat, RunState state, Queue<string> forced, ScenarioStep step)
    {
        // Surface available choices.
        foreach (var c in beat.Choices)
            step.AvailableChoices.Add($"{c.Id}: {c.Text}");

        if (forced.Count > 0)
        {
            var wanted = forced.Dequeue();
            var pick = beat.Choices.FirstOrDefault(c => c.Id == wanted);
            if (pick is null)
                step.RequirementWarnings.Add($"forced choice '{wanted}' not found at beat '{beat.Id}'; falling back.");
            else
                return pick;
        }

        // First choice is the default path.
        return beat.Choices.FirstOrDefault();
    }

    private static bool MeetsRequirements(QuestRequires req, RunState state, out List<string> missing)
    {
        missing = new();
        foreach (var f in req.Flags)
            if (!state.Flags.Contains(f)) missing.Add($"missing flag '{f}'");
        foreach (var i in req.Items)
            if (!state.Inventory.TryGetValue(i.Key, out var have) || have < i.Amount)
                missing.Add($"missing item '{i.Key}' x{i.Amount}");
        foreach (var kv in req.Reputation)
            if (!state.Reputation.TryGetValue(kv.Key, out var have) || have < kv.Value)
                missing.Add($"reputation '{kv.Key}' below {kv.Value}");
        return missing.Count == 0;
    }

    private static string ApplyEffect(QuestEffect eff, RunState state)
    {
        switch (eff.Type)
        {
            case "setFlag":
                state.Flags.Add(eff.Key); return $"flag+ {eff.Key}";
            case "clearFlag":
                state.Flags.Remove(eff.Key); return $"flag- {eff.Key}";
            case "addItem":
                state.Inventory[eff.Key] = (state.Inventory.TryGetValue(eff.Key, out var a) ? a : 0) + eff.Amount;
                return $"item+ {eff.Key} x{eff.Amount}";
            case "removeItem":
                state.Inventory[eff.Key] = Math.Max(0, (state.Inventory.TryGetValue(eff.Key, out var r) ? r : 0) - eff.Amount);
                return $"item- {eff.Key} x{eff.Amount}";
            case "addReputation":
                state.Reputation[eff.Key] = (state.Reputation.TryGetValue(eff.Key, out var rep) ? rep : 0) + eff.Amount;
                return $"rep+ {eff.Key} {eff.Amount:+#;-#;0}";
            default:
                return $"unknown-effect {eff.Type}";
        }
    }

    private static Dictionary<string, object> StateSnapshot(RunState state) => new()
    {
        ["flags"] = state.Flags.OrderBy(f => f).ToList(),
        ["inventory"] = state.Inventory.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => (object)kv.Value),
        ["reputation"] = state.Reputation.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => (object)kv.Value),
    };

    private sealed class RunState
    {
        public HashSet<string> Flags { get; set; } = new();
        public Dictionary<string, int> Inventory { get; set; } = new();
        public Dictionary<string, int> Reputation { get; set; } = new();
    }
}

public sealed class ScenarioResult
{
    public string QuestId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ScenarioStep> Steps { get; set; } = new();
    public Dictionary<string, object> FinalState { get; set; } = new();
    public string? FinalOutcome { get; set; }
    public List<string> Errors { get; set; } = new();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Quest:** `{QuestId}` — {Title}  ");
        sb.AppendLine($"**Outcome:** {FinalOutcome ?? "(none)"}  ");
        sb.AppendLine($"**Steps:** {Steps.Count}");
        sb.AppendLine();
        foreach (var s in Steps)
        {
            sb.AppendLine($"- **{s.BeatId}** — {s.Description}");
            if (s.ChosenChoiceId is not null)
                sb.AppendLine($"  - choice: `{s.ChosenChoiceId}` — {s.ChosenChoiceText}");
            foreach (var d in s.Deltas) sb.AppendLine($"  - delta: {d}");
            foreach (var w in s.RequirementWarnings) sb.AppendLine($"  - warn: {w}");
            if (s.Terminal) sb.AppendLine($"  - terminal ({s.Outcome})");
        }
        if (Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Errors:**");
            foreach (var e in Errors) sb.AppendLine($"- {e}");
        }
        return sb.ToString();
    }
}

public sealed class ScenarioStep
{
    public string BeatId { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> AvailableChoices { get; set; } = new();
    public string? ChosenChoiceId { get; set; }
    public string? ChosenChoiceText { get; set; }
    public List<string> Deltas { get; set; } = new();
    public List<string> RequirementWarnings { get; set; } = new();
    public bool Terminal { get; set; }
    public string? Outcome { get; set; }
}
