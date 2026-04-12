namespace FirstMud.Domain.Entities;

/// <summary>
/// Join entity that places an Item in a homestead's storage vault.
/// The Item row itself keeps OwnerId = null (not in a player's inventory).
/// </summary>
public class HomesteadStorageItem
{
    public Guid Id { get; private set; }
    public Guid HomesteadId { get; private set; }
    public Guid ItemId { get; private set; }

    private HomesteadStorageItem() { }

    public static HomesteadStorageItem Create(Guid homesteadId, Guid itemId)
    {
        return new HomesteadStorageItem
        {
            Id = Guid.NewGuid(),
            HomesteadId = homesteadId,
            ItemId = itemId,
        };
    }
}
