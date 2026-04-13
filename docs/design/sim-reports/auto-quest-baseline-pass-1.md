# Auto-quest completion — baseline pass 1

**Playbook**: `tools/design/playbooks/auto-quest-completion.json` (`kind: flow`)
**Sim service**: `AutoQuestSimulationService` (in DesignTools — no server dep)
**Seed**: 42, simulatedMinutes=30, rolls/cell=1
**Corpus**: 13 authored quests in `content/quests.json`
**Date**: 2026-04-13 (agent worktree `a28b3adf`)

## What the sim does

For each cell `(startZoneId, questPoolSize)` we build a zone-preferred quest
pool (prefer quests whose `startingZoneId == startZoneId`, then fill from the
remaining corpus), then step each quest through the same beats the client
runner uses: `accept → navigate → interact → complete`. At each beat we
classify the outcome using content-level probes that mirror the server-side
`QuestAutoCompleteService` quest-type heuristic + keyword extraction:

| Outcome | Meaning |
|---|---|
| `Completed` | auto-quest path is data-complete end-to-end |
| `StalledNoStartZone` | quest has no `startingZoneId` → client has nowhere to navigate |
| `StalledNoMonsters` | kill quest but its zone has no spawnable monsters at/near its danger band |
| `StalledUnknownItem` | gather/deliver quest whose description has no extractable item keyword |
| `StalledNoItemSource` | gather/deliver quest whose item isn't produced by any monster/recipe/loot pool |
| `Timeout` | accepted but the simulated clock exceeded `simulatedMinutes` before close |

## Failure mode taxonomy (final)

1. `StalledNoStartZone` — fatal; client has nowhere to walk.
2. `StalledUnknownItem` — quest type heuristic says gather/deliver but the
   description lacks a verb-phrase the `ExtractItemKeyword` regex can bite on.
3. `StalledNoItemSource` — the keyword resolves but nothing in content produces
   it (no monster drop, recipe, or loot pool entry).
4. `StalledNoMonsters` — kill quest sited in a zone whose danger band has no
   monster candidates.
5. `Timeout` — loop spent too long on navigation / kill grind.
6. `QuestTypeMisclassified` — **not a sim output** but a root cause: the
   server's `GetQuestType` matches title nouns ("Delivery", "Hunt") as if
   they were verbs, routing narrative quests through the item-gather path.

## AutoQuestSimulationService — yes, extracted

Built a new `AutoQuestSimulationService` under
`tools/design/FirstMud.DesignTools/Tools/PlaybookRunner/AutoQuestFlow/`. It
consumes `IContentProvider` only — no Neo4j / SignalR / React state. The
`QuestAutoCompleteService` helpers (`GetQuestType`, `ParseKillCount`,
`ParseItemCount`, `ExtractItemKeyword`) are duplicated here verbatim so the
sim faithfully reproduces server-side decisions. If/when these helpers change
we add a cross-assembly equivalence test; for now the duplication is a
conscious shim because `DesignTools` must not reference `FirstMud.GameServer`.

## Flow cell-evaluator shape

`PlaybookEngine.Execute` dispatches on `playbook.Kind`:

```
if kind == "flow":  FlowCellEvaluator.Execute(...)    // new
else:               (existing combat path, unchanged)
```

`FlowCellEvaluator` shares `PlaybookEngine.PlaybookRunResult` / `CellResult`
so all existing infra (CLI, JSON log writer, compare-to-prior diff, ledger)
works for flow playbooks unchanged. WinRate carries completionRate; AvgRounds
carries avgMinutesPerQuest; AvgPlayerHpPct carries stallRate; Difficulty
carries the healthy/rough/broken/blocked label.

`AxisValues` now accepts either ints (combat) or strings (flow — zone ids).
Combat playbook JSON is byte-identical to before.

## Baseline per-quest outcomes (corpus-wide, `RunAll`)

| Quest | Type | Outcome |
|---|---|---|
| RIDER_001 A Delivery Gone Wrong | deliver | **StalledUnknownItem** |
| RIDER_002a What Was In The Package | explore | Completed |
| RIDER_002b Finding The Interceptors | explore | Completed |
| THORN_001 The Mage on the Moor | explore | Completed |
| THORN_002 Safe Passage | explore | Completed |
| THORN_003 The Rootweave Hums | explore | Completed |
| THORN_004 Eldest Mira Asks | explore | Completed |
| GRAVE_001 Maps and Rumors | explore | Completed |
| GRAVE_002 The Ruin at Coldmere | explore | Completed |
| GRAVE_003 Commander Drest's Secret | explore | Completed |
| FAIR_001 Shore Crossing | explore | Completed |
| FAIR_002 Why They Came Inland | explore | Completed |
| ASHEN_001 A Dream You Can't Explain | explore | ~~StalledNoStartZone~~ → Completed (post-tune) |

