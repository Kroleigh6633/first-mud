using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

/// <summary>
/// Handles homestead building placement, construction progress, companion assignment,
/// and seeding starter buildings on first visit.
/// </summary>
public class BuildingService
{
    private readonly IHomesteadBuildingRepository _buildings;
    private readonly IHomesteadRepository _homesteads;
    private readonly ICompanionRepository _companions;
    private readonly ILogger<BuildingService> _logger;

    // Construction cost in (material-name, quantity) pairs per building type.
    // Material names match the canonical resource names used in HomesteadStorageItems.
    private static readonly Dictionary<BuildingType, (string Material, int Qty)[]> ConstructionCosts = new()
    {
        [BuildingType.Forge]          = [("Wood", 20), ("Stone", 30), ("Iron Ore", 10)],
        [BuildingType.Fletcher]       = [("Wood", 15), ("Stone", 10)],
        [BuildingType.Tannery]        = [("Wood", 15), ("Leather", 10)],
        [BuildingType.EnchantingTower]= [("Stone", 30), ("Wood", 10)],
        [BuildingType.AlchemistHut]   = [("Wood", 10), ("Herbs", 15)],
        [BuildingType.Stoneworker]    = [("Stone", 25), ("Wood", 10)],
        [BuildingType.Woodworker]     = [("Wood", 25), ("Stone", 10)],
        [BuildingType.MarketStall]    = [("Wood", 10), ("Stone", 5)],
        [BuildingType.Farm]           = [("Wood", 5),  ("Stone", 5)],
        [BuildingType.Mine]           = [("Stone", 20), ("Iron Ore", 15), ("Wood", 10)],
        [BuildingType.Barracks]       = [("Stone", 25), ("Wood", 15)],
        [BuildingType.Library]        = [("Wood", 20), ("Stone", 15)],
        [BuildingType.Warehouse]      = [("Wood", 25), ("Stone", 15)],
    };

    // Best HomesteadDuty for each building type — used for auto-assignment.
    private static readonly Dictionary<BuildingType, HomesteadDuty> BuildingDuty = new()
    {
        [BuildingType.Forge]          = HomesteadDuty.Crafter,
        [BuildingType.Fletcher]       = HomesteadDuty.Crafter,
        [BuildingType.Tannery]        = HomesteadDuty.Crafter,
        [BuildingType.EnchantingTower]= HomesteadDuty.Crafter,
        [BuildingType.AlchemistHut]   = HomesteadDuty.Crafter,
        [BuildingType.Stoneworker]    = HomesteadDuty.Crafter,
        [BuildingType.Woodworker]     = HomesteadDuty.Harvester,
        [BuildingType.MarketStall]    = HomesteadDuty.Crafter,
        [BuildingType.Farm]           = HomesteadDuty.Harvester,
        [BuildingType.Mine]           = HomesteadDuty.Harvester,
        [BuildingType.Barracks]       = HomesteadDuty.Guard,
        [BuildingType.Library]        = HomesteadDuty.Salvager,
        [BuildingType.Warehouse]      = HomesteadDuty.Guard,
    };

    // Starter buildings placed (free, already constructed) on first homestead visit.
    // Each tuple: (type, gridX, gridY)
    private static readonly (BuildingType Type, int Gx, int Gy)[] StarterBuildings =
    [
        (BuildingType.Forge,     0,  2),   // Workbench/Forge at south-centre
        (BuildingType.Warehouse, -2, 0),   // Storage Shed to the west
        (BuildingType.MarketStall, 2, 0),  // Market Stall to the east
    ];

    public BuildingService(
        IHomesteadBuildingRepository buildings,
        IHomesteadRepository homesteads,
        ICompanionRepository companions,
        ILogger<BuildingService> logger)
    {
        _buildings  = buildings;
        _homesteads = homesteads;
        _companions = companions;
        _logger     = logger;
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all construction costs for a given building type.
    /// </summary>
    public static IReadOnlyList<(string Material, int Qty)> GetConstructionCost(BuildingType type)
    {
        return ConstructionCosts.TryGetValue(type, out var costs)
            ? costs
            : Array.Empty<(string, int)>();
    }

    /// <summary>
    /// Places a new building plot on the homestead grid.
    /// Does not consume materials — caller should verify storage first.
    /// Returns (success, message, building?).
    /// </summary>
    public async Task<(bool Success, string Message, HomesteadBuilding? Building)> PlaceBuildingAsync(
        Guid playerId,
        BuildingType buildingType,
        int gridX,
        int gridY,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null)
            return (false, "No homestead found.", null);

        // Prevent duplicate placement at same grid position
        var existing = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        if (existing.Any(b => b.GridX == gridX && b.GridY == gridY))
            return (false, $"Position ({gridX},{gridY}) is already occupied.", null);

        var building = HomesteadBuilding.Create(homestead.Id, buildingType, gridX, gridY);
        await _buildings.AddAsync(building, ct);

        _logger.LogInformation("Player {PlayerId} placed {BuildingType} at ({Gx},{Gy}).", playerId, buildingType, gridX, gridY);
        return (true, $"{buildingType} plot placed at ({gridX},{gridY}). Assign a builder to begin construction.", building);
    }

