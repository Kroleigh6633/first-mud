using System.Text.Json;
using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Executes the <c>capture-flow</c> playbook as an executable regression matrix.
/// The playbook documents the cells (playerPower × targetMonsterLayer ×
/// targetMonsterElement); this test iterates them and runs the pathological
/// "three identical CapturedMonsters in the party" simulation that originally
/// blew up with the duplicate-key Dictionary.Add crash. Any cell with
/// errorRate &gt; 0 is a regression of the 2026-04-13 playtest bug.
/// </summary>
public class CaptureFlowPlaybookTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    private static MonsterTemplate TimberWolf()
    {
        var def = Content().GetMonster("timber-wolf")
            ?? throw new InvalidOperationException("timber-wolf missing from content.");
        return new MonsterTemplate(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities);
    }

    [Fact]
    public void Capture_flow_playbook_loads_and_declares_zero_expected_error_rate()
    {
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "capture-flow.json");
        Assert.True(File.Exists(path), $"capture-flow playbook missing at {path}");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.Equal("capture-flow", root.GetProperty("id").GetString());
        Assert.Equal("flow", root.GetProperty("kind").GetString());

        var metrics = root.GetProperty("flow").GetProperty("metrics");
        bool sawErrorRate = false;
        foreach (var m in metrics.EnumerateArray())
        {
            if (m.GetProperty("name").GetString() == "errorRate")
            {
                sawErrorRate = true;
                Assert.Equal(0.0, m.GetProperty("expected").GetDouble());
            }
        }
        Assert.True(sawErrorRate, "errorRate metric missing from capture-flow playbook");
    }

    [Fact]
    public void Capture_flow_baseline_every_cell_has_zero_error_rate()
    {
        // Pins the duplicate-key fix. For each cell, simulate `rolls` encounters
        // with `captureCopies` identical CapturedMonsters in the party — the
        // exact arrangement that triggered the original crash. A non-zero
        // errorRate in any cell means the fix regressed.
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "capture-flow.json");
        var pb = PlaybookRunnerCommand.LoadPlaybook(path);

        // Pull flow params out manually since Playbook POCO doesn't model them.
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var flow = doc.RootElement.GetProperty("flow");
        int captureCopies = flow.GetProperty("captureCopies").GetInt32();

        var monsters = new[] { TimberWolf() };
        int totalCells = 0;
        int cellsWithErrors = 0;
        var firstErrorByCell = new List<string>();

        foreach (var combo in PlaybookEngine.Cartesian(pb.Axes))
        {
            totalCells++;
            int playerPower      = combo["playerPower"];
            int monsterLayer     = combo["targetMonsterLayer"];
            int monsterElementIx = combo["targetMonsterElement"];
            var element = (MagicElement)monsterElementIx;

            int errors = 0;
            for (int i = 0; i < pb.Rolls; i++)
            {
                try
                {
                    var party = new List<CombatSimulationService.PartyMember>(captureCopies);
                    for (int k = 0; k < captureCopies; k++)
                    {
                        party.Add(new CombatSimulationService.PartyMember(
                            CompanionType.CapturedMonster, element, monsterLayer, Math.Max(1, playerPower)));
                    }

                    var rng = new Random(unchecked(pb.Seed * 1_000_003 + totalCells * 97 + i));
                    var svc = new CombatSimulationService(rng);
                    var enc = svc.BuildEncounter(MagicElement.Aether, playerPower, party, monsters);
                    _ = svc.Run(enc, maxRounds: 30);
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errors == 1)
                    {
                        firstErrorByCell.Add(
                            $"cell(pp={playerPower}, layer={monsterLayer}, element={element}): {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (errors > 0) cellsWithErrors++;
        }

        Assert.True(cellsWithErrors == 0,
            $"capture-flow baseline: {cellsWithErrors}/{totalCells} cells had errorRate > 0. "
            + $"First errors: {string.Join(" | ", firstErrorByCell.Take(5))}");
    }
}
