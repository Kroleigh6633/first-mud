using FirstMud.Domain.Enums;

namespace FirstMud.Domain.ValueObjects;

/// <summary>
/// Element triangle: Fire > Earth > Air > Water > Fire. Aether is neutral.
/// </summary>
public static class ElementMatchup
{
    public static float GetMultiplier(MagicElement attacker, MagicElement defender)
    {
        if (attacker == MagicElement.Aether || defender == MagicElement.Aether)
            return 1.0f;

        return (attacker, defender) switch
        {
            (MagicElement.Fire,  MagicElement.Earth) => 1.5f,
            (MagicElement.Earth, MagicElement.Air)   => 1.5f,
            (MagicElement.Air,   MagicElement.Water)  => 1.5f,
            (MagicElement.Water, MagicElement.Fire)   => 1.5f,

            // Reverse: disadvantaged
            (MagicElement.Earth, MagicElement.Fire)  => 0.5f,
            (MagicElement.Air,   MagicElement.Earth)  => 0.5f,
            (MagicElement.Water, MagicElement.Air)    => 0.5f,
            (MagicElement.Fire,  MagicElement.Water)  => 0.5f,

            _ => 1.0f
        };
    }
}
