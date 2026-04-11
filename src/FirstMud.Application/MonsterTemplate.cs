using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Application;

public sealed record MonsterTemplate(
    string Name,
    int Hp,
    int Speed,
    int Level,
    MagicElement Element,
    IReadOnlyList<CombatAbility> Abilities);
