using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class StartCombatCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    CombatService combatService,
    CombatHelpers combatHelpers,
    IHubContext<GameHub> hubContext) : ICommandHandler<StartCombatCommand>
{
    public async Task<CommandResult> HandleAsync(StartCombatCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var dangerLevel = (int)(cmd.ZoneId.GetHashCode() & 0x7FFFFFFF) % 3 + 1;
        var monsters = CombatHelpers.BuildMonsterPack(dangerLevel);

        var equippedWeapon = player.EquippedWeaponId.HasValue
            ? await itemRepository.GetByIdAsync(player.EquippedWeaponId.Value, ct)
            : null;
        var equippedArmor = player.EquippedArmorId.HasValue
            ? await itemRepository.GetByIdAsync(player.EquippedArmorId.Value, ct)
            : null;

        var encounter = await combatService.StartEncounterAsync(
            cmd.PlayerId, cmd.ZoneId, player, [], monsters, equippedWeapon, equippedArmor, ct);

        await combatHelpers.ProcessEnemyTurnsAsync(cmd.PlayerId, encounter, ct);

        var dto = CombatHelpers.BuildCombatUpdateDto(encounter);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, "Combat started.", dto);
    }
}

public class UseCombatAbilityCommandHandler(
    CombatService combatService,
    CombatHelpers combatHelpers,
    IHubContext<GameHub> hubContext) : ICommandHandler<UseCombatAbilityCommand>
{
    public async Task<CommandResult> HandleAsync(UseCombatAbilityCommand cmd, CancellationToken ct)
    {
        var encounter = combatService.GetEncounter(cmd.EncounterId);
        if (encounter is null)
            return new CommandResult(false, "Encounter not found.");

        var currentActor = encounter.CurrentActor;
        if (currentActor is null)
            return new CommandResult(false, "No current actor.");

        var (success, message, updated) = await combatService.ExecuteActionAsync(
            cmd.EncounterId, currentActor.Id, cmd.AbilityName, cmd.TargetId, ct);

        if (!success || updated is null)
            return new CommandResult(false, message);

        await combatHelpers.ProcessEnemyTurnsAsync(cmd.PlayerId, updated, ct);

        if (updated.State == EncounterState.Victory)
        {
            await combatHelpers.AwardCombatXpAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.TryRollLootAsync(cmd.PlayerId, updated.ZoneId, ct);
            await combatHelpers.TryCaptureCompanionAsync(cmd.PlayerId, updated, ct);
        }

        var dto = CombatHelpers.BuildCombatUpdateDto(updated);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, message, dto);
    }
}

public class FleeCombatCommandHandler(
    CombatService combatService,
    IHubContext<GameHub> hubContext) : ICommandHandler<FleeCombatCommand>
{
    public async Task<CommandResult> HandleAsync(FleeCombatCommand cmd, CancellationToken ct)
    {
        var (success, message, updated) = await combatService.FleeAsync(cmd.EncounterId, ct);

        if (!success || updated is null)
            return new CommandResult(false, message);

        var dto = CombatHelpers.BuildCombatUpdateDto(updated);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, message, dto);
    }
}
