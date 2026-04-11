using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

public class AutomationService
{
    private readonly IBaseAssetRepository _assets;

    public AutomationService(IBaseAssetRepository assets)
    {
        _assets = assets;
    }

    public async Task TickUpkeepAsync(Guid ownerId, CancellationToken ct = default)
    {
        var assets = await _assets.GetByOwnerAsync(ownerId, ct);
        var overdue = assets.Where(a => a.IsOverdue).ToList();
        foreach (var asset in overdue)
            asset.SetOperational(false);

        if (overdue.Count > 0)
            await _assets.UpdateManyAsync(overdue, ct);
    }

    public async Task<bool> PerformUpkeepAsync(Guid assetId, Guid ownerId, CancellationToken ct = default)
    {
        var assets = await _assets.GetByOwnerAsync(ownerId, ct);
        var asset = assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return false;

        asset.PerformUpkeep();
        await _assets.UpdateAsync(asset, ct);
        return true;
    }

    public async Task<BaseAsset> InstallAssetAsync(
        Guid ownerId,
        BaseAssetType assetType,
        CancellationToken ct = default)
    {
        var (upkeepCost, upkeepItem) = GetDefaultUpkeep(assetType);
        var asset = BaseAsset.Create(ownerId, assetType, upkeepCost, upkeepItem);
        await _assets.AddAsync(asset, ct);
        return asset;
    }

    private static (int cost, string item) GetDefaultUpkeep(BaseAssetType assetType) => assetType switch
    {
        BaseAssetType.SortingEngine  => (5,  "Iron Cog"),
        BaseAssetType.HerbTender     => (3,  "Thornroot Extract"),
        BaseAssetType.ScribeWard     => (4,  "Inscribed Vellum"),
        BaseAssetType.TradeGolem     => (8,  "Golem Oil"),
        BaseAssetType.GuardWard      => (6,  "Ward Crystal"),
        BaseAssetType.TaperRefinery  => (10, "Raw Taper Filament"),
        _                            => (5,  "Maintenance Parts")
    };
}
