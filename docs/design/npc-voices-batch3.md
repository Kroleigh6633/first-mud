# NPC Voice Samples — Batch 3 (addendum)

This batch covers NPCs **referenced in existing quest fixtures** but not
yet present in `npc-voices.md` / `npc-voices-batch2.md`. The fifth creative
pass surfaced these as gaps when cross-referencing `quest-sealed-letter.json`
against the canonical voice catalogue.

Written in the same format as Pass #1/#2 voice samples.

> **Note on `content/npcs.json`:** this file was **not present** in the
> worktree during Pass #5. The three voices below are drafted for either
> direct consumption by the missing `content/npcs.json` migration, or for
> `npcId` matching when that catalogue lands. Canonical ids are
> hyphen-lowercase, consistent with monsters/zones.

---

## 7. Kesh of Reed-End — Bog-runner, Reed-End (Gravenmarsh)

**Canonical id:** `kesh-of-reed-end`

**Lines.**
- "Drest. Eyes only. Not Varn. Especially not Varn. You swear?"
- "Reed-End doesn't miss me yet. Not for three days. Use the three days, Rider."
- "If I'm in the bog when you come back, then you were too slow. No hard feelings."

**Tells.**
1. Names the tide of the causeway aloud before speaking — "it's a low-water
   noon" — as a checksum that she is oriented and has not been turned.
2. Never takes coin. Takes *tally-marks* — chalk strokes on the back of a
   stave she keeps. She will later show it to someone who matters.
3. Clean nails. Everything else is bog-mud. She is meticulous about the one
   thing she is willing to be seen about.

**Voice by relationship.**
- **Low rep (Wary):** Transactional. Will still pass the letter, but will
  not meet the player's eye, and will not answer about Reed-End. "The
  letter. Take it. Walk." Shorter sentences than her default.
  *`requiresReputationMax: { faction: "gravenguard", max: 0 }`*
- **High rep (Trusted+):** Calls the player by use-name, not title.
  Explains what Reed-End actually is (a hidden Gravenguard source, not a
  village). Offers a second letter sight-unseen.
- **By faction stance (player leans Caervorn):** Passes the letter anyway
  — it's Drest's problem — but warns the player, once, that "the Commander
  will read you sideways, Rider. Be ready." Her last favor before she
  vanishes back into the reeds.

---

## 8. Lieutenant Varn — Gravenhold gate officer (antagonist)

**Canonical id:** `lieutenant-varn`

**Lines.**
- "The Commander's schedule is tight. I'll take that, Rider."
- "You are new. I am not. I have filed the paperwork that my superiors will need, and none that they will not."
- "A good officer catalogues his embarrassments quietly. Pass me the letter, and we'll call this nothing at all."

**Tells.**
1. Adopts Drest's own verbal tic — "catalogue" — one sentence in every
   three, the way an understudy borrows a lead's gesture. He does not
   know he does this.
2. His helm is always perfect. His boots are always wrong — one lace
   different. A captured habit from somewhere.
3. Reaches with the *left* hand when asking for documents; right hand
   stays at his sword-hilt. Parade manners, not trust.

**Voice by relationship.**
- **Low rep (Wary):** Formal to the point of insult. Refers to the
  player exclusively in third person — "the Rider will stand aside" —
  even when the player is the only person present.
  *`requiresReputationMax: { faction: "gravenguard", max: 0 }`*
- **High rep (Trusted+):** *Warmer*. This is the tell. A Rider who
  Gravenguard has learned to trust is a Rider Varn has learned to
  *feed*. He smiles when he asks for documents now.
- **By faction stance (player known-Ashen-adjacent):** Speaks softly.
  Explicitly does not log the visit. Later, a report will exist anyway,
  in someone else's hand.

---

## 9. Brother Velm — Archivist, Gravenhold Chapel (Ardweld glyph reader)

**Canonical id:** `brother-velm`

**Lines.**
- "Bring it to the south table. The north light is wrong for glyphs, and
  I am too old to argue with the sun."
- "This is a *naming*. A funerary naming. Something in that expedition
  was already dead, and someone had already un-killed it. Oh. Oh no."
- "I have read three Ardweld hands in my life. None of them was this
  one. Whoever carried this was, I think, not reading anymore."

**Tells.**
1. Reads glyphs aloud in whisper, then *louder*, like tuning a string.
   The louder reading is the one he trusts; the whisper is to check for
   shame.
2. Keeps a grinding-bowl of sea-salt on his desk. Touches a pinch to his
   tongue before reading anything Ardweld. Never explains why; the
   Chapel knows.
3. Weeps without sound. Has a handkerchief folded three ways, always in
   the same pocket. Never pretends he isn't weeping.

**Voice by relationship.**
- **Low rep (Wary):** Treats the player as a courier only. Accepts the
  text, delivers the reading, dismisses. "Thank you. Safe roads."
  *`requiresReputationMax: { faction: "gravenguard", max: 0 }`*
- **High rep (Honored+):** Will explain what a *naming* is (an Ardweld
  binding of a soul to a deed) and what the implication of an un-killed
  subject of one is. Offers the player a piece of sea-salt. It is a
  small gift and an enormous one.
- **By faction stance (player leans Thornwood):** *Listens* more than he
  speaks. Asks the player what the Coven would read this as. Has a
  private reason; canon-load the answer to Q6 below.

---

## Fixture wiring status (low-rep variants)

Voices drafted in `npc-voices.md` that **do not yet have a fixture with
a `requiresReputationMax` low-rep branch wired**:

| NPC | Voice source | Low-rep variant drafted? | Fixture has it? |
|-----|--------------|:-------------------------:|:----------------:|
| Senna Orrick   | batch1 #1 | yes | no |
| Auld Maerwyn   | batch1 #2 | yes | no |
| Solan Drest    | batch1 #3 | yes | no |
| Bryn Hollemar  | batch1 #4 | yes | no |
| Harken Vos     | batch1 #5 | yes | **yes** (`greet-low-rep`) |
| Rhianne Moss   | batch1 #6 | yes | no |
| Kesh           | batch3 #7 | yes | no |
| Varn           | batch3 #8 | yes | no |
| Brother Velm   | batch3 #9 | yes | no |

**Gap:** 8 of 9 NPCs have a drafted low-rep variant that is not wired
into a dialogue fixture. Harken Vos is the reference implementation
(`tools/design/fixtures/dialogue-harken-vos.json` roots now include
`greet-low-rep`, linter reaches all 17 nodes).

**Next-pass hand-off:** produce one dialogue fixture per NPC, each with a
low-rep entry node gated on `requiresReputationMax`, mirroring the Harken
pattern. That unblocks dialogue-lint on the full voice catalogue.
