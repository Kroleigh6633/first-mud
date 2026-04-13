using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class PracticeEnchantingCommandHandler(
    ImbueService imbueService,
    SalvageService salvageService,
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService) : ICommandHandler<PracticeEnchantingCommand>
{
    public async Task<CommandResult> HandleAsync(PracticeEnchantingCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var allItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);

        // Find imbue-able items: weapons/armor, not locked, not equipped, has available imbue slots
        var imbueTargets = allItems
            .Where(i => (i.Category == ItemCategory.Weapon || i.Category == ItemCategory.Armor)
                        && !i.IsLocked
                        && !player.IsItemEquipped(i.Id)
                        && i.Imbues.Count < i.MaxImbueSlots)
            .ToList();

        // Find tapers/reagents
        var tapers = allItems
            .Where(i => i.Category == ItemCategory.Reagent)
            .ToList();

        if (imbueTargets.Count == 0)
        {
            var msg = "Practice enchanting: no imbue-able items found (need unequipped, unlocked weapons/armor with open imbue slots).";
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", msg, ct);
            return new CommandResult(false, msg);
        }

        if (tapers.Count == 0)
        {
            var msg = "Practice enchanting: no tapers/reagents found in inventory.";
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", msg, ct);
            return new CommandResult(false, msg);
        }

        var imbueAttempts = 0;
        var imbueSuccesses = 0;
        var imbueFails = 0;
        var imbueCatastrophic = 0;
        var salvageCount = 0;
        var materialsRecovered = new List<string>();

        foreach (var target in imbueTargets)
        {
            // Re-fetch available tapers each iteration (they get consumed)
            var currentTapers = await GetAvailableTapersAsync(cmd.PlayerId, ct);
            if (currentTapers.Count == 0)
                break;

            var taper = currentTapers[0];
            imbueAttempts++;

            var result = await imbueService.ImbueAsync(cmd.PlayerId, target.Id, taper.Id, ct);

            switch (result.Outcome)
            {
                case ImbueOutcome.Success:
                case ImbueOutcome.CriticalSuccess:
                    imbueSuccesses++;
                    break;
                case ImbueOutcome.CatastrophicFailure:
                    imbueCatastrophic++;
                    break;
                default:
                    imbueFails++;
                    break;
            }

            // After imbue, check if item is now fully imbued — auto-salvage it
            var updatedItem = await itemRepository.GetByIdAsync(target.Id, ct);
            if (updatedItem is not null && updatedItem.Imbues.Count >= updatedItem.MaxImbueSlots)
            {
                var salvageResult = await salvageService.SalvageAsync(cmd.PlayerId, target.Id, ct);
                if (salvageResult.Success)
                {
                    salvageCount++;
                    foreach (var y in salvageResult.Yields)
                        materialsRecovered.Add($"{y.Name} x{y.Quantity}");
                }
            }
        }

        var materialsMsg = materialsRecovered.Count > 0
            ? $", recovered: {string.Join(", ", materialsRecovered)}"
            : "";

        var summaryMsg = $"Practice enchanting: imbued {imbueAttempts} item(s) " +
                         $"({imbueSuccesses} success, {imbueFails} fail, {imbueCatastrophic} catastrophic), " +
                         $"salvaged {salvageCount} item(s){materialsMsg}";

        await notificationService.SendMessageAsync(cmd.PlayerId, "loot", summaryMsg, ct);

        // Refresh inventory
        var refreshedPlayer = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (refreshedPlayer is not null)
        {
            var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
            var inventoryPayload = new
            {
                PlayerId = refreshedPlayer.Id,
                refreshedPlayer.Name,
                ActiveCompanionIds = refreshedPlayer.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
                CraftingSkill = refreshedPlayer.CraftingSkill,
                SalvageSkill = refreshedPlayer.SalvageSkill,
                AutoSalvageWeaponThreshold = refreshedPlayer.AutoSalvageWeaponThreshold,
                AutoSalvageArmorThreshold = refreshedPlayer.AutoSalvageArmorThreshold,
                EquippedItems = refreshedPlayer.EquippedItems.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => kv.Value.ToString()),
                Items = items.Select(i => new
                {
                    Id = i.Id.ToString(),
                    i.Name,
                    i.Description,
                    Workmanship = i.Workmanship.Value,
                    Category = i.Category.ToString(),
                    Slot = i.Slot.ToString(),
                    i.Quantity,
                    i.IsStackable,
                    i.IsLocked,
                    i.IsUnstable,
                    MaxImbueSlots = i.MaxImbueSlots,
                    Imbues = i.Imbues.Select(imbue => new
                    {
                        Type = imbue.Type.ToString(),
                        imbue.Power
                    }).ToList()
                }).ToList()
            };

            await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", inventoryPayload, ct);
        }

        return new CommandResult(true, summaryMsg);
    }

    private async Task<List<Item>> GetAvailableTapersAsync(Guid playerId, CancellationToken ct)
    {
        var allItems = await itemRepository.GetByOwnerAsync(playerId, ct);
        return allItems
            .Where(i => i.Category == ItemCategory.Reagent)
            .ToList();
    }
}
