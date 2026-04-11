using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace FirstMud.IntegrationTests.Fixtures;

/// <summary>
/// Starts a real SQL Server 2025 container, applies EF migrations, and exposes
/// a factory method for creating <see cref="GameDbContext"/> instances.
///
/// Container lifetime matches the xUnit collection so all tests in the collection
/// share the same container (expensive startup happens once).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
        .WithPassword("IntegrationTest_1!")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Apply migrations so the schema is fully current
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>Creates a fresh <see cref="GameDbContext"/> against the container.</summary>
    public GameDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlServer(
                ConnectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(GameDbContext).Assembly.FullName))
            .Options;

        return new GameDbContext(options);
    }
}
