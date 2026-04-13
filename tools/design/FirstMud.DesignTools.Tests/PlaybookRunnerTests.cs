using System.Text.Json;
using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Tests for the balance playbook harness. Uses real content (the live monster
/// roster and curves) so the viability bands reflect the actual game math.
/// </summary>
public class PlaybookRunnerTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    private static Playbook TinyPlaybook() => new()
    {
        Id = "test-tiny",
        DisplayName = "tiny",
        Description = "two-cell test playbook",
        Rolls = 40,
        Seed = 1234,
        Holdouts = new Holdouts { PlayerLevel = 5, PlayerElement = "Aether" },
        Axes = new List<Axis>
        {
            new() { Name = "dangerLevel", Values = new AxisValues { Ints = new[] { 0, 8 } } },
        },
        ExpectedViability = new ExpectedViability
        {
            Cells = new List<ExpectedCell>
            {
                BuildCell(new[] { ("dangerLevel", 0) }, "trivial"),
                BuildCell(new[] { ("dangerLevel", 8) }, "punishing"),
            }
        },
        ToleranceBands = new Dictionary<string, double[]>
        {
            ["trivial"]   = new[] { 95.0, 100.0 },
            ["easy"]      = new[] { 85.0, 95.0 },
            ["balanced"]  = new[] { 60.0, 85.0 },
            ["hard"]      = new[] { 30.0, 60.0 },
            ["punishing"] = new[] { 0.0, 30.0 },
        },
    };

    private static ExpectedCell BuildCell((string name, int val)[] axes, string expected)
    {
        // Serialise via JSON so the JsonElement values match what the real
        // loader produces (we want Axis(...) lookups to work identically).
        var obj = new Dictionary<string, object> { ["expected"] = expected };
        foreach (var (n, v) in axes) obj[n] = v;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<ExpectedCell>(json)!;
    }

    [Fact]
    public void Seed_is_deterministic_across_runs()
    {
        var engine = new PlaybookEngine(Content());
        var pb = TinyPlaybook();

        var a = engine.Execute(pb);
        var b = engine.Execute(pb);

        Assert.Equal(a.Cells.Count, b.Cells.Count);
        for (int i = 0; i < a.Cells.Count; i++)
        {
            Assert.Equal(a.Cells[i].Summary.Wins,      b.Cells[i].Summary.Wins);
            Assert.Equal(a.Cells[i].Summary.Losses,    b.Cells[i].Summary.Losses);
            Assert.Equal(a.Cells[i].Summary.AvgRounds, b.Cells[i].Summary.AvgRounds);
            Assert.Equal(a.Cells[i].ActualBand,        b.Cells[i].ActualBand);
        }
    }

    [Fact]
    public void Classify_maps_win_rate_to_correct_band()
    {
        var bands = TinyPlaybook().ToleranceBands;

        Assert.Equal("trivial",   PlaybookEngine.Classify(0.99, bands));
        Assert.Equal("easy",      PlaybookEngine.Classify(0.90, bands));
        Assert.Equal("balanced",  PlaybookEngine.Classify(0.70, bands));
        Assert.Equal("hard",      PlaybookEngine.Classify(0.45, bands));
        Assert.Equal("punishing", PlaybookEngine.Classify(0.10, bands));
    }

    [Fact]
    public void Divergence_flags_cells_whose_actual_band_misses_expected()
    {
        var engine = new PlaybookEngine(Content());
        var pb = TinyPlaybook();

        // Replace expectations with something guaranteed wrong for the high-danger
        // cell so divergence detection fires there.
        pb.ExpectedViability.Cells[1] = BuildCell(new[] { ("dangerLevel", 8) }, "trivial");

        var result = engine.Execute(pb);

        Assert.Contains(result.Cells, c => c.Divergent);
        var diverged = result.Cells.Single(c => c.AxisValues["dangerLevel"] == 8);
        Assert.True(diverged.Divergent, $"Expected danger=8 to diverge from 'trivial', got band={diverged.ActualBand}");
    }

    [Fact]
    public void Compare_to_prior_emits_no_diff_when_run_is_identical()
    {
        var engine = new PlaybookEngine(Content());
        var pb = TinyPlaybook();
        var result = engine.Execute(pb);

        // Build a "prior" payload matching the current result, write it to a temp
        // JSON log, and diff — output should report zero changes.
        var payload = new
        {
            playbookId = pb.Id,
            cells = result.Cells.Select(c => new
            {
                axis = c.AxisValues,
                winRate = c.Summary.WinRate,
                avgRounds = c.Summary.AvgRounds,
                avgHpPct = c.Summary.AvgPlayerHpPct,
                actual = c.ActualBand,
                expected = c.ExpectedBand,
                divergent = c.Divergent,
            }),
        };
        var path = Path.Combine(Path.GetTempPath(), $"playbook-prior-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
        try
        {
            var diff = PlaybookRunnerCommand.CompareToPriorRun(path, result);
            Assert.Contains("no cells changed", diff, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Compare_to_prior_highlights_band_shift()
    {
        var engine = new PlaybookEngine(Content());
        var pb = TinyPlaybook();
        var result = engine.Execute(pb);

        // Prior run: claim the high-danger cell had 95% winRate ("trivial"). Current
        // run certainly won't match — so we expect a BAND SHIFT row.
        var priorCells = result.Cells.Select((c, idx) => new
        {
            axis = c.AxisValues,
            winRate = idx == 1 ? 0.99 : c.Summary.WinRate,
            avgRounds = c.Summary.AvgRounds,
            avgHpPct = c.Summary.AvgPlayerHpPct,
            actual = idx == 1 ? "trivial" : c.ActualBand,
            expected = c.ExpectedBand,
            divergent = false,
        });
        var path = Path.Combine(Path.GetTempPath(), $"playbook-prior-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { cells = priorCells }));
        try
        {
            var diff = PlaybookRunnerCommand.CompareToPriorRun(path, result);
            Assert.Contains("BAND SHIFT", diff);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cell_salt_is_deterministic_from_index_tuple_only()
    {
        // Determinism regression guard (Fix #1 from CP7 follow-ups):
        // DeterministicCellSalt must depend ONLY on the integer index tuple.
        // No string hashing, no process-randomized input, no globals.
        // If someone replaces the implementation with GetHashCode-style
        // randomization, this test will start producing different values
        // across CI runs and the comparison below will still hold per-run,
        // but the hard-coded regression vector at the bottom will fail.
        var a1 = PlaybookEngine.DeterministicCellSalt(new[] { 0, 0 });
        var a2 = PlaybookEngine.DeterministicCellSalt(new[] { 0, 0 });
        Assert.Equal(a1, a2);

        Assert.NotEqual(
            PlaybookEngine.DeterministicCellSalt(new[] { 0, 1 }),
            PlaybookEngine.DeterministicCellSalt(new[] { 1, 0 }));

        // Hard-coded regression vector — these values are produced by the
        // prime-weighted positional mix. Changing the mix is OK but requires
        // updating these constants deliberately.
        Assert.Equal(257,                       PlaybookEngine.DeterministicCellSalt(new[] { 0 }));
        Assert.Equal(514,                       PlaybookEngine.DeterministicCellSalt(new[] { 1 }));
        // [2, 1]: a=0 → 0*1_000_003 + 3*1*257 = 771; a=1 → 771*1_000_003 + 2*2*257 = 771000003771 + 1028.
        Assert.Equal(unchecked(771 * 1_000_003 + 2 * 2 * 257),
                     PlaybookEngine.DeterministicCellSalt(new[] { 2, 1 }));
    }

    [Fact]
    public void Gear_tier_and_imbue_level_are_distinct_from_player_level()
    {
        // Fix #2 regression guard: CombatContext must apply gear and imbue
        // DIFFERENTLY from raw player level. Run three identical combats
        // that differ only in which axis is boosted; if all three yield the
        // same win rate, the proxy collapsed and the playbook framework is
        // not telling us what we think it's telling us.
        var engine = new PlaybookEngine(Content());

        // Shared rig: danger 7 vs timber-wolf, solo player, level 5 baseline.
        var baseline = MakeSinglePlayerPlaybook(level: 5, gear: 0, imbue: 0);
        var gear     = MakeSinglePlayerPlaybook(level: 5, gear: 5, imbue: 0);
        var imbue    = MakeSinglePlayerPlaybook(level: 5, gear: 0, imbue: 5);

        var bWin = engine.Execute(baseline).Cells.Single().Summary.WinRate;
        var gWin = engine.Execute(gear).Cells.Single().Summary.WinRate;
        var iWin = engine.Execute(imbue).Cells.Single().Summary.WinRate;

        Assert.True(gWin > bWin, $"gear (gt=5) should beat baseline (gt=0); got {gWin:F3} vs {bWin:F3}");
        Assert.True(iWin > bWin, $"imbue (il=5) should beat baseline (il=0); got {iWin:F3} vs {bWin:F3}");
        // Gear affects Strike (the default auto-player pick); imbue affects
        // magical abilities which the auto-player doesn't cast, so gear should
        // outpace imbue for this sim configuration. This asymmetry is the
        // whole point — they're NOT collapsed into the level curve.
        Assert.NotEqual(gWin, iWin);
    }

    private static Playbook MakeSinglePlayerPlaybook(int level, int gear, int imbue)
    {
        var pb = new Playbook
        {
            Id = $"test-ctx-L{level}-G{gear}-I{imbue}",
            DisplayName = "ctx probe",
            Description = "single cell probe for Fix #2",
            Rolls = 120,
            Seed = 2026,
            Holdouts = new Holdouts
            {
                PlayerLevel = level,
                PlayerElement = "Aether",
                GearTier = gear,
                ImbueLevel = imbue,
                DangerLevel = 7,
            },
            Axes = new List<Axis>
            {
                new() { Name = "dangerLevel", Values = new[] { 7 } },
            },
            ToleranceBands = new Dictionary<string, double[]>
            {
                ["trivial"]   = new[] { 95.0, 100.0 },
                ["easy"]      = new[] { 85.0, 95.0 },
                ["balanced"]  = new[] { 60.0, 85.0 },
                ["hard"]      = new[] { 30.0, 60.0 },
                ["punishing"] = new[] { 0.0, 30.0 },
            },
        };
        pb.ExpectedViability = new ExpectedViability();
        return pb;
    }

    [Fact]
    public void Loaded_seed_playbooks_have_valid_structure()
    {
        // Sanity check that every shipped playbook deserializes and has the
        // fields the engine requires. Guards against typo regressions in the
        // JSON files themselves.
        var dir = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks");
        Assert.True(Directory.Exists(dir), $"playbooks dir missing: {dir}");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.NotEmpty(files);

        foreach (var f in files)
        {
            var pb = PlaybookRunnerCommand.LoadPlaybook(f);
            Assert.False(string.IsNullOrWhiteSpace(pb.Id), $"{f}: id missing");
            Assert.NotEmpty(pb.Axes);
            var kind = string.IsNullOrWhiteSpace(pb.Kind) ? "combat" : pb.Kind.ToLowerInvariant();
            if (kind == "combat")
                Assert.NotEmpty(pb.ToleranceBands);
            else if (kind == "crafting")
                Assert.NotEmpty(pb.ExpectedDistribution.Cells);
            Assert.True(pb.Rolls > 0, $"{f}: rolls must be positive");
        }
    }
}
