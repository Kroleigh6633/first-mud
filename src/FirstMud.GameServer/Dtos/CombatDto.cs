namespace FirstMud.GameServer.Dtos;

public sealed record CombatantDto(
    Guid Id,
    string Name,
    string CombatantType,
    int CurrentHp,
    int MaxHp,
    int Speed,
    string Element,
    bool IsPlayerSide,
    bool IsDefeated);

public sealed record CombatUpdateDto(
    Guid EncounterId,
    string State,
    List<CombatantDto> Combatants,
    Guid CurrentActorId,
    int Round);
