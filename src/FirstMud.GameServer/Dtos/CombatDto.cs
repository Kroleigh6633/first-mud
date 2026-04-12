namespace FirstMud.GameServer.Dtos;

public sealed record AbilityDto(
    string Name,
    int BasePower,
    int WeaveCost,
    string Element,
    string Category);

public sealed record CombatantDto(
    Guid Id,
    string Name,
    string CombatantType,
    int CurrentHp,
    int MaxHp,
    int Speed,
    string Element,
    bool IsPlayerSide,
    bool IsDefeated,
    List<AbilityDto> Abilities);

public sealed record CombatUpdateDto(
    Guid EncounterId,
    string State,
    List<CombatantDto> Combatants,
    Guid CurrentActorId,
    int Round,
    string? LastActionText = null);
