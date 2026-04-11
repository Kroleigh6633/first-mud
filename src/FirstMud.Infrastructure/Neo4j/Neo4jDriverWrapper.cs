using Neo4j.Driver;

namespace FirstMud.Infrastructure.Neo4j;

/// <summary>
/// Thread-safe singleton wrapper around the Neo4j IDriver.
/// Provides async session factory methods and executes read/write transactions.
/// </summary>
public sealed class Neo4jDriverWrapper : IDisposable
{
    private readonly IDriver _driver;
    private bool _disposed;

    public Neo4jDriverWrapper(string uri, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
    }

    /// <summary>Opens a new async session. Caller is responsible for disposing.</summary>
    public IAsyncSession OpenSession() => _driver.AsyncSession();

    /// <summary>Executes a read transaction and returns its result.</summary>
    public async Task<T> ExecuteReadAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(work);
    }

    /// <summary>Executes a write transaction.</summary>
    public async Task ExecuteWriteAsync(Func<IAsyncQueryRunner, Task> work, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(work);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.Dispose();
    }
}
