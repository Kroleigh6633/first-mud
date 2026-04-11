using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("Zones");

        builder.HasKey(z => z.Id);

        builder.Property(z => z.Id)
            .ValueGeneratedNever();

        builder.Property(z => z.WorldId)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(z => z.ZoneId)
            .IsRequired();

        builder.Property(z => z.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(z => z.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(z => z.AsciiSymbol)
            .IsRequired()
            .HasMaxLength(1);

        builder.Property(z => z.DangerLevel)
            .IsRequired();

        builder.Property(z => z.IsPortalZone);

        builder.Property(z => z.PortalDestination)
            .HasConversion<int?>()
            .HasColumnName("PortalDestination");

        var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
            v => v.ToList());

        builder.Property(z => z.LootTableIds)
            .HasColumnName("LootTableIds")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>)System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(z => z.EncounterTableIds)
            .HasColumnName("EncounterTableIds")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>)System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(stringListComparer);

        builder.HasIndex(z => new { z.WorldId, z.ZoneId })
            .IsUnique();
    }
}
