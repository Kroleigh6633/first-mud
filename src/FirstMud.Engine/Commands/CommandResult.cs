namespace FirstMud.Engine.Commands;

public record CommandResult(bool Success, string Message, object? Payload = null);
