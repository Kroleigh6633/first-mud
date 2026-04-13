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
    public Guid? AssignedCompanionId { get; private set; }

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
            AssignedCompanionId = null,
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
    /// Assigns a companion to work at this building (crafter, guard, etc.).
    /// </summary>
    public void AssignCompanion(Guid companionId)
    {
        AssignedCompanionId = companionId;
    }

    /// <summary>
    /// Removes the companion assignment (companion recalled or reassigned).
    /// </summary>
    public void UnassignCompanion()
    {
        AssignedCompanionId = null;
    }
}