    /// <summary>
    /// Assigns a companion as builder on a building under construction.
    /// The companion must not be actively adventuring.
    /// </summary>
    public async Task<(bool Success, string Message)> AssignBuilderAsync(
        Guid playerId,
        Guid companionId,
        Guid buildingId,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null)
            return (false, "No homestead found.");

        var building = await _buildings.GetByIdAsync(buildingId, ct);
        if (building is null)
            return (false, "Building not found.");
        if (building.HomesteadId != homestead.Id)
            return (false, "That building does not belong to your homestead.");
        if (building.IsConstructed)
            return (false, "That building is already complete.");

        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            return (false, "Companion not found.");
        if (companion.OwnerId != playerId)
            return (false, "That companion does not belong to you.");
        if (companion.IsActive)
            return (false, $"{companion.Name} is currently adventuring — deactivate them first.");

        // Assign the companion to Builder duty on homestead
        companion.AssignToHomestead(HomesteadDuty.Crafter); // Builder uses Crafter duty
        building.AssignCompanion(companionId);

        await _companions.UpdateAsync(companion, ct);
        await _buildings.UpdateAsync(building, ct);

        return (true, $"{companion.Name} assigned to build {building.Type}.");
    }

    /// <summary>
    /// Removes the companion assigned to a building, recalling them to idle.
    /// </summary>
    public async Task<(bool Success, string Message)> UnassignBuilderAsync(
        Guid playerId,
        Guid buildingId,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null)
            return (false, "No homestead found.");

        var building = await _buildings.GetByIdAsync(buildingId, ct);
        if (building is null)
            return (false, "Building not found.");
        if (building.HomesteadId != homestead.Id)
            return (false, "That building does not belong to your homestead.");
        if (!building.AssignedCompanionId.HasValue)
            return (false, "No companion is assigned to that building.");

        var companionId = building.AssignedCompanionId.Value;
        building.UnassignCompanion();
        await _buildings.UpdateAsync(building, ct);

        // Recall companion back to idle (clear duty)
        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is not null)
        {
            companion.RecallFromHomestead();
            await _companions.UpdateAsync(companion, ct);
        }

        var name = companion?.Name ?? "Companion";
        _logger.LogInformation("Player {PlayerId} unassigned {CompanionName} from {BuildingType}.", playerId, name, building.Type);
        return (true, $"{name} recalled from {building.Type}.");
    }

    /// <summary>
    /// Assigns the best-aptitude idle companion to a newly completed building.
    /// Called automatically when construction finishes.
    /// </summary>
    public async Task<Companion?> AutoAssignCompanionAsync(
        Guid playerId,
        HomesteadBuilding building,
        CancellationToken ct = default)
    {
        if (!building.IsConstructed) return null;
        if (!BuildingDuty.TryGetValue(building.Type, out var duty)) return null;

        var companions = await _companions.GetByOwnerAsync(playerId, ct);

        // Find idle companions (not adventuring, not on duty) with the best aptitude
        var best = companions
            .Where(c => !c.IsPermanentlyGone && !c.IsActive && !c.AssignedDuty.HasValue)
            .OrderByDescending(c => c.GetAptitude(duty))
            .ThenByDescending(c => c.Level)
            .FirstOrDefault();

        if (best is null) return null;

        best.AssignToHomestead(duty);
        building.AssignCompanion(best.Id);

        await _companions.UpdateAsync(best, ct);
        await _buildings.UpdateAsync(building, ct);

        _logger.LogInformation("Auto-assigned {CompanionName} to {BuildingType} (duty: {Duty}).", best.Name, building.Type, duty);
        return best;
    }

    /// <summary>
    /// Seeds the three free starter buildings for a new homestead.
    /// Only places them if none exist yet.
    /// </summary>
    public async Task SeedStarterBuildingsAsync(Guid homesteadId, CancellationToken ct = default)
    {
        var existing = await _buildings.GetByHomesteadIdAsync(homesteadId, ct);
        if (existing.Count > 0) return; // already seeded

        foreach (var (type, gx, gy) in StarterBuildings)
        {
            var building = HomesteadBuilding.Create(homesteadId, type, gx, gy, tier: 1, alreadyConstructed: true);
            await _buildings.AddAsync(building, ct);
        }

        _logger.LogInformation("Seeded 3 starter buildings for homestead {HomesteadId}.", homesteadId);
    }

    /// <summary>
    /// Advances construction for all buildings under construction.
    /// Counts companions assigned to each building (AssignedCompanionId) and applies 5% per builder.
    /// Returns BuildingConstructionResult entries for newly completed buildings.
    /// </summary>
    public async Task<IReadOnlyList<BuildingConstructionResult>> TickConstructionAsync(CancellationToken ct = default)
    {
        var results = new List<BuildingConstructionResult>();
        var underConstruction = await _buildings.GetUnderConstructionAsync(ct);

        foreach (var building in underConstruction)
        {
            // Count assigned builders (each assigned companion = 1 builder)
            int builderCount = building.AssignedCompanionId.HasValue ? 1 : 0;
            if (builderCount == 0) continue; // no builder assigned, no progress

            // 5% per builder per tick
            int progressDelta = builderCount * 5;
            bool completed = building.AdvanceConstruction(progressDelta);
            await _buildings.UpdateAsync(building, ct);

            if (completed)
            {
                // Look up homestead owner for the notification
                Guid playerId = await GetHomesteadOwnerAsync(building.HomesteadId, ct);

                // Auto-assign best companion if building just finished
                Companion? assignee = null;
                if (playerId != Guid.Empty)
                    assignee = await AutoAssignCompanionAsync(playerId, building, ct);

                results.Add(new BuildingConstructionResult(
                    playerId,
                    building.Id,
                    building.Type,
                    assignee?.Name));
            }
        }

        return results;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> GetHomesteadOwnerAsync(Guid homesteadId, CancellationToken ct)
    {
        // We load via the buildings repo; homestead entity has PlayerId
        // Use the homestead repo — GetByPlayerIdAsync doesn't help here,
        // so we load all homesteads and match by id.
        // For now we do it by loading the buildings list (already done by caller).
        // We need to expose a GetByIdAsync on the homestead repo or just scan.
        // Simplest: we can check from the homestead's PlayerId via a direct query.
        // Since IHomesteadRepository only has GetByPlayerIdAsync we need to add GetByIdAsync
        // or work around it. We'll query via the building's homesteadId directly.
        // For Phase 1 we accept a small inefficiency and add a helper method that
        // reads from IHomesteadRepository once per unique homesteadId.
        // The IHomesteadRepository doesn't have GetByIdAsync — but we can use
        // the EF context indirectly.  For now we store the PlayerId in a local
        // dictionary keyed by homesteadId on the first tick.
        // Best approach: add GetByIdAsync to IHomesteadRepository in Phase 2.
        // For Phase 1 we walk all buildings in the same homestead to find one
        // that has a known owner.
        //
        // Actually, the cleanest solution is to expose a simple GetByIdAsync
        // in IHomesteadRepository — but to avoid scope creep here we just
        // use the buildings list (the building already has HomesteadId).
        // We must look up homestead→player. We'll cache a small dictionary
        // within the service scope.  Since BuildingService is scoped, this is
        // fine.
        if (_homesteadOwnerCache.TryGetValue(homesteadId, out var cached))
            return cached;

        // Fall back: scan for a homestead. IHomesteadRepository.GetByPlayerIdAsync
        // needs a playerId, not a homesteadId, so we rely on an extension added below.
        // For now return empty — the tick caller will still update construction,
        // just without a targeted push.
        return Guid.Empty;
    }

    // Cache built during a single service scope lifetime.
    private readonly Dictionary<Guid, Guid> _homesteadOwnerCache = [];

    /// <summary>
    /// Populates the homestead→player cache so TickConstructionAsync can emit
    /// per-player notifications.  Call once per tick cycle before calling TickConstructionAsync.
    /// </summary>
    public void RegisterHomesteadOwner(Guid homesteadId, Guid playerId)
    {
        _homesteadOwnerCache[homesteadId] = playerId;
    }
}

public record BuildingConstructionResult(
    Guid PlayerId,
    Guid BuildingId,
    BuildingType BuildingType,
    string? AutoAssignedCompanionName);
