using FirstMud.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FirstMud.Infrastructure.Data.Configurations;

internal sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("Recipes");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.RecipeId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(r => r.RecipeId)
            .IsUnique();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ResultCategory)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.ResultItemName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.BaseWorkmanshipMin);
        builder.Property(r => r.BaseWorkmanshipMax);

        builder.Property(r => r.RequiredTaperType)
            .HasConversion<int?>()
            .HasColumnName("RequiredTaperType");

        builder.Property(r => r.RequiredWorld)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.RequiredCraftingSkill);
        builder.Property(r => r.IsDiscoverable);

        // Ingredients stored as JSON column
        builder.Property(r => r.Ingredients)
            .HasColumnName("IngredientsJson")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(
                    v.Select(i => new RecipeIngredientDto
                    {
                        Category = (int)i.Category,
                        IngredientName = i.IngredientName,
                        BaseQuantity = i.BaseQuantity
                    }).ToList(),
                    (System.Text.Json.JsonSerializerOptions?)null),
                v => (IReadOnlyList<RecipeIngredient>)System.Text.Json.JsonSerializer.Deserialize<List<RecipeIngredientDto>>(
                    v, (System.Text.Json.JsonSerializerOptions?)null)!
                    .Select(dto => RecipeIngredient.Create(
                        (FirstMud.Domain.Entities.ItemCategory)dto.Category,
                        dto.IngredientName,
                        dto.BaseQuantity))
                    .ToList())
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<RecipeIngredient>>(
                (a, b) => a != null && b != null && a.Count == b.Count && a.Zip(b).All(p => p.First.IngredientName == p.Second.IngredientName),
                v => v.Aggregate(0, (h, e) => HashCode.Combine(h, e.IngredientName.GetHashCode())),
                v => v.ToList()));
    }

    private sealed class RecipeIngredientDto
    {
        public int Category { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public int BaseQuantity { get; set; }
    }
}
