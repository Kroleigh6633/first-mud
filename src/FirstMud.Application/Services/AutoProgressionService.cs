using System.Collections.Concurrent;
using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

/// <summary>
/// Sub-mode the AutoProgressionService may dispatch to. Actual dispatch of
/// Craft/Imbue is stubbed until those sub-modes ship (see
/// <c>docs/design/auto-craft-auto-imbue-gap.md</c>).
/// </summary>
public enum ProgressionMode { Done, Farm, Craft, Imbue, Quest, Capture, Stalled }

/// <summary>
/// Which axis of the party's power is currently most-limiting. The scoring
/// formula lives in <see cref="AutoProgressionService.ComputeBindingConstraint"/>.
/// </summary>
public enum ProgressionAxis { None, Gear, Imbue, Companion, Player }

public record ProgressionStep(
    ProgressionMode Mode,
    ProgressionAxis BindingConstraint,
    double Reliability,
    IReadOnlyDictionary<ProgressionAxis, double> DeficitScores,
    string Reason,
    TimeSpan NextCheckIn,
    bool DispatchedActual);

public record AutoProgressionSession(
    Guid PlayerId,
    DateTimeOffset StartedAt,
    CancellationTokenSource Cts)
{
    public int ConsecutiveHitsAtTarget { get; set; }
    public int TicksWithoutProgress { get; set; }
    public double LastTopDeficit { get; set; } = double.PositiveInfinity;
    public ProgressionStep? LastStep { get; set; }
}

/// <summary>
/// Pass-7 progression targets — mirror the <c>full-party</c> playbook's holdouts
/// (that stack reliably clears d10 post-rebalance).
/// </summary>
public sealed record ProgressionTargets(
    int TargetPlayerLevel   = 8,
    int TargetGearTier      = 3,
    double TargetImbueCoverage = 1.0,
    int TargetAvgCompanionLayer = 3,
    int TargetDangerLevel   = 10,
    double RequiredReliability = 0.50,
    int GraceTicks          = 3,
    int StallTickLimit      = 30);

/// <summary>
/// Singleton session store for auto-progression. Separated from the scoped
/// <see cref="AutoProgressionService"/> so session state survives across
/// DI scopes (the tick handler creates a fresh scope every invocation).
/// </summary>
public sealed class AutoProgressionSessionStore
{
    private readonly ConcurrentDictionary<Guid, AutoProgressionSession> _sessions = new();

    public bool IsActive(Guid playerId) => _sessions.ContainsKey(playerId);
    public AutoProgressionSession? Get(Guid playerId)
        => _sessions.TryGetValue(playerId, out var s) ? s : null;

    public AutoProgressionSession Start(Guid playerId)
    {
        if (_sessions.TryGetValue(playerId, out var existing))
        {
            existing.Cts.Cancel();
            _sessions.TryRemove(playerId, out _);
        }
        var session = new AutoProgressionSession(playerId, DateTimeOffset.UtcNow, new CancellationTokenSource());
        _sessions[playerId] = session;
        return session;
    }

    public void End(Guid playerId)
    {
        if (_sessions.TryRemove(playerId, out var s))
            s.Cts.Cancel();
    }
}

/// <summary>
/// Meta-mode that drives a fresh character → d10-viable progression loop.
///
/// The decision logic (what is the binding constraint right now?) is fully
/// implemented. Actual dispatch to sub-modes is stubbed pending auto-craft
/// and auto-imbue (see gap doc). See <c>docs/design/auto-progression-design.md</c>.
///
/// Scoped — uses scoped repos. Session state lives in the singleton
/// <see cref="AutoProgressionSessionStore"/>.
/// </summary>
public class AutoProgressionService
{
    private readonly AutoProgressionSessionStore _sessions;
    private readonly IPlayerRepository _players;
    private readonly ICompanionRepository _companions;
    private readonly IItemRepository _items;
    private readonly IContentProvider _content;
    private readonly ProgressionTargets _targets;

    public AutoProgressionService(
        AutoProgressionSessionStore sessions,
        IPlayerRepository players,
        ICompanionRepository companions,
        IItemRepository items,
        IContentProvider content)
        : this(sessions, players, companions, items, content, new ProgressionTargets()) { }

    public AutoProgressionService(
        AutoProgressionSessionStore sessions,
        IPlayerRepository players,
        ICompanionRepository companions,
        IItemRepository items,
        IContentProvider content,
        ProgressionTargets targets)
    {
        _sessions = sessions;
        _players = players;
        _companions = companions;
        _items = items;
        _content = content;
        _targets = targets;
    }

    // ─── Session lifecycle (delegates to singleton store) ──────────────────

    public bool IsActive(Guid playerId) => _sessions.IsActive(playerId);
    public AutoProgressionSession? GetSession(Guid playerId) => _sessions.Get(playerId);
    public AutoProgressionSession StartSession(Guid playerId) => _sessions.Start(playerId);
    public void EndSession(Guid playerId) => _sessions.End(playerId);

