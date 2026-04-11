using FirstMud.Domain.Enums;

namespace FirstMud.Domain.ValueObjects;

public sealed record ReputationScore
{
    public int Points { get; private init; }

    public static ReputationScore Zero => new() { Points = 0 };

    public static ReputationScore Of(int points) => new() { Points = points };

    public ReputationScore Add(int amount) => new() { Points = Points + amount };

    public ReputationScore Subtract(int amount) => new() { Points = Points - amount };

    public ReputationTier Tier => Points switch
    {
        < -500 => ReputationTier.Hostile,
        < 0 => ReputationTier.Wary,
        < 1 => ReputationTier.Unknown,
        < 1000 => ReputationTier.Known,
        < 3000 => ReputationTier.Trusted,
        < 6000 => ReputationTier.Honored,
        _ => ReputationTier.Bound
    };

    public bool IsRising(ReputationScore previous) => Points > previous.Points;
    public bool IsSlipping(ReputationScore previous) => Points < previous.Points;
}
