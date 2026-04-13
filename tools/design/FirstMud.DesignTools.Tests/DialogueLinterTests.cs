using FirstMud.DesignTools.Tools.DialogueLint;
using Xunit;

namespace FirstMud.DesignTools.Tests;

public class DialogueLinterTests
{
    [Fact]
    public void Clean_tree_reports_no_errors()
    {
        var tree = new DialogueTree
        {
            NpcId = "harken",
            Roots = new() { "greet" },
            Nodes = new()
            {
                new() { Id = "greet", Text = "Hi", Options = new() { new() { Text = "Bye", Next = "bye" } } },
                new() { Id = "bye", Text = "Bye", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Detects_unresolved_next_ref()
    {
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            Nodes = new()
            {
                new() { Id = "a", Text = "", Options = new() { new() { Text = "to ghost", Next = "ghost" } } },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Contains(r.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Detects_orphan_and_duplicate_ids()
    {
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            Nodes = new()
            {
                new() { Id = "a", Text = "", Terminal = true },
                new() { Id = "a", Text = "", Terminal = true },
                new() { Id = "orphan", Text = "", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Contains(r.Errors, e => e.Contains("Duplicate"));
        Assert.Contains(r.Warnings, w => w.Contains("orphan"));
    }

    [Fact]
    public void Flags_unreachable_reputation_gate()
    {
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            StartingReputation = new() { ["w"] = 0 },
            Nodes = new()
            {
                new() { Id = "a", Text = "", Options = new()
                {
                    new() { Text = "gated", Next = "b", RequiresReputation = new() { Faction = "w", Min = 50 } },
                    new() { Text = "open",  Next = "b" },
                }},
                new() { Id = "b", Text = "", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Contains(r.Warnings, w => w.Contains("rep") || w.Contains("w>=50"));
    }

    [Fact]
    public void Flags_low_rep_branch_made_unreachable_by_prior_rep_gain()
    {
        // a --(effect +50 wytchwood)--> b has a rep<=10 gate. Player's worst
        // reachable rep at b is 50, so the low-rep branch is unreachable.
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            StartingReputation = new() { ["wytchwood"] = 0 },
            Nodes = new()
            {
                new()
                {
                    Id = "a", Text = "",
                    Effects = new() { ["wytchwood"] = 50 },
                    Options = new() { new() { Text = "onward", Next = "b" } },
                },
                new()
                {
                    Id = "b", Text = "",
                    Options = new()
                    {
                        new()
                        {
                            Text = "you don't know us",
                            Next = "c",
                            RequiresReputationMax = new() { Faction = "wytchwood", Max = 10 },
                        },
                    },
                },
                new() { Id = "c", Text = "", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Empty(r.Errors);
        Assert.Contains(r.Warnings, w => w.Contains("<=10") && w.Contains("unreachable"));
    }

    [Fact]
    public void Errors_when_min_exceeds_max_on_same_option()
    {
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            StartingReputation = new() { ["w"] = 0 },
            Nodes = new()
            {
                new()
                {
                    Id = "a", Text = "",
                    Options = new()
                    {
                        new()
                        {
                            Text = "impossible",
                            Next = "b",
                            RequiresReputation = new() { Faction = "w", Min = 50 },
                            RequiresReputationMax = new() { Faction = "w", Max = 10 },
                        },
                    },
                },
                new() { Id = "b", Text = "", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Contains(r.Errors, e => e.Contains("min") || e.Contains("Min") || e.Contains(">"));
    }

    [Fact]
    public void Low_rep_branch_with_no_prior_rep_gain_is_reachable()
    {
        var tree = new DialogueTree
        {
            NpcId = "x", Roots = new() { "a" },
            StartingReputation = new() { ["w"] = 0 },
            Nodes = new()
            {
                new()
                {
                    Id = "a", Text = "",
                    Options = new()
                    {
                        new()
                        {
                            Text = "stranger",
                            Next = "b",
                            RequiresReputationMax = new() { Faction = "w", Max = 10 },
                        },
                    },
                },
                new() { Id = "b", Text = "", Terminal = true },
            },
        };
        var r = new DialogueLinter().Lint(tree);
        Assert.Empty(r.Errors);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("unreachable"));
    }
}