    // ─── The decision loop ──────────────────────────────────────────────────

    /// <summary>
    /// Assess current party state and return the next progression step.
    /// Called by <c>AutoProgressionTickHandler</c> every 60 s (live) or once
    /// per simulated 10 minutes (playbook).
    /// </summary>
    public async Task<ProgressionStep> EvaluateAsync(Guid playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
        {
            return new ProgressionStep(
                ProgressionMode.Stalled, ProgressionAxis.None, 0.0,
                new Dictionary<ProgressionAxis, double>(),
                "Player not found.",
                TimeSpan.FromSeconds(60), DispatchedActual: false);
        }

        var companions = await _companions.GetByOwnerAsync(playerId, ct);
        var activeCompanions = companions
            .Where(c => !c.IsPermanentlyGone && player.ActiveCompanionIds.Contains(c.Id))
            .ToList();

        // 1. Reliability at target danger.
        var reliability = await AssessReliabilityAsync(player, activeCompanions, ct);

        if (reliability >= _targets.RequiredReliability)
        {
            return new ProgressionStep(
                ProgressionMode.Done,
                ProgressionAxis.None,
                reliability,
                new Dictionary<ProgressionAxis, double>(),
                $"Target met: d{_targets.TargetDangerLevel} reliability {reliability:P0} ≥ {_targets.RequiredReliability:P0}.",
                TimeSpan.FromSeconds(60),
                DispatchedActual: false);
        }

        // 2. Binding constraint.
        var (binding, deficits) = await ComputeBindingConstraintAsync(player, activeCompanions, ct);

        // 3. Map axis → sub-mode. Craft + Imbue sub-modes don't exist yet →
        //    dispatchedActual=false so the tick handler skips the action.
        var (mode, dispatched, reason) = MapAxisToSubMode(binding);

        return new ProgressionStep(
            mode,
            binding,
            reliability,
            deficits,
            reason,
            TimeSpan.FromSeconds(60),
            DispatchedActual: dispatched);
    }

    // ─── Reliability assessment ─────────────────────────────────────────────

    /// <summary>
    /// Runs <c>CombatSimulationService</c> at the target danger level and
    /// returns win-rate (0..1). Deterministic per invocation (seed derived
    /// from playerId + UTC-day so answer is stable across a session but
    /// varies day to day).
    /// </summary>
    public virtual Task<double> AssessReliabilityAsync(
        Player player,
        IReadOnlyList<Companion> activeCompanions,
        CancellationToken ct)
    {
        const int rolls = 50; // lighter than playbook's 200 — we run every 60s

        var partyMembers = activeCompanions
            .Select(c => new CombatSimulationService.PartyMember(
                c.Type, c.Element, c.CurrentLayer, c.Level))
            .ToList();

        var monsterDef = _content.GetMonster("frost-giant");
        if (monsterDef is null) return Task.FromResult(0.0);
        var baseTemplate = new MonsterTemplate(
            monsterDef.Name, monsterDef.Hp, monsterDef.Speed,
            monsterDef.Level, monsterDef.Element, monsterDef.Abilities);
        var scaled = MonsterScaling.Apply(
            baseTemplate, _targets.TargetDangerLevel,
            _content.CombatCurves.MonsterScaling, isBoss: false);

        int seed = unchecked(player.Id.GetHashCode() * 1_000_003 + (int)(DateTimeOffset.UtcNow.Date.Ticks % int.MaxValue));
        int wins = 0;
        for (int i = 0; i < rolls; i++)
        {
            var rng = new Random(seed + i);
            var svc = new CombatSimulationService(rng);
            var enc = svc.BuildEncounter(
                player.PrimaryElement == default ? MagicElement.Aether : player.PrimaryElement,
                Math.Max(1, player.Level),
                partyMembers,
                new[] { scaled });
            var result = svc.Run(enc, maxRounds: 60, dangerLevel: _targets.TargetDangerLevel);
            if (result.Outcome == CombatSimulationService.Outcome.Victory) wins++;
        }
        return Task.FromResult((double)wins / rolls);
    }

    // ─── Binding constraint ─────────────────────────────────────────────────

