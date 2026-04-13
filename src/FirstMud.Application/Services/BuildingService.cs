using FirstMud.Application.Content;
using FirstMud.Application.Events;
using FirstMud.Engine.Events;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

/// <summary>
/// Handles homestead building placement, construction progress, companion assignment,
/// and seeding starter buildings on first visit.
///
/// Construction costs, worker capacities, duty mappings, and hut tier capacities
/// are defined in <c>content/buildings.json</c> and resolved through
/// <see cref="IContentProvider"/>. See <see cref="BuildingDefinition"/>.
/// </summary>
public class BuildingService
{
    private readonly IHomesteadBuildingRepository _buildings;
    private readonly IHomesteadRepository _homesteads;
    private readonly ICompanionRepository _companions;
    private readonly IItemRepository _items;
    private readonly IGameEventPublisher _events;
    private readonly IContentProvider _content;
    private readonly ILogger<BuildingService> _logger;

    /// <summary>
    /// Returns true if this building type is residential housing (holds residents,
    /// not workers). Shape-of-the-game check; kept as a pure static helper because
    /// callers shouldn't need an <see cref="IContentProvider"/> to know a Hut is a hut.
    /// </summary>
    public static bool IsHousingType(BuildingType type) => type == BuildingType.Hut;

    /// <summary>Returns true if this building type is a production building (holds workers).</summary>
    public static bool IsProductionType(BuildingType type) => !IsHousingType(type);

    /// <summary>Returns the max worker count for a building type, from content/buildings.json.</summary>
    public int GetWorkerCapacity(BuildingType type) =>
        _content.GetBuilding(type)?.WorkerCapacity ?? 1;

    /// <summary>Returns the resident capacity of a Hut at the given tier, from content/buildings.json.</summary>
    public int GetHutCapacity(int tier) => _content.GetHutCapacity(BuildingType.Hut, tier);

    /// <summary>Returns the HomesteadDuty mapped to a production building type, or null for housing.</summary>
    public HomesteadDuty? GetBuildingDuty(BuildingType type) =>
        _content.GetBuilding(type)?.Duty;

    /// <summary>Returns all construction costs for the given building type.</summary>
    public IReadOnlyList<(string Material, int Qty)> GetConstructionCost(BuildingType type)
    {
        var def = _content.GetBuilding(type);
        if (def is null) return Array.Empty<(string, int)>();
        return def.ConstructionCost
            .Select(c => (c.Material, c.Quantity))
            .ToList();
    }

    // Starter buildings placed (free, already constructed) on first homestead visit.
    // Each tuple: (type, gridX, gridY, alreadyConstructed, constructionProgress)
    private static readonly (BuildingType Type, int Gx, int Gy, bool Built, int Progress)[] StarterBuildings =
    [
        // Core infrastructure — fully constructed
        (BuildingType.Forge,       0,  2, true,  100),   // Forge at south-centre
        (BuildingType.Warehouse,  -2,  0, true,  100),   // Warehouse to the west
        (BuildingType.MarketStall, 2,  0, true,  100),   // Market Stall to the east
        (BuildingType.Barracks,    0, -2, true,  100),   // Barracks to the north (guards)
        (BuildingType.Farm,       -2, -2, true,  100),   // Farm to the northwest (food)
        // Under construction at 50% — needs a builder to finish
        (BuildingType.Tannery,     2,  2, false,  50),   // Tannery to the southeast
        (BuildingType.Woodworker, -2,  2, false,  50),   // Woodworker to the southwest
        (BuildingType.AlchemistHut,2, -2, false,  50),   // Alchemist to the northeast
    ];

    // Hut positions in a ring around the starter buildings for companion housing.
    // Each is at radius 3 around the centre to avoid core building overlap.
    private static readonly (int Gx, int Gy)[] HutRingPositions =
    [
        ( 3,  0), ( 3,  1), ( 3, -1),   // east arc
        (-3,  0), (-3,  1), (-3, -1),   // west arc
        ( 0,  3), ( 1,  3), (-1,  3),   // south arc
        ( 0, -3), ( 1, -3), (-1, -3),   // north arc (extra)
    ];

