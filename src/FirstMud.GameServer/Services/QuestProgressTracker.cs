using System.Collections.Concurrent;

namespace FirstMud.GameServer.Services;

/// <summary>
/// In-memory kill/gather progress for in-progress quests.
/// Lives as a singleton so CombatHelpers (scoped) can record kills and
/// InteractQuestCommandHandler can check them.
/// </summary>
public class QuestProgressTracker
{
    // Key: (playerId, questId) → kills recorded in the quest zone since accept
    private readonly ConcurrentDictionary<(Guid PlayerId, string QuestId), int> _kills = new();

    // Key: (playerId, questId) → items gathered / consumed for the quest
    private readonly ConcurrentDictionary<(Guid PlayerId, string QuestId), int> _items = new();

    /// <summary>Increment the kill counter for the given player's active quest.</summary>
    public void RecordKill(Guid playerId, string questId, int count = 1)
        => _kills.AddOrUpdate((playerId, questId), count, (_, existing) => existing + count);

    /// <summary>Returns how many kills have been recorded for this player/quest pair.</summary>
    public int GetKills(Guid playerId, string questId)
        => _kills.TryGetValue((playerId, questId), out var v) ? v : 0;

    /// <summary>Increment the gathered-item counter (for gather quests).</summary>
    public void RecordItemGathered(Guid playerId, string questId, int count = 1)
        => _items.AddOrUpdate((playerId, questId), count, (_, existing) => existing + count);

    /// <summary>Returns how many items have been gathered for this player/quest pair.</summary>
    public int GetItems(Guid playerId, string questId)
        => _items.TryGetValue((playerId, questId), out var v) ? v : 0;

    /// <summary>Remove all tracked progress for this player/quest (called on completion).</summary>
    public void Clear(Guid playerId, string questId)
    {
        _kills.TryRemove((playerId, questId), out _);
        _items.TryRemove((playerId, questId), out _);
    }
}
