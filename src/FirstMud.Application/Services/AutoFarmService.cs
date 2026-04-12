using System.Collections.Concurrent;

namespace FirstMud.Application.Services;

public record AutoFarmSession(
    Guid PlayerId,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    CancellationTokenSource Cts)
{
    public int Kills { get; set; }
    public int ItemsFound { get; set; }
}

/// <summary>
/// Singleton that tracks active auto-farm sessions.
/// Actual farm logic is driven externally (CommandDispatcher / GameLoopService)
/// so that it can access scoped services (IItemRepository, etc.).
/// </summary>
public class AutoFarmService
{
    private readonly ConcurrentDictionary<Guid, AutoFarmSession> _sessions = new();

    public AutoFarmSession? GetSession(Guid playerId)
        => _sessions.TryGetValue(playerId, out var session) ? session : null;

    public bool IsActive(Guid playerId) => _sessions.ContainsKey(playerId);

    public AutoFarmSession StartSession(Guid playerId, int durationSeconds)
    {
        // Cancel any existing session first
        if (_sessions.TryGetValue(playerId, out var existing))
        {
            existing.Cts.Cancel();
            _sessions.TryRemove(playerId, out _);
        }

        var cts = new CancellationTokenSource();
        var session = new AutoFarmSession(playerId, durationSeconds, DateTimeOffset.UtcNow, cts);
        _sessions[playerId] = session;
        return session;
    }

    public void EndSession(Guid playerId)
    {
        if (_sessions.TryRemove(playerId, out var session))
            session.Cts.Cancel();
    }

    public void RecordKill(Guid playerId) =>
        _sessions.GetValueOrDefault(playerId)
            ?.Let(s => s.Kills++);

    public void RecordItem(Guid playerId) =>
        _sessions.GetValueOrDefault(playerId)
            ?.Let(s => s.ItemsFound++);
}

internal static class AutoFarmExtensions
{
    internal static T Let<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