    public BuildingService(
        IHomesteadBuildingRepository buildings,
        IHomesteadRepository homesteads,
        ICompanionRepository companions,
        IItemRepository items,
        IGameEventPublisher events,
        IContentProvider content,
        ILogger<BuildingService> logger)
    {
        _buildings  = buildings;
        _homesteads = homesteads;
        _companions = companions;
        _items      = items;
        _events     = events;
        _content    = content;
        _logger     = logger;
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Places a new building plot on the homestead grid.
    /// Checks and consumes the construction cost from homestead storage (first) then
    /// player inventory before placing.  Returns a failure result if materials are insufficient.
    /// When gridX/gridY are both the sentinel value <c>AutoPositionSentinel</c>, the server
    /// automatically picks the next available spiral position so the client never needs to
    /// specify coordinates.
    /// Returns (success, message, building?).
    /// </summary>
    public const int AutoPositionSentinel = -999;

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

        // ── 1. Check construction cost ────────────────────────────────────────────
        var costs = GetConstructionCost(buildingType);
        if (costs.Count > 0)
        {
            foreach (var (material, required) in costs)
            {
                int available = 0;

                // Count how much is in homestead storage
                var storageRow = await _homesteads.GetStorageItemByNameAsync(homestead.Id, material, ct);
                if (storageRow is not null)
                {
                    var storageItem = await _items.GetByIdAsync(storageRow.ItemId, ct);
                    if (storageItem is not null)
                        available += storageItem.Quantity;
                }

                // Count how much is in player inventory
                var invItem = await _items.GetByOwnerAndNameAsync(playerId, material, ItemCategory.Component, ct);
                if (invItem is not null)
                    available += invItem.Quantity;

                if (available < required)
                    return (false, $"Not enough {material} (need {required}, have {available}).", null);
            }

            // ── 2. Consume materials — storage first, then inventory ─────────────
            foreach (var (material, required) in costs)
            {
                int remaining = required;

                // Deduct from storage first
                var storageRow = await _homesteads.GetStorageItemByNameAsync(homestead.Id, material, ct);
                if (storageRow is not null)
                {
                    var storageItem = await _items.GetByIdAsync(storageRow.ItemId, ct);
                    if (storageItem is not null)
                    {
                        int fromStorage = Math.Min(remaining, storageItem.Quantity);
                        storageItem.TryRemoveQuantity(fromStorage, out _);
                        remaining -= fromStorage;

                        if (storageItem.Quantity <= 0)
                        {
                            // Remove the join row first, then delete the item
                            await _homesteads.RemoveStorageItemAsync(homestead.Id, storageItem.Id, ct);
                            await _items.DeleteAsync(storageItem.Id, ct);
                        }
                        else
                        {
                            await _items.UpdateAsync(storageItem, ct);
                        }
                    }
                }

                // Deduct remainder from inventory
                if (remaining > 0)
                {
                    var invItem = await _items.GetByOwnerAndNameAsync(playerId, material, ItemCategory.Component, ct);
                    if (invItem is not null)
                    {
                        invItem.TryRemoveQuantity(remaining, out _);
                        remaining = 0;

                        if (invItem.Quantity <= 0)
                            await _items.DeleteAsync(invItem.Id, ct);
                        else
                            await _items.UpdateAsync(invItem, ct);
                    }
                }

                // remaining > 0 here would mean insufficient funds — but we already checked above,
                // so this should never happen in practice.
            }
        }

        // ── 3. Place the building ─────────────────────────────────────────────────
        var existing = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);

        // Auto-pick position when client sends sentinel or (0,0) is already occupied
        if (gridX == AutoPositionSentinel && gridY == AutoPositionSentinel)
        {
            var pos = NextSpiralPosition(existing);
            gridX = pos.gx;
            gridY = pos.gy;
        }
        else if (existing.Any(b => b.GridX == gridX && b.GridY == gridY))
        {
            // Specific position requested but occupied — fall back to auto
            var pos = NextSpiralPosition(existing);
            gridX = pos.gx;
            gridY = pos.gy;
        }

