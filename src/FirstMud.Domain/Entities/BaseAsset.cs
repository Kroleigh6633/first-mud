using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Entities;

public class BaseAsset
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public BaseAssetType AssetType { get; private set; }
    public int Tier { get; private set; }
    public bool IsOperational { get; private set; }
    public DateTimeOffset? LastUpkeepAt { get; private set; }
    public DateTimeOffset? NextUpkeepDue { get; private set; }
    public int UpkeepCostAmount { get; private set; }
    public string UpkeepCostItemName { get; private set; } = string.Empty;

    public bool IsOverdue => NextUpkeepDue.HasValue && DateTimeOffset.UtcNow > NextUpkeepDue;

    private BaseAsset() { }

    public static BaseAsset Create(
        Guid ownerId,
        BaseAssetType assetType,
        int upkeepCostAmount,
        string upkeepCostItemName)
    {
        return new BaseAsset
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            AssetType = assetType,
            Tier = 1,
            IsOperational = true,
            LastUpkeepAt = DateTimeOffset.UtcNow,
            NextUpkeepDue = DateTimeOffset.UtcNow.AddDays(7),
            UpkeepCostAmount = upkeepCostAmount,
            UpkeepCostItemName = upkeepCostItemName
        };
    }

    public void PerformUpkeep()
    {
        LastUpkeepAt = DateTimeOffset.UtcNow;
        NextUpkeepDue = DateTimeOffset.UtcNow.AddDays(7);
        IsOperational = true;
    }

    public void SetOperational(bool operational)
    {
        IsOperational = operational;
    }

    public void UpgradeTier()
    {
        if (Tier >= 3)
            throw new InvalidOperationException("BaseAsset is already at maximum tier (3).");
        Tier++;
    }
}
