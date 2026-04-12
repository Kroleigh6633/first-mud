using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class MoveCommandHandler(
    IPlayerRepository playerRepository,
    IZoneRepository zoneRepository,
    CombatService combatService,
    CombatHelpers combatHelpers,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<MoveCommand>
{
    public async Task<CommandResult> HandleAsync(MoveCommand cmd, CancellationToken ct)
    {
        if (Math.Abs(cmd.DeltaX) > 1 || Math.Abs(cmd.DeltaY) > 1)
            return new CommandResult(false, "Move delta must be within ±1 per axis.");

        if (cmd.DeltaX == 0 && cmd.DeltaY == 0)
            return new CommandResult(false, "Move delta cannot be (0, 0).");

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var current = player.Position;
        var newPosition = new Position(
            current.World,
            current.ZoneId,
            current.X + cmd.DeltaX,
            current.Y + cmd.DeltaY);

        player.Move(newPosition);
        await playerRepository.UpdateAsync(player, ct);

        var positionPayload = new
        {
            player.Id,
            newPosition.X,
            newPosition.Y,
            newPosition.ZoneId,
            World = newPosition.World.ToString()
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", positionPayload, ct);

        await TryTriggerEncounterAsync(cmd.PlayerId, newPosition, ct);

        return new CommandResult(true, $"Moved to ({newPosition.X}, {newPosition.Y}).", positionPayload);
    }

    private async Task TryTriggerEncounterAsync(Guid playerId, Position pos, CancellationToken ct)
    {
        var zones = await zoneRepository.GetByWorldAsync(pos.World, ct);
        var nearbyZone = ZoneProximity.FindNearby(zones, pos.X, pos.Y);

        if (nearbyZone is null || nearbyZone.DangerLevel <= 0) return;

        var chance = Math.Min(nearbyZone.DangerLevel * 8, 80);
        if (Random.Shared.Next(100) >= chance) return;

        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var monsters = CombatHelpers.BuildMonsterPack(nearbyZone.DangerLevel);

        var encounter = await combatService.StartEncounterAsync(
            playerId, Guid.NewGuid(), player, [], monsters, null, null, ct);

        var monsterNames = string.Join(", ", monsters.Select(m => m.Name));
        await notificationService.SendMessageAsync(
            playerId,
            "combat",
            $"Hostile creatures emerge from {nearbyZone.Name}! You face: {monsterNames}.",
            ct);

        await combatHelpers.ProcessEnemyTurnsAsync(playerId, encounter, ct);

        var dto = CombatHelpers.BuildCombatUpdateDto(encounter);
        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);
    }
}
