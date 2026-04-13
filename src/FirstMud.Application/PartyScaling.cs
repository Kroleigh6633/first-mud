using FirstMud.Application.Content;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Application;

/// <summary>
/// Symmetry counterpart to <see cref="MonsterScaling"/>.
///
/// Enemies get multiplied by a danger-based curve; without a matching buff
/// on the player side, the party hits a scaling wall at mid-danger and
/// TPKs through no fault of its own. This helper produces the factor and
/// applies it at combat start time to player + companion MaxHp and every
/// ability's BasePower (both <see cref="Services.CombatService"/> and
/// <see cref="Services.CombatSimulationService"/> call through here so the
/// sim and live game stay in lock-step).
///
/// Formula: <c>factor = 1 + scalingPerTier * (playerLevel + avgCompanionLayer)</c>.
/// A fresh level-1 solo player hits factor = 1.0 (no buff — exactly matches
/// the pre-change baseline). A level-5 + 3×layer-2 party at the default
/// coefficient (0.12) hits factor ≈ 1.84, roughly tracking danger 5's
/// +100% enemy HP.
/// </summary>
public static class PartyScaling
{
    /// <summary>
    /// Returns the party-scaling multiplier. Minimum 1.0 — this is always
    /// a buff, never a nerf.
    /// </summary>
    public static double Factor(PartyScalingCurve curve, int playerLevel, double avgCompanionLayer)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var tiers = Math.Max(0, playerLevel + avgCompanionLayer);
        return Math.Max(1.0, 1.0 + curve.ScalingPerTier * tiers);
    }

    /// <summary>
    /// Convenience: computes average companion layer (0 if no companions).
    /// </summary>
    public static double AvgCompanionLayer(IReadOnlyList<int> layers)
    {
        if (layers is null || layers.Count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < layers.Count; i++) sum += layers[i];
        return sum / layers.Count;
    }

    /// <summary>
    /// Applies the HP/power multiplier to a list of abilities (returns a new
    /// list — abilities are immutable records).
    /// </summary>
    public static List<CombatAbility> ScaleAbilities(IEnumerable<CombatAbility> abilities, double factor)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        if (factor <= 1.0) return abilities.ToList();
        return abilities
            .Select(a => a with { BasePower = (int)(a.BasePower * factor) })
            .ToList();
    }
}
