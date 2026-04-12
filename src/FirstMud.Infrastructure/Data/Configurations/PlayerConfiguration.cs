using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(p => p.Name)
            .IsUnique();

        builder.Property(p => p.Level);
        builder.Property(p => p.Experience);

        builder.Property(p => p.PrimaryElement)
            .HasConversion<int>();

        builder.Property(p => p.Polarity)
            .HasConversion<int>();

        builder.Property(p => p.ElementRevealed);
        builder.Property(p => p.PolarityRevealed);

        builder.Property(p => p.WyrdTangle);
        builder.Property(p => p.HasWyrdThread);

        builder.Property(p => p.Strength);
        builder.Property(p => p.Agility);
        builder.Property(p => p.Intellect);
        builder.Property(p => p.Fortitude);
        builder.Property(p => p.Speed);
        builder.Property(p => p.CraftingSkill);
        builder.Property(p => p.SalvageSkill);
        builder.Property(p => p.CraftingSeed);

        builder.Property(p => p.CurrentHp);
        builder.Property(p => p.MaxHp);
        builder.Property(p => p.ActionPoints);
        builder.Property(p => p.MaxActionPoints);

        // Weave owned type — stored as two int columns
        builder.OwnsOne(p => p.Weave, weave =>
        {
            weave.Property(w => w.Current).HasColumnName("CurrentWeave");
            weave.Property(w => w.Maximum).HasColumnName("MaxWeave");
        });

        // Position owned type — stored as 4 columns
        builder.OwnsOne(p => p.Position, pos =>
        {
            pos.Property(p => p.World)
                .HasColumnName("WorldId")
                .HasConversion<int>();
            pos.Property(p => p.ZoneId).HasColumnName("ZoneId");
            pos.Property(p => p.X).HasColumnName("PosX");
            pos.Property(p => p.Y).HasColumnName("PosY");
        });

        // ActiveCompanionIds stored as JSON
        builder.Property(p => p.ActiveCompanionIds)
            .HasColumnName("ActiveCompanionIds")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => (IReadOnlyList<Guid>)System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(v, (System.Text.Json.JsonSerializerOptions?)null)!)
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<Guid>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
                v => v.ToList()));

        // UnlockedPortals stored as JSON
        builder.Property(p => p.UnlockedPortals)
            .HasColumnName("UnlockedPortals")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v.Select(x => (int)x).ToList(), (System.Text.Json.JsonSerializerOptions?)null),
                v => (IReadOnlyCollection<WorldId>)System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null)!
                         .Select(x => (WorldId)x).ToHashSet())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<WorldId>>(
                (a, b) => a != null && b != null && a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x)),
                v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
                v => v.ToHashSet()));

        builder.Property(p => p.MaxInventorySlots)
            .HasColumnName("MaxInventorySlots")
            .HasDefaultValue(20);

        // SavedReturnPosition owned type — nullable, stored as 4 nullable columns
        builder.OwnsOne(p => p.SavedReturnPosition, pos =>
        {
            pos.Property(p => p.World)
                .HasColumnName("SavedReturnWorldId")
                .HasConversion<int>();
            pos.Property(p => p.ZoneId).HasColumnName("SavedReturnZoneId");
            pos.Property(p => p.X).HasColumnName("SavedReturnPosX");
            pos.Property(p => p.Y).HasColumnName("SavedReturnPosY");
        });

        // Shadow property for last seen timestamp
        builder.Property<DateTime>("LastSeenAt")
            .HasDefaultValueSql("GETUTCDATE()");

        // Navigation to reputations via private backing field
        builder.HasMany(p => p.Reputations)
            .WithOne()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Reputations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_reputations");
    }
}
