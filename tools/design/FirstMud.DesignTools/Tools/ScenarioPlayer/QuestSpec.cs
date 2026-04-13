namespace FirstMud.DesignTools.Tools.ScenarioPlayer;

/// <summary>Quest-chain spec v1. Consumed by the scenario-player tool.</summary>
public sealed class QuestSpec
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public QuestStartState StartState { get; set; } = new();
    public List<QuestBeat> Beats { get; set; } = new();
    public string Root { get; set; } = "";
    /// <summary>Per-fixture override: when true, removeItem underflow degrades to a warning instead of a hard error.</summary>
    public bool AllowUnderflow { get; set; }
}

public sealed class QuestStartState
{
    public string? Player { get; set; }
    public List<string> Flags { get; set; } = new();
    public List<QuestItem> Inventory { get; set; } = new();
    public Dictionary<string, int> Reputation { get; set; } = new();
}

public sealed class QuestItem
{
    public string Key { get; set; } = "";
    public int Amount { get; set; }
}

public sealed class QuestBeat
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public QuestRequires? Requires { get; set; }
    public List<QuestChoice> Choices { get; set; } = new();
    public bool Terminal { get; set; }
    public string? Outcome { get; set; }
}

public sealed class QuestRequires
{
    public List<string> Flags { get; set; } = new();
    public List<QuestItem> Items { get; set; } = new();
    public Dictionary<string, int> Reputation { get; set; } = new();
}

public sealed class QuestChoice
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Next { get; set; }
    public QuestRequires? Requires { get; set; }
    public List<QuestEffect> Effects { get; set; } = new();
}

public sealed class QuestEffect
{
    /// <summary>one of: setFlag, clearFlag, addItem, removeItem, addReputation</summary>
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public int Amount { get; set; }
}
