using FirstMud.Application.Content;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

public class ReputationService
{
    private readonly IPlayerRepository _players;
    private readonly IContentProvider _content;

    // Canonical faction-hostility data now lives in content/factions.json
    // (FactionDefinition.HostileTo). The numeric tension multipliers that
    // drive cross-faction reputation knock-on remain in
    // Player.ApplyFactionTensions for now — see content-factions migration
    // report for the planned follow-up.

    public ReputationService(IPlayerRepository players, IContentProvider content)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// True if <paramref name="a"/> is declared hostile to <paramref name="b"/>
    /// in content/factions.json. Replaces inspecting the old TensionPairs table.
    /// </summary>
    public bool IsHostileTo(FactionId a, FactionId b)
    {
        var def = _content.GetFaction(a);
        return def is not null && def.HostileTo.Contains(b);
    }

    public async Task AdjustReputationAsync(
        Guid playerId,
        FactionId factionId,
        int delta,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        player.AdjustReputation(factionId, delta);
        await _players.UpdateAsync(player, ct);
    }

    public async Task<ReputationTier> GetTierAsync(
        Guid playerId,
        FactionId factionId,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        return player.GetReputationTier(factionId);
    }

    public async Task<bool> MeetsTierRequirementAsync(
        Guid playerId,
        FactionId factionId,
        ReputationTier required,
        CancellationToken ct = default)
    {
        var tier = await GetTierAsync(playerId, factionId, ct);
        return tier >= required;
    }

    public async Task ApplyQuestReputationRewardsAsync(
        Guid playerId,
        FactionId primaryFaction,
        int reputationReward,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        // Apply main reward — Player.AdjustReputation already applies faction tension penalties internally
        player.AdjustReputation(primaryFaction, reputationReward);

        await _players.UpdateAsync(player, ct);
    }
}
