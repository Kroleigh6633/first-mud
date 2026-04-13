using System.Text.Json;
using FirstMud.Engine.Commands;

namespace FirstMud.GameServer.Hubs;

/// <summary>
/// Registry-based command dispatcher. Builds a lookup of <see cref="ICommandParser"/>
/// instances keyed by <see cref="ICommandParser.CommandName"/> (case-insensitive).
/// Adding a new command is: add a parser class + register it in Program.cs. No
/// central switch to edit.
/// </summary>
public sealed class GameServerCommandFactory
{
    private readonly Dictionary<string, ICommandParser> _parsers;

    public GameServerCommandFactory(IEnumerable<ICommandParser> parsers)
    {
        _parsers = parsers.ToDictionary(p => p.CommandName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to parse an incoming SignalR command into a typed <see cref="IGameCommand"/>.
    /// Returns <c>null</c> for unknown command names or when a parser rejects the payload —
    /// the hub treats both cases as "Unknown command" (matching original behaviour).
    /// </summary>
    public IGameCommand? TryParse(string command, Guid playerId, object? payload)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        if (!_parsers.TryGetValue(command, out var parser)) return null;

        // SignalR deserialises anonymous/object payloads as JsonElement. Missing or
        // non-JsonElement payloads are handled by giving each parser an "undefined"
        // element so their tolerant helpers produce defaulted fields (same as before).
        var json = payload is JsonElement el ? el : default;
        return parser.Parse(json, playerId);
    }
}
