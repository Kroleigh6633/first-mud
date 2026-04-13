using FirstMud.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FirstMud.Application;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<CraftingService>();
        services.AddScoped<CompanionService>();
        services.AddScoped<ReputationService>();
        services.AddScoped<QuestService>();
        services.AddScoped<AutomationService>();
        services.AddSingleton<CombatService>();
        services.AddScoped<LootService>();
        services.AddScoped<SalvageService>();
        services.AddScoped<ImbueService>();
        services.AddSingleton<AutoFarmService>();
        services.AddScoped<HomesteadCompanionService>();
        services.AddScoped<SmeltService>();
        services.AddScoped<BuildingService>();
        return services;
    }
}
