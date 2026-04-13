# Quest Chains — Batch 4

Setting: **The Drowned Coast** (zone `aeldran-5-drowned-coast`, danger 5).
One chain. Fixture: `tools/design/fixtures/quest-chain5-wrong-tide.json`.

## Chain 5 — "The Wrong Tide"

**Premise.** A coastal hamlet named Kelpholm has started seeing its tide
come in at the wrong hour — by three full hours, every third day. The
bogmen pilgrims who winter at Kelpholm read it as an omen. A Rider commission
arrives: find out what is moving the tide.

**Faction touch-points.**
- **Fairgean** — the off-shore variable; rep gain only through the
  diplomatic terminal.
- **Bogman pilgrims** — local informants, non-combat. Rep gain on all
  non-failure terminals.
- **Rider Compact** — contract-giver; pays on any terminal that closes the
  commission (even the "bad answer").

**Structure.** 7 beats, 5 terminals.

- `intro` (Rider post at Kelpholm) — commission presented.
  - accept → `survey`
  - refuse → terminal `end-refused` (rep: rider-compact -50; Chain 1
    remains available for redemption)
- `survey` — pick a read method:
  - talk-pilgrims → `pilgrim-read`
  - survey-tide → `tide-read`
  - spook-fairgean → `provoke` (gated: player must have
    `drowned-coast.visited` flag, set on zone-entry)
- `pilgrim-read` — The pilgrims speak of a "below-song" — rhythmic
  low-frequency sound from the deep. They beg the player not to dive.
  - promise-no-dive (sets `pilgrim.trusted`) → `choose-approach`
  - ignore-warning → `choose-approach`
- `tide-read` — the tide's off-cycle is mechanically regular. Something
  sub-surface is displacing a predictable volume of water.
  - measured-read (requires craftingSkill ≥ 3 OR pilgrim.trusted) →
    `choose-approach` with `flag: tide.measured`
  - hand-wave → `choose-approach` (no flag; `commune` terminal closed later)
- `choose-approach` —
  - dive (requires NOT pilgrim.trusted) → `confront-creature`
  - commune-fairgean (requires `tide.measured` OR
    `reputation.fairgean >= 10`) → `diplomatic`
  - leave-and-report → terminal `end-reported-no-close`
    (rider-compact +50 pay, but no close; chain can be retried next arc)
- `provoke` — dangerous shortcut: fire-signal the deep to force a Fairgean
  surfacing. Always costs `pilgrim.trusted` if set.
  - hold-nerve → `diplomatic-hostile` (rep.fairgean -100)
  - flee → terminal `end-scared-off` (rep.bogmen -50)
- `confront-creature` — a **Tide-Eater**, a territorial Fairgean
  maintenance-beast that has been rerouting a deep current as nest
  construction. Combat probe.
  - kill-beast → terminal `end-killed-beast`
    (rider-compact +250, fairgean -300, `tide.restored` flag,
    unlocks Chain 6 hook "Fairgean Reprisal" next arc)
  - spare-and-redirect (requires `tide.measured`) →
    terminal `end-redirected` (rider-compact +250, fairgean +100,
    `tide.restored`)
- `diplomatic` — surface parley, Fairgean speaker emerges.
  - negotiate → terminal `end-diplomatic`
    (rider-compact +200, fairgean +200, bogmen +100, unlocks the
    "Below-Song Translator" recurring NPC for Chain 6)
- `diplomatic-hostile` — Fairgean arrives hostile, expects ambush.
  - stand-down → `diplomatic` (fairgean -50 penalty persists)
  - attack → `confront-creature`

### Terminals

| id                      | outcome                        | rep                                                    |
|-------------------------|--------------------------------|--------------------------------------------------------|
| `end-refused`           | `refused`                      | rider-compact -50                                       |
| `end-reported-no-close` | `partial-incomplete`           | rider-compact +50                                       |
| `end-scared-off`        | `failure-withdrew`             | bogmen -50                                              |
| `end-killed-beast`      | `success-violent`              | rider-compact +250, fairgean -300, bogmen +50          |
| `end-redirected`        | `success-clean`                | rider-compact +250, fairgean +100, bogmen +100         |
| `end-diplomatic`        | `success-diplomatic`           | rider-compact +200, fairgean +200, bogmen +100         |

### Why two "clean" terminals

`end-redirected` rewards the player who *measured* the tide (a skill-gate
choice); `end-diplomatic` rewards the player who made *peace with the
pilgrims* before acting. Both are good ends; they feed different late-game
faction arcs. This is the inverse of the Sealed Letter chain, where the
"good" path was narrow — Chain 5 deliberately widens the success fan to
reward investigation breadth.

### Cross-chain hooks

- `tide.restored` flag is needed by a future Chain 6 "Below-Song" quest
  (Fairgean-faction deep-dive); if chain ends at `end-killed-beast`, Chain 6
  opens on a revenge footing instead.
- "Below-Song Translator" NPC unlocked only via `end-diplomatic`. Makes this
  terminal the canonical route for players who want Fairgean story access.

### Design notes

- **No `requiresReputationMax`** used in this chain; all gates are positive.
  Confirms the existing `requires` schema handles a full chain without the
  new predicate.
- **`removeItem`** only used for the optional `bribe-pilgrim-elder` branch
  (cut from v1; left as a comment in the fixture for a future pass).
- `provoke` path is deliberately *punishing by design*: fast, high-rep-cost,
  locks out two of the three good terminals. Mirrors the user's stated
  preference that shortcuts must cost.

### Outstanding canon questions

1. **Fairgean diplomatic language.** Is the "Below-Song" canon, or
   creative-coined? Lore file `lore/factions.md` mentions Fairgean but not
   a communication mechanism. (Speculative — flag for ruling.)
2. **Tide-Eater** — does the bestiary have an existing Fairgean
   maintenance-beast? If yes, rename to match. If no, this is a proposed
   monsters.json entry (separate PR).
3. **Rider commission pay in gold** — set to 0 in the fixture because gold
   economy is not balanced. Fixture uses reputation-only payout; real
   fixture-to-content promotion needs a pay-rate table.
