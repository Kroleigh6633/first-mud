using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// A single construction cost line: a named material + the quantity required.
/// Material names match the canonical resource names used in HomesteadStorageItems.
/// </summary>
public sealed record BuildingCost(string Material, int Quantity);

/// <summary>
/// Data-driven building definition, loaded from content/buildings.json.
///
/// Replaces the hardcoded <c>ConstructionCosts</c>, <c>BuildingDuty</c>,
/// <c>WorkerCapacity</c>, and <c>GetHutCapacity</c> dictionaries that used
/// to live at the top of <see cref="Services.BuildingService"/>.
///
/// Housing buildings (currently only Hut) have <see cref="IsHousing"/> = true,
/// null <see cref="Duty"/>, <see cref="WorkerCapacity"/> = 0, and a non-null
/// <see cref="HutCapacityByTier"/> (index 0 = tier 1, etc.). Production
/// buildings have <see cref="IsHousing"/> = false, a non-null <see cref="Duty"/>,
/// a positive <see cref="WorkerCapacity"/>, and null <see cref="HutCapacityByTier"/>.
/// </summary>
public sealed record BuildingDefinition(
    BuildingType Type,
    IReadOnlyList<BuildingCost> ConstructionCost,
    HomesteadDuty? Duty,
    int WorkerCapacity,
    bool IsHousing,
    IReadOnlyList<int>? HutCapacityByTier);
