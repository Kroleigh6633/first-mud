# Quest Chain Batch 3 — Event-Hooked Chains

Batch 3 introduces a **single new chain** designed specifically to
exercise a `world-event` trigger window: the Seed-Conversion Attempt
(event #8 from `world-events.md`, fixture
`tools/design/fixtures/event-seed-conversion.json`).

The chain demonstrates the design pattern of **soft-failure on event-
window expiry** — a beat that does not gate-block the player but
silently fails over into a different end state if the window closes
first. This is the first quest in the design corpus to test the world-
event/quest-chain contract.

---

## Chain 4 — *The Thinking Girl*

**Hook.** Auld Maerwyn finds the player at the edge of the Thornwood
Verge and drops a single line: *"There's a girl thinking in the Compact
house. Thinking at the wrong frequency. Go listen."* The line is also
delivered cold by a passing Brother Velm if the player has Trusted+
Thornwood and skips Maerwyn for two ticks.

**Event coupling.** The chain's intro auto-fires on the *first* dawn
after `seedConversion.thenna.active` becomes true. It cannot be started
otherwise and cannot be re-played once Thenna's status resolves either
way.

**Window.** 5 in-game days from event fire (per fixture
`event-seed-conversion.json` `duration.ticks: 5`). Beats 4–5 must
complete before the window closes or the chain soft-fails to terminal
**`end-too-late`** (Thenna joins Ashen Reach voluntarily; the player is
not told until much later).

### Beats

**Beat 1 — Maerwyn's Hint.** Player accepts or refuses to investigate.
- Accept → `intel-compact-house` (rep neutral; sets `chain4.engaged`).
- Refuse → `end-uninvolved` (chain soft-closes; world-event still runs
  per fixture's `interceptionWindow.onFailure`).

**Beat 2 — The Compact House.** Player arrives at the Compact-house
zone and must locate Thenna. Two branches:
- **Direct (social).** Ask Halven Orys for an introduction.
  - Pass: persuasion + Compact ≥ Known. Grants `thenna.met`. Cost: 0.
  - Fail: Halven blocks ("She's resting, Rider"). Player can retry next
    tick OR branch to `intel-side-door` at -25 Compact.
- **Side door (covert).** Bribe a Compact servant or pick the lock.
  - Pass: Stealth check or 30 silver. Grants `thenna.met` and flag
    `chain4.covert`. Triggers a **delayed -50 Compact** at chain end if
    the side-door is detected (rolled at end of window).

**Beat 3 — The Listening.** Conversation with Thenna in dream-fragmented
voice. Three exchanges, dialogue-tree gated. Player learns:
- The dream-contact is named (an Ashen handler — *Calix*; first named
  Ashen handler in design corpus, flag for canon).
- Thenna can describe the *route* she's been told to walk.
- Thenna will not call it conversion — she calls it "remembering."

Player must extract at least **two** of three facts to satisfy
`chain4.intelGood`. Otherwise Beat 4 is locked to the harder branch.

**Beat 4 — The Choice.** Three branches, all event-window sensitive:
- **A) Walk her out to the Verge.** Requires `chain4.intelGood`.
  Player escorts Thenna to the Singing Brook on the Verge for a
  Thornwood hedge-school placement. **Time cost:** 2 ticks (i.e. must
  be entered no later than tick 3 of the window).
  - Success terminal: `end-rescue` → matches event fixture
    `interceptionWindow.successConditions` → `+150 Thornwood, +50
    Compact, -200 Ashen`, sets `thenna.protected.thornwood`.
- **B) Confront Calix on the route.** Player ambushes the Ashen
  handler. Combat encounter (designer note: Calix should be `tier:
  rare`, Aether-aligned, Dream-Walk-flee on 30% HP, drops `ashen-
  passphrase-token`).
  - Success: `end-handler-broken` → +100 Caervorn (Ewan Truce's ledger
    suddenly cooperates), +200 Gravenguard (Drest hears about it),
    Thenna's contact is severed but she is *not* counted as rescued —
    she remains in the Compact house, rattled. Sets
    `thenna.contact.severed` (NOT `thenna.protected.thornwood`).
    Soft-success — the event still resolves to `interceptionWindow.
    onSuccess` because the conversion is prevented, but the
    Thornwood-rescue rep package does not fire.
  - Failure: Calix Dream-Walks away. Player takes Aether damage. Beat
    4 is now locked to branch A on the next tick (one retry).
