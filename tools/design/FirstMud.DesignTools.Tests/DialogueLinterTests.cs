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
}
