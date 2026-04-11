using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class PlayerFactionReputationConfiguration : IEntityTypeConfiguration<PlayerFactionReputation>
{
    public void Configure(EntityTypeBuilder<PlayerFactionReputation> builder)
    {
        builder.ToTable("PlayerFactionReputations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.PlayerId)
            .IsRequired();

        builder.Property(r => r.FactionId)
            .HasConversion<int>();

        builder.HasIndex(r => new { r.PlayerId, r.FactionId })
            .IsUnique();

        // ReputationScore owned type — stored as int column Points
        builder.OwnsOne(r => r.Score, score =>
        {
            score.Property(s => s.Points).HasColumnName("Points");
        });

        builder.Property(r => r.HasPermanentFloor);

        builder.Property(r => r.PermanentFloor)
            .HasConversion<int?>()
            .HasColumnName("PermanentFloor");
    }
}
