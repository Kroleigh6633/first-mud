using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class RecipeRepository : IRecipeRepository
{
    private readonly GameDbContext _context;

    public RecipeRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Recipe?> GetByRecipeIdAsync(string recipeId, CancellationToken ct = default)
    {
        return await _context.Recipes
            .FirstOrDefaultAsync(r => r.RecipeId == recipeId, ct);
    }

    public async Task<IReadOnlyList<Recipe>> GetByWorldAsync(WorldId worldId, CancellationToken ct = default)
    {
        return await _context.Recipes
            .Where(r => r.RequiredWorld == worldId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Recipe>> GetByCraftingSkillAsync(int maxSkill, CancellationToken ct = default)
    {
        return await _context.Recipes
            .Where(r => r.RequiredCraftingSkill <= maxSkill)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Recipe recipe, CancellationToken ct = default)
    {
        await _context.Recipes.AddAsync(recipe, ct);
        await _context.SaveChangesAsync(ct);
    }
}
