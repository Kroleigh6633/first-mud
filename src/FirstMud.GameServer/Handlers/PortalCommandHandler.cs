using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class PortalHomeCommandHandler(
    IPlayerRepository playerRepository,
    IHomesteadRepository homesteadRepository,
    IHomesteadBuildingRepository buildingRepository,
    ICompanionRepository companionRepository,
    BuildingService buildingService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<PortalHomeCommand>
{
    public async Task<CommandResult> HandleAsync(PortalHomeCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var homesteadPos = new Position(player.Position.World, 0, -100, -100);
        player.PortalHome(homesteadPos);
        await playerRepository.UpdateAsync(player, ct);

        var posPayload = new
        {
            player.Id,
            X = homesteadPos.X,
            Y = homesteadPos.Y,
            ZoneId = homesteadPos.ZoneId,
            World = homesteadPos.World.ToString()
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", posPayload, ct);

        await notificationService.SendMessageAsync(
            cmd.PlayerId,
            "system",
            "You step through the portal and arrive at your homestead. The world is quiet here.",
            ct);

        // Seed starter buildings on first visit
        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is not null)
        {
            await buildingService.SeedStarterBuildingsAsync(homestead.Id, ct);

            // Push CityView so buildings appear on the map immediately on arrival
            var buildings = await buildingRepository.GetByHomesteadIdAsync(homestead.Id, ct);
            var buildingDtos = new List<object>();
            foreach (var b in buildings)
            {
                string? assignedName = null;
                if (b.AssignedCompanionId.HasValue)
                {
                    var companion = await companionRepository.GetByIdAsync(b.AssignedCompanionId.Value, ct);
                    assignedName = companion?.Name;
                }

                buildingDtos.Add(new
                {
                    id                    = b.Id,
                    homesteadId           = b.HomesteadId,
                    type                  = b.Type.ToString(),
                    tier                  = b.Tier,
                    gridX                 = b.GridX,
                    gridY                 = b.GridY,
                    isConstructed         = b.IsConstructed,
                    constructionProgress  = b.ConstructionProgress,
                    assignedCompanionId   = b.AssignedCompanionId,
                    assignedCompanionName = assignedName,
                });
            }

            var cityViewPayload = new
            {
                homesteadId      = homestead.Id,
                homesteadName    = homestead.Name,
                buildingCount    = buildings.Count,
                constructedCount = buildings.Count(b => b.IsConstructed),
                buildings        = buildingDtos,
            };

            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("CityView", cityViewPayload, ct);
        }

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("AtHomestead", new { playerId = cmd.PlayerId }, ct);

        return new CommandResult(true, "Portaled home.", posPayload);
    }
}

public class PortalBackCommandHandler(
    IPlayerRepository playerRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<PortalBackCommand>
{
    public async Task<CommandResult> HandleAsync(PortalBackCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.SavedReturnPosition is null)
            return new CommandResult(false, "No saved return position. Explore the world first.");

        player.PortalBack();
        await playerRepository.UpdateAsync(player, ct);

        var posPayload = new
        {
            player.Id,
            player.Position.X,
            player.Position.Y,
            ZoneId = player.Position.ZoneId,
            World = player.Position.World.ToString()
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", posPayload, ct);

        await notificationService.SendMessageAsync(
            cmd.PlayerId,
            "system",
            "You step back through the portal and return to where you were.",
            ct);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("LeftHomestead", new { playerId = cmd.PlayerId }, ct);

        return new CommandResult(true, "Portaled back.", posPayload);
    }
}
