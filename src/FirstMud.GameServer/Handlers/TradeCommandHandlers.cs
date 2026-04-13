using FirstMud.Application.Services;
using FirstMud.Engine.Commands;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public sealed class ViewVendorCommandHandler(
    TradeService tradeService,
    GameNotificationService notificationService) : ICommandHandler<ViewVendorCommand>
{
    public async Task<CommandResult> HandleAsync(ViewVendorCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.NpcId))
            return new CommandResult(false, "Vendor npcId required.");

        var snapshot = await tradeService.GetVendorInventoryAsync(cmd.NpcId, ct);
        if (snapshot is null)
            return new CommandResult(false, $"No vendor '{cmd.NpcId}' exists.");

        var payload = new
        {
            snapshot.NpcId,
            snapshot.DisplayName,
            snapshot.ZoneNumber,
            World = snapshot.World.ToString(),
            BuysCategories = snapshot.BuysCategories.Select(c => c.ToString()).ToArray(),
            Stock = snapshot.Stock.Select(s => new
            {
                s.ItemName,
                Category = s.Category.ToString(),
                s.Quantity,
                s.Workmanship,
                s.BuyPrice,
                s.SellPrice,
            }).ToArray(),
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "VendorInventory", payload, ct);
        return new CommandResult(true, $"Viewing {snapshot.DisplayName}.", payload);
    }
}

public sealed class BuyItemCommandHandler(
    TradeService tradeService,
    IHubContext<GameHub> hubContext) : ICommandHandler<BuyItemCommand>
{
    public async Task<CommandResult> HandleAsync(BuyItemCommand cmd, CancellationToken ct)
    {
        var result = await tradeService.BuyItemAsync(cmd.PlayerId, cmd.NpcId, cmd.ItemName, cmd.Quantity, ct);
        await SendGameMessage(hubContext, cmd.PlayerId, "trade", result.Message, ct);
        return new CommandResult(result.Success, result.Message, result);
    }

    internal static Task SendGameMessage(IHubContext<GameHub> ctx, Guid playerId, string category, string text, CancellationToken ct) =>
        ctx.Clients.Group(playerId.ToString()).SendAsync("GameMessage", new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            category,
            text,
        }, ct);
}

public sealed class SellItemCommandHandler(
    TradeService tradeService,
    IHubContext<GameHub> hubContext) : ICommandHandler<SellItemCommand>
{
    public async Task<CommandResult> HandleAsync(SellItemCommand cmd, CancellationToken ct)
    {
        var result = await tradeService.SellItemAsync(cmd.PlayerId, cmd.NpcId, cmd.ItemName, cmd.Quantity, ct);
        await BuyItemCommandHandler.SendGameMessage(hubContext, cmd.PlayerId, "trade", result.Message, ct);
        return new CommandResult(result.Success, result.Message, result);
    }
}
