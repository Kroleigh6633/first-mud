using FirstMud.Domain.Enums;

namespace FirstMud.Domain.ValueObjects;

public sealed record CombatAbility(
    string Name,
    int BasePower,
    int WeaveCost,
    MagicElement Element,
    AbilityTargetType TargetType,
    AbilityCategory Category);
