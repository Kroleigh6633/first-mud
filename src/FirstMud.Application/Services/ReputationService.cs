using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

public class ReputationService
{
    private readonly IPlayerRepository _players;

    // Faction tension multipliers (opposing faction -> delta multiplier)
    private static readonly Dictionary<(FactionId, FactionId), float> TensionPairs = new()
    {
        [(FactionId.ThornwoodCovens, FactionId.HouseCaervorn)] = -0.5f,
        [(FactionId.HouseCaervorn, FactionId.ThornwoodCovens)] = -0.5f,
        [(FactionId.Golvari, FactionId.Gravenguard)] = -0.4f,
        [(FactionId.Gravenguard, FactionId.Golvari)] = -0.4f,
        [(FactionId.Fairgean, FactionId.EmeraldCompact)] = -0.2f,
        [(FactionId.EmeraldCompact, FactionId.Fairgean)] = -0.2f,
    };

    public ReputationService(IPlayerRepository players)
    {
        _players = players;
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

        // Apply main reward
        player.AdjustReputation(primaryFaction, reputationReward);

        // Apply tension penalties to opposing factions
        foreach (var ((sourceFaction, affectedFaction), multiplier) in TensionPairs)
        {
            if (sourceFaction != primaryFaction) continue;
            var penaltyDelta = (int)(reputationReward * multiplier);
            if (penaltyDelta == 0) continue;
            player.AdjustReputation(affectedFaction, penaltyDelta);
        }

        await _players.UpdateAsync(player, ct);
    }
}
