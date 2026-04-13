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
