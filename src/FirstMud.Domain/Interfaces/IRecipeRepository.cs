using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Interfaces;

public interface IRecipeRepository
{
    Task<Recipe?> GetByRecipeIdAsync(string recipeId, CancellationToken ct = default);
    Task<IReadOnlyList<Recipe>> GetByWorldAsync(WorldId worldId, CancellationToken ct = default);
    Task<IReadOnlyList<Recipe>> GetByCraftingSkillAsync(int maxSkill, CancellationToken ct = default);
    Task AddAsync(Recipe recipe, CancellationToken ct = default);
}
