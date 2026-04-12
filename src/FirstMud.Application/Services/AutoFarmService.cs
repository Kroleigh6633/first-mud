using System.Collections.Concurrent;

namespace FirstMud.Application.Services;

/// <summary>Outcome of a single auto-farm encounter, used to adapt the safe danger cap.</summary>
public enum FarmEncounterOutcome { Victory, Fled, Defeat }

public record AutoFarmSession(
    Guid PlayerId,
    DateTimeOffset StartedAt,
    CancellationTokenSource Cts,
    int? TargetX = null,
    int? TargetY = null,
    int MaxDanger = 10,
    string Priority = "balanced")
{
    public int Kills { get; set; }
    public int ItemsFound { get; set; }
    public int ItemsAutoSalvaged { get; set; }
    public int ItemsDeposited { get; set; }

    /// <summary>Current loop state: idle | walking | fighting | resting | depositing</summary>
    public string FarmState { get; set; } = "idle";

    // ---- Adaptive danger cap ----

    /// <summary>
    /// Sliding window of the last 5 encounter outcomes.
    /// Used to raise or lower the safe danger cap dynamically.
    /// </summary>
    public Queue<FarmEncounterOutcome> RecentOutcomes { get; } = new();

    /// <summary>
    /// Manual override for the safe danger cap set after a defeat.
    /// null = use the default formula (playerLevel + 2 ± streak bonus).
    /// </summary>
    public int? SafeDangerCapOverride { get; set; }

    /// <summary>
    /// How many extra danger levels above the baseline (playerLevel + 2) the
    /// player has earned through winning streaks.  Capped at +2.
    /// </summary>
    public int StreakBonus { get; set; }

    /// <summary>Coordinates the player has visited during this session.</summary>
    public HashSet<(int X, int Y)> VisitedTiles { get; } = new();
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

    public AutoFarmSession StartSession(
        Guid playerId,
        int? targetX = null,
        int? targetY = null,
        int maxDanger = 10,
        string priority = "balanced")
    {
        // Cancel any existing session first
        if (_sessions.TryGetValue(playerId, out var existing))
        {
            existing.Cts.Cancel();
            _sessions.TryRemove(playerId, out _);
        }

        var cts = new CancellationTokenSource();
        var session = new AutoFarmSession(playerId, DateTimeOffset.UtcNow, cts, targetX, targetY, maxDanger, priority);
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

    /// <summary>
    /// Records a combat outcome and recalculates the adaptive danger state.
    /// Window = last 5 encounters.
    /// </summary>
    public void RecordEncounterOutcome(Guid playerId, FarmEncounterOutcome outcome, int currentDanger)
    {
        if (!_sessions.TryGetValue(playerId, out var session)) return;

        // Maintain a sliding window of size 5
        session.RecentOutcomes.Enqueue(outcome);
        while (session.RecentOutcomes.Count > 5)
            session.RecentOutcomes.Dequeue();

        // Defeat → immediately lower cap to currentDanger - 1 (retreat to easier areas)
        if (outcome == FarmEncounterOutcome.Defeat)
        {
            session.SafeDangerCapOverride = Math.Max(0, currentDanger - 1);
            session.StreakBonus = 0;
            return;
        }

        // Clear the defeat override once we're accumulating outcomes again
        var recent = session.RecentOutcomes.ToList();

        // 2+ fled in last 5 → reduce streak bonus by 1 (more cautious)
        int fleCount = recent.Count(o => o == FarmEncounterOutcome.Fled);
        if (fleCount >= 2)
        {
            session.StreakBonus = Math.Max(session.StreakBonus - 1, -1);
            session.SafeDangerCapOverride = null;
            return;
        }

        // 5 victories, 0 defeats → allow +1 danger level (braver)
        int victoryCount = recent.Count(o => o == FarmEncounterOutcome.Victory);
        int defeatCount  = recent.Count(o => o == FarmEncounterOutcome.Defeat);
        if (victoryCount == 5 && defeatCount == 0)
        {
            session.StreakBonus = Math.Min(session.StreakBonus + 1, 2);
        }

        session.SafeDangerCapOverride = null;
    }

    /// <summary>
    /// Returns the current safe danger cap for the player.
    /// Default: playerLevel + 2 + StreakBonus, capped at 10.
    /// After a defeat this is overridden to a lower value until the next recalculation.
    /// </summary>
    public int GetSafeDangerCap(Guid playerId, int playerLevel)
    {
        if (!_sessions.TryGetValue(playerId, out var session)) return playerLevel + 2;

        if (session.SafeDangerCapOverride.HasValue)
            return session.SafeDangerCapOverride.Value;

        // Player-specified MaxDanger acts as a hard upper bound
        int computed = Math.Clamp(playerLevel + 2 + session.StreakBonus, 0, 10);
        return Math.Min(computed, session.MaxDanger);
    }
}

internal static class AutoFarmExtensions
{
    internal static T Let<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