    private async Task<(ProgressionAxis axis, IReadOnlyDictionary<ProgressionAxis, double> deficits)>
        ComputeBindingConstraintAsync(
            Player player,
            IReadOnlyList<Companion> activeCompanions,
            CancellationToken ct)
    {
        // Gear tier proxy: scan equipped items, take max Workmanship (crude,
        // but the sim also uses a level-bump proxy so it's consistent).
        int currentGearTier = await EstimateGearTierAsync(player, ct);
        double gearDeficit = Clamp01(
            (_targets.TargetGearTier - currentGearTier) / (double)Math.Max(1, _targets.TargetGearTier));

        double currentImbueCoverage = await EstimateImbueCoverageAsync(player, ct);
        double imbueDeficit = Clamp01(_targets.TargetImbueCoverage - currentImbueCoverage);

        double avgLayer = activeCompanions.Count == 0
            ? 0.0
            : activeCompanions.Average(c => (double)c.CurrentLayer);
        double companionDeficit = Clamp01(
            (_targets.TargetAvgCompanionLayer - avgLayer) / (double)Math.Max(1, _targets.TargetAvgCompanionLayer));

        double playerDeficit = Clamp01(
            (_targets.TargetPlayerLevel - player.Level) / (double)Math.Max(1, _targets.TargetPlayerLevel));

        var deficits = new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = gearDeficit,
            [ProgressionAxis.Imbue]     = imbueDeficit,
            [ProgressionAxis.Companion] = companionDeficit,
            [ProgressionAxis.Player]    = playerDeficit,
        };

        var scores = ScoreDeficits(deficits);

        // All scores below noise floor → None (caller falls back to Farm).
        if (scores.Values.All(v => v < 0.05))
            return (ProgressionAxis.None, deficits);

        var top = scores.OrderByDescending(kv => kv.Value).First().Key;
        return (top, deficits);
    }

    /// <summary>
    /// Public so tests can exercise it without hitting the DB.
    /// Weights: Gear 1.25 · Imbue 1.10 · Companion 1.00 · Player 0.90.
    /// Justification: gear and imbues have the largest per-point payoff in
    /// the sim's playerLevel proxy (gear × 2, imbue × 1).
    /// </summary>
    public static IReadOnlyDictionary<ProgressionAxis, double> ScoreDeficits(
        IReadOnlyDictionary<ProgressionAxis, double> deficits)
    {
        double Get(ProgressionAxis a) => deficits.TryGetValue(a, out var v) ? v : 0.0;
        return new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = Get(ProgressionAxis.Gear)      * 1.25,
            [ProgressionAxis.Imbue]     = Get(ProgressionAxis.Imbue)     * 1.10,
            [ProgressionAxis.Companion] = Get(ProgressionAxis.Companion) * 1.00,
            [ProgressionAxis.Player]    = Get(ProgressionAxis.Player)    * 0.90,
        };
    }

    private async Task<int> EstimateGearTierAsync(Player player, CancellationToken ct)
    {
        // Proxy: highest Workmanship across equipped items.
        // Detailed tier accounting will land with auto-craft.
        var equipped = player.EquippedItems.Values.Where(id => id != Guid.Empty).ToList();
        if (equipped.Count == 0) return 0;
        int maxTier = 0;
        foreach (var id in equipped)
        {
            var item = await _items.GetByIdAsync(id, ct);
            if (item is null) continue;
            if (item.Workmanship.Value > maxTier) maxTier = item.Workmanship.Value;
        }
        return maxTier;
    }

    private async Task<double> EstimateImbueCoverageAsync(Player player, CancellationToken ct)
    {
        var equipped = player.EquippedItems.Values.Where(id => id != Guid.Empty).ToList();
        if (equipped.Count == 0) return 0.0;
        int slotsWithImbue = 0;
        foreach (var id in equipped)
        {
            var item = await _items.GetByIdAsync(id, ct);
            if (item is null) continue;
            if (item.Imbues.Count > 0) slotsWithImbue++;
        }
        return slotsWithImbue / (double)equipped.Count;
    }

    // ─── Axis → sub-mode dispatch ───────────────────────────────────────────

    /// <summary>
    /// See docs/design/auto-progression-design.md "Sub-mode dispatch table".
    /// Gear/Imbue return dispatchedActual=false because auto-craft / auto-imbue
    /// sub-modes don't exist yet. Tick handler MUST respect the flag and
    /// emit a diagnostic rather than crashing.
    /// </summary>
    public static (ProgressionMode mode, bool dispatched, string reason) MapAxisToSubMode(ProgressionAxis axis)
        => axis switch
        {
            ProgressionAxis.Gear      => (ProgressionMode.Craft,  false, "auto-craft sub-mode not implemented yet; see docs/design/auto-craft-auto-imbue-gap.md"),
            ProgressionAxis.Imbue     => (ProgressionMode.Imbue,  false, "auto-imbue sub-mode not implemented yet; see docs/design/auto-craft-auto-imbue-gap.md"),
            ProgressionAxis.Companion => (ProgressionMode.Capture, true,  "Farm/capture to raise average companion layer."),
            ProgressionAxis.Player    => (ProgressionMode.Farm,    true,  "Farm to raise player level."),
            _                         => (ProgressionMode.Farm,    true,  "No binding deficit; default farming for components + XP."),
        };

    private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
}
