using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Handles city management commands: placing buildings, assigning builders,
/// and viewing the city panel.
/// </summary>
public class PlaceBuildingCommandHandler(
    BuildingService buildingService,
    IHomesteadRepository homesteadRepository,
    GameNotificationService notificationService) : ICommandHandler<PlaceBuildingCommand>
{
    public async Task<CommandResult> HandleAsync(PlaceBuildingCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<BuildingType>(cmd.BuildingType, ignoreCase: true, out var buildingType))
            return new CommandResult(false, $"Unknown building type: '{cmd.BuildingType}'. Valid types: Forge, Tannery, MarketStall, Farm, Mine, Warehouse, Barracks, Fletcher, EnchantingTower, AlchemistHut, Stoneworker, Woodworker, Library.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "You don't have a homestead yet.");

        var cost = BuildingService.GetConstructionCost(buildingType);
        var costDesc = string.Join(", ", cost.Select(c => $"{c.Qty}× {c.Material}"));

        // Always use auto-positioning — the client no longer needs to specify coordinates
        var (success, message, _) = await buildingService.PlaceBuildingAsync(
            cmd.PlayerId, buildingType,
            BuildingService.AutoPositionSentinel, BuildingService.AutoPositionSentinel, ct);

        if (!success)
            return new CommandResult(false, message);

        await notificationService.SendMessageAsync(
            cmd.PlayerId, "system",
            $"{message} Construction cost: {costDesc}. Use [G] to view your city.",
            ct);

        return new CommandResult(true, message);
    }
}

public class AssignBuilderCommandHandler(
    BuildingService buildingService,
    IPlayerRepository playerRepository,
    GameNotificationService notificationService) : ICommandHandler<AssignBuilderCommand>
{
    public async Task<CommandResult> HandleAsync(AssignBuilderCommand cmd, CancellationToken ct)
    {
        // Load the player so we can pass the authoritative ActiveCompanionIds list.
        // This avoids relying on the stale IsActive boolean on the Companion entity.
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        var activeIds = player?.ActiveCompanionIds;

        var (success, message) = await buildingService.AssignBuilderAsync(
            cmd.PlayerId, cmd.CompanionId, cmd.BuildingId, activeIds, ct);

        if (!success)
            return new CommandResult(false, message);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", message, ct);
        return new CommandResult(true, message);
    }
}

public class UnassignBuilderCommandHandler(
    BuildingService buildingService,
    GameNotificationService notificationService) : ICommandHandler<UnassignBuilderCommand>
{
    public async Task<CommandResult> HandleAsync(UnassignBuilderCommand cmd, CancellationToken ct)
    {
        var (success, message) = await buildingService.UnassignBuilderAsync(
            cmd.PlayerId, cmd.BuildingId, ct);

        if (!success)
            return new CommandResult(false, message);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", message, ct);
        return new CommandResult(true, message);
    }
}

public class BuildStaffEverythingCommandHandler(
    BuildingService buildingService,
    IPlayerRepository playerRepository,
    IHomesteadRepository homesteadRepository,
    IHomesteadBuildingRepository buildingRepository,
    ICompanionRepository companionRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<BuildStaffEverythingCommand>
{
    public async Task<CommandResult> HandleAsync(BuildStaffEverythingCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var (buildingsAdded, companionsAssigned, summary) =
            await buildingService.BuildStaffEverythingAsync(cmd.PlayerId, player.ActiveCompanionIds, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", summary, ct);

        // Push a fresh CityView so the panel updates immediately
        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is not null)
        {
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
                    id                   = b.Id,
                    homesteadId          = b.HomesteadId,
                    type                 = b.Type.ToString(),
                    tier                 = b.Tier,
                    gridX                = b.GridX,
                    gridY                = b.GridY,
                    isConstructed        = b.IsConstructed,
                    constructionProgress = b.ConstructionProgress,
                    assignedCompanionId  = b.AssignedCompanionId,
                    assignedCompanionName= assignedName,
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

        return new CommandResult(true, summary, new { buildingsAdded, companionsAssigned });
    }
}

public class ViewCityCommandHandler(
    IHomesteadRepository homesteadRepository,
    IHomesteadBuildingRepository buildingRepository,
    ICompanionRepository companionRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<ViewCityCommand>
{
    public async Task<CommandResult> HandleAsync(ViewCityCommand cmd, CancellationToken ct)
    {
        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "No homestead found.");

        var buildings = await buildingRepository.GetByHomesteadIdAsync(homestead.Id, ct);

        // Build DTO list for the client
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
                id                   = b.Id,
                homesteadId          = b.HomesteadId,
                type                 = b.Type.ToString(),
                tier                 = b.Tier,
                gridX                = b.GridX,
                gridY                = b.GridY,
                isConstructed        = b.IsConstructed,
                constructionProgress = b.ConstructionProgress,
                assignedCompanionId  = b.AssignedCompanionId,
                assignedCompanionName= assignedName,
            });
        }

        var payload = new
        {
            homesteadId       = homestead.Id,
            homesteadName     = homestead.Name,
            buildingCount     = buildings.Count,
            constructedCount  = buildings.Count(b => b.IsConstructed),
            buildings         = buildingDtos,
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CityView", payload, ct);

        return new CommandResult(true, $"City view loaded. {buildings.Count} building(s).", payload);
    }
}
