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
}

public sealed class DialogueOption
{
    public string Text { get; set; } = "";
    public string? Next { get; set; }
    public RepRequirement? RequiresReputation { get; set; }
}

public sealed class RepRequirement
{
    public string Faction { get; set; } = "";
    public int Min { get; set; }
}
