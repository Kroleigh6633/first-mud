using System.Text.Json;
using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class HomesteadConfiguration : IEntityTypeConfiguration<Homestead>
{
    public void Configure(EntityTypeBuilder<Homestead> builder)
    {
        builder.ToTable("Homesteads");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .ValueGeneratedNever();

        builder.Property(h => h.PlayerId)
            .IsRequired();

        builder.HasIndex(h => h.PlayerId)
            .IsUnique();

        builder.Property(h => h.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(h => h.StorageSlots);

        builder.Property(h => h.SalvageQueue)
            .HasColumnName("SalvageQueueJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>())
            .HasDefaultValue(new List<Guid>())
            .IsRequired(false);
    }
}
