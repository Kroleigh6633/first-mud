using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.EncounterSim;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Black-box tests over the encounter simulator. Uses real monster content
/// from <c>content/monsters.json</c> so the bands reflect the live game.
/// </summary>
public class EncounterSimTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    private static MonsterTemplate TemplateFor(string id)
    {
        var def = Content().GetMonster(id)
            ?? throw new InvalidOperationException($"Monster '{id}' not in content.");
        return new MonsterTemplate(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities);
    }

    private static EncounterSimCommand.Summary RunBatch(
        int seed,
        int rolls,
        int playerLevel,
        MagicElement playerElement,
        IReadOnlyList<CombatSimulationService.PartyMember> party,
        IReadOnlyList<MonsterTemplate> monsters)
    {
        var results = new List<CombatSimulationService.SimulationResult>(rolls);
        for (int i = 0; i < rolls; i++)
        {
            var rng = new Random(unchecked(seed * 1_000_003 + i));
            var svc = new CombatSimulationService(rng);
            var enc = svc.BuildEncounter(playerElement, playerLevel, party, monsters);
            results.Add(svc.Run(enc));
        }
        return EncounterSimCommand.Summarize(results);
    }

    [Fact]
    public void Same_seed_produces_identical_outcome_distribution()
    {
        var monsters = new[] { TemplateFor("timber-wolf"), TemplateFor("wild-boar") };
        var party = new[]
        {
            new CombatSimulationService.PartyMember(CompanionType.Wildfolk, MagicElement.Fire, 2, 4),
        };

        var a = RunBatch(seed: 42, rolls: 200, playerLevel: 5, MagicElement.Fire, party, monsters);
        var b = RunBatch(seed: 42, rolls: 200, playerLevel: 5, MagicElement.Fire, party, monsters);

        Assert.Equal(a.Wins, b.Wins);
        Assert.Equal(a.Losses, b.Losses);
        Assert.Equal(a.Timeouts, b.Timeouts);
        Assert.Equal(a.DmgMedian, b.DmgMedian);
        Assert.Equal(a.DmgP75, b.DmgP75);
        Assert.Equal(a.AvgRounds, b.AvgRounds);
    }

    [Fact]
    public void Easy_fixture_over_leveled_party_wins_most_rolls()
    {
        // Level-20 Earth player + two layer-5 companions vs a single tier-0 goat.
        var monsters = new[] { TemplateFor("mountain-goat") };
        var party = new[]
        {
            new CombatSimulationService.PartyMember(CompanionType.Wildfolk, MagicElement.Fire, 5, 12),
            new CombatSimulationService.PartyMember(CompanionType.HiredHero, MagicElement.Earth, 5, 12),
        };

        var s = RunBatch(seed: 7, rolls: 500, playerLevel: 20, MagicElement.Earth, party, monsters);

        Assert.True(s.WinRate >= 0.95,
            $"Expected trivially-easy fixture to win ≥95%, got {s.WinRate:P1}.");
        Assert.Equal("trivial", s.Difficulty);
    }

    [Fact]
    public void Hard_fixture_level_1_solo_vs_tier3_pack_loses_most_rolls()
    {
        // Level-1 solo player vs a pack of 4 t3 bosses: statistically punishing.
        var monsters = new[]
        {
            TemplateFor("frost-giant"),
            TemplateFor("dragon-whelp"),
            TemplateFor("thornwood-guardian"),
            TemplateFor("swamp-hydra"),
        };
        var party = Array.Empty<CombatSimulationService.PartyMember>();

        var s = RunBatch(seed: 11, rolls: 500, playerLevel: 1, MagicElement.Air, party, monsters);

        Assert.True(s.WinRate <= 0.20,
            $"Expected punishing matchup to win ≤20%, got {s.WinRate:P1}.");
    }

    [Fact]
    public void Difficulty_bands_cover_win_rate_correctly()
    {
        EncounterSimCommand.Summary MakeSummary(double winRate)
        {
            int rolls = 1000;
            int wins = (int)(winRate * rolls);
            var results = Enumerable.Range(0, rolls)
                .Select(i => new CombatSimulationService.SimulationResult(
                    i < wins ? CombatSimulationService.Outcome.Victory : CombatSimulationService.Outcome.Defeat,
                    10, 100, 100, 0,
                    new Dictionary<string, int>(), null))
                .ToList();
            return EncounterSimCommand.Summarize(results);
        }

        Assert.Equal("trivial",   MakeSummary(0.99).Difficulty);
        Assert.Equal("easy",      MakeSummary(0.90).Difficulty);
        Assert.Equal("balanced",  MakeSummary(0.70).Difficulty);
        Assert.Equal("hard",      MakeSummary(0.45).Difficulty);
        Assert.Equal("punishing", MakeSummary(0.10).Difficulty);
    }
}
