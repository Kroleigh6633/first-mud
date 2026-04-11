using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class AutomationServiceTests
{
    private static BaseAsset CreateOverdueAsset(Guid ownerId)
    {
        var asset = BaseAsset.Create(ownerId, BaseAssetType.SortingEngine, 5, "Iron Cog");
        // Force overdue by backdating NextUpkeepDue via PerformUpkeep, then manipulate
        // Since there's no direct API to make it overdue, we set it up and rely on the
        // fact that IsOverdue checks DateTimeOffset.UtcNow > NextUpkeepDue.
        // We can't set the date, so we'll test with non-overdue assets in upkeep tests.
        return asset;
    }

    // ---- TickUpkeepAsync ----

    [Fact]
    public async Task TickUpkeepAsync_NoAssets_IsNoOp()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        assetRepo.GetByOwnerAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new List<BaseAsset>().AsReadOnly());

        var svc = new AutomationService(assetRepo);

        await svc.TickUpkeepAsync(ownerId);

        // No overdue assets → UpdateManyAsync should not be called
        await assetRepo.DidNotReceive().UpdateManyAsync(Arg.Any<IEnumerable<BaseAsset>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TickUpkeepAsync_AssetsNotOverdue_DoesNotSetOperationalFalse()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        // Freshly created assets have NextUpkeepDue = UtcNow + 7 days — not overdue
        var asset = BaseAsset.Create(ownerId, BaseAssetType.HerbTender, 3, "Thornroot Extract");
        assetRepo.GetByOwnerAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new List<BaseAsset> { asset }.AsReadOnly());

        var svc = new AutomationService(assetRepo);

        await svc.TickUpkeepAsync(ownerId);

        asset.IsOperational.Should().BeTrue();
        await assetRepo.DidNotReceive().UpdateManyAsync(Arg.Any<IEnumerable<BaseAsset>>(), Arg.Any<CancellationToken>());
    }

    // ---- PerformUpkeepAsync ----

    [Fact]
    public async Task PerformUpkeepAsync_AssetFound_UpdatesViaRepo_AndReturnsTrue()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        var asset = BaseAsset.Create(ownerId, BaseAssetType.ScribeWard, 4, "Inscribed Vellum");
        assetRepo.GetByOwnerAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new List<BaseAsset> { asset }.AsReadOnly());

        var svc = new AutomationService(assetRepo);

        var result = await svc.PerformUpkeepAsync(asset.Id, ownerId);

        result.Should().BeTrue();
        asset.IsOperational.Should().BeTrue();
        await assetRepo.Received(1).UpdateAsync(asset, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerformUpkeepAsync_AssetNotFound_ReturnsFalse()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        assetRepo.GetByOwnerAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new List<BaseAsset>().AsReadOnly());

        var svc = new AutomationService(assetRepo);

        var result = await svc.PerformUpkeepAsync(Guid.NewGuid(), ownerId);

        result.Should().BeFalse();
        await assetRepo.DidNotReceive().UpdateAsync(Arg.Any<BaseAsset>(), Arg.Any<CancellationToken>());
    }

    // ---- InstallAssetAsync ----

    [Fact]
    public async Task InstallAssetAsync_PersistsAssetViaRepo_AndReturnsCreatedAsset()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        var svc = new AutomationService(assetRepo);

        var asset = await svc.InstallAssetAsync(ownerId, BaseAssetType.TradeGolem);

        asset.Should().NotBeNull();
        asset.OwnerId.Should().Be(ownerId);
        asset.AssetType.Should().Be(BaseAssetType.TradeGolem);
        asset.IsOperational.Should().BeTrue();
        await assetRepo.Received(1).AddAsync(asset, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAssetAsync_AssignsCorrectUpkeepDefaults_ForGuardWard()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        var svc = new AutomationService(assetRepo);

        var asset = await svc.InstallAssetAsync(ownerId, BaseAssetType.GuardWard);

        asset.UpkeepCostAmount.Should().Be(6);
        asset.UpkeepCostItemName.Should().Be("Ward Crystal");
    }

    [Fact]
    public async Task InstallAssetAsync_AssignsCorrectUpkeepDefaults_ForTaperRefinery()
    {
        var assetRepo = Substitute.For<IBaseAssetRepository>();
        var ownerId = Guid.NewGuid();

        var svc = new AutomationService(assetRepo);

        var asset = await svc.InstallAssetAsync(ownerId, BaseAssetType.TaperRefinery);

        asset.UpkeepCostAmount.Should().Be(10);
        asset.UpkeepCostItemName.Should().Be("Raw Taper Filament");
    }
}
