using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class CompanionConfiguration : IEntityTypeConfiguration<Companion>
{
    public void Configure(EntityTypeBuilder<Companion> builder)
    {
        builder.ToTable("Companions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.OwnerId)
            .IsRequired();

        builder.HasIndex(c => c.OwnerId);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Type)
            .HasConversion<int>();

        builder.Property(c => c.Element)
            .HasConversion<int>();

        builder.Property(c => c.CurrentLayer);
        builder.Property(c => c.UsageCounter);
        builder.Property(c => c.DriftAccumulator);
        builder.Property(c => c.IsActive);
        builder.Property(c => c.IsPermanentlyGone);
        builder.Property(c => c.EvolutionTier);
        builder.Property(c => c.Level);

        builder.Property(c => c.EvolutionBranch)
            .HasMaxLength(200);

        builder.Property(c => c.RelationshipDepth);
        builder.Property(c => c.IgnoredWarnings);

        builder.Property(c => c.AssignedDuty)
            .HasConversion<int?>()
            .IsRequired(false);

        builder.Property(c => c.DutyStartedAt)
            .IsRequired(false);
    }
}
