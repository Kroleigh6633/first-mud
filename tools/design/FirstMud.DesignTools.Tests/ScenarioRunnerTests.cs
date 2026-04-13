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
