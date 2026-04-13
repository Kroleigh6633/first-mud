using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.Domain.Entities;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Handlers;

public class StartCombatCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IZoneRepository zoneRepository,
    ICompanionRepository companionRepository,
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

        // Load all equipped items into a slot → Item dictionary
        var equippedItems = new Dictionary<Domain.Enums.EquipmentSlot, Domain.Entities.Item>();
        foreach (var (slot, itemId) in player.EquippedItems)
        {
            var equippedItem = await itemRepository.GetByIdAsync(itemId, ct);
            if (equippedItem is not null)
                equippedItems[slot] = equippedItem;
        }

        var activeCompanions = new List<Companion>();
        foreach (var compId in player.ActiveCompanionIds)
        {
            var comp = await companionRepository.GetByIdAsync(compId, ct);
            if (comp != null && !comp.IsPermanentlyGone)
                activeCompanions.Add(comp);
        }

        int partySize = 1 + activeCompanions.Count;
        var monsters = combatHelpers.BuildMonsterPack(dangerLevel, player.Level, biome, partySize);

        var encounter = await combatService.StartEncounterAsync(
            cmd.PlayerId, cmd.ZoneId, player, activeCompanions, monsters, equippedItems, ct, dangerLevel);

        await combatHelpers.ProcessEnemyTurnsAsync(cmd.PlayerId, encounter, ct, dangerLevel);

        var dto = CombatHelpers.BuildCombatUpdateDto(encounter, dangerLevel: dangerLevel);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, "Combat started.", dto);
    }
}

public class UseCombatAbilityCommandHandler(
    CombatService combatService,
    CombatHelpers combatHelpers,
    IPlayerRepository playerRepository,
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

        var encounterDangerLevel = combatService.GetEncounterDangerLevel(cmd.EncounterId);
        await combatHelpers.ProcessEnemyTurnsAsync(cmd.PlayerId, updated, ct, encounterDangerLevel);

        if (updated.State == EncounterState.Victory)
        {
            await combatHelpers.AwardCombatXpAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.SyncPlayerHpAfterCombatAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.TryRollLootAsync(cmd.PlayerId, updated.ZoneId, ct);
            await combatHelpers.TryCaptureCompanionAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.UpdateCompanionUsageAsync(cmd.PlayerId, 10, ct);

            // Auto-rotate maxed companions on every manual combat victory
            var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
            if (player is not null && player.AutoRotateMaxedCompanions)
            {
                player.RecordCombatVictory();
                await playerRepository.UpdateAsync(player, ct);
                await combatHelpers.TryRotateMaxedCompanionsAsync(cmd.PlayerId, ct);
            }
        }
        else if (updated.State == EncounterState.Defeat)
        {
            await combatHelpers.HandlePlayerDefeatAsync(cmd.PlayerId, ct);
            await combatHelpers.UpdateCompanionUsageAsync(cmd.PlayerId, 10, ct);
        }
        else if (updated.State == EncounterState.Fled)
        {
            await combatHelpers.SyncPlayerHpAfterCombatAsync(cmd.PlayerId, updated, ct);
            await combatHelpers.UpdateCompanionUsageAsync(cmd.PlayerId, 10, ct);
        }

        var dangerLevel = combatService.GetEncounterDangerLevel(cmd.EncounterId);
        var dto = CombatHelpers.BuildCombatUpdateDto(updated, message, dangerLevel);

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
        var dangerLevel = combatService.GetEncounterDangerLevel(cmd.EncounterId);
        var (success, message, updated) = await combatService.FleeAsync(cmd.EncounterId, ct);

        if (!success || updated is null)
            return new CommandResult(false, message);

        var dto = CombatHelpers.BuildCombatUpdateDto(updated, dangerLevel: dangerLevel);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, message, dto);
    }
}
