using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Handlers;

public class SalvageCommandHandler(
    SalvageService salvageService,
    IItemRepository itemRepository,
    IPlayerRepository playerRepository,
    GameNotificationService notificationService) : ICommandHandler<SalvageCommand>
{
    public async Task<CommandResult> HandleAsync(SalvageCommand cmd, CancellationToken ct)
    {
        var result = await salvageService.SalvageAsync(cmd.PlayerId, cmd.ItemId, ct);

        var category = result.Success ? "loot" : "system";
        await notificationService.SendMessageAsync(cmd.PlayerId, category, result.Message, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        // Refresh inventory so the client sees the updated stacks
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is not null)
        {
            var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
            var payload = new
            {
                PlayerId = player.Id,
                player.Name,
                ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
                CraftingSkill = player.CraftingSkill,
                SalvageSkill = player.SalvageSkill,
                AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
                AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
                Items = items.Select(i => new
                {
                    Id = i.Id.ToString(),
                    i.Name,
                    i.Description,
                    Workmanship = i.Workmanship.Value,
                    Category = i.Category.ToString(),
                    i.Quantity,
                    i.IsStackable,
                    i.IsLocked
                }).ToList()
            };

            await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", payload, ct);
        }

        var salvagePayload = new
        {
            Yields = result.Yields.Select(y => new { y.Name, y.Quantity }).ToList(),
            Message = result.Message
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "SalvageComplete", salvagePayload, ct);

        return new CommandResult(true, result.Message, salvagePayload);
    }
}

public class SalvageAllCommandHandler(
    SalvageService salvageService,
    IItemRepository itemRepository,
    IPlayerRepository playerRepository,
    GameNotificationService notificationService) : ICommandHandler<SalvageAllCommand>
{
    public async Task<CommandResult> HandleAsync(SalvageAllCommand cmd, CancellationToken ct)
    {
        var result = await salvageService.SalvageAllAsync(cmd.PlayerId, cmd.Category, ct);

        var category = result.Success ? "loot" : "system";
        await notificationService.SendMessageAsync(cmd.PlayerId, category, result.Message, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        // Refresh inventory
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is not null)
        {
            var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
            var payload = new
            {
                PlayerId = player.Id,
                player.Name,
                ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
                CraftingSkill = player.CraftingSkill,
                SalvageSkill = player.SalvageSkill,
                AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
                AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
                Items = items.Select(i => new
                {
                    Id = i.Id.ToString(),
                    i.Name,
                    i.Description,
                    Workmanship = i.Workmanship.Value,
                    Category = i.Category.ToString(),
                    i.Quantity,
                    i.IsStackable,
                    i.IsLocked
                }).ToList()
            };

            await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", payload, ct);
        }

        var salvagePayload = new
        {
            Yields = result.Yields.Select(y => new { y.Name, y.Quantity }).ToList(),
            Message = result.Message
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "SalvageComplete", salvagePayload, ct);

        return new CommandResult(true, result.Message, salvagePayload);
    }
}

public class SetAutoSalvageCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService,
    ILogger<SetAutoSalvageCommandHandler> logger) : ICommandHandler<SetAutoSalvageCommand>
{
    public async Task<CommandResult> HandleAsync(SetAutoSalvageCommand cmd, CancellationToken ct)
    {
        var category = cmd.Category?.Trim().ToLowerInvariant();
        if (category != "weapon" && category != "armor")
        {
            await notificationService.SendMessageAsync(cmd.PlayerId, "system",
                "Invalid category. Use 'weapon' or 'armor'.", ct);
            return new CommandResult(false, "Invalid category. Use 'weapon' or 'armor'.");
        }

        var maxW = Math.Clamp(cmd.MaxWorkmanship, 0, 10);

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        player.SetAutoSalvageThreshold(category, maxW);
        await playerRepository.UpdateAsync(player, ct);

        var message = maxW == 0
            ? $"Auto-salvage for {category}s disabled."
            : $"Auto-salvage enabled: will salvage {category}s at W{maxW} or below.";

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", message, ct);

        // Refresh inventory so the client reflects the new thresholds
        var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var payload = new
        {
            PlayerId = player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill,
            AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
            AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
            Items = items.Select(i => new
            {
                Id = i.Id.ToString(),
                i.Name,
                i.Description,
                Workmanship = i.Workmanship.Value,
                Category = i.Category.ToString(),
                i.Quantity,
                i.IsStackable,
                i.IsLocked
            }).ToList()
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", payload, ct);
        logger.LogInformation("Player {PlayerId} set auto-salvage {Category} threshold to W{Max}",
            cmd.PlayerId, category, maxW);

        return new CommandResult(true, message, new { category, maxWorkmanship = maxW });
    }
}
