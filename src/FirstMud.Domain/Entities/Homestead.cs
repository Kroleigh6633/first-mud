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

    public static Homestead Create(Guid playerId, string name, int storageSlots = 100)
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
    /// Expands storage by the given number of additional slots.
    /// Called by ExpandStorageCommandHandler after verifying material cost.
    /// </summary>
    public void ExpandStorage(int additionalSlots)
    {
        if (additionalSlots <= 0) return;
        StorageSlots += additionalSlots;
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
    /// Flat slot bonus per guard based on bond level (layer).
    /// </summary>
    private static int BondStorageBonus(int bondLevel) => bondLevel switch
    {
        1 => 5,
        2 => 10,
        3 => 20,
        4 => 40,
        5 => 70,
        6 => 100,
        _ => 5
    };

    /// <summary>
    /// Computes effective storage capacity factoring in companion guards.
    /// Bonus scales with each guard's bond level (CurrentLayer).
    /// Diminishing returns: guards 4-5 provide 50% bonus; beyond 5 guards is ignored.
    /// </summary>
    public int EffectiveStorageSlots(IEnumerable<int> guardBondLevels)
    {
        var sortedGuards = guardBondLevels.OrderByDescending(b => b).ToList();
        int bonus = 0;
        for (int i = 0; i < sortedGuards.Count; i++)
        {
            if (i >= 5) break; // cap at 5 effective guards
            int guardBonus = BondStorageBonus(sortedGuards[i]);
            if (i >= 3) guardBonus /= 2; // 50% for guards 4 and 5
            bonus += guardBonus;
        }
        return StorageSlots + bonus;
    }
}
