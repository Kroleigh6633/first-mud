using System.Collections.Concurrent;

namespace FirstMud.Application.Services;

public record AutoFarmSession(
    Guid PlayerId,
    DateTimeOffset StartedAt,
    CancellationTokenSource Cts)
{
    public int Kills { get; set; }
    public int ItemsFound { get; set; }
    public int ItemsAutoSalvaged { get; set; }
    public int ItemsDeposited { get; set; }

    /// <summary>Current loop state: idle | walking | fighting | resting | depositing</summary>
    public string FarmState { get; set; } = "idle";
}

/// <summary>
/// Singleton that tracks active auto-farm sessions.
/// Actual farm logic is driven externally (AutoFarmCommandHandler)
/// so that it can access scoped services (IItemRepository, etc.).
/// </summary>
public class AutoFarmService
{
    private readonly ConcurrentDictionary<Guid, AutoFarmSession> _sessions = new();

    public AutoFarmSession? GetSession(Guid playerId)
        => _sessions.TryGetValue(playerId, out var session) ? session : null;

    public bool IsActive(Guid playerId) => _sessions.ContainsKey(playerId);

    public AutoFarmSession StartSession(Guid playerId)
    {
        // Cancel any existing session first
        if (_sessions.TryGetValue(playerId, out var existing))
        {
            existing.Cts.Cancel();
            _sessions.TryRemove(playerId, out _);
        }

        var cts = new CancellationTokenSource();
        var session = new AutoFarmSession(playerId, DateTimeOffset.UtcNow, cts);
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

    public void RecordAutoSalvage(Guid playerId) =>
        _sessions.GetValueOrDefault(playerId)
            ?.Let(s => s.ItemsAutoSalvaged++);

    public void RecordDeposit(Guid playerId, int count) =>
        _sessions.GetValueOrDefault(playerId)
            ?.Let(s => s.ItemsDeposited += count);

    public void SetState(Guid playerId, string state) =>
        _sessions.GetValueOrDefault(playerId)
            ?.Let(s => s.FarmState = state);
}

internal static class AutoFarmExtensions
{
    internal static T Let<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
