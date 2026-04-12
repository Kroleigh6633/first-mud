using FirstMud.Application.Services;
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
    IZoneRepository zoneRepository,
    CombatService combatService,
    CombatHelpers combatHelpers,
    IHubContext<GameHub> hubContext) : ICommandHandler<StartCombatCommand>
{
    public async Task<CommandResult> HandleAsync(StartCombatCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var zones = await zoneRepository.GetByWorldAsync(player.Position.World, ct);
        var zone = zones.FirstOrDefault(z => z.Id == cmd.ZoneId);
        var dangerLevel = zone?.DangerLevel ?? (int)(cmd.ZoneId.GetHashCode() & 0x7FFFFFFF) % 3 + 1;
        var biome = CombatHelpers.GetBiome(zone);
        var monsters = CombatHelpers.BuildMonsterPack(dangerLevel, player.Level, biome);

        // Load all equipped items into a slot → Item dictionary
        var equippedItems = new Dictionary<Domain.Enums.EquipmentSlot, Domain.Entities.Item>();
        foreach (var (slot, itemId) in player.EquippedItems)
        {
            var equippedItem = await itemRepository.GetByIdAsync(itemId, ct);
            if (equippedItem is not null)
                equippedItems[slot] = equippedItem;
        }

        var encounter = await combatService.StartEncounterAsync(
            cmd.PlayerId, cmd.ZoneId, player, [], monsters, equippedItems, ct);

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
            await combatHelpers.SyncPlayerHpAfterCombatAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.TryRollLootAsync(cmd.PlayerId, updated.ZoneId, ct);
            await combatHelpers.TryCaptureCompanionAsync(cmd.PlayerId, updated, ct);
        }
        else if (updated.State == EncounterState.Defeat)
        {
            await combatHelpers.HandlePlayerDefeatAsync(cmd.PlayerId, ct);
        }
        else if (updated.State == EncounterState.Fled)
        {
            await combatHelpers.SyncPlayerHpAfterCombatAsync(cmd.PlayerId, updated, ct);
        }

        var dto = CombatHelpers.BuildCombatUpdateDto(updated, message);

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
