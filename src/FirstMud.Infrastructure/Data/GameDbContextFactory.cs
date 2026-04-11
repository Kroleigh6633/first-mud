using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FirstMud.Infrastructure.Data;

/// <summary>
/// Design-time factory used by EF Core tools (migrations) when no startup project is configured.
/// </summary>
internal sealed class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=FirstMud;User Id=sa;Password=REDACTED_DEV_SA_PASSWORD;TrustServerCertificate=True;",
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(GameDbContext).Assembly.FullName))
            .Options;

        return new GameDbContext(options);
    }
}