- **C) Tell Halven the truth.** Requires `chain4.intelGood`.
  - Halven faces a personal-vs-political choice. Two sub-outcomes:
    - Halven *acts*: Thenna is sent to a southern Compact house under
      escort. `end-compact-shielded` → +200 Compact, -100 Thornwood
      (Maerwyn is bitter), +0 Ashen (Calix loses contact but the
      Reach is unbothered).
    - Halven *delays*: He needs "a day to consider." That day costs
      a tick. If the window closes, soft-fail to `end-too-late`.

**Beat 5 — Resolution.** Whichever branch resolved, Maerwyn finds the
player one tick later. Her line varies:
- A: "Good. Tea." Permanent +100 Thornwood reputation floor.
- B: "Good. Tea? — but a different leaf today. The dark one." Permanent
  unlock: Maerwyn will sell `dravenite-counter-tincture` (1 per moon).
- C: "Compact does what Compact does." No permanent floor; +25
  Thornwood once.
- Soft-fail: Maerwyn does not appear. *That* is the tell.

### Soft-failure beats

The chain has **two** soft-failure paths beyond the event window:
1. **`end-too-late`** — fires when the event window closes
   (`seedConversion.thenna.active` cleared) before Beat 4 reaches a
   terminal. Player is not informed; the next time they pass the
   Compact house, the staff are quieter. Three ticks later Halven
   appears in mourning at the Bent Mare. `chain4.outcome.tooLate` is
   set; locked out of Beat 1 forever.
2. **`end-uninvolved`** — Beat 1 refuse path. Same downstream as
   too-late but no Halven mourning scene; Halven is generous-and-empty
   for one tick instead.

### Branches and reputation summary

| Terminal | Trigger | Thornwood | Compact | Ashen | Other | Token sets |
|---|---|---|---|---|---|---|
| `end-rescue`         | Beat 4A success | +150 | +50 | -200 | — | `thenna.protected.thornwood` |
| `end-handler-broken` | Beat 4B success | 0 | 0 | -100 | Caervorn +100, Gravenguard +200 | `thenna.contact.severed` |
| `end-compact-shielded` | Beat 4C "act"   | -100 | +200 | 0 | — | `thenna.compact.south` |
| `end-too-late`       | window expiry   | 0 | 0 | +100 | — | `thenna.converted` |
| `end-uninvolved`     | Beat 1 refuse   | 0 | 0 | +100 | — | `thenna.converted` |

### World-event interaction notes

- The event fixture's `interceptionWindow.onSuccess` fires on
  `end-rescue` AND `end-handler-broken` AND `end-compact-shielded` —
  any of the three. The reputation packages above are **on top of** the
  fixture's `+150 Thornwood, +50 Compact, -200 Ashen`. The chain's
  branch-specific deltas are *additional design pressure* that the
  fixture's flat package alone cannot express.
- This surfaces a **schema gap** in `event-seed-conversion.json`: the
  fixture's `successConditions` are list-AND, but the chain wants
  per-end-state success packages. Either:
  - the event fixture should support **multiple `successConditions`
    each with their own `onSuccess`**, or
  - the quest chain owns the rep deltas and the fixture only owns
    Thenna's spawn / despawn.
  The second is cleaner. Recommendation: strip rep deltas from the
  fixture's `onSuccess`, push them all into the chain. Documented in
  `canon-deliberations.md` Pass-3 follow-up.

### Hand-derived coverage

Five terminals, four entered via Beat 4, one via Beat 1, one via window
expiry. No dead branches. Beat 2 has retry; Beat 4 branch B has retry.
Beat 3 gates Beat 4 branches A and C on `chain4.intelGood`; if missing,
only Beat 4B remains, which is the most player-skill-dependent branch
(by design — under-prepared players are funnelled toward combat as the
"loud" option).

### JSON fixture

See `tools/design/fixtures/quest-chain4-thinking-girl.json`. Twelve beat
nodes, five terminal ends, exercises `requires.flags`, branching
`choices`, and the new effect type **`requireEventWindow`** (proposed
schema extension — see canon-deliberations Pass-3 follow-up question).

If `requireEventWindow` is not adopted, the fixture falls back to a
flag check: `requires.flags: ["seedConversion.thenna.active"]` on every
choice that advances toward Beat 4. Soft-failure is then implemented
not by the runner but by the world-event scheduler clearing the flag
on cleanup, leaving the chain stuck mid-beat — playable only via the
`end-too-late` terminal which auto-fires when re-entering the Compact
house zone.
