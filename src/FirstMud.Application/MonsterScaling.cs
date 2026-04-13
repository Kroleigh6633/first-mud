using FirstMud.Application.Content;

namespace FirstMud.Application;

/// <summary>
/// Single source of truth for monster danger-level scaling math.
///
/// Both <c>FirstMud.GameServer.Services.MonsterFactory.ScaleMonster</c>
/// (live game) and <c>tools/design/encounter-sim</c> (design tool) call
/// into <see cref="Apply(MonsterTemplate, int, MonsterScalingCurve, bool)"/>
/// so a sim run at <c>--danger-level N</c> produces stats bit-identical to
/// what the live game spawns at that danger level.
///
/// Coefficients come from <c>content/combat-curves.json</c>. A
/// <see cref="DefaultCurve"/> mirrors the original inline constants so this
/// helper can be called from contexts that have no <see cref="IContentProvider"/>
/// in hand (early bootstrap, isolated unit tests).
///
/// Variance, per-monster-level overrides, pack-size selection, and the
/// boss-tier flag itself stay in <c>MonsterFactory</c> — this helper only
/// houses the deterministic, data-driven curve math.
/// </summary>
public static class MonsterScaling
{
    /// <summary>
    /// Hardcoded fallback that matches the pre-content-migration constants
    /// (HP +0.4/danger, power +0.3/danger, speed +1/danger, boss x2 HP / +5
    /// speed). Returned by <see cref="DefaultCurve"/>.
    /// </summary>
    public static readonly MonsterScalingCurve DefaultCurve = new(
        HpPerDanger:      0.20,
        PowerPerDanger:   0.15,
        SpeedPerDanger:   0.7,
        BossHpMultiplier: 1.6,
        BossSpeedBonus:   4);

    /// <summary>
    /// Applies the danger-level scaling curve to <paramref name="template"/>.
    /// Pure / deterministic: no RNG, no variance — those layers belong in
    /// <c>MonsterFactory</c>.
    ///
    /// <paramref name="dangerLevel"/> of 0 is a guaranteed no-op: the input
    /// template is returned bit-identical (modulo record-equality), which is
    /// what unscaled <c>encounter-sim</c> runs rely on.
    /// </summary>
    public static MonsterTemplate Apply(
        MonsterTemplate template,
        int dangerLevel,
        MonsterScalingCurve curve,
        bool isBoss = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(curve);

        if (dangerLevel < 0)
            throw new ArgumentOutOfRangeException(
                nameof(dangerLevel), dangerLevel, "danger level must be >= 0.");

        // Fast path: dangerLevel 0 + non-boss must be a true identity. Even
        // the multiplications below would produce identical numbers, but
        // returning the same record avoids allocating a new ability array
        // for every "unscaled" call site.
        if (dangerLevel == 0 && !isBoss)
            return template;

        double hpMultiplier    = 1.0 + dangerLevel * curve.HpPerDanger;
        double powerMultiplier = 1.0 + dangerLevel * curve.PowerPerDanger;

        int scaledHp    = (int)(template.Hp * hpMultiplier);
        int scaledSpeed = template.Speed + (int)(dangerLevel * curve.SpeedPerDanger);

        var scaledAbilities = template.Abilities
            .Select(a => a with { BasePower = (int)(a.BasePower * powerMultiplier) })
            .ToArray();

        if (isBoss)
        {
            scaledHp    = (int)(scaledHp * curve.BossHpMultiplier);
            scaledSpeed += curve.BossSpeedBonus;
        }

        return template with
        {
            Hp        = scaledHp,
            Speed     = scaledSpeed,
            Abilities = scaledAbilities,
        };
    }
}
