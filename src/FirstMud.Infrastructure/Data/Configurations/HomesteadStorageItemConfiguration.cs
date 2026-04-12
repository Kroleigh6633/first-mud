using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class HomesteadStorageItemConfiguration : IEntityTypeConfiguration<HomesteadStorageItem>
{
    public void Configure(EntityTypeBuilder<HomesteadStorageItem> builder)
    {
        builder.ToTable("HomesteadStorageItems");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.HomesteadId)
            .IsRequired();

        builder.Property(s => s.ItemId)
            .IsRequired();

        builder.HasIndex(s => s.HomesteadId);
        builder.HasIndex(s => s.ItemId).IsUnique();
    }
}
