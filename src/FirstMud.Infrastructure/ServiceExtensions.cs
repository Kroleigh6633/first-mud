using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using FirstMud.Infrastructure.Repositories.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FirstMud.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<GameDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("GameDb"),
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(GameDbContext).Assembly.FullName)));

        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<ICompanionRepository, CompanionRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<IBaseAssetRepository, BaseAssetRepository>();
        services.AddScoped<IHomesteadRepository, HomesteadRepository>();
        services.AddScoped<IResourceNodeRepository, ResourceNodeRepository>();

        return services;
    }
}
