# Canon Deliberations — Pass #2

Pass #1 flagged three canon-ambiguous items that the user has not ruled on. This
document takes the **conservative** interpretation on each so that downstream
design (quest chains, faction threads, NPC voices) is coherent. If the user
overrules, a later creative agent edits the design docs — **do not edit
`lore/*.md`**.

"Conservative" here means: the reading that (a) preserves the strongest existing
canon constraints, (b) keeps player agency minimally coupled to canon, and
(c) keeps the darkest content off the table until explicitly sanctioned.

---

## Q1 — Riders' commission contracts

> Can a Rider refuse a King's writ without losing Rider status?

**Ruling (conservative): Yes, once per arc, with a reputation cost and a logged mark.**

A Rider may formally decline a specific commission by invoking a **"reasoned
refusal"** — a clause implicit in the Rider's Post charter, since Riders are
commissioned *free agents* under royal license rather than soldiers. The refusal:

- Costs **-500 Caervorn** (or the commissioning faction) flat reputation,
- Adds a permanent mark on the Rider's Post record (visible to Commander Drest,
  Aldric, and Senna's gossip),
- May be invoked **at most once per story arc** — a second refusal triggers
  termination review.

**Why conservative:** Canon says Riders serve the King (via the Rider's Post),
not individual lords. Refusing one lord's writ is therefore not mutiny. But
canon also implies Riders live or die by reputation, so "free" refusal would
trivialize the role.

**Implications for already-drafted chains:**
- **Chain 3, Beat 4 "Refuse":** Already applies -400 Caervorn, permanent Wary
  floor. Consistent with ruling. No change.
- **Chain 2, Beat 2 Branch A "Rider-writ rescue":** Already described as
  "burns a commission." This is the **opposite** lever — using the Rider's
  commission *for* someone. The once-per-arc budget is shared with the refusal
  budget: both tap the same well.
- **Chain 1, Beat 2b "Rider's privilege":** Already described as
  "once-per-arc resource." Consistent. No change.
- **New implication:** A single "commission token" per arc is a tracked
  resource. Spend it on refusal, on writ-rescue, or on privilege — not all
  three. Flag for future mechanics: `player.commissionToken.available: bool`.

---

## Q2 — Ashen Court silence (Maren's 9-month absence)

> Is Maren's silence story-triggered or time-gated?

**Ruling (conservative): Story-triggered, but with a time-gate *floor*.**

Maren surfaces only when **both** conditions hold:

1. **Story trigger:** the player has reached Trusted+ with at least one of
   Gravenguard, Thornwood, or Caervorn **and** has touched at least one Ashen
   thread (e.g. caught Ewan Truce, discovered a Dream-installation, or rescued
   Brother Velm from Dream-Walking).
2. **Time floor:** no fewer than **60 in-game days** since the Lost Expedition
   beat (Chain 1 Beat 4) has resolved in the world state.

Reasoning: canon says Maren has been absent "nine months" from an unstated
reference point. Making it purely time-gated guarantees every playthrough
sees Maren's return at the same beat and trivializes player-driven pacing.
Making it purely story-gated risks Maren surfacing five in-game days into a
new character's career, which breaks the staged menace of the threads.
The **floor** protects menace; the **trigger** protects agency.

**Implications:**
- **Chain 1 end-state "Ardweld Remnant precursor":** Maren's re-emergence
  can be hooked as the *reason* the Remnant petition race matters. Time floor
  means a fast Chain 1 speedrunner still cannot force the Ashen arc early.
- **Faction-threads.md "Completion of the Sundering":** The Dream-installation
  build-out proceeds on a *tick* (see `world-events.md` in this pass) whether
  or not Maren is visible. Maren's return is the *reveal*, not the mechanism.
- **New implication:** The world-event tick `ashen.installations.progress`
  fires independently of Maren — the threat is running, he's just offstage.

---

## Q3 — Children as Ashen recruits

> Canon is silent on minors. Seed-Conversion Program assumed yes.

**Ruling (conservative): The Seed-Conversion Program targets adolescents
(16+), NOT children, until the user explicitly approves a lower age.**

Rationale: canon is silent; design should not quietly push into ethically
heavy territory on silence alone. The **Dream-contact cultivation** is
coherent with adolescents (noble wards, younger squires, apprentices) — they
still count as "seed" in the Ashen symbolic logic (pre-Choice, pre-Soul-
Touched), and the thread's menace is preserved.

**Implications for faction-threads.md Seed-Conversion line:**
- Keep the program as described, but re-cast "children of minor nobility" as
  **"wards and younger kin of minor nobility, typically 16–20"**.
- Gwyn Calder (already named as a "Thornwood apprentice" — adolescent-coded,
  no change needed) remains canonical.
- A new named seed is added in `world-events.md`: **Thenna Orys** (Halven's
  ward, 17, Dream-contacted at a Compact house). This gives the thread a
  live named target that a player can intercept or fail to.
- If the user later approves minors, swap the cast down; the machinery is
  unchanged.

**What this forecloses until user rules:** Any scene, dialogue, or quest beat
depicting a Dream-Walk of a named character under 16. Lint rule for next
cycle's dialogue-lint pass: flag any node referencing a minor in Ashen
context.

---

## Summary for the user

| Question | Conservative ruling | Overturnable? |
|---|---|---|
| Q1 Rider refusal | Once per arc, -500 rep, logged | Yes — user may declare unlimited refusal, or zero |
| Q2 Ashen silence | Story-trigger AND 60-day floor | Yes — user may declare pure time or pure story |
| Q3 Child recruits | 16+ only until approved | Yes — user may approve minors or forbid entirely |

Overturning any of these is a one-line user statement; the later creative
agent edits this file and the downstream quest / thread docs. Lore is
untouched either way.

---

# Canon Deliberations — Pass #3 (Creative Pass #4 follow-up)

Pass #3 surfaced three NEW canon-ambiguous items via tool behavior. As
before: the **conservative** ruling is taken so downstream design is
coherent. User may overrule.

## Q4 — `scenario-player` `removeItem` underflow

> The runner currently silently floors `removeItem` to 0 when the player
> has fewer items than the effect requests. Should this be allowed?

**Ruling (conservative): No. `removeItem` on insufficient stock should
be a fixture-validation error, not a silent floor.**

Rationale: a fixture that says `removeItem letter x1` is asserting a
chain-state contract — the letter MUST be in inventory at that beat, or
the fixture's `requires` block is wrong. Silent floor masks fixture bugs
(Pass #2 found exactly this in the sealed-letter chain — see
`sim-reports/sealed-letter-playthrough.md`, the "commission-token
scoping" hole). Surface the error; force the fixture author to add a
`requires.items` predicate or a guard branch.

**Implications:**
- The current behavior (worktree-local — Pass-3 fix not yet merged here)
  is preserved during this pass; new Chain 4 fixture does not exercise
  `removeItem`. When the fix lands, no Pass-1/2/3 fixture should regress
  (none reach a `removeItem` on empty stock under hand-traced paths).
- Migration agents lifting fixtures into `content/quests/` should add
  defensive `requires.items` blocks for any beat with `removeItem`.

## Q5 — `notFlags` predicates for cross-chain token enforcement

> Should the fixture schema add a `requires.notFlags: [...]` predicate
> so a chain can express "this beat is locked if some other chain set X"?

**Ruling (conservative): Yes — but as `requires.absentFlags`, not
`notFlags`.** Naming consistency: positive (`flags`) and negative
(`absentFlags`) read naturally; `notFlags` invites Boolean confusion in
JSON (does it negate the list, or each item?).

Rationale: Pass #2's commission-token problem can only be solved cleanly
with a negative predicate. Chain 1 Beat 2b "Rider's privilege" must be
locked if Chain 2 Beat 2 Branch A "Rider-writ rescue" already burned
the token (or vice versa). Without `absentFlags`, every chain would have
to carry redundant gating logic in choice text or be enforced by the
runner via global state — both are worse than a one-line schema add.

**Schema addition (proposed):**
```jsonc
"requires": {
  "flags": ["chain1.engaged"],
  "absentFlags": ["commissionToken.spent"],
  "items": [],
  "reputation": {}
}
```

**Implications:**
- Chain 4 *almost* needed it (covert side-door vs. honest entry). Did
  not adopt because the current fixture handles it via differential rep
  loss, but if the user approves, Chain 4's `side-door` choice could
  add `absentFlags: ["compact.audit.active"]` to lock covert entry
  during a Compact audit event.
- All three Pass-2 fixtures should add `absentFlags` once available;
  the sealed-letter chain in particular is currently relying on
  positive flag-presence to imply absence elsewhere.

## Q6 — Faction reputation extending to sub-region groupings

> Do faction reputations extend to sub-region groupings (e.g. Caervorn
> rep applied to Marchkeep specifically, or split per-keep)?

**Ruling (conservative): No. Faction reputation is faction-global. Sub-
region behavior is expressed through *event flags* and per-NPC opinion
modifiers, not a separate rep ledger.**

Rationale: a per-sub-region rep ledger explodes the reputation matrix
combinatorially (Caervorn alone would split into ~6 keeps + Marchkeep +
Ironspire) and forces every NPC dialogue gate to specify *which*
Caervorn rep it reads. The maintenance cost is enormous and the design
payoff is small — most regional flavor (Hollemar's mood, Vekter Stain's
hostility) is better expressed as **per-NPC opinion** layered on top of
the faction baseline.

**Mechanism instead of new ledger:**
- Each NPC carries an `opinion` integer in -200..+200, **derived** from
  faction rep at first meet, then drifting independently based on the
  player's actions toward that NPC.
- World-event flags (e.g. `assaySweep.active`) modify NPC behavior at
  the zone scope without touching rep.
- Sub-region "tier" gates (e.g. "Marchkeep refuses entry below Wary")
  read faction rep but apply zone-local thresholds.

**Implications:**
- `world-events.md` event #3 "Caervorn Assay Sweep" already operates
  this way — it's a flag, not a rep delta on a sub-region. Consistent.
- `npc-voices.md` should grow an `opinion-drift` annotation per NPC in a
  later pass: how much does this NPC's opinion drift per significant
  player action?
- Quest chains do not need to specify sub-region rep deltas; only
  faction rep + flags. **Chain 4 is consistent** — it deltas factions
  (Compact, Thornwood, Ashen, Caervorn, Gravenguard), not Marchkeep
  specifically.

---

## Pass-3 follow-up open question (for Pass #4 next cycle)

The Chain 4 / event-fixture overlap (see `quest-chains-batch3.md` §
"World-event interaction notes") surfaced a NEW schema question:

**Q7 (deferred):** Should `world-event` fixtures own reputation deltas
on their `interceptionWindow.onSuccess`, or should that ownership move
entirely to whichever quest chain hooks the window?

Strawman: chain owns rep, event owns world-state and NPC presence. This
keeps the event fixture composable (multiple chains can hook the same
window without colliding rep packages). Defer to next cycle pending
user direction.

## Summary for the user (Pass #3 additions)

| Q | Question | Conservative ruling | Overturnable? |
|---|---|---|---|
| Q4 | `removeItem` underflow | Hard error, not silent floor | Yes |
| Q5 | Negative-flag predicates | Add as `requires.absentFlags` | Yes |
| Q6 | Sub-region reputation | No — use per-NPC opinion + flags | Yes |
| Q7 | Event vs. chain rep ownership | (Deferred — chain owns rep, strawman) | — |

