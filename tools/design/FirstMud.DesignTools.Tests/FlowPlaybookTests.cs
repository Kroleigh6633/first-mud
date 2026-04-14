using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using FirstMud.DesignTools.Tools.PlaybookRunner.AutoQuestFlow;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Covers the flow cell-evaluator and the AutoQuestSimulationService.
/// These tests exercise the data path: every authored quest is either
/// completed by the sim or categorised as a specific stall reason.
/// </summary>
public class FlowPlaybookTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    [Fact]
    public void Auto_quest_sim_classifies_every_authored_quest()
    {
        var svc = new AutoQuestSimulationService(Content());
        var runs = svc.RunAll();

        Assert.NotEmpty(runs);
        foreach (var r in runs)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.QuestId));
            // Every run resolves to a known outcome bucket — no "undefined" case.
            Assert.True(Enum.IsDefined(typeof(AutoQuestSimulationService.Outcome), r.Outcome));
        }
    }

    [Fact]
    public void Flow_playbook_runs_and_shipped_playbook_is_loadable()
    {
        // The shipped auto-quest-completion.json should deserialize cleanly and
        // the engine should dispatch it to the flow evaluator without throwing.
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "auto-quest-completion.json");
        Assert.True(File.Exists(path), $"shipped flow playbook missing: {path}");

        var pb = PlaybookRunnerCommand.LoadPlaybook(path);
        Assert.Equal("flow", pb.Kind);
        Assert.NotEmpty(pb.Axes);

        var engine = new PlaybookEngine(Content());
        var result = engine.Execute(pb);

        Assert.NotEmpty(result.Cells);
        // Every flow cell records a completion rate in [0,1].
        foreach (var c in result.Cells)
        {
            Assert.InRange(c.Summary.WinRate, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Regression for Sweep #3 zone-id garbage: the auto-quest-completion
    /// playbook axes over string zone-ids. Before the fix, the table / JSON
    /// log rendered cells keyed by `Math.Abs(str.GetHashCode()) % 100000` —
    /// and string.GetHashCode() is randomized per-process in .NET 6+, so the
    /// displayed startZoneId was (a) non-deterministic across runs and
    /// (b) printed as garbage integers like 73978 / 92099 / 12814 that did
    /// NOT correspond to any real zone-id in content/zones.json.
    ///
    /// After the fix, every flow cell carries its original string axis value
    /// on AxisDisplay, and that value MUST match a value authored in the
    /// playbook's startZoneId axis.
    /// </summary>
    [Fact]
    public void Flow_cells_carry_original_string_axis_values_not_hashed_ints()
    {
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "auto-quest-completion.json");
        var pb = PlaybookRunnerCommand.LoadPlaybook(path);
        var authoredZoneIds = pb.Axes
            .First(a => a.Name == "startZoneId")
            .Values.Strings
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(authoredZoneIds);

        var engine = new PlaybookEngine(Content());
        var result = engine.Execute(pb);

        Assert.NotEmpty(result.Cells);
        foreach (var c in result.Cells)
        {
            Assert.NotNull(c.AxisDisplay);
            Assert.True(c.AxisDisplay!.TryGetValue("startZoneId", out var zoneId));
            Assert.False(string.IsNullOrWhiteSpace(zoneId));
            Assert.Contains(zoneId, authoredZoneIds);
            // Must be the real zone-id string, not a digit-only hash.
            Assert.False(int.TryParse(zoneId, out _),
                $"startZoneId rendered as integer '{zoneId}' — regression of Sweep #3 zone-id hash bug.");
        }
    }

    /// <summary>
    /// Two runs of the same flow playbook must produce IDENTICAL
    /// int-keyed axis values (used as table/log stable keys). Before the
    /// fix, the hash used for string axes was `string.GetHashCode()` which
    /// is randomized per-process — so the "same" cell printed different
    /// integers every launch. Now we use FNV-1a, which is deterministic.
    /// </summary>
    [Fact]
    public void Flow_cell_int_keys_are_deterministic_across_runs()
    {
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "auto-quest-completion.json");
        var pb = PlaybookRunnerCommand.LoadPlaybook(path);

        var r1 = new PlaybookEngine(Content()).Execute(pb);
        var r2 = new PlaybookEngine(Content()).Execute(pb);

        Assert.Equal(r1.Cells.Count, r2.Cells.Count);
        for (int i = 0; i < r1.Cells.Count; i++)
        {
            foreach (var kv in r1.Cells[i].AxisValues)
            {
                Assert.Equal(kv.Value, r2.Cells[i].AxisValues[kv.Key]);
            }
        }
    }
}
