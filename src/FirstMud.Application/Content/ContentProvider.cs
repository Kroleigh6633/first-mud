using System.Text.Json;
using System.Text.Json.Serialization;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Content;

/// <summary>
/// JSON-backed implementation of <see cref="IContentProvider"/>.
///
/// Loads and validates content files at construction time. Validation is
/// strict-enough-to-catch-typos: required fields, enum values, and Buff
/// effects must have a buffKey. Anything invalid throws on startup so
/// misconfigured content cannot reach production silently.
/// </summary>
public sealed class ContentProvider : IContentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> ValidEffectTypes =
        new(StringComparer.Ordinal) { "Heal", "RestoreWeave", "Buff" };

    private static readonly HashSet<string> ValidBuffKeys =
        new(StringComparer.Ordinal) { "MaxHpBonus", "SpeedBonus", "StrikeDamageBonus" };

    private static readonly HashSet<string> ValidDutyNames =
        new(Enum.GetNames<HomesteadDuty>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidItemCategoryNames =
        new(Enum.GetNames<ItemCategory>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidWorldIdNames =
        new(Enum.GetNames<WorldId>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidTaperTypeNames =
        new(Enum.GetNames<TaperType>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidBiomes =
        new(StringComparer.Ordinal)
        { "mountain", "forest", "desert", "water", "swamp", "plains", "wyrd" };

    private readonly string _contentRoot;
    private readonly ILogger<ContentProvider>? _logger;

    private IReadOnlyList<ConsumableDefinition> _consumables = Array.Empty<ConsumableDefinition>();
    private IReadOnlyList<BuildingDefinition> _buildings = Array.Empty<BuildingDefinition>();
    private Dictionary<BuildingType, BuildingDefinition> _buildingsByType = new();
    private IReadOnlyList<RecipeDefinition> _recipes = Array.Empty<RecipeDefinition>();
    private Dictionary<string, RecipeDefinition> _recipesById = new(StringComparer.Ordinal);
    private IReadOnlyList<MonsterDefinition> _monsters = Array.Empty<MonsterDefinition>();
    private Dictionary<string, MonsterDefinition> _monstersById = new(StringComparer.Ordinal);

    public ContentProvider(string contentRoot, ILogger<ContentProvider>? logger = null)
    {
        _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<ConsumableDefinition> Consumables => _consumables;

    public IReadOnlyList<BuildingDefinition> AllBuildings() => _buildings;

    public BuildingDefinition? GetBuilding(BuildingType type) =>
        _buildingsByType.TryGetValue(type, out var def) ? def : null;

    public int GetHutCapacity(BuildingType type, int tier)
    {
        if (!_buildingsByType.TryGetValue(type, out var def)) return 0;
        if (!def.IsHousing || def.HutCapacityByTier is null || def.HutCapacityByTier.Count == 0)
            return 0;

        // Clamp tier into the defined range (preserves the legacy switch's fallback
        // behaviour where unknown tiers returned the tier-1 value).
        var idx = tier - 1;
        if (idx < 0 || idx >= def.HutCapacityByTier.Count)
            idx = 0;
        return def.HutCapacityByTier[idx];
    }

    public ConsumableDefinition? ResolveConsumable(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        var lower = itemName.ToLowerInvariant();
        foreach (var def in _consumables)
        {
            if (lower.Contains(def.MatchToken))
                return def;
        }
        return null;
    }

    public IReadOnlyList<ConsumableDefinition> ConsumablesByGroup(string priorityGroup)
    {
        return _consumables
            .Where(c => string.Equals(c.PriorityGroup, priorityGroup, StringComparison.Ordinal))
            .OrderBy(c => c.PriorityRank)
            .ToList();
    }

    public IReadOnlyList<RecipeDefinition> AllRecipes() => _recipes;

    public RecipeDefinition? GetRecipe(string recipeId) =>
        !string.IsNullOrEmpty(recipeId) && _recipesById.TryGetValue(recipeId, out var def) ? def : null;

    public IReadOnlyList<MonsterDefinition> AllMonsters() => _monsters;

    public MonsterDefinition? GetMonster(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _monstersById.TryGetValue(id, out var def) ? def : null;
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiome(string biome)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiomeAndTier(string biome, int tier)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            m.Tier == tier &&
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
    }

    public void Reload()
    {
        _consumables = LoadConsumables();
        _buildings = LoadBuildings();
        _buildingsByType = _buildings.ToDictionary(b => b.Type);
        _recipes = LoadRecipes();
        _recipesById = _recipes.ToDictionary(r => r.RecipeId, StringComparer.Ordinal);
        _monsters = LoadMonsters();
        _monstersById = _monsters.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables, {BuildingCount} buildings, {RecipeCount} recipes, {MonsterCount} monsters from {Root}",
            _consumables.Count, _buildings.Count, _recipes.Count, _monsters.Count, _contentRoot);
    }

    private IReadOnlyList<RecipeDefinition> LoadRecipes()
    {
        var path = Path.Combine(_contentRoot, "recipes.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<RecipesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Recipes is null || doc.Recipes.Count == 0)
            throw new InvalidDataException($"{path}: no recipes defined.");

        var list = new List<RecipeDefinition>(doc.Recipes.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Recipes)
        {
            if (string.IsNullOrWhiteSpace(raw.RecipeId))
                throw new InvalidDataException($"{path}: recipe missing recipeId.");
            if (!seenIds.Add(raw.RecipeId))
                throw new InvalidDataException($"{path}: duplicate recipeId '{raw.RecipeId}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: recipe '{raw.RecipeId}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.ResultItemName))
                throw new InvalidDataException($"{path}: recipe '{raw.RecipeId}' missing resultItemName.");

            if (string.IsNullOrWhiteSpace(raw.ResultCategory) || !ValidItemCategoryNames.Contains(raw.ResultCategory))
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has invalid resultCategory '{raw.ResultCategory}'.");
            var resultCategory = Enum.Parse<ItemCategory>(raw.ResultCategory);

            if (string.IsNullOrWhiteSpace(raw.RequiredWorld) || !ValidWorldIdNames.Contains(raw.RequiredWorld))
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has invalid requiredWorld '{raw.RequiredWorld}'.");
            var requiredWorld = Enum.Parse<WorldId>(raw.RequiredWorld);

            TaperType? requiredTaperType = null;
            if (!string.IsNullOrWhiteSpace(raw.RequiredTaperType))
            {
                if (!ValidTaperTypeNames.Contains(raw.RequiredTaperType))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' has invalid requiredTaperType '{raw.RequiredTaperType}'.");
                requiredTaperType = Enum.Parse<TaperType>(raw.RequiredTaperType);
            }

            if (raw.BaseWorkmanshipMin < 1 || raw.BaseWorkmanshipMin > 10)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMin must be between 1 and 10.");
            if (raw.BaseWorkmanshipMax < 1 || raw.BaseWorkmanshipMax > 10)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMax must be between 1 and 10.");
            if (raw.BaseWorkmanshipMin > raw.BaseWorkmanshipMax)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMin must not exceed baseWorkmanshipMax.");

            if (raw.RequiredCraftingSkill < 0)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' requiredCraftingSkill must be >= 0.");

            if (raw.Ingredients is null || raw.Ingredients.Count == 0)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has no ingredients.");

            var ingredients = new List<RecipeIngredientDefinition>(raw.Ingredients.Count);
            foreach (var ing in raw.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ing.Name))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' has ingredient with empty name.");
                if (string.IsNullOrWhiteSpace(ing.Category) || !ValidItemCategoryNames.Contains(ing.Category))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' ingredient '{ing.Name}' has invalid category '{ing.Category}'.");
                if (ing.BaseQuantity <= 0)
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' ingredient '{ing.Name}' baseQuantity must be > 0.");
                ingredients.Add(new RecipeIngredientDefinition(
                    Enum.Parse<ItemCategory>(ing.Category), ing.Name, ing.BaseQuantity));
            }

            list.Add(new RecipeDefinition(
                RecipeId: raw.RecipeId,
                Name: raw.Name,
                ResultItemName: raw.ResultItemName,
                ResultCategory: resultCategory,
                RequiredCraftingSkill: raw.RequiredCraftingSkill,
                RequiredWorld: requiredWorld,
                RequiredTaperType: requiredTaperType,
                BaseWorkmanshipMin: raw.BaseWorkmanshipMin,
                BaseWorkmanshipMax: raw.BaseWorkmanshipMax,
                IsDiscoverable: raw.IsDiscoverable,
                Ingredients: ingredients));
        }

        return list;
    }

    private IReadOnlyList<BuildingDefinition> LoadBuildings()
    {
        var path = Path.Combine(_contentRoot, "buildings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<BuildingsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Buildings is null || doc.Buildings.Count == 0)
            throw new InvalidDataException($"{path}: no buildings defined.");

        var list = new List<BuildingDefinition>(doc.Buildings.Count);
        var seenTypes = new HashSet<BuildingType>();

        foreach (var raw in doc.Buildings)
        {
            if (string.IsNullOrWhiteSpace(raw.Type))
                throw new InvalidDataException($"{path}: building missing type.");

            if (!Enum.TryParse<BuildingType>(raw.Type, ignoreCase: false, out var type))
                throw new InvalidDataException(
                    $"{path}: unknown building type '{raw.Type}'.");

            if (!seenTypes.Add(type))
                throw new InvalidDataException($"{path}: duplicate building type '{raw.Type}'.");

            if (raw.ConstructionCost is null || raw.ConstructionCost.Count == 0)
                throw new InvalidDataException(
                    $"{path}: building '{raw.Type}' has no constructionCost entries.");

            var costs = new List<BuildingCost>(raw.ConstructionCost.Count);
            foreach (var c in raw.ConstructionCost)
            {
                if (string.IsNullOrWhiteSpace(c.Material))
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' has a cost entry with no material.");
                if (c.Quantity <= 0)
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' cost '{c.Material}' must have quantity > 0.");
                costs.Add(new BuildingCost(c.Material, c.Quantity));
            }

            HomesteadDuty? duty = null;
            if (!string.IsNullOrWhiteSpace(raw.Duty))
            {
                if (!ValidDutyNames.Contains(raw.Duty))
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' has invalid duty '{raw.Duty}'. " +
                        $"Must be one of: {string.Join(", ", ValidDutyNames)}.");
                duty = Enum.Parse<HomesteadDuty>(raw.Duty);
            }

            if (raw.WorkerCapacity < 0)
                throw new InvalidDataException(
                    $"{path}: building '{raw.Type}' workerCapacity must be >= 0.");

            IReadOnlyList<int>? hutCapacity = null;
            if (raw.IsHousing)
            {
                if (raw.HutCapacityByTier is null || raw.HutCapacityByTier.Count == 0)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' requires a non-empty hutCapacityByTier array.");
                foreach (var cap in raw.HutCapacityByTier)
                {
                    if (cap <= 0)
                        throw new InvalidDataException(
                            $"{path}: housing building '{raw.Type}' has invalid hutCapacityByTier entry {cap} (must be > 0).");
                }
                hutCapacity = raw.HutCapacityByTier.ToList();

                if (duty is not null)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' must not specify a duty.");
                if (raw.WorkerCapacity != 0)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' must have workerCapacity = 0.");
            }
            else
            {
                if (duty is null)
                    throw new InvalidDataException(
                        $"{path}: production building '{raw.Type}' must specify a duty.");
                if (raw.HutCapacityByTier is not null)
                    throw new InvalidDataException(
                        $"{path}: non-housing building '{raw.Type}' must not specify hutCapacityByTier.");
            }

            list.Add(new BuildingDefinition(
                Type: type,
                ConstructionCost: costs,
                Duty: duty,
                WorkerCapacity: raw.WorkerCapacity,
                IsHousing: raw.IsHousing,
                HutCapacityByTier: hutCapacity));
        }

        // Every BuildingType enum value must be present — this is the whole
        // point of startup validation: misconfigured content cannot reach
        // production silently.
        foreach (var enumValue in Enum.GetValues<BuildingType>())
        {
            if (!seenTypes.Contains(enumValue))
                throw new InvalidDataException(
                    $"{path}: missing definition for BuildingType.{enumValue}.");
        }

        return list;
    }

    private IReadOnlyList<ConsumableDefinition> LoadConsumables()
    {
        var path = Path.Combine(_contentRoot, "consumables.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ConsumablesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Consumables is null || doc.Consumables.Count == 0)
            throw new InvalidDataException($"{path}: no consumables defined.");

        var list = new List<ConsumableDefinition>(doc.Consumables.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Consumables)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: consumable missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate consumable id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.MatchToken))
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' missing matchToken.");
            if (raw.MatchToken != raw.MatchToken.ToLowerInvariant())
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' matchToken must be lowercase.");
            if (string.IsNullOrWhiteSpace(raw.EffectType) || !ValidEffectTypes.Contains(raw.EffectType))
                throw new InvalidDataException(
                    $"{path}: consumable '{raw.Id}' has invalid effectType '{raw.EffectType}'. " +
                    $"Must be one of: {string.Join(", ", ValidEffectTypes)}.");

            if (raw.EffectType == "Buff")
            {
                if (string.IsNullOrWhiteSpace(raw.BuffKey) || !ValidBuffKeys.Contains(raw.BuffKey))
                    throw new InvalidDataException(
                        $"{path}: Buff consumable '{raw.Id}' has invalid buffKey '{raw.BuffKey}'.");
            }

            list.Add(new ConsumableDefinition(
                Id: raw.Id,
                MatchToken: raw.MatchToken,
                EffectType: raw.EffectType,
                Amount: raw.Amount,
                BuffKey: raw.BuffKey,
                BuffValue: raw.BuffValue,
                PriorityGroup: raw.PriorityGroup,
                PriorityRank: raw.PriorityRank));
        }

        return list;
    }

    // ─── JSON DTOs (private; external callers use ConsumableDefinition) ─────

    private sealed class ConsumablesFile
    {
        [JsonPropertyName("consumables")]
        public List<RawConsumable>? Consumables { get; set; }
    }

    private sealed class RawConsumable
    {
        public string Id { get; set; } = "";
        public string MatchToken { get; set; } = "";
        public string EffectType { get; set; } = "";
        public int Amount { get; set; }
        public string? BuffKey { get; set; }
        public float BuffValue { get; set; }
        public string? PriorityGroup { get; set; }
        public int PriorityRank { get; set; }
    }

    private sealed class BuildingsFile
    {
        [JsonPropertyName("buildings")]
        public List<RawBuilding>? Buildings { get; set; }
    }

    private sealed class RawBuilding
    {
        public string Type { get; set; } = "";
        public List<RawBuildingCost>? ConstructionCost { get; set; }
        public string? Duty { get; set; }
        public int WorkerCapacity { get; set; }
        public bool IsHousing { get; set; }
        public List<int>? HutCapacityByTier { get; set; }
    }

    private sealed class RawBuildingCost
    {
        public string Material { get; set; } = "";
        public int Quantity { get; set; }
    }

    private sealed class RecipesFile
    {
        [JsonPropertyName("recipes")]
        public List<RawRecipe>? Recipes { get; set; }
    }

    private sealed class RawRecipe
    {
        public string RecipeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ResultItemName { get; set; } = "";
        public string ResultCategory { get; set; } = "";
        public int RequiredCraftingSkill { get; set; }
        public string RequiredWorld { get; set; } = "";
        public string? RequiredTaperType { get; set; }
        public int BaseWorkmanshipMin { get; set; }
        public int BaseWorkmanshipMax { get; set; }
        public bool IsDiscoverable { get; set; }
        public List<RawRecipeIngredient>? Ingredients { get; set; }
    }

    private sealed class RawRecipeIngredient
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public int BaseQuantity { get; set; }
    }
    // ─── Monsters ────────────────────────────────────────────────────────────

    private IReadOnlyList<MonsterDefinition> LoadMonsters()
    {
        var path = Path.Combine(_contentRoot, "monsters.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<MonstersFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Abilities is null || doc.Abilities.Count == 0)
            throw new InvalidDataException($"{path}: no abilities defined.");
        if (doc.Monsters is null || doc.Monsters.Count == 0)
            throw new InvalidDataException($"{path}: no monsters defined.");

        // Build ability table first — monsters reference abilities by id.
        var abilityTable = new Dictionary<string, CombatAbility>(StringComparer.Ordinal);
        foreach (var raw in doc.Abilities)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: ability missing id.");
            if (abilityTable.ContainsKey(raw.Id))
                throw new InvalidDataException($"{path}: duplicate ability id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: ability '{raw.Id}' missing name.");
            if (raw.BasePower < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative basePower.");
            if (raw.WeaveCost < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative weaveCost.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid element '{raw.Element}'.");
            if (!Enum.TryParse<AbilityTargetType>(raw.TargetType, ignoreCase: false, out var target))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid targetType '{raw.TargetType}'.");
            if (!Enum.TryParse<AbilityCategory>(raw.Category, ignoreCase: false, out var category))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid category '{raw.Category}'.");

            abilityTable[raw.Id] = new CombatAbility(
                raw.Name, raw.BasePower, raw.WeaveCost,
                element, target, category,
                raw.LifestealPower, raw.WyrdProcChance);
        }

        var list = new List<MonsterDefinition>(doc.Monsters.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Monsters)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: monster missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate monster id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: monster '{raw.Id}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.Biome) || !ValidBiomes.Contains(raw.Biome))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid biome '{raw.Biome}'. " +
                    $"Must be one of: {string.Join(", ", ValidBiomes)}.");
            if (raw.Tier < 0 || raw.Tier > 3)
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has tier {raw.Tier} outside range 0-3.");
            if (raw.Hp < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative hp.");
            if (raw.Speed < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative speed.");
            if (raw.Level < 1)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has level < 1.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid element '{raw.Element}'.");
            if (raw.Abilities is null || raw.Abilities.Count == 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has no abilities.");

            var abilities = new List<CombatAbility>(raw.Abilities.Count);
            foreach (var abilityId in raw.Abilities)
            {
                if (!abilityTable.TryGetValue(abilityId, out var ability))
                    throw new InvalidDataException(
                        $"{path}: monster '{raw.Id}' references unknown ability '{abilityId}'.");
                abilities.Add(ability);
            }

            list.Add(new MonsterDefinition(
                Id: raw.Id,
                Name: raw.Name,
                Biome: raw.Biome,
                Tier: raw.Tier,
                Hp: raw.Hp,
                Speed: raw.Speed,
                Level: raw.Level,
                Element: element,
                Abilities: abilities));
        }

        return list;
    }

    private sealed class MonstersFile
    {
        [JsonPropertyName("abilities")]
        public List<RawAbility>? Abilities { get; set; }

        [JsonPropertyName("monsters")]
        public List<RawMonster>? Monsters { get; set; }
    }

    private sealed class RawAbility
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int BasePower { get; set; }
        public int WeaveCost { get; set; }
        public string Element { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string Category { get; set; } = "";
        public float LifestealPower { get; set; }
        public float WyrdProcChance { get; set; }
    }

    private sealed class RawMonster
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Biome { get; set; } = "";
        public int Tier { get; set; }
        public int Hp { get; set; }
        public int Speed { get; set; }
        public int Level { get; set; }
        public string Element { get; set; } = "";
        public List<string>? Abilities { get; set; }
    }
}
