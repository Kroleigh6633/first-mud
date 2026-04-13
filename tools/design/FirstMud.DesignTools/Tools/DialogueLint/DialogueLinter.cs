using System.Text;

namespace FirstMud.DesignTools.Tools.DialogueLint;

/// <summary>
/// Validates dialogue trees: unique ids, resolvable next refs, no orphans,
/// reputation gates are reachable from fresh-start, every leaf is terminal
/// or leads somewhere.
/// </summary>
public sealed class DialogueLinter
{
    public LintReport Lint(DialogueTree tree)
    {
        var report = new LintReport { NpcId = tree.NpcId };
        var seen = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);

        foreach (var n in tree.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id))
            {
                report.Errors.Add("Node with blank id.");
                continue;
            }
            if (!seen.TryAdd(n.Id, n))
                report.Errors.Add($"Duplicate node id '{n.Id}'.");
        }

        foreach (var root in tree.Roots)
            if (!seen.ContainsKey(root))
                report.Errors.Add($"Root '{root}' not found among nodes.");

        // Resolve references + reachability BFS from roots.
        var reachableAny = new HashSet<string>();
        var reachableFresh = new HashSet<string>();

        foreach (var root in tree.Roots.Where(seen.ContainsKey))
        {
            Walk(root, seen, reachableAny, _ => true);
            Walk(root, seen, reachableFresh, opt =>
                opt.RequiresReputation is null
                || CanReach(opt.RequiresReputation, tree.StartingReputation));
        }

        // next-ref resolution + non-terminal leaves
        foreach (var n in tree.Nodes)
        {
            foreach (var opt in n.Options)
            {
                if (!string.IsNullOrEmpty(opt.Next) && !seen.ContainsKey(opt.Next))
                    report.Errors.Add($"Node '{n.Id}' option '{opt.Text}' -> unknown next '{opt.Next}'.");
                if (string.IsNullOrEmpty(opt.Next))
                    report.Warnings.Add($"Node '{n.Id}' option '{opt.Text}' has no next and is not terminal.");
            }

            if (!n.Terminal && n.Options.Count == 0)
                report.Warnings.Add($"Node '{n.Id}' is a non-terminal leaf with no options (dead-end).");
        }

        foreach (var id in seen.Keys)
            if (!reachableAny.Contains(id))
                report.Warnings.Add($"Orphan node '{id}' — not reachable from any root.");

        // Reputation gates that can never be met from fresh start.
        foreach (var n in tree.Nodes)
        {
            foreach (var opt in n.Options)
            {
                if (opt.RequiresReputation is null) continue;
                if (!CanReach(opt.RequiresReputation, tree.StartingReputation))
                    report.Warnings.Add(
                        $"Node '{n.Id}' option '{opt.Text}' gated by {opt.RequiresReputation.Faction}>={opt.RequiresReputation.Min} " +
                        $"but starting rep is {GetRep(opt.RequiresReputation.Faction, tree.StartingReputation)}. " +
                        $"Ensure an off-tree path grants this reputation.");
            }
        }

        report.NodeCount = tree.Nodes.Count;
        report.ReachableCount = reachableAny.Count;
        return report;
    }

    private static void Walk(string start, Dictionary<string, DialogueNode> nodes,
        HashSet<string> visited, Func<DialogueOption, bool> optionPredicate)
    {
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            if (!nodes.TryGetValue(id, out var node)) continue;
            foreach (var opt in node.Options)
                if (optionPredicate(opt) && !string.IsNullOrEmpty(opt.Next))
                    stack.Push(opt.Next);
        }
    }

    // Very conservative: starting rep must already meet min. (Rep gains come from
    // quest chains, not dialogue itself; scenario-player validates that path.)
    private static bool CanReach(RepRequirement r, Dictionary<string, int> startingRep) =>
        GetRep(r.Faction, startingRep) >= r.Min;

    private static int GetRep(string faction, Dictionary<string, int> rep) =>
        rep.TryGetValue(faction, out var v) ? v : 0;
}

public sealed class LintReport
{
    public string NpcId { get; set; } = "";
    public int NodeCount { get; set; }
    public int ReachableCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**NPC:** `{NpcId}`  ");
        sb.AppendLine($"**Nodes:** {NodeCount} (reachable: {ReachableCount})  ");
        sb.AppendLine($"**Errors:** {Errors.Count}  **Warnings:** {Warnings.Count}");
        sb.AppendLine();
        if (Errors.Count > 0)
        {
            sb.AppendLine("### Errors");
            foreach (var e in Errors) sb.AppendLine($"- {e}");
            sb.AppendLine();
        }
        if (Warnings.Count > 0)
        {
            sb.AppendLine("### Warnings");
            foreach (var w in Warnings) sb.AppendLine($"- {w}");
        }
        if (Errors.Count == 0 && Warnings.Count == 0)
            sb.AppendLine("No issues found.");
        return sb.ToString();
    }
}
