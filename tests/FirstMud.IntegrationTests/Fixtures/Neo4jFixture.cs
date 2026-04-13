using FirstMud.Application.Content;
using FirstMud.Infrastructure.Neo4j;
using Neo4j.Driver;
using Testcontainers.Neo4j;

namespace FirstMud.IntegrationTests.Fixtures;

/// <summary>
/// Starts a real Neo4j 5 Community container and exposes helpers to build
/// <see cref="QuestGraphRepository"/> and <see cref="LoreSeeder"/> instances.
///
/// Container lifetime matches the xUnit collection so all tests share one container.
/// </summary>
public sealed class Neo4jFixture : IAsyncLifetime
{
    private readonly Neo4jContainer _container = new Neo4jBuilder()
        .WithImage("neo4j:5-community")
        .Build();

    public string BoltUri { get; private set; } = string.Empty;
    public string Username { get; private set; } = "neo4j";
    public string Password { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connectionString = _container.GetConnectionString();
        BoltUri = connectionString;

        // Extract password from bolt URI: bolt://neo4j:<password>@host:port
        try
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo; // "neo4j:password"
            var parts = userInfo.Split(':', 2);
            Password = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "neo4j";
        }
        catch
        {
            Password = "neo4j";
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>Creates a new <see cref="Neo4jDriverWrapper"/> for the container.</summary>
    public Neo4jDriverWrapper CreateDriverWrapper() =>
        new(BoltUri, Username, Password);

    /// <summary>Creates a <see cref="QuestGraphRepository"/> backed by the container.</summary>
    public QuestGraphRepository CreateQuestGraphRepository() =>
        new(CreateDriverWrapper());

    /// <summary>Creates a <see cref="LoreSeeder"/> backed by the container.</summary>
    public LoreSeeder CreateLoreSeeder() =>
        new(CreateDriverWrapper(), new ContentProvider(ContentRootResolver.Resolve()));
}
