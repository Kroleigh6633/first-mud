using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Dtos;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class EnterZoneCommandHandler(
    IPlayerRepository playerRepository,
    IZoneRepository zoneRepository,
    GameNotificationService notificationService) : ICommandHandler<EnterZoneCommand>
{
    public async Task<CommandResult> HandleAsync(EnterZoneCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var worldId = player.Position.World;
        var zones = await zoneRepository.GetByWorldAsync(worldId, ct);

        var tiles = zones.Select(z =>
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (x, y) = known ?? ZoneGridLayout.GetPosition(z.Id);
            return new ZoneTileDto(
                z.WorldId.ToString(),
                z.ZoneId,
                z.Name,
                z.Description,
                z.AsciiSymbol,
                z.DangerLevel,
                z.IsPortalZone,
                x,
                y);
        }).ToList();

        var viewDto = new ZoneViewDto(tiles);

        await notificationService.SendEventAsync(cmd.PlayerId, "ZoneView", viewDto, ct);

        return new CommandResult(true, $"Zone view sent with {tiles.Count} tiles.", viewDto);
    }
}