        var building = HomesteadBuilding.Create(homestead.Id, buildingType, gridX, gridY);
        await _buildings.AddAsync(building, ct);

        _logger.LogInformation("Player {PlayerId} placed {BuildingType} at ({Gx},{Gy}).", playerId, buildingType, gridX, gridY);
        return (true, $"{buildingType} plot placed at ({gridX},{gridY}). Assign a builder to begin construction.", building);
    }

    /// <summary>
    /// Assigns a companion as builder on a building under construction.
    /// The companion must not be in the player's active adventuring party.
    /// Pass <paramref name="playerActiveCompanionIds"/> (from <c>player.ActiveCompanionIds</c>)
    /// as the authoritative active list — avoids relying on the stale <c>IsActive</c> boolean.
    /// </summary>
    public async Task<(bool Success, string Message)> AssignBuilderAsync(
        Guid playerId,
        Guid companionId,
        Guid buildingId,
        IReadOnlyList<Guid>? playerActiveCompanionIds = null,
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

        if (IsHousingType(building.Type))
            return (false, $"{building.Type} is a housing building — companions live there automatically. Use the housing assignment system instead.");

        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            return (false, "Companion not found.");
        if (companion.OwnerId != playerId)
            return (false, "That companion does not belong to you.");

        // Use the authoritative ActiveCompanionIds list when provided; fall back to IsActive flag
        bool isActivelyAdventuring = playerActiveCompanionIds is not null
            ? playerActiveCompanionIds.Contains(companionId)
            : companion.IsActive;

        if (isActivelyAdventuring)
            return (false, $"{companion.Name} is currently adventuring — deactivate them first.");

        // Determine duty: buildings under construction use Crafter; completed buildings use their proper duty
        var duty = building.IsConstructed
            ? (GetBuildingDuty(building.Type) ?? HomesteadDuty.Crafter)
            : HomesteadDuty.Crafter;

        companion.AssignToHomestead(duty);
        building.AssignCompanion(companionId);

        await _companions.UpdateAsync(companion, ct);
        await _buildings.UpdateAsync(building, ct);

        return (true, $"{companion.Name} assigned to {building.Type}.");
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
        if (building.WorkerCount == 0)
            return (false, "No companion is assigned to that building.");

        // Recall all workers from this building
        var workerIds = building.AssignedCompanionIds.ToList();
        building.UnassignCompanion();
        await _buildings.UpdateAsync(building, ct);

        var names = new List<string>();
        foreach (var companionId in workerIds)
        {
            var companion = await _companions.GetByIdAsync(companionId, ct);
            if (companion is not null)
            {
                companion.RecallFromHomestead();
                await _companions.UpdateAsync(companion, ct);
                names.Add(companion.Name);
            }
        }

        var name = names.Count > 0 ? string.Join(", ", names) : "Companion";
        _logger.LogInformation("Player {PlayerId} unassigned {CompanionName} from {BuildingType}.", playerId, name, building.Type);
        return (true, $"{name} recalled from {building.Type}.");
    }

    /// <summary>
    /// Assigns the best-aptitude idle companion to a newly completed building.
    /// Called automatically when construction finishes.
    /// Pass <paramref name="playerActiveCompanionIds"/> (from <c>player.ActiveCompanionIds</c>)
    /// as the authoritative active list — avoids relying on the stale <c>IsActive</c> boolean.
    /// </summary>
    public async Task<Companion?> AutoAssignCompanionAsync(
        Guid playerId,
        HomesteadBuilding building,
        IReadOnlyList<Guid>? playerActiveCompanionIds = null,
        CancellationToken ct = default)
    {
        if (!building.IsConstructed) return null;
        // Huts are housing — they don't take workers via this method
        if (IsHousingType(building.Type)) return null;
        var maybeDuty = GetBuildingDuty(building.Type);
        if (maybeDuty is null) return null;
        var duty = maybeDuty.Value;

        var companions = await _companions.GetByOwnerAsync(playerId, ct);
        var activeIds = playerActiveCompanionIds ?? [];

        // Find idle companions (not adventuring per authoritative list, not on duty) with the best aptitude
        // Use activeIds (from player.ActiveCompanionIds) rather than the stale IsActive boolean.
        var best = companions
            .Where(c => !c.IsPermanentlyGone && !activeIds.Contains(c.Id) && !c.AssignedDuty.HasValue)
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
    /// Seeds (or fills in missing) starter village buildings for a homestead.
    /// Uses an UPSERT pattern — checks which buildings already exist by type+position
    /// and only adds the missing ones.  Safe to call repeatedly; never duplicates.
    /// Places core infrastructure (5 complete + 3 under construction) plus 10 Huts.
    /// Returns the number of buildings actually added.
    /// </summary>
    public async Task<int> SeedStarterBuildingsAsync(Guid homesteadId, CancellationToken ct = default)
    {
        var existing = await _buildings.GetByHomesteadIdAsync(homesteadId, ct);

        // Index existing buildings by their canonical grid position so we can skip them
        var occupiedPositions = existing
            .Select(b => (b.GridX, b.GridY))
            .ToHashSet();

        int added = 0;

        // Place the core starter buildings (some constructed, some at 50%) — skip any already present
        foreach (var (type, gx, gy, built, progress) in StarterBuildings)
        {
            if (occupiedPositions.Contains((gx, gy)))
                continue;

            var building = HomesteadBuilding.Create(homesteadId, type, gx, gy, tier: 1, alreadyConstructed: built);
            if (!built && progress > 0)
                building.AdvanceConstruction(progress);
            await _buildings.AddAsync(building, ct);
            occupiedPositions.Add((gx, gy));
            added++;
        }

        // Seed up to 10 Huts — skip positions already occupied
        int hutCount = 0;
        foreach (var (gx, gy) in HutRingPositions)
        {
            if (hutCount >= 10) break;
            if (occupiedPositions.Contains((gx, gy)))
            {
                hutCount++;
                continue;
            }

            var hut = HomesteadBuilding.Create(homesteadId, BuildingType.Hut, gx, gy, tier: 1, alreadyConstructed: true);
            await _buildings.AddAsync(hut, ct);
            occupiedPositions.Add((gx, gy));
            hutCount++;
            added++;
        }

        if (added > 0)
            _logger.LogInformation(
                "Seeded {Added} missing starter buildings for homestead {HomesteadId} (total now: {Total}).",
                added, homesteadId, existing.Count + added);

        return added;
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
        var ownerByHomestead = new Dictionary<Guid, Guid>();
        var touchedPlayers = new HashSet<Guid>();

        foreach (var building in underConstruction)
        {
            // Count assigned builders (each assigned companion = 1 builder)
            int builderCount = building.WorkerCount;
            if (builderCount == 0) continue; // no builder assigned, no progress

            // 5% per builder per tick
            int progressDelta = builderCount * 5;
            bool completed = building.AdvanceConstruction(progressDelta);
            await _buildings.UpdateAsync(building, ct);

            // Track owning player (batched per homestead) for post-loop city refresh publish.
            if (!ownerByHomestead.TryGetValue(building.HomesteadId, out var owner))
            {
                owner = await GetHomesteadOwnerAsync(building.HomesteadId, ct);
                ownerByHomestead[building.HomesteadId] = owner;
            }
            if (owner != Guid.Empty)
                touchedPlayers.Add(owner);

            if (completed)
            {
                // Reuse the cached homestead owner lookup from above.
                Guid playerId = owner;

                if (playerId != Guid.Empty)
                {
                    // Free all builders from the completed building so they can be reassigned
                    foreach (var builderId in building.AssignedCompanionIds.ToList())
                    {
                        var builder = await _companions.GetByIdAsync(builderId, ct);
                        if (builder is not null)
                        {
                            builder.RecallFromHomestead();
                            await _companions.UpdateAsync(builder, ct);
                        }
                    }
                    building.UnassignCompanion();
                    await _buildings.UpdateAsync(building, ct);

                    // Now auto-assign everyone (including the freed builder) to their best fit
                    await AutoAssignIdleCompanionsAsync(playerId, null, ct);

                    // Reload to get the new assignee name
                    var refreshedBuilding = await _buildings.GetByIdAsync(building.Id, ct);
                    string? assigneeName = null;
                    if (refreshedBuilding is not null && refreshedBuilding.WorkerCount > 0)
                    {
                        var assignee = await _companions.GetByIdAsync(refreshedBuilding.AssignedCompanionIds[0], ct);
                        assigneeName = assignee?.Name;
                    }

                    results.Add(new BuildingConstructionResult(
                        playerId,
                        building.Id,
                        building.Type,
                        assigneeName));
                }
                else
                {
                    results.Add(new BuildingConstructionResult(
                        Guid.Empty,
                        building.Id,
                        building.Type,
                        null));
                }
            }
        }

        // MINOR #9: per-player city refresh for any homestead whose construction advanced.
        foreach (var playerId in touchedPlayers)
            await _events.PublishAsync(playerId, new CityStateChangedEvent("ConstructionTick"), ct);

        return results;
    }

    /// <summary>
    /// Master one-click operation: seeds all missing starter buildings then delegates to
    /// the smart auto-assign system which fills production → construction → guard overflow,
    /// pulling guards for critical needs when no idle companions are available.
    /// </summary>
    public async Task<(int BuildingsAdded, int CompanionsAssigned, string Summary)> BuildStaffEverythingAsync(
        Guid playerId,
        IReadOnlyList<Guid>? playerActiveCompanionIds = null,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null)
            return (0, 0, "No homestead found.");

        // Step 1 — seed missing starter buildings
        int added = await SeedStarterBuildingsAsync(homestead.Id, ct);

        // Step 2 — smart auto-assign: production → construction → guard overflow
        // This pulls guards for critical needs when idle pool is empty.
        int assigned = await AutoAssignIdleCompanionsAsync(playerId, playerActiveCompanionIds, ct);

        var summary = $"Placed {added} building(s), reassigned {assigned} companion(s) to optimal duties.";
        _logger.LogInformation("BuildStaffEverything for player {PlayerId}: {Summary}", playerId, summary);

        return (added, assigned, summary);
    }

    /// <summary>
    /// Finds the first constructed hut with a vacancy and assigns the given companion to it.
    /// Called automatically when a new companion is captured (if not activated into the active party).
    /// Returns the hut the companion was assigned to, or null if no vacancy is available.
    /// </summary>
    public async Task<HomesteadBuilding?> AutoAssignHousingAsync(
        Guid playerId,
        Companion companion,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null) return null;

        var buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        var huts = buildings
            .Where(b => IsHousingType(b.Type) && b.IsConstructed)
            .ToList();
        if (huts.Count == 0) return null;

        var allCompanions = await _companions.GetByOwnerAsync(playerId, ct);

        foreach (var hut in huts)
        {
            var capacity = GetHutCapacity(hut.Tier);
            var currentResidents = allCompanions.Count(c => c.HousingBuildingId == hut.Id);
            if (currentResidents >= capacity) continue;

            companion.AssignHousing(hut.Id);
            await _companions.UpdateAsync(companion, ct);

            _logger.LogInformation(
                "Auto-assigned {CompanionName} housing in {HutId} for player {PlayerId}.",
                companion.Name, hut.Id, playerId);

            return hut;
        }

        return null; // all huts full
    }

    /// <summary>
    /// Smart auto-assign: the city administrator assigns all non-adventuring companions.
    /// Priority order:
    ///   1. Staff constructed production buildings (city needs first)
    ///   2. Assign builders to under-construction buildings
    ///   3. Overflow → Guard duty
    /// When idle companions aren't enough, PULLS guards from overflow to fill critical roles.
    /// Also auto-houses all companions in available huts.
    /// Call this whenever the companion roster changes (capture, party swap, recall, building done, etc.).
    /// </summary>
    public async Task<int> AutoAssignIdleCompanionsAsync(
        Guid playerId,
        IReadOnlyList<Guid>? playerActiveCompanionIds = null,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null) return 0;

        var buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        var companions = await _companions.GetByOwnerAsync(playerId, ct);
        var activeIds = playerActiveCompanionIds ?? [];

        // Pool = all non-adventuring, non-permanently-gone companions
        var pool = companions
            .Where(c => !c.IsPermanentlyGone && !activeIds.Contains(c.Id))
            .ToList();

        if (pool.Count == 0) return 0;

        int changes = 0;

        // ── Step 1: Staff constructed production buildings to capacity (highest priority) ─
        var understaffedProduction = buildings
            .Where(b => IsProductionType(b.Type) && b.IsConstructed && b.WorkerCount < GetWorkerCapacity(b.Type))
            .OrderByDescending(b => GetWorkerCapacity(b.Type) - b.WorkerCount) // most vacant first
            .ToList();

        foreach (var building in understaffedProduction)
        {
            var maybeDuty = GetBuildingDuty(building.Type);
            if (maybeDuty is null) continue;
            var duty = maybeDuty.Value;
            int vacancies = GetWorkerCapacity(building.Type) - building.WorkerCount;

            for (int i = 0; i < vacancies; i++)
            {
                // Try idle companions first, then pull a guard
                var candidate = pool
                    .Where(c => !c.AssignedDuty.HasValue)
                    .OrderByDescending(c => c.GetAptitude(duty))
                    .ThenByDescending(c => c.Level)
                    .FirstOrDefault();

                candidate ??= pool
                    .Where(c => c.AssignedDuty == HomesteadDuty.Guard)
                    .OrderByDescending(c => c.GetAptitude(duty))
                    .ThenByDescending(c => c.Level)
                    .FirstOrDefault();

                if (candidate is null) break;

                // If pulling from guard, clear their old building assignment first
                if (candidate.AssignedDuty == HomesteadDuty.Guard)
                {
                    var oldBuilding = buildings.FirstOrDefault(b => b.HasCompanion(candidate.Id));
                    if (oldBuilding is not null)
                    {
                        oldBuilding.RemoveCompanion(candidate.Id);
                        await _buildings.UpdateAsync(oldBuilding, ct);
                    }
                }

                candidate.AssignToHomestead(duty);
                building.AssignCompanion(candidate.Id);
                await _companions.UpdateAsync(candidate, ct);
                await _buildings.UpdateAsync(building, ct);
                pool.Remove(candidate);
                changes++;
            }
        }

        // ── Step 2: Auto-detect housing shortage → place huts BEFORE assigning builders
        buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        companions = await _companions.GetByOwnerAsync(playerId, ct);
        var totalNonActive = companions.Count(c => !c.IsPermanentlyGone && !activeIds.Contains(c.Id));
        var totalHousingCapacity = buildings
            .Where(b => IsHousingType(b.Type))  // count ALL huts, including under-construction
            .Sum(b => GetHutCapacity(b.Tier));

        if (totalNonActive > totalHousingCapacity)
        {
            int hutsNeeded = (int)Math.Ceiling((totalNonActive - totalHousingCapacity) / 3.0);
            for (int i = 0; i < hutsNeeded; i++)
            {
                var (success, _, _) = await PlaceBuildingAsync(playerId, BuildingType.Hut,
                    AutoPositionSentinel, AutoPositionSentinel, ct);
                if (success) changes++;
            }
        }

        // ── Step 3: Assign builders to ALL under-construction buildings (pull guards) ─
        buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        companions = await _companions.GetByOwnerAsync(playerId, ct);
        pool = companions.Where(c => !c.IsPermanentlyGone && !activeIds.Contains(c.Id)).ToList();

        var needBuilders = buildings
            .Where(b => !b.IsConstructed && b.WorkerCount == 0)
            .ToList();

        foreach (var building in needBuilders)
        {
            // Try idle first, then pull a guard for construction
            var candidate = pool
                .Where(c => !c.AssignedDuty.HasValue)
                .OrderByDescending(c => c.GetAptitude(HomesteadDuty.Crafter))
                .ThenByDescending(c => c.Level)
                .FirstOrDefault();

            candidate ??= pool
                .Where(c => c.AssignedDuty == HomesteadDuty.Guard)
                .OrderByDescending(c => c.GetAptitude(HomesteadDuty.Crafter))
                .ThenByDescending(c => c.Level)
                .FirstOrDefault();

            if (candidate is null) break;

            if (candidate.AssignedDuty == HomesteadDuty.Guard)
            {
                var oldBuilding = buildings.FirstOrDefault(b => b.HasCompanion(candidate.Id));
                if (oldBuilding is not null)
                {
                    oldBuilding.RemoveCompanion(candidate.Id);
                    await _buildings.UpdateAsync(oldBuilding, ct);
                }
            }

            candidate.AssignToHomestead(HomesteadDuty.Crafter);
            building.AssignCompanion(candidate.Id);
            await _companions.UpdateAsync(candidate, ct);
            await _buildings.UpdateAsync(building, ct);
            pool.Remove(candidate);
            changes++;
        }

        // ── Step 4: Overflow → Guard duty ─────────────────────────────────────────
        companions = await _companions.GetByOwnerAsync(playerId, ct);
        foreach (var idle in companions
            .Where(c => !c.IsPermanentlyGone
                     && !activeIds.Contains(c.Id)
                     && !c.AssignedDuty.HasValue))
        {
            idle.AssignToHomestead(HomesteadDuty.Guard);
            await _companions.UpdateAsync(idle, ct);
            changes++;
        }

        // ── Step 5: House all unhoused companions ─────────────────────────────────
        companions = await _companions.GetByOwnerAsync(playerId, ct);
        var huts = buildings.Where(b => IsHousingType(b.Type) && b.IsConstructed).ToList();
        foreach (var hut in huts)
        {
            var capacity = GetHutCapacity(hut.Tier);
            var currentResidents = companions.Count(c => c.HousingBuildingId == hut.Id);
            if (currentResidents >= capacity) continue;

            var vacancies = capacity - currentResidents;
            var unhoused = companions
                .Where(c => !c.IsPermanentlyGone && !activeIds.Contains(c.Id) && c.HousingBuildingId is null)
                .Take(vacancies)
                .ToList();

            foreach (var c in unhoused)
            {
                c.AssignHousing(hut.Id);
                await _companions.UpdateAsync(c, ct);
            }
        }

        if (changes > 0)
        {
            _logger.LogInformation("AutoAssign for {PlayerId}: {Count} companion assignment(s) changed.", playerId, changes);
            await _events.PublishAsync(playerId, new CompanionStateChangedEvent(Guid.Empty, "AutoAssign"), ct);
            await _events.PublishAsync(playerId, new CityStateChangedEvent("AutoAssign"), ct);
        }

        return changes;
    }

    /// <summary>
    /// Clears any building assignment referencing the given companion.
    /// Call this whenever a companion is recalled from homestead duty so the building
    /// doesn't retain a ghost reference.
    /// </summary>
    public async Task ClearCompanionFromBuildingsAsync(Guid playerId, Guid companionId, CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null) return;

        var buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        var assigned = buildings.FirstOrDefault(b => b.HasCompanion(companionId));
        if (assigned is not null)
        {
            assigned.RemoveCompanion(companionId);
            await _buildings.UpdateAsync(assigned, ct);
            await _events.PublishAsync(playerId, new CityStateChangedEvent("ClearCompanion"), ct);
            await _events.PublishAsync(playerId, new CompanionStateChangedEvent(companionId, "ClearedFromBuilding"), ct);
        }
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
    /// Returns the first unoccupied grid cell by scanning outward from (0,0) in a spiral.
    /// </summary>
    private static (int gx, int gy) NextSpiralPosition(IReadOnlyList<HomesteadBuilding> existing)
    {
        var occupied = existing.Select(b => (b.GridX, b.GridY)).ToHashSet();
        for (int r = 0; r <= 20; r++)
        {
            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    if (Math.Abs(x) == r || Math.Abs(y) == r)
                    {
                        if (!occupied.Contains((x, y)))
                            return (x, y);
                    }
                }
            }
        }
        return (0, 0); // fallback — should never be reached for reasonable grid sizes
    }

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
