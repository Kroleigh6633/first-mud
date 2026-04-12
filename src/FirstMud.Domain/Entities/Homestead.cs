namespace FirstMud.Domain.Entities;

public class Homestead
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int StorageSlots { get; private set; }

    /// <summary>
    /// Item IDs queued for companion-assisted salvaging.
    /// Stored as a JSON column. Companions on Salvager duty consume from this list.
    /// </summary>
    public List<Guid> SalvageQueue { get; private set; } = [];

    private Homestead() { }

    public static Homestead Create(Guid playerId, string name, int storageSlots = 50)
    {
        return new Homestead
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Name = name,
            StorageSlots = storageSlots,
            SalvageQueue = [],
        };
    }

    /// <summary>
    /// Adds an item to the salvage queue (if not already present).
    /// </summary>
    public void EnqueueForSalvage(Guid itemId)
    {
        if (!SalvageQueue.Contains(itemId))
            SalvageQueue.Add(itemId);
    }

    /// <summary>
    /// Removes and returns the next item to salvage, or null if queue is empty.
    /// </summary>
    public Guid? DequeueNextSalvage()
    {
        if (SalvageQueue.Count == 0) return null;
        var id = SalvageQueue[0];
        SalvageQueue.RemoveAt(0);
        return id;
    }

    /// <summary>
    /// Removes a specific item from the salvage queue (e.g. item was picked up / withdrawn).
    /// </summary>
    public void RemoveFromSalvageQueue(Guid itemId) => SalvageQueue.Remove(itemId);

    /// <summary>
    /// Computes effective storage capacity factoring in companion guards.
    /// +20% per guard companion (50 base → 60 with 1 guard, etc.).
    /// </summary>
    public int EffectiveStorageSlots(int guardCount) =>
        (int)(StorageSlots * (1.0 + guardCount * 0.2));
}
