using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class InteractCommandHandler(IPlayerRepository playerRepository) : ICommandHandler<InteractCommand>
{
    public async Task<CommandResult> HandleAsync(InteractCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var payload = new
        {
            player.Id,
            player.Name,
            player.Level,
            player.CurrentHp,
            player.MaxHp,
            Position = new { player.Position.X, player.Position.Y, player.Position.ZoneId },
            ObjectId = cmd.ObjectId
        };

        return new CommandResult(true, "Interaction noted.", payload);
    }
}
