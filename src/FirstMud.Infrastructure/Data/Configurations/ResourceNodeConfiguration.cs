using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class ResourceNodeConfiguration : IEntityTypeConfiguration<ResourceNode>
{
    public void Configure(EntityTypeBuilder<ResourceNode> builder)
    {
        builder.ToTable("ResourceNodes");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.ZoneId)
            .IsRequired();

        builder.Property(r => r.ResourceType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.RemainingYield);
        builder.Property(r => r.MaxYield);
        builder.Property(r => r.RegenerationRate);

        builder.HasIndex(r => r.ZoneId);
    }
}
