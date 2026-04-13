using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class HomesteadBuildingConfiguration : IEntityTypeConfiguration<HomesteadBuilding>
{
    public void Configure(EntityTypeBuilder<HomesteadBuilding> builder)
    {
        builder.ToTable("HomesteadBuildings");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .ValueGeneratedNever();

        builder.Property(b => b.HomesteadId)
            .IsRequired();

        builder.HasIndex(b => b.HomesteadId);

        builder.Property(b => b.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(b => b.Tier)
            .IsRequired();

        builder.Property(b => b.GridX)
            .IsRequired();

        builder.Property(b => b.GridY)
            .IsRequired();

        builder.Property(b => b.IsConstructed)
            .IsRequired();

        builder.Property(b => b.ConstructionProgress)
            .IsRequired();

        builder.Property(b => b.AssignedCompanionIdsJson)
            .HasColumnName("AssignedCompanionIdsJson")
            .HasDefaultValue("[]")
            .IsRequired();

        // Ignore computed properties — EF should not try to map these columns
        builder.Ignore(b => b.AssignedCompanionIds);
        builder.Ignore(b => b.AssignedCompanionId);
        builder.Ignore(b => b.WorkerCount);
    }
}
