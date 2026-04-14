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
| Q4 Feed-the-stone (Ashen sacrifice) | Forbidden until approved | Yes — user may unlock, restrict, or forbid permanently |

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

## Pass #4 / #5 additions

Three new canon-ambiguous questions surfaced while reconciling the Pass #3
encounter-balance sweep, the `content/npcs.json` gap, and the Sealed Letter
`--allow-underflow` non-result.

### Q4 — "Catalogue" as a spoken marker of Gravenguard allegiance

In Pass #1 voice samples, Commander Drest uses **"catalogue"** as an idiolect
tic. In Pass #5 drafting of Lieutenant Varn (batch 3), Varn is shown
*borrowing* that tic — the way an understudy borrows a lead's gesture. This
was a writing choice, not a canon fact.

> Is the word "catalogue" an institutional marker inside the Gravenguard
> (i.e. anyone who has served under Drest for 3+ years picks it up), or is
> it private to Drest alone?

**Ruling (conservative): Private to Drest alone. Varn's borrowing of it is
a *tell* — the speaker is copying a superior they have not earned.**

Rationale: making it an institutional tic trivializes the tell; keeping it
private makes Varn's mimicry load-bearing for the Sealed Letter chain (an
observant player notices the borrowed vocabulary and flags Varn early).

**Implications:** Any future Gravenguard NPC voice may not use "catalogue"
unless they are explicitly Drest-adjacent and drafted as imitating him.

### Q5 — `content/npcs.json` canonical identity keys

Named NPCs appear in quest fixtures (`kesh`, `varn`, `velm`) as *text*, not
as ids. When `content/npcs.json` lands, each NPC needs a stable
canonical id for fixture wiring.

> What is the canonical naming convention for NPC ids?

**Ruling (conservative): `hyphen-lowercase` full use-name, matching the
monsters/zones convention.** Examples: `kesh-of-reed-end`, `lieutenant-varn`,
`brother-velm`, `solan-drest`, `harken-vos`.

- Titles (Lieutenant, Brother, Commander) are **part of the id** when the
  character is *canonically* known by title more than first name.
- Use-names only — no surnames unless canon names them.
- Ambiguous collisions (two NPCs named "Varn") resolved by zone suffix:
  `varn-gravenhold` vs `varn-thornwood`.

**Implication:** The Pass #5 voice-batch-3 file uses these ids. Any
content-migration pass should consume those as-is.

### Q6 — Brother Velm's Thornwood listening habit

Batch 3 drafts Brother Velm (Gravenhold Chapel archivist) as treating a
Thornwood-leaning player with *more attention, fewer words*, and asks the
player what the Coven would read an Ardweld glyph as.

> Does the Chapel archive formally or informally consult the Thornwood
> Coven on Ardweld translations?

**Ruling (conservative): Informally, and exactly one archivist at a time
— whoever currently holds the position.** Brother Velm is that person now.
His predecessor consulted with Auld Maerwyn's mother (lore silent on
whether Maerwyn continues the relationship).

**Implication:** Rhianne Moss is **not** privy to this channel. If the
player tells Rhianne about it, that's new information, with
consequences. Flag for a future quest beat.

### Q7 — Tool-gap etiquette when a flag is missing

