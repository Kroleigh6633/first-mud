using FirstMud.Application.Content;
using FirstMud.Application.Events;
using FirstMud.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Data-driven content (JSON files under ./content at the repo/app root).
        // Resolved relative to AppContext.BaseDirectory so it works from both
        // `dotnet run` and a published build; dev layout walks up to find repo root.
        services.AddSingleton<IContentProvider>(sp =>
        {
            var logger = sp.GetService<ILogger<ContentProvider>>();
            var root = ContentRootResolver.Resolve();
            return new ContentProvider(root, logger);
        });

        services.AddScoped<IGameEventPublisher, GameEventPublisher>();
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
