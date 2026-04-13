using System.Text.Json;

namespace FirstMud.Engine.Commands;

/// <summary>
/// Parses a single named command from a JSON payload into an <see cref="IGameCommand"/>.
/// One implementation per command name. Adding a new command is: add a parser class +
/// a DI registration. No central switch to edit.
/// </summary>
public interface ICommandParser
{
    /// <summary>
    /// Command name as sent by the client (case-insensitive dispatch).
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Build the concrete command. Returning <c>null</c> signals "payload did not
    /// match the shape this command needs" — the dispatcher treats that the same
    /// as an unknown command name (the hub reports "Unknown command" to the caller).
    /// </summary>
    IGameCommand? Parse(JsonElement payload, Guid playerId);
}
