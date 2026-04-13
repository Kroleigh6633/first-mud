# Auto-progression mode — design

Status: **design + MVP scaffold** (2026-04-13). Sub-modes `auto-craft` and `auto-imbue`
do not yet exist; see `auto-craft-auto-imbue-gap.md` for scope.

## Vision

User wants the character and party to drive themselves toward end-game viability:

> "on auto-farming, the character and the companions should constantly look to
> improve their gear, do imbuings/enchantings and also farm the components
> needed to do all that. […] I want to see this group take on d10 and survive
> at least 50% of the time."

Auto-progression is the **meta-mode** that selects and dispatches between the
focused sub-modes (farm / craft / imbue / quest / capture) based on the current
**binding constraint** — the single factor most limiting reliable-danger.

## Benchmark

`reliableDanger(N) ≥ 10 @ ≥ 50%` using `CombatSimulationService` against the
canonical d10 monster scaled via `MonsterScaling` at 200 rolls, with the
party's current gear / imbues / levels / companions as inputs.

The progression run is complete when this holds across a **grace window** of 3
consecutive re-evaluation ticks (≈ 3 minutes live, or ≈ 30 simulated minutes in
playbook mode) so a lucky spike doesn't terminate early.

## Decision-loop cadence

| Mode              | Tick interval            | Rationale                                         |
|-------------------|--------------------------|---------------------------------------------------|
| Live play         | **60 s**                 | Matches `AiPlayerTickHandler` / farming loop, gives the player responsive feedback without flooding chat |
| Playbook / sim    | 10 simulated minutes     | Granular enough to observe constraint shifts; cheap because the simulation runs in-process |

The `AutoProgressionTickHandler` walks active sessions every 60 s and invokes
`AutoProgressionService.EvaluateAsync(playerId, ct)`.

## Binding-constraint scoring

The service computes a **deficit score** `0..1` per axis. The axis with the
**highest deficit** is the binding constraint and dispatched to its sub-mode.
Ties break by priority: `Gear > Imbue > Companion > Player > Quest`.

### Formula

```
// Assess viability via CombatSimulationService at the target danger.
reliability = wins / rolls                      // 0..1 at danger=10

// If we're already there, we're done.
if (reliability >= 0.50) return Done

// Per-axis deficits:
gearDeficit      = clamp01( (targetGearTier     - currentGearTier)     / targetGearTier )
imbueDeficit     = clamp01( (targetImbueCoverage - currentImbueCoverage) )       // fraction 0..1
companionDeficit = clamp01( (targetAvgLayer     - avgCompanionLayer)   / targetAvgLayer )
playerDeficit    = clamp01( (targetPlayerLevel  - currentPlayerLevel)  / targetPlayerLevel )

// Weighted scoring: gear + imbues matter most for a mid-game stack.
score = {
  Gear:      gearDeficit      * 1.25,
  Imbue:     imbueDeficit     * 1.10,
  Companion: companionDeficit * 1.00,
  Player:    playerDeficit    * 0.90,
}

// Pick max; if all scores < 0.05, fall back to Farm (components / xp trickle).
binding = argmax(score) ?? Farm
```

### Targets (pass 7 balance baseline)

| Axis              | Target                                |
|-------------------|---------------------------------------|
| `PlayerLevel`     | 8                                     |
| `GearTier`        | 3                                     |
| `ImbueCoverage`   | 1.0 (all 5 slots imbued ≥ level 2)    |
| `AvgCompanionLayer` | 3                                   |

These mirror the `full-party` playbook holdouts (which reliably clear d10 in
pass-7 balance). If creative passes shift the balance, targets move with them.

### Weight justification

- **Gear (1.25)** — biggest single multiplier in combat math (`playerLevel +=
  gearTier * 2` in the playbook engine). A tier-3 stack is worth 6 effective
  levels of raw stats.
- **Imbue (1.10)** — `playerLevel += imbueLevel`; less punchy per point but
  stacks with gear and is usually cheaper to produce (salvage → tapers).
- **Companion (1.00)** — party scaling buff in `PartyScaling.Factor` is real
  but secondary to player raw stats at the d10 threshold.
- **Player (0.90)** — weighted lowest because leveling is implicit in farming;
  it shouldn't hijack a cycle unless gear + imbues are already maxed and the
  character is simply under-levelled.

## Sub-mode dispatch table

| Binding constraint | Sub-mode           | Ships with this cycle? |
|--------------------|--------------------|------------------------|
| `Gear`             | `auto-craft`       | ❌ TODO (see gap doc)   |
| `Imbue`            | `auto-imbue`       | ❌ TODO (see gap doc)   |
| `Companion`        | `auto-farm` (capture priority) | ✅ via existing auto-farm |
| `Player`           | `auto-farm` (xp priority)      | ✅ via existing auto-farm |
| `Quest`            | `auto-quest` (existing runner)  | ✅                      |
| (fallback)         | `auto-farm` balanced            | ✅                      |

Until the `auto-craft` / `auto-imbue` sub-modes exist, the scaffolded service
returns a `ProgressionStep { Mode: Craft | Imbue, DispatchedActual: false,
Reason: "sub-mode not implemented" }`. The tick handler logs this and does
**not** attempt a dispatch. The benchmark playbook will therefore stall when
Gear or Imbue is binding — that's the honest expected curve until sub-modes land.

## Stop conditions

1. **Success** — reliability ≥ 0.50 for 3 consecutive ticks → broadcast
   `"Progression target reached: d10 survival at X%."` + `AutoProgressionStatus
   { active: false, reason: "completed" }`, end session.
2. **Stall** — 30 consecutive ticks with no movement in any deficit score
   → broadcast `"Auto-progression stalled. You may need to take manual
   action."` and pause (user must restart with new guidance).
3. **User stop** — explicit `autoprogression stop` command ends the session.
4. **Defeat cascade** — 3 auto-farm defeats within a session → downgrades the
   max-danger cap and re-evaluates; this is handled by the existing
   `AutoFarmService` adaptive cap, we just consume its state.

## Failure modes + stall recovery

| Failure                                       | Response                                                                                   |
|-----------------------------------------------|--------------------------------------------------------------------------------------------|
| `Gear` binding but no recipes craftable       | Log reason, fall back to `Farm` for ingredients; re-evaluate on next tick                   |
| `Imbue` binding but no tapers in storage      | Fall back to `Farm` at `auto-salvage` priority; re-evaluate                                 |
| All deficits below 0.05 and reliability < 0.5 | Diagnostic: emit `"Progression puzzle — combat sim can't beat d10 with current ceilings."` and pause; user takeover |
| Sub-mode not implemented                      | Log `dispatched=false, reason="sub-mode stub"`; tick handler skips                          |

User-takeover prompt (when `Stall` fires):

```
[auto-progression] No progress for 30 ticks. Binding constraint is <axis>.
Try: <suggested manual action>. Use [progression stop] to cancel.
```

## Client surface (this cycle)

- Typed command: `commands.autoProgression(action: 'start' | 'stop' | 'status')`.
- No UI yet. Server sends `GameMessage` + `AutoProgressionStatus` events
  (same shape as `AutoFarmStatus`) that the client console already surfaces.
- Banner + keybind come in a later cycle once the sub-modes are real.

## Concurrency with existing modes

- Starting auto-progression auto-ends any active auto-farm session (the service
  owns auto-farm for the duration; it will re-start a new farm session with
  the appropriate priority on dispatch).
- `autoprogression stop` ends progression but does **not** auto-resume farming.
- Only one auto-\* session per player at a time (same rule as auto-farm).
