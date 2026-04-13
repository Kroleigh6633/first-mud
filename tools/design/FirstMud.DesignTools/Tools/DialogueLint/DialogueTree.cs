namespace FirstMud.DesignTools.Tools.DialogueLint;

public sealed class DialogueTree
{
    public string NpcId { get; set; } = "";
    public List<string> Roots { get; set; } = new();
    public Dictionary<string, int> StartingReputation { get; set; } = new();
    public List<DialogueNode> Nodes { get; set; } = new();
}

public sealed class DialogueNode
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<DialogueOption> Options { get; set; } = new();
    public bool Terminal { get; set; }

    /// <summary>
    /// Optional: reputation-delta effects applied when this node is entered,
    /// keyed by faction -> delta. Used by the fresh-start reachability walk
    /// to decide whether downstream rep-max gates can still be met.
    /// </summary>
    public Dictionary<string, int> Effects { get; set; } = new();
}

public sealed class DialogueOption
{
    public string Text { get; set; } = "";
    public string? Next { get; set; }

    /// <summary>Reputation floor gate: player's rep with faction must be >= Min.</summary>
    public RepRequirement? RequiresReputation { get; set; }

    /// <summary>Reputation ceiling gate: player's rep with faction must be &lt;= Max.</summary>
    public RepLimit? RequiresReputationMax { get; set; }
}

public sealed class RepRequirement
{
    public string Faction { get; set; } = "";
    public int Min { get; set; }
}

public sealed class RepLimit
{
    public string Faction { get; set; } = "";
    public int Max { get; set; }
}