**Overall completion rate (corpus baseline)**: 11/13 = **84.6%** before tune;
**12/13 = 92.3%** after the ASHEN_001 data tune in this pass.

## Per-zone completion rate (zone-preferred pool, size=10)

| Zone | Completed / Accepted | Rate | Notes |
|---|---|---|---|
| aeldran-1-caervorn-highlands | 9/10 | 90% | RIDER_001 lives here — only quest that stalls |
| aeldran-2-thornwood          | 10/10 | 100% | clean |
| aeldran-5-drowned-coast      | 10/10 | 100% | clean |
| aeldran-7-starting-road      | 9/10 | 90% | inherits RIDER_001 via fallback pool |
| aeldran-8-gravenhold         | 10/10 | 100% | clean |

No kill-type quests exist in the current corpus, so the `StalledNoMonsters`
branch of the taxonomy is currently unreached. Adding any "Hunt / Slay /
Defeat N X in zone Y" quest will exercise it.

## Quests that never complete

- **RIDER_001 "A Delivery Gone Wrong"** — persistently stalls with
  `StalledUnknownItem` across all five zones. Not a narrative gap — it is a
  **quest-type misclassification**. The title contains the noun "Delivery",
  which `QuestAutoCompleteService.GetQuestType` does not match on, but
  matches on "Deliver" and gets a false positive. The description never
  names a recoverable item because the quest is narrative (branch: report
  vs. conceal), not a physical delivery. Any auto-quest player rolling this
  quest will stall in the interact phase with "I can't extract an item
  keyword from your description."

## Root cause category

**MIXED, but 100% DATA-or-classifier:** no evidence of a runtime client-loop
bug or a server handler bug in the sim path. Two distinct failures; both at
the content/heuristic layer:

1. **DATA (fixed this pass)** — ASHEN_001 missing `startingZoneId`.
2. **LOGIC (queued for engineering)** — `GetQuestType` in
   `QuestAutoCompleteService` (`src/FirstMud.GameServer/Services/QuestAutoCompleteService.cs`)
   treats narrative titles that happen to contain gather/deliver/kill nouns
   as if they were objective titles. The long-term fix is an explicit
   `questType` field on `QuestDefinition` authored in `quests.json`; a short
   fix is tightening the heuristic (require the word appear in imperative
   position, or require a verb-phrase match in the description too). Either
   way, the fix is a C# edit + a content/quests.json schema bump — **not**
   a client-runner change.

## Action taken

- **Data tune applied**: `content/quests.json` — added
  `"startingZoneId": "aeldran-6-ashen-reach"` to ASHEN_001 (Ashen Court
  faction's home zone; no other candidate).
- **Engineering follow-up queued**:
  - File: `src/FirstMud.GameServer/Services/QuestAutoCompleteService.cs`
  - Proposed issue title: "Quest-type classifier false-positives on
    narrative titles (e.g. RIDER_001 → deliver)"
  - Repro: run `playbook-runner --playbook auto-quest-completion`; RIDER_001
    will diverge with `StalledUnknownItem` in every cell.
  - Proposed fix: add optional `questType` field to `QuestDefinition` and
    read it in `GetQuestType` before falling back to title keywords. For the
    12 existing quests 11 are already `explore` and this would be unchanged;
    RIDER_001 gets `"questType": "explore"` (it completes on decision, not
    delivery).

## Test count change

- DesignTools tests: 34 → 36 (+2 new flow tests, +1 diagnostic dump = +3).
  Live count verified at 36 passing.
- Main `FirstMud.slnx` test count: unchanged at 418 (402 unit + 16 integ).

## Ledger

Row added with `status: in-progress`; bumped to `ready-to-merge` before
handing back.

## One question for next cycle

Should the flow playbook enumerate across simulated player states
(levelRange × gearTier × companionCount) too, so we catch quests that can be
accepted early-game but can only be *completed* at a gear/level the player
hasn't reached? Right now the sim assumes any gather target that has *any*
content source is reachable; it doesn't check whether the mid-game player
specced in `holdouts` can actually farm a low-drop-rate item in the
`simulatedMinutes` budget.