The Pass #5 runner found that `scenario-player --allow-underflow` is
silently accepted (the CLI arg-parser discards unknown flags) rather than
erroring. On the `quest-sealed-letter.json` fixture the `bribe-scribe`
branch (which needs 25 gold the player doesn't carry) is therefore
**unreachable in the sim**, and the playthrough silently picks a different
path.

> Should a design agent proceed with a sim run that silently ignored an
> unknown CLI flag, or block on a tool-gap sim-log?

**Ruling (conservative): Block. Record the run as a gap, not a result.**
This cycle records it as a gap in `docs/design/sim-logs/sealed-letter-
allowunderflow-attempt-20260413.txt` and does *not* promote the default-
path outcome to the sim-reports catalogue. A future cycle re-runs once
the flag is wired.

**Implication:** A standing "missing-flag detection" heuristic — a design
agent running any tool command should first `dotnet run -- <tool> --help`
or grep the command source for the flag before assuming a result is
meaningful.

---

## Pass #6 follow-up (2026-04-13, agent-a12aa569)

### Q7 update — `--allow-underflow` is now real and the bribe-scribe error surfaces

`scenario-player` now accepts `--allow-underflow` and the fixture-level
`allowUnderflow` flag. Verified by temporarily flipping the Sealed Letter
fixture's `allowUnderflow: true → false` and forcing
`accept,refuse-varn,bribe-scribe`:

```
ERROR at beat 'past-varn' choice 'bribe-scribe': removeItem 'gold' x25 exceeds stock (0 available)
```

The error fires mid-run but the runner continues traversal (the subsequent
nodes still evaluate because `past-varn` is not the forced beat after
`refuse-varn` — actually the tool walked on to the Velm path and the
bribe-scribe was reported as a side error). **Default semantics are now:
hard-error on underflow, continue the walk only if the fixture or CLI
opts in.** The Sealed Letter fixture keeps `allowUnderflow: true` so its
existing sim-reports remain valid.

**Standing rule:** New fixtures start with `allowUnderflow: false`. Only
flip to `true` if the authoring gap (missing reward chain for the stock)
is intentional and tracked. `quest-chain5-wrong-tide.json` is authored
with `false` and passes all 6 terminal walks.

### Q8 — Fairgean communication mechanism ("Below-Song")

Chain 5 introduces a "below-song" — a low-frequency Fairgean communication
the pilgrims can hear faintly. No lore file names this. **Ruling
(conservative):** treat as creative-coined, non-canon, not referenced in
`lore/*.md`. Flag for user ruling; rename or remove from the fixture on
request. Currently scoped to Chain 5 only — does not leak into other
fixtures or docs.

### Q9 — Two "good" terminals per chain: design pattern?

Chain 5 has **two** success terminals (`end-redirected` and
`end-diplomatic`). Earlier chains (Sealed Letter, Thinking Girl) funneled
to one canonical good end. **Ruling (conservative):** allowed, but each
good terminal must gate a **distinct downstream hook** (Chain 5 does:
diplomatic unlocks Below-Song Translator NPC, redirected does not). Prevents
the design anti-pattern where two "success" buttons give the same reward.

### Q10 — Pyrrhic-win band in encounter-sim

The progression-audit surfaced a tool-gap: encounter-sim's win/loss band
classifies a 99%-win, 19%-HP-remaining fight as "trivial." This gap is
why the progression-sim's "reliable d10" doesn't match the user's "can't
clear d5." **Ruling (out-of-scope tool change, not canon):** the creative
agent should weight HP% alongside win-rate when citing encounter-sim
results in design proposals. `progression-tune-proposal-v1.md` §5 does
this manually for now.

---

## Pass #8 follow-up (creative pass #8)

## Q4 — Player-initiated Ashen sacrifice

> Can a player actively *feed* an Ashen-aligned artifact by offering a
> named traveling-companion NPC to it?

**Ruling (conservative): Forbidden until explicitly approved.**

Surfaced by the Ashen Reach chain draft (`quest-chain-ashen-reach.md`,
Beat 4 "Feed it" option). The machinery is spec'd but content-locked
behind a flag. Reasoning parallels Q3: canon is silent on player-as-Ashen-
agent; design should not open that door on silence.

**Implications if approved:**
- "Feed it" becomes an Ashen-aligned win path (+world-event tick +3,
  permanent Ashen-sympathizer flag on player, blacklists all three
  major factions by one tier).
- Requires NPC-sacrifice dialogue scenes which lint will need to police
  for consent/agency (is the NPC aware? is the player told what happens?).

**If permanently forbidden:** Beat 4 ships with three options (Extract /
Unmake / Leave). No machinery change.

---

## Pass-7 follow-ups resolved in this pass

- **Zone rumors (GH #8):** Delivered as `content/zone-rumors.json`, 4–5
  strings per zone across all 9 Aeldran zones. Text-only, no mechanical
  coupling. Schema-free for now (shape is obvious; schema can be added
  when a consuming service exists).
- **Ashen Reach quest coverage:** Chain drafted (see
  `quest-chain-ashen-reach.md`) with an escort sub-step designed to
  ship the day the escort-template blocklist is partially lifted, plus
  a fallback travel-montage implementation that runs today against
  existing `content/monsters.json` biome spawns.

---

## Pass #9 follow-up (creative sweep #3, 2026-04-13)

### Q5 — Wyrdcut Blade: canonical status

> The new Chain 6 (Ash and Salt) rewards a **Wyrdcut Blade**. Is this
> a named unique, a tier, or a recipe-craftable item?

**Ruling (conservative): Quest-reward unique, not craftable, not a tier.**

The blade is a narrative item issued by Hazir at the Kiln-Gate at the
end of chain6. Its name appears in no existing weapons table and should
remain a one-per-character quest reward until the user rules otherwise.
A "Flawed Wyrdcut Blade" variant drops in the chain's skimmed-terminal
branch — same ruling.

**If later promoted:** becomes the anchor item for an Ashen weapon tree.
Until then, treat as narrative flavor; do not add to loot tables, shop
inventories, or salvage yields.

### Q6 — Caervorn courier rep ceiling

> Chain 7 (Highland Post) grants +200 Caervorn on the clean terminal
> plus a permanent +25 floor. Does this stack with RIDER_001's
> +150 reward for the same faction?

**Ruling (conservative): Stacks fully; no per-faction-per-arc cap yet.**

Faction rep is additive by design today, and the chain gates are short
enough that stacking +350 across two chains is not game-breaking at mid-
tier. If the user adds a rep-bounds system later, the cap should apply
uniformly across all chains rather than being retrofitted to chain 7
specifically.

### Q7 — Obsidian Shard reagent classification

> Chain 6 requires an **Obsidian Shard** from the black vent. Is this
> the same Obsidian Shard suppressed from salvage yields per
> `salvage-material-flow.json`?

**Ruling: Yes, same item. Chain 6 is the canonical acquisition path.**

`salvage-material-flow.json` already lists Obsidian Shard as
rare-suppressed — meaning salvage cannot produce it. Chain 6 and the
Ashen Reach boss loot tables (already present) remain the only legal
sources. No content change required; the suppression is working as
designed and chain 6 exploits it intentionally.
