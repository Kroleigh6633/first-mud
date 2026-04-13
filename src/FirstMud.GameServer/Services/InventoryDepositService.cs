using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Shared auto-deposit service: portal home, deposit all non-equipped non-locked items
/// to homestead storage (auto-salvaging to make room when storage is full), then
/// portal back to the return position.
///
/// Callable from any context — auto-farm loop, quest auto-run, or manual play —
/// by injecting this singleton.
/// </summary>
public class InventoryDepositService(
    IHubContext<GameHub> hubContext,
    AutoFarmService autoFarmService,
    IServiceScopeFactory scopeFactory,
    ILogger<InventoryDepositService> logger)
{
    /// <summary>
    /// Portal the player home, deposit all eligible items, then move back to
    /// <paramref name="returnPos"/>.
    ///
    /// <paramref name="session"/> may be null when called outside of auto-farm
    /// (e.g. quest auto-run or manual play).  Stats are still recorded to
    /// <see cref="AutoFarmService"/> so the session panel reflects them if a
    /// farm session is active; when null the session-level counters are simply
    /// not updated.
    /// </summary>
    public async Task AutoDepositAndReturnAsync(
        Guid playerId,
        Position returnPos,
        AutoFarmSession? session,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var playerRepo    = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var itemRepo      = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var homesteadRepo = scope.ServiceProvider.GetRequiredService<IHomesteadRepository>();

        var p = await playerRepo.GetByIdAsync(playerId, ct);
        if (p is null) return;

        var homePos = new Position(p.Position.World, 0, -100, -100);
        p.PortalHome(homePos);
        await playerRepo.UpdateAsync(p, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("PlayerMoved", new
            {
                p.Id,
                X = homePos.X,
                Y = homePos.Y,
                ZoneId = homePos.ZoneId,
                World = homePos.World.ToString()
            }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("AtHomestead", new { playerId }, ct);

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "system",
                text      = "Inventory full — portalling home to deposit..."
            }, ct);

        var equippedIds = new HashSet<Guid>(p.EquippedItems.Values);
        var allItems    = await itemRepo.GetByOwnerAsync(playerId, ct);
        var homestead   = await homesteadRepo.GetByPlayerIdAsync(playerId, ct);

        int deposited = 0;
        if (homestead is not null)
        {
            var storageItems = await homesteadRepo.GetStorageItemsAsync(homestead.Id, ct);
            int freeSlots    = homestead.StorageSlots - storageItems.Count;

            // ---- STORAGE FULL: auto-salvage to make room before depositing ----
            if (freeSlots <= 0)
            {
                var salvagePlayer = await playerRepo.GetByIdAsync(playerId, ct);

                if (salvagePlayer is not null)
                {
                    await using var salvageScope = scopeFactory.CreateAsyncScope();
                    var salvageSvc = salvageScope.ServiceProvider.GetRequiredService<SalvageService>();

                    // Step 1: salvage all non-locked, non-equipped weapons/armor below auto-salvage threshold
                    var candidates = allItems
                        .Where(i => !i.IsLocked && !equippedIds.Contains(i.Id))
                        .Where(i => i.Category == ItemCategory.Weapon || i.Category == ItemCategory.Armor)
                        .Where(i =>
                        {
                            var threshold = i.Category == ItemCategory.Weapon
                                ? salvagePlayer.AutoSalvageWeaponThreshold
                                : salvagePlayer.AutoSalvageArmorThreshold;
                            return threshold > 0 && i.Workmanship.Value <= threshold;
                        })
                        .ToList();

                    int autoSalvaged = 0;
                    foreach (var item in candidates)
                    {
                        var result = await salvageSvc.SalvageAsync(playerId, item.Id, ct);
                        if (result.Success)
                        {
                            autoSalvaged++;
                            autoFarmService.RecordAutoSalvage(playerId);
                            if (session is not null)
                                session.ItemsAutoSalvaged = autoFarmService.GetSession(playerId)?.ItemsAutoSalvaged ?? session.ItemsAutoSalvaged;
                        }
                    }

                    if (autoSalvaged > 0)
                    {
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category  = "system",
                                text      = $"Storage full — auto-salvaged {autoSalvaged} item(s) below threshold to free space."
                            }, ct);
                    }

                    // Step 2: if still no room, salvage the lowest-W non-locked gear
                    var freshStorageItems = await homesteadRepo.GetStorageItemsAsync(homestead.Id, ct);
                    freeSlots = homestead.StorageSlots - freshStorageItems.Count;

                    if (freeSlots <= 0)
                    {
                        var freshItems  = await itemRepo.GetByOwnerAsync(playerId, ct);
                        var lowestGear  = freshItems
                            .Where(i => !i.IsLocked && !equippedIds.Contains(i.Id))
                            .Where(i => i.Category == ItemCategory.Weapon || i.Category == ItemCategory.Armor)
                            .OrderBy(i => i.Workmanship.Value)
                            .FirstOrDefault();

                        if (lowestGear is not null)
                        {
                            var result = await salvageSvc.SalvageAsync(playerId, lowestGear.Id, ct);
                            if (result.Success)
                            {
                                autoFarmService.RecordAutoSalvage(playerId);
                                if (session is not null)
                                    session.ItemsAutoSalvaged = autoFarmService.GetSession(playerId)?.ItemsAutoSalvaged ?? session.ItemsAutoSalvaged;

                                await hubContext.Clients
                                    .Group(playerId.ToString())
                                    .SendAsync("GameMessage", new
                                    {
                                        timestamp = DateTime.UtcNow.ToString("O"),
                                        category  = "system",
                                        text      = $"Storage full — salvaged lowest-grade gear ({lowestGear.DisplayName} W{lowestGear.Workmanship.Value}) to free a slot."
                                    }, ct);
                            }
                        }
                    }

                    // Recompute free slots after salvaging
                    var postSalvageStorageItems = await homesteadRepo.GetStorageItemsAsync(homestead.Id, ct);
                    freeSlots = homestead.StorageSlots - postSalvageStorageItems.Count;
                }
            }
            // ---- END STORAGE FULL HANDLING ----

            // Refresh item list after any salvaging
            allItems = await itemRepo.GetByOwnerAsync(playerId, ct);

            foreach (var item in allItems)
            {
                if (freeSlots <= 0) break;
                if (equippedIds.Contains(item.Id)) continue;
                if (item.IsLocked) continue;

                // Materials stack into existing storage stacks; no slot cost for merges
                if (item.IsStackable)
                {
                    var currentStorage  = await homesteadRepo.GetStorageItemsAsync(homestead.Id, ct);
                    var storageItemIds  = currentStorage.Select(s => s.ItemId).ToList();
                    var storageEntities = await itemRepo.GetByIdsAsync(storageItemIds, ct);
                    var existingStack   = storageEntities.FirstOrDefault(s =>
                        s.Name == item.Name && s.Category == item.Category && s.IsStackable);

                    if (existingStack is not null)
                    {
                        existingStack.AddQuantity(item.Quantity);
                        await itemRepo.UpdateAsync(existingStack, ct);
                        await itemRepo.DeleteAsync(item.Id, ct);
                        deposited++;
                        // No freeSlots decrement — stacking doesn't consume a slot
                        continue;
                    }
                }

                item.SetOwner(null);
                await itemRepo.UpdateAsync(item, ct);

                var storageItem = HomesteadStorageItem.Create(homestead.Id, item.Id);
                await homesteadRepo.AddStorageItemAsync(storageItem, ct);

                deposited++;
                freeSlots--;
            }

            // If still full after all salvaging, warn the player
            if (freeSlots <= 0)
            {
                var remainingInv    = await itemRepo.GetByOwnerAsync(playerId, ct);
                var nonEquippedCount = remainingInv.Count(i => !equippedIds.Contains(i.Id) && !i.IsLocked);
                if (nonEquippedCount > 0)
                {
                    await hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category  = "system",
                            text      = "Storage and inventory both full. Consider expanding storage (expandstorage command: 20x Wood + 10x Stone = +25 slots) or salvaging more."
                        }, ct);
                }
            }
        }

        autoFarmService.RecordDeposit(playerId, deposited);
        if (session is not null)
            session.ItemsDeposited += deposited;

        // Check companion auto-rotation on every deposit cycle (safe: not mid-fight)
        await using (var rotScope = scopeFactory.CreateAsyncScope())
        {
            var rotHelpers    = rotScope.ServiceProvider.GetRequiredService<CombatHelpers>();
            var rotPlayerRepo = rotScope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var rotPlayer     = await rotPlayerRepo.GetByIdAsync(playerId, ct);
            if (rotPlayer is not null && rotPlayer.AutoRotateMaxedCompanions)
                await rotHelpers.TryRotateMaxedCompanionsAsync(playerId, ct);
        }

        await Task.Delay(2_000, ct);

        var returnPlayer = await playerRepo.GetByIdAsync(playerId, ct);
        if (returnPlayer is not null)
        {
            returnPlayer.Move(returnPos);
            await playerRepo.UpdateAsync(returnPlayer, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("PlayerMoved", new
                {
                    returnPlayer.Id,
                    X = returnPos.X,
                    Y = returnPos.Y,
                    ZoneId = returnPos.ZoneId,
                    World = returnPos.World.ToString()
                }, ct);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category  = "system",
                    text      = $"Deposited {deposited} item(s) — resuming..."
                }, ct);
        }

        logger.LogInformation(
            "InventoryDepositService: player {PlayerId} deposited {Count} item(s) and returned to ({X}, {Y})",
            playerId, deposited, returnPos.X, returnPos.Y);
    }
}
