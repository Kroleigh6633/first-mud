using FirstMud.DesignTools.Tools.ScenarioPlayer;
using Xunit;

namespace FirstMud.DesignTools.Tests;

public class ScenarioRunnerTests
{
    [Fact]
    public void Runs_happy_path_with_forced_choices()
    {
        var spec = BuildFetchPeltsSpec();
        var runner = new ScenarioRunner();

        var result = runner.Run(spec, new[] { "accept", "hunt-fair", "honest" });

        Assert.Empty(result.Errors);
        Assert.Equal("success", result.FinalOutcome);
        Assert.Contains(result.Steps, s => s.ChosenChoiceId == "accept");
        Assert.Contains(result.Steps, s => s.Deltas.Contains("rep+ wytchwood +10"));
        var rep = (Dictionary<string, object>)result.FinalState["reputation"];
        Assert.Equal(10, (int)rep["wytchwood"]);
    }

    [Fact]
    public void Defaults_to_first_choice_when_no_forced_path()
    {
        var spec = BuildFetchPeltsSpec();
        var result = new ScenarioRunner().Run(spec);
        // First choice at each beat leads to: accept -> hunt-fair -> honest -> end-good
        Assert.Equal("success", result.FinalOutcome);
    }

    [Fact]
    public void Declining_reaches_decline_terminal()
    {
        var spec = BuildFetchPeltsSpec();
        var result = new ScenarioRunner().Run(spec, new[] { "decline" });
        Assert.Equal("declined", result.FinalOutcome);
    }

    [Fact]
    public void RemoveItem_underflow_without_flag_errors_hard()
    {
        var spec = BuildBribeSpec();
        var result = new ScenarioRunner().Run(spec, new[] { "bribe" });

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e =>
            e.Contains("removeItem", StringComparison.Ordinal)
            && e.Contains("'gold'", StringComparison.Ordinal)
            && e.Contains("exceeds stock", StringComparison.Ordinal)
            && e.Contains("0 available", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveItem_underflow_with_allow_flag_warns_and_clamps_to_zero()
    {
        var spec = BuildBribeSpec();
        var result = new ScenarioRunner().Run(
            spec,
            new[] { "bribe" },
            new ScenarioRunner.Options { AllowUnderflow = true });

        Assert.Empty(result.Errors);
        Assert.Contains(result.Steps.SelectMany(s => s.RequirementWarnings),
            w => w.Contains("removeItem", StringComparison.Ordinal)
                 && w.Contains("clamped to 0", StringComparison.Ordinal));
        var inv = (Dictionary<string, object>)result.FinalState["inventory"];
        Assert.Equal(0, (int)inv["gold"]);
    }

    [Fact]
    public void RequiresItems_gate_blocks_branch_when_stock_missing()
    {
        var spec = BuildGatedChoiceSpec();
        // Default path picks first ELIGIBLE choice: 'gated' is gated by gold x25,
        // so the runner must skip it and pick 'walk-away'.
        var result = new ScenarioRunner().Run(spec);

        Assert.Empty(result.Errors);
        Assert.Equal("walked-away", result.FinalOutcome);
        Assert.DoesNotContain(result.Steps, s => s.ChosenChoiceId == "gated");
    }

    [Fact]
    public void Forcing_into_gated_branch_without_stock_errors()
    {
        var spec = BuildGatedChoiceSpec();
        var result = new ScenarioRunner().Run(spec, new[] { "gated" });

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("not traversable", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixture_level_allowUnderflow_overrides_off_to_on()
    {
        var spec = BuildBribeSpec();
        spec.AllowUnderflow = true;
        var result = new ScenarioRunner().Run(spec, new[] { "bribe" });

        Assert.Empty(result.Errors);
    }

    private static QuestSpec BuildBribeSpec() => new()
    {
        Id = "bribe-quest",
        Title = "Bribe Test",
        Root = "intro",
        StartState = new() { Inventory = new() /* zero gold */ },
        Beats = new()
        {
            new QuestBeat
            {
                Id = "intro", Description = "A scribe waits.",
                Choices = new()
                {
                    new() { Id = "bribe", Text = "Bribe.", Next = "end",
                        Effects = new() { new() { Type = "removeItem", Key = "gold", Amount = 25 } } }
                }
            },
            new QuestBeat { Id = "end", Description = "Done.", Terminal = true, Outcome = "bribed" }
        }
    };

    private static QuestSpec BuildGatedChoiceSpec() => new()
    {
        Id = "gated-quest",
        Title = "Gated Test",
        Root = "intro",
        StartState = new() { Inventory = new() /* zero gold */ },
        Beats = new()
        {
            new QuestBeat
            {
                Id = "intro", Description = "Choose.",
                Choices = new()
                {
                    new() { Id = "gated", Text = "Pay 25g.", Next = "end-paid",
                        Requires = new() { Items = new() { new() { Key = "gold", Amount = 25 } } },
                        Effects = new() { new() { Type = "removeItem", Key = "gold", Amount = 25 } } },
                    new() { Id = "walk-away", Text = "Walk away.", Next = "end-walk" }
                }
            },
            new QuestBeat { Id = "end-paid", Description = "Paid.", Terminal = true, Outcome = "paid" },
            new QuestBeat { Id = "end-walk", Description = "Walked.", Terminal = true, Outcome = "walked-away" }
        }
    };

    private static QuestSpec BuildFetchPeltsSpec() => new()
    {
        Id = "test-quest",
        Title = "Test",
        Root = "intro",
        Beats = new()
        {
            new QuestBeat
            {
                Id = "intro", Description = "Harken asks.",
                Choices = new()
                {
                    new() { Id = "accept", Text = "Yes", Next = "hunt",
                        Effects = new() { new() { Type = "setFlag", Key = "harken.accepted" } } },
                    new() { Id = "decline", Text = "No", Next = "end-decline" },
                }
            },
            new QuestBeat
            {
                Id = "hunt", Description = "Hunt.",
                Choices = new()
                {
                    new() { Id = "hunt-fair", Text = "Fair", Next = "return",
                        Effects = new() { new() { Type = "addItem", Key = "wolf-pelt", Amount = 3 } } }
                }
            },
            new QuestBeat
            {
                Id = "return", Description = "Return.",
                Choices = new()
                {
                    new() { Id = "honest", Text = "Deliver", Next = "end-good",
                        Effects = new()
                        {
                            new() { Type = "removeItem", Key = "wolf-pelt", Amount = 3 },
                            new() { Type = "addReputation", Key = "wytchwood", Amount = 10 },
                        } }
                }
            },
            new QuestBeat { Id = "end-decline", Description = "Declined.", Terminal = true, Outcome = "declined" },
            new QuestBeat { Id = "end-good", Description = "Success.", Terminal = true, Outcome = "success" },
        },
    };
}
