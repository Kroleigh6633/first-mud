using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

public class CompanionService
{
    private readonly ICompanionRepository _companions;
    private readonly IPlayerRepository _players;

    public CompanionService(ICompanionRepository companions, IPlayerRepository players)
    {
        _companions = companions;
        _players = players;
    }

    public async Task RecordCombatUsageAsync(Guid companionId, int usagePoints, CancellationToken ct = default)
    {
        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            throw new InvalidOperationException($"Companion {companionId} not found.");

        // Rubber-banding (task #74): look up the companion's owner to apply
        // level-gap catch-up. If the owner record has gone missing (stale data,
        // ghost companion) we skip the bonus and record raw usage — the live
        // combat path uses player.Level directly and is the canonical entry
        // point; this service is mainly test/admin surface.
        var owner = await _players.GetByIdAsync(companion.OwnerId, ct);
        var mult = owner is null
            ? 1.0
            : FirstMud.Domain.Configuration.ProgressionCurvesAccessor
                .RubberBandMultiplier(owner.Level, companion.CurrentLayer);

        companion.RecordUsage((int)Math.Round(usagePoints * mult));
        await _companions.UpdateAsync(companion, ct);
    }

    public async Task TickDriftAsync(Guid ownerId, float hoursPassed, CancellationToken ct = default)
    {
        var companions = await _companions.GetByOwnerAsync(ownerId, ct);
        foreach (var companion in companions)
            companion.AccumulateDrift(hoursPassed);

        await _companions.UpdateManyAsync(companions, ct);
    }

    public async Task<bool> AssignToActiveSlotAsync(Guid playerId, Guid companionId, CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            throw new InvalidOperationException($"Companion {companionId} not found.");

        if (companion.OwnerId != playerId)
            throw new InvalidOperationException("Companion does not belong to this player.");

        if (!player.TryAddActiveCompanion(companionId))
            return false;

        companion.SetActive(true);

        await _players.UpdateAsync(player, ct);
        await _companions.UpdateAsync(companion, ct);
        return true;
    }

    public async Task RemoveFromActiveSlotAsync(Guid playerId, Guid companionId, CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            throw new InvalidOperationException($"Companion {companionId} not found.");

        player.RemoveActiveCompanion(companionId);
        companion.SetActive(false);

        await _players.UpdateAsync(player, ct);
        await _companions.UpdateAsync(companion, ct);
    }

    public async Task RecordWarningIgnoredAsync(Guid companionId, CancellationToken ct = default)
    {
        var companion = await _companions.GetByIdAsync(companionId, ct);
        if (companion is null)
            throw new InvalidOperationException($"Companion {companionId} not found.");

        companion.RecordIgnoredWarning();

        // If companion departed (IsPermanentlyGone or no longer active after warnings),
        // mark as permanently gone when warnings pushed it over the edge.
        if (!companion.IsActive && !companion.IsPermanentlyGone)
            companion.MarkPermanentlyGone();

        await _companions.UpdateAsync(companion, ct);
    }

    public async Task<Companion?> AttemptCaptureAsync(
        Guid captorId,
        string monsterName,
        MagicElement element,
        int captureRoll,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(captorId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {captorId} not found.");

        // captureRoll 1-100; success if roll > 60
        if (captureRoll <= 60)
            return null;

        var companion = Companion.Create(captorId, monsterName, CompanionType.CapturedMonster, element);
        await _companions.AddAsync(companion, ct);
        return companion;
    }
}
