using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(i => i.Category)
            .HasConversion<int>();

        // Workmanship owned type — stored as single int column
        builder.OwnsOne(i => i.Workmanship, w =>
        {
            w.Property(x => x.Value).HasColumnName("WorkmanshipValue");
        });

        builder.Property(i => i.MagicalElement)
            .HasColumnName("MagicalElement")
            .HasConversion<int?>();

        builder.Property(i => i.MagicalPolarity)
            .HasColumnName("MagicalPolarity")
            .HasConversion<int?>();

        builder.Property(i => i.AppliedTaper)
            .HasColumnName("AppliedTaper")
            .HasConversion<int?>();

        builder.Property(i => i.TaperQuality)
            .HasColumnName("TaperQuality")
            .HasConversion<int?>();

        builder.Property(i => i.IsWyrdTouched);
        builder.Property(i => i.IsArdweldOrigin);
        builder.Property(i => i.Durability);
        builder.Property(i => i.MaxDurability);
        builder.Property(i => i.IsSalvageable);

        builder.Property(i => i.OriginWorld)
            .HasConversion<int>();

        builder.Property(i => i.DiscoveredByPlayerName)
            .HasMaxLength(100);

        // OwnerId — nullable, null means world loot
        builder.Property<Guid?>("OwnerId")
            .HasColumnName("OwnerId");

        builder.HasIndex("OwnerId");
    }
}
