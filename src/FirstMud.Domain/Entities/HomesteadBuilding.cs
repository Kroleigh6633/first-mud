using System.Text.Json;
using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Entities;

public class HomesteadBuilding
{
    public Guid Id { get; private set; }
    public Guid HomesteadId { get; private set; }
    public BuildingType Type { get; private set; }
    public int Tier { get; private set; }           // 1–3
    public int GridX { get; private set; }          // position relative to homestead centre
    public int GridY { get; private set; }
    public bool IsConstructed { get; private set; } // false = under construction
    public int ConstructionProgress { get; private set; } // 0–100

    // Multi-worker support: JSON array of companion GUIDs assigned to this building.
    // Replaces the old single AssignedCompanionId column.
    public string AssignedCompanionIdsJson { get; private set; } = "[]";

    /// <summary>Deserialized list of all companion IDs assigned to this building.</summary>
    public IReadOnlyList<Guid> AssignedCompanionIds =>
        string.IsNullOrEmpty(AssignedCompanionIdsJson) || AssignedCompanionIdsJson == "[]"
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(AssignedCompanionIdsJson) ?? [];

    /// <summary>Backward compat: returns first assigned companion or null.</summary>
    public Guid? AssignedCompanionId =>
        AssignedCompanionIds.Count > 0 ? AssignedCompanionIds[0] : null;

    /// <summary>Number of workers currently assigned.</summary>
    public int WorkerCount => AssignedCompanionIds.Count;

    private HomesteadBuilding() { }

    public static HomesteadBuilding Create(
        Guid homesteadId,
        BuildingType type,
        int gridX,
        int gridY,
        int tier = 1,
        bool alreadyConstructed = false)
    {
        return new HomesteadBuilding
        {
            Id = Guid.NewGuid(),
            HomesteadId = homesteadId,
            Type = type,
            Tier = Math.Clamp(tier, 1, 3),
            GridX = gridX,
            GridY = gridY,
            IsConstructed = alreadyConstructed,
            ConstructionProgress = alreadyConstructed ? 100 : 0,
            AssignedCompanionIdsJson = "[]",
        };
    }

    /// <summary>
    /// Advances construction by the given percentage points (1 builder = 5 pts/tick).
    /// Returns true if this tick completed the building.
    /// </summary>
    public bool AdvanceConstruction(int progressPoints)
    {
        if (IsConstructed) return false;

        ConstructionProgress = Math.Min(100, ConstructionProgress + progressPoints);
        if (ConstructionProgress >= 100)
        {
            IsConstructed = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Assigns a companion to work at this building. Supports multiple workers.
    /// </summary>
    public void AssignCompanion(Guid companionId)
    {
        var ids = AssignedCompanionIds.ToList();
        if (!ids.Contains(companionId))
        {
            ids.Add(companionId);
            AssignedCompanionIdsJson = JsonSerializer.Serialize(ids);
        }
    }

    /// <summary>
    /// Removes a specific companion from this building.
    /// </summary>
    public void RemoveCompanion(Guid companionId)
    {
        var ids = AssignedCompanionIds.ToList();
        if (ids.Remove(companionId))
            AssignedCompanionIdsJson = JsonSerializer.Serialize(ids);
    }

    /// <summary>
    /// Removes ALL companion assignments (clears the building).
    /// </summary>
    public void UnassignCompanion()
    {
        AssignedCompanionIdsJson = "[]";
    }

    /// <summary>
    /// Returns true if the given companion is assigned to this building.
    /// </summary>
    public bool HasCompanion(Guid companionId) => AssignedCompanionIds.Contains(companionId);
}
