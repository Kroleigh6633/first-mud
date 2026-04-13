namespace FirstMud.Engine.Commands;

/// <summary>
/// Marker interface for any command processed by the engine's command dispatcher.
/// The engine is domain-agnostic; concrete command types live in game code.
/// </summary>
public interface IGameCommand
{
    Guid PlayerId { get; }
}
