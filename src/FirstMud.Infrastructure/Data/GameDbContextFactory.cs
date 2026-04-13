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
        // Design-time only. Developers export FIRSTMUD_DESIGNTIME_CONNECTION locally;
        // see docs/dev-setup.md. Never commit a real connection string here.
        var connectionString = Environment.GetEnvironmentVariable("FIRSTMUD_DESIGNTIME_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set FIRSTMUD_DESIGNTIME_CONNECTION before running EF Core design-time tools. "
                + "See docs/dev-setup.md.");

        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(GameDbContext).Assembly.FullName))
            .Options;

        return new GameDbContext(options);
    }
}
