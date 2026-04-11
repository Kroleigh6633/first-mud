using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class BaseAssetConfiguration : IEntityTypeConfiguration<BaseAsset>
{
    public void Configure(EntityTypeBuilder<BaseAsset> builder)
    {
        builder.ToTable("BaseAssets");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.OwnerId)
            .IsRequired();

        builder.Property(a => a.AssetType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.Tier);
        builder.Property(a => a.IsOperational);
        builder.Property(a => a.LastUpkeepAt);
        builder.Property(a => a.NextUpkeepDue);
        builder.Property(a => a.UpkeepCostAmount);

        builder.Property(a => a.UpkeepCostItemName)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(a => new { a.OwnerId, a.AssetType });
    }
}
