using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Entities;

public enum CombatantType
{
    Player,
    Companion,
    Monster
}

public class Combatant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public CombatantType CombatantType { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int Speed { get; private set; }
    public int Agility { get; private set; }
    public MagicElement Element { get; private set; }
    public bool IsPlayerSide { get; private set; }
    public int Level { get; private set; }

    private readonly List<CombatAbility> _abilities = [];
    public IReadOnlyList<CombatAbility> Abilities => _abilities.AsReadOnly();

    public bool IsDefeated => CurrentHp <= 0;

    private Combatant() { }

    public static Combatant Create(
        string name,
        CombatantType combatantType,
        Guid sourceEntityId,
        int maxHp,
        int speed,
        MagicElement element,
        bool isPlayerSide,
        int level,
        IEnumerable<CombatAbility> abilities,
        int agility = 10)
    {
        var combatant = new Combatant
        {
            Id = Guid.NewGuid(),
            Name = name,
            CombatantType = combatantType,
            SourceEntityId = sourceEntityId,
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Speed = speed,
            Agility = agility,
            Element = element,
            IsPlayerSide = isPlayerSide,
            Level = level
        };

        combatant._abilities.AddRange(abilities);
        return combatant;
    }

    public void TakeDamage(int amount)
    {
        if (amount < 0) amount = 0;
        CurrentHp = Math.Max(0, CurrentHp - amount);
    }

    public void Heal(int amount)
    {
        if (amount < 0) amount = 0;
        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }
}
