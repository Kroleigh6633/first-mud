namespace FirstMud.Domain.ValueObjects;

public sealed record Workmanship
{
    public int Value { get; private init; }

    public static readonly Workmanship Min = new() { Value = 1 };
    public static readonly Workmanship Max = new() { Value = 10 };

    public static Workmanship Of(int value)
    {
        if (value < 1 || value > 10)
            throw new ArgumentOutOfRangeException(nameof(value), "Workmanship must be between 1 and 10.");
        return new Workmanship { Value = value };
    }

    public static Workmanship Combine(Workmanship a, Workmanship b, int craftingSkill)
    {
        var baseValue = (a.Value + b.Value) / 2.0;
        var skillBonus = craftingSkill / 20.0;
        var result = (int)Math.Round(baseValue + skillBonus);
        return Of(Math.Clamp(result, 1, 10));
    }

    public bool IsExceptional => Value >= 8;
    public bool IsMasterwork => Value == 10;
}
