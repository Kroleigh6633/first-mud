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
    }
}
