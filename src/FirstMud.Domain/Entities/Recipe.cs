using FirstMud.Domain.Enums;
using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Entities;

public class RecipeIngredient
{
    public ItemCategory Category { get; private init; }
    public string IngredientName { get; private init; } = string.Empty;
    public int BaseQuantity { get; private init; }

    private RecipeIngredient() { }

    public static RecipeIngredient Create(ItemCategory category, string ingredientName, int baseQuantity) => new()
    {
        Category = category,
        IngredientName = ingredientName,
        BaseQuantity = baseQuantity
    };
}

public class Recipe
{
    public Guid Id { get; private set; }
    public string RecipeId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    private readonly List<RecipeIngredient> _ingredients = [];
    public IReadOnlyList<RecipeIngredient> Ingredients => _ingredients.AsReadOnly();

    public ItemCategory ResultCategory { get; private set; }
    public string ResultItemName { get; private set; } = string.Empty;
    public int BaseWorkmanshipMin { get; private set; }
    public int BaseWorkmanshipMax { get; private set; }
    public TaperType? RequiredTaperType { get; private set; }
    public WorldId RequiredWorld { get; private set; }
    public int RequiredCraftingSkill { get; private set; }
    public bool IsDiscoverable { get; private set; }

    private Recipe() { }

    public static Recipe Create(
        string recipeId,
        string name,
        IEnumerable<RecipeIngredient> ingredients,
        ItemCategory resultCategory,
        string resultItemName,
        int baseWorkmanshipMin,
        int baseWorkmanshipMax,
        TaperType? requiredTaperType,
        WorldId requiredWorld,
        int requiredCraftingSkill,
        bool isDiscoverable)
    {
        if (baseWorkmanshipMin < 1 || baseWorkmanshipMin > 10)
            throw new ArgumentOutOfRangeException(nameof(baseWorkmanshipMin), "BaseWorkmanshipMin must be between 1 and 10.");
        if (baseWorkmanshipMax < 1 || baseWorkmanshipMax > 10)
            throw new ArgumentOutOfRangeException(nameof(baseWorkmanshipMax), "BaseWorkmanshipMax must be between 1 and 10.");
        if (baseWorkmanshipMin > baseWorkmanshipMax)
            throw new ArgumentException("BaseWorkmanshipMin must not exceed BaseWorkmanshipMax.");

        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            Name = name,
            ResultCategory = resultCategory,
            ResultItemName = resultItemName,
            BaseWorkmanshipMin = baseWorkmanshipMin,
            BaseWorkmanshipMax = baseWorkmanshipMax,
            RequiredTaperType = requiredTaperType,
            RequiredWorld = requiredWorld,
            RequiredCraftingSkill = requiredCraftingSkill,
            IsDiscoverable = isDiscoverable
        };

        recipe._ingredients.AddRange(ingredients);
        return recipe;
    }
}
