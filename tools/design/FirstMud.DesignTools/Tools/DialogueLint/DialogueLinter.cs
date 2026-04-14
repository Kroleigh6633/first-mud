using System.Text;

namespace FirstMud.DesignTools.Tools.DialogueLint;

/// <summary>
/// Validates dialogue trees: unique ids, resolvable next refs, no orphans,
/// reputation gates (min and max) are reachable from fresh-start, min&lt;=max
/// ordering, every leaf is terminal or leads somewhere.
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

        // Min <= Max consistency per option (same faction).
        foreach (var n in tree.Nodes)
        {
            foreach (var opt in n.Options)
            {
                if (opt.RequiresReputation is { } min && opt.RequiresReputationMax is { } max
                    && string.Equals(min.Faction, max.Faction, StringComparison.Ordinal)
                    && min.Min > max.Max)
                {
                    report.Errors.Add(
                        $"Node '{n.Id}' option '{opt.Text}' has requiresReputation.min ({min.Min}) > " +
                        $"requiresReputationMax.max ({max.Max}) for faction '{min.Faction}'.");
                }
            }
        }

        // Resolve references + reachability BFS from roots.
        var reachableAny = new HashSet<string>();
        var bestRepOnEntry = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var worstRepOnEntry = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var root in tree.Roots.Where(seen.ContainsKey))
        {
            Walk(root, seen, reachableAny);
            WalkWithRep(root, seen, tree.StartingReputation, bestRepOnEntry, worstRepOnEntry);
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

        // Rep-gate reachability — compare gates against best/worst rep reachable at each node.
        foreach (var n in tree.Nodes)
        {
            bestRepOnEntry.TryGetValue(n.Id, out var best);
            worstRepOnEntry.TryGetValue(n.Id, out var worst);

            foreach (var opt in n.Options)
            {
                if (opt.RequiresReputation is { } min)
                {
                    var haveMax = best is null ? GetRep(min.Faction, tree.StartingReputation)
                                               : GetRep(min.Faction, best);
                    if (haveMax < min.Min)
                    {
                        report.Warnings.Add(
                            $"Node '{n.Id}' option '{opt.Text}' gated by {min.Faction}>={min.Min} " +
                            $"but best reachable rep here is {haveMax}. " +
                            $"Ensure an off-tree path grants this reputation.");
                    }
                }

                if (opt.RequiresReputationMax is { } max)
                {
                    var haveMin = worst is null ? GetRep(max.Faction, tree.StartingReputation)
                                                : GetRep(max.Faction, worst);
                    if (haveMin > max.Max)
                    {
                        report.Warnings.Add(
                            $"Node '{n.Id}' option '{opt.Text}' gated by {max.Faction}<={max.Max} " +
                            $"but worst reachable rep here is {haveMin} (a prior node's effect pushes rep above the ceiling). " +
                            $"This low-rep branch is unreachable on this path.");
                    }
                }
            }
        }

        report.NodeCount = tree.Nodes.Count;
        report.ReachableCount = reachableAny.Count;
        return report;
    }

    private static void Walk(string start, Dictionary<string, DialogueNode> nodes, HashSet<string> visited)
    {
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            if (!nodes.TryGetValue(id, out var node)) continue;
            foreach (var opt in node.Options)
                if (!string.IsNullOrEmpty(opt.Next))
                    stack.Push(opt.Next);
        }
    }

    /// <summary>
    /// Walks the tree tracking per-faction best (max) and worst (min) reputation
    /// the player could have when arriving at each node, given the starting rep
    /// plus any Effects applied on entered nodes along the path. Only follows
    /// edges whose min/max gates could plausibly be satisfied by the current rep
    /// state at that step.
    /// </summary>
    private static void WalkWithRep(
        string start,
        Dictionary<string, DialogueNode> nodes,
        Dictionary<string, int> startingRep,
        Dictionary<string, Dictionary<string, int>> bestRepOnEntry,
        Dictionary<string, Dictionary<string, int>> worstRepOnEntry)
    {
        var queue = new Queue<(string id, Dictionary<string, int> rep)>();
        queue.Enqueue((start, new Dictionary<string, int>(startingRep, StringComparer.Ordinal)));

        // Cap iterations to guard against pathological cycles with unbounded rep growth.
        var steps = 0;
        var maxSteps = Math.Max(1000, nodes.Count * 32);

        while (queue.Count > 0 && steps++ < maxSteps)
        {
            var (id, rep) = queue.Dequeue();
            if (!nodes.TryGetValue(id, out var node)) continue;

            var changedBest = MergeRep(bestRepOnEntry, id, rep, keepMax: true);
            var changedWorst = MergeRep(worstRepOnEntry, id, rep, keepMax: false);
            if (!changedBest && !changedWorst) continue;

            // Apply node effects (rep deltas) before traversing outbound edges.
            var afterEffects = new Dictionary<string, int>(rep, StringComparer.Ordinal);
            foreach (var (faction, delta) in node.Effects)
            {
                afterEffects.TryGetValue(faction, out var cur);
                afterEffects[faction] = cur + delta;
            }

            foreach (var opt in node.Options)
            {
                if (string.IsNullOrEmpty(opt.Next)) continue;
                if (opt.RequiresReputation is { } min
                    && GetRep(min.Faction, afterEffects) < min.Min) continue;
                if (opt.RequiresReputationMax is { } max
                    && GetRep(max.Faction, afterEffects) > max.Max) continue;

                queue.Enqueue((opt.Next, new Dictionary<string, int>(afterEffects, StringComparer.Ordinal)));
            }
        }
    }

    private static bool MergeRep(
        Dictionary<string, Dictionary<string, int>> bag,
        string id,
        Dictionary<string, int> rep,
        bool keepMax)
    {
        if (!bag.TryGetValue(id, out var cur))
        {
            bag[id] = new Dictionary<string, int>(rep, StringComparer.Ordinal);
            return true;
        }
        var changed = false;
        foreach (var (k, v) in rep)
        {
            if (!cur.TryGetValue(k, out var existing))
            {
                cur[k] = v;
                changed = true;
            }
            else if (keepMax ? v > existing : v < existing)
            {
                cur[k] = v;
                changed = true;
            }
        }
        return changed;
    }

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
