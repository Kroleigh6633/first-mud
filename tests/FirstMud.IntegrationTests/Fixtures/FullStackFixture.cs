namespace FirstMud.IntegrationTests.Fixtures;

/// <summary>
/// Combines SQL Server and Neo4j fixtures for tests that need both databases.
/// Both containers start concurrently for faster total startup.
/// </summary>
public sealed class FullStackFixture : IAsyncLifetime
{
    public SqlServerFixture SqlServer { get; } = new();
    public Neo4jFixture Neo4j { get; } = new();

    public async Task InitializeAsync()
    {
        // Start both containers concurrently — they are independent.
        await Task.WhenAll(
            SqlServer.InitializeAsync(),
            Neo4j.InitializeAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            SqlServer.DisposeAsync(),
            Neo4j.DisposeAsync());
    }
}
