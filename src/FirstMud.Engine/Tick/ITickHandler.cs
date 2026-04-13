namespace FirstMud.Engine.Tick;

/// <summary>
/// A unit of periodic work scheduled by the engine's game loop. The engine does
/// not know or care what a handler does — only how often it should run.
///
/// Implementations are registered in DI and discovered by <c>GameLoopService</c>.
/// </summary>
public interface ITickHandler
{
    /// <summary>Human-readable name for logging.</summary>
    string Name { get; }

    /// <summary>How often this handler should fire. Must be &gt; 0.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// If true, the handler runs on the very first tick. If false, it waits a full
    /// <see cref="Interval"/> before first firing.
    /// </summary>
    bool RunOnFirstTick => false;

    Task HandleTickAsync(CancellationToken ct);
}
