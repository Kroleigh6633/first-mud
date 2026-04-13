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
    public void Scaling_at_danger_zero_is_bit_identical_noop()
    {
        // Regression fence: encounter-sim with --danger-level 0 (omitted) MUST
        // hand CombatSimulationService bit-identical templates so existing
        // sim-log baselines remain reproducible.
        var template = TemplateFor("timber-wolf");
        var curve = MonsterScaling.DefaultCurve;

        var scaled = MonsterScaling.Apply(template, dangerLevel: 0, curve, isBoss: false);

        Assert.Same(template, scaled);
        Assert.Equal(template.Hp,    scaled.Hp);
        Assert.Equal(template.Speed, scaled.Speed);
        Assert.Equal(template.Level, scaled.Level);
        Assert.Equal(template.Abilities.Count, scaled.Abilities.Count);
        for (int i = 0; i < template.Abilities.Count; i++)
            Assert.Equal(template.Abilities[i].BasePower, scaled.Abilities[i].BasePower);
    }

    [Fact]
    public void Scaled_run_at_danger_5_strictly_lowers_win_rate_vs_unscaled()
    {
        // Same seed/monster/party — only the danger-level scaling differs.
        // At danger 5 HP becomes 3x and ability power 2.5x, so a fight that
        // sits in a contested win-rate band unscaled MUST shed measurable
        // win rate when scaled. We pick a t3 monster vs a low-level solo
        // player so neither end pegs at 100%/0% (which would mask the delta).
        var baseTemplate = TemplateFor("frost-giant");
        var curve = MonsterScaling.DefaultCurve;
        var scaledTemplate = MonsterScaling.Apply(baseTemplate, dangerLevel: 5, curve, isBoss: false);

        var party = Array.Empty<CombatSimulationService.PartyMember>();

        var unscaled = RunBatch(seed: 99, rolls: 400, playerLevel: 6, MagicElement.Fire, party,
            new[] { baseTemplate });
        var scaled   = RunBatch(seed: 99, rolls: 400, playerLevel: 6, MagicElement.Fire, party,
            new[] { scaledTemplate });

        Assert.True(scaled.WinRate < unscaled.WinRate,
            $"Expected danger-5 scaling to strictly lower win rate. Unscaled={unscaled.WinRate:P1} Scaled={scaled.WinRate:P1}.");
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

    // ─── TPK-fix sanity pin ─────────────────────────────────────────────────
    //
    // 2026-04-13: user TPK'd three times in a row at danger 5/6/7. Root cause
    // was asymmetric scaling — monsters got +40% HP & +30% power per danger
    // with no matching party buff. The fix (combat-curves.json halving + a
    // new PartyScaling curve + pack-size cap softening in MonsterFactory)
    // must keep the intended minimum: a typical level-5 party with 3 layer-2
    // companions clears danger-5 zone content at ≥60% sim win-rate, and a
    // typical level-8 party clears danger-7. If this test fails, someone
    // either reverted the curves or changed combat math without re-tuning.

    [Fact]
    public void Sanity_level5_party_clears_danger5_at_60pct_or_better()
    {
        var partyCurve = Content().CombatCurves.PartyScaling;
        var party = new List<CombatSimulationService.PartyMember>
        {
            new(CompanionType.Wildfolk, MagicElement.Fire,  2, 4),
            new(CompanionType.Wildfolk, MagicElement.Earth, 2, 4),
            new(CompanionType.Wildfolk, MagicElement.Air,   2, 4),
        };

        var scaled = new List<MonsterTemplate>
        {
            MonsterScaling.Apply(TemplateFor("stone-troll"), 5, Content().CombatCurves.MonsterScaling, isBoss: false),
            MonsterScaling.Apply(TemplateFor("stone-troll"), 5, Content().CombatCurves.MonsterScaling, isBoss: false),
            MonsterScaling.Apply(TemplateFor("stone-troll"), 5, Content().CombatCurves.MonsterScaling, isBoss: false),
        };

        int rolls = 300;
        int wins = 0;
        for (int i = 0; i < rolls; i++)
        {
            var rng = new Random(unchecked(17 * 1_000_003 + i));
            var svc = new CombatSimulationService(rng, partyCurve);
            var enc = svc.BuildEncounter(MagicElement.Aether, 5, party, scaled);
            var res = svc.Run(enc, dangerLevel: 5);
            if (res.Outcome == CombatSimulationService.Outcome.Victory) wins++;
        }
        double winRate = (double)wins / rolls;
        Assert.True(winRate >= 0.60,
            $"TPK regression pin: L5 + 3×layer-2 vs D5 3-pack must win ≥60%, got {winRate:P1}.");
    }

    [Fact]
    public void Sanity_level8_party_clears_danger7_at_60pct_or_better()
    {
        var partyCurve = Content().CombatCurves.PartyScaling;
        var party = new List<CombatSimulationService.PartyMember>
        {
            new(CompanionType.Wildfolk, MagicElement.Fire,  3, 6),
            new(CompanionType.Wildfolk, MagicElement.Earth, 3, 6),
            new(CompanionType.Wildfolk, MagicElement.Air,   3, 6),
        };

        var scaled = new List<MonsterTemplate>
        {
            MonsterScaling.Apply(TemplateFor("rogue-knight"),   7, Content().CombatCurves.MonsterScaling, isBoss: false),
            MonsterScaling.Apply(TemplateFor("highway-bandit"), 7, Content().CombatCurves.MonsterScaling, isBoss: false),
            MonsterScaling.Apply(TemplateFor("wild-horse"),     7, Content().CombatCurves.MonsterScaling, isBoss: false),
        };

        int rolls = 300;
        int wins = 0;
        for (int i = 0; i < rolls; i++)
        {
            var rng = new Random(unchecked(23 * 1_000_003 + i));
            var svc = new CombatSimulationService(rng, partyCurve);
            var enc = svc.BuildEncounter(MagicElement.Aether, 8, party, scaled);
            var res = svc.Run(enc, dangerLevel: 7);
            if (res.Outcome == CombatSimulationService.Outcome.Victory) wins++;
        }
        double winRate = (double)wins / rolls;
        Assert.True(winRate >= 0.60,
            $"TPK regression pin: L8 + 3×layer-3 vs D7 boss-pack must win ≥60%, got {winRate:P1}.");
    }

    [Fact]
    public void Duplicate_captured_monster_companions_do_not_crash_sim()
    {
        // Regression for playtest crash (2026-04-13): a party with two
        // CapturedMonsters sharing (Element, Layer) produced two combatants
        // named "CapturedMonster-Earth-L5". Run()'s final ToDictionary(byName)
        // then threw "An item with the same key has already been added. Key:
        // CapturedMonster-Earth-L5" and bubbled out of EstimateWinRate, turning
        // every wilderness move into a "Command failed" on the client.
        //
        // The fix is twofold:
        //   1. BuildEncounter now appends a "#<index>" disambiguator so two
        //      companions with identical (Type, Element, Layer) still produce
        //      distinct combatant names.
        //   2. Run()'s damageByName aggregation uses GroupBy instead of
        //      ToDictionary so any future caller that constructs a custom
        //      encounter with duplicate names still gets a sane result.
        // This test pins both behaviors: 3 identical CapturedMonsters must
        // simulate without throwing and must produce 3 distinct damage keys.
        var monsters = new[] { TemplateFor("timber-wolf") };
        var party = new[]
        {
            new CombatSimulationService.PartyMember(CompanionType.CapturedMonster, MagicElement.Earth, 5, 10),
            new CombatSimulationService.PartyMember(CompanionType.CapturedMonster, MagicElement.Earth, 5, 10),
            new CombatSimulationService.PartyMember(CompanionType.CapturedMonster, MagicElement.Earth, 5, 10),
        };

        var rng = new Random(123);
        var svc = new CombatSimulationService(rng);
        var enc = svc.BuildEncounter(MagicElement.Aether, 8, party, monsters);

        // Must not throw — historically this threw ArgumentException.
        var result = svc.Run(enc, maxRounds: 40);

        Assert.Equal(3, result.DamageByCompanion.Count);
        foreach (var key in result.DamageByCompanion.Keys)
            Assert.Contains("CapturedMonster-Earth-L5", key);
    }
}
