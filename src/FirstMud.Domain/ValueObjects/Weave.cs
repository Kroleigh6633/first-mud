namespace FirstMud.Domain.ValueObjects;

public sealed record Weave
{
    public int Current { get; private init; }
    public int Maximum { get; private init; }

    public static Weave Create(int maximum) => new() { Current = maximum, Maximum = maximum };

    public Weave Spend(int amount)
    {
        var newCurrent = Math.Max(0, Current - amount);
        return this with { Current = newCurrent };
    }

    public Weave Restore(int amount)
    {
        var newCurrent = Math.Min(Maximum, Current + amount);
        return this with { Current = newCurrent };
    }

    public Weave RestoreFull() => this with { Current = Maximum };

    public Weave ExpandMaximum(int amount) => this with { Maximum = Maximum + amount };

    public bool IsEmpty => Current == 0;
    public bool IsCritical => Current <= Maximum * 0.1;
    public bool IsLow => Current <= Maximum * 0.25;
    public float Percentage => Maximum == 0 ? 0 : (float)Current / Maximum;

    public WeaveState VisibleState => Percentage switch
    {
        >= 0.75f => WeaveState.Full,
        >= 0.50f => WeaveState.Steady,
        >= 0.25f => WeaveState.Strained,
        >= 0.10f => WeaveState.Critical,
        _ => WeaveState.Depleted
    };
}

public enum WeaveState
{
    Full,
    Steady,
    Strained,
    Critical,
    Depleted
}
