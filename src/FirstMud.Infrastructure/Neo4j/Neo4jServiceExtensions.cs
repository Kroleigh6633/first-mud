using FirstMud.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FirstMud.Infrastructure.Neo4j;

/// <summary>
/// IServiceCollection extension that wires Neo4j infrastructure into the DI container.
///
/// Reads from configuration section "Neo4j" with keys:
///   Uri      — bolt connection URI  (e.g. "bolt://localhost:7687")
///   Username — database username    (e.g. "neo4j")
///   Password — database password
/// </summary>
public static class Neo4jServiceExtensions
{
    public static IServiceCollection AddNeo4j(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Neo4j");

        var uri      = section["Uri"]      ?? throw new InvalidOperationException("Neo4j:Uri is required.");
        var username = section["Username"] ?? throw new InvalidOperationException("Neo4j:Username is required.");
        var password = section["Password"] ?? throw new InvalidOperationException("Neo4j:Password is required.");

        // Singleton driver wrapper — one connection pool for the lifetime of the application.
        services.AddSingleton(_ => new Neo4jDriverWrapper(uri, username, password));

        // Scoped repository — one per request/unit-of-work.
        services.AddScoped<IQuestGraphRepository, QuestGraphRepository>();

        // Transient seeder — created fresh each time it is requested (typically once on startup).
        services.AddTransient<LoreSeeder>();

        return services;
    }
}
