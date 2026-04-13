# NPC Voices — Batch 2

Six NPCs drawn from Chain 4 (Compact) and Chain 5 (Golvari), plus
`world-events.md` #8 (Seed-Conversion). Each entry follows the Pass-1
schema: 2–3 lines, 3 tells, voice-by-relationship (low/high/faction),
**and a new `low-rep variant`** block that is the fixture-linter-callable
counterpart to the prose, using the newly-available
`requiresReputationMax` schema.

The `low-rep variant` block is written in dialogue-tree JSON shape so
the content-migration agent can paste it directly into a node. The
`requiresReputationMax` gate fires only when faction rep is **at or
below** the listed max (i.e. Wary or worse). This replaces the Pass-1
workaround of writing low-rep content as prose-only.

---

## 1. Jena Pell — Junior Compact Factor, Portmere

**Lines.**
- "Three items. One, your audit closed clean — congratulations, few do.
  Two, I was not supposed to read your audit. Three, I did. May I sit
  down, please, I have been carrying these for a week."
- "I number things. My aunt says I number things because I am afraid
  of them. My aunt is correct. I am afraid of them."
- "If the Council does not answer me in thirty days I shall file
  formally. I understand what filing formally means. I have done the
  arithmetic."

**Tells.**
1. Speaks in enumerated lists ("One… two… three…") even under stress
   — especially under stress.
2. Carries a cloth-wrapped counting abacus everywhere; produces it when
   thinking, never when actually counting.
3. Her left shoe is always slightly looser than her right — habit from
   fleeing one place as a child and keeping the option open.

**Voice by relationship.**
- **Low rep (Wary or worse):** Withdraws to formal factor-speak. "A
  Factor of the Emerald Compact has no business I can discuss in this
  room. Good day, Rider." Does not look up from her ledger.
- **High rep (Trusted+):** Drops the numbered-list armor. Long
  sentences, occasionally whole paragraphs. Admits fear.
- **Faction (player Caervorn-leaning):** Treats with civil coolness.
  Never names the warehouse. Filters everything through the
  Merchants' Guild treaty framework.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "jena-greet-lowrep",
  "text": "A Factor of the Emerald Compact has no business I can discuss in this room. Good day, Rider.",
  "requiresReputationMax": { "faction": "compact", "max": -1 },
  "options": [
    { "text": "Understood.", "next": "farewell-neutral" }
  ]
}
```

---

## 2. Merin Pell — Warehouse-Keeper, Compact Safehouse (Jena's cousin)

**Lines.**
- "I am not held. I am *guested*. They used the word. I wrote it down."
- "The slab is black. It hums at about the pitch a tuning fork gives
  off when you drop it on stone. It does not speak. It listens. I
  listened back once and they came and took the chair I was sitting in."
- "Tell Jena I am well. Tell her I remember the spring we learned to
  swim. Do not tell her I am writing this in pencil because they will
  not give me ink."

**Tells.**
1. Writes every word spoken to him on the back of his own hand, then
   rubs it off before the guards see.
2. Laughs once, softly, at each new cruelty; then is silent.
3. Quotes his father ("my father said") whenever he is about to refuse
   something. His father is dead.

**Voice by relationship.**
- **Low rep (any faction — he is a prisoner, there is no Wary with Merin):**
  Suspicious. Will not give the letter. "I do not know you. I have
  been told not to know people."
- **High rep (any Compact Known+ OR Jena-contact flag):** Immediate
  trust, accelerated talk, reads the player three encoded details and
  makes them repeat it back.
- **Faction (player Caervorn-leaning):** Refuses to speak. Sits.
  Waits for the guards to escort the player out.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "merin-silent",
  "text": "I do not know you. I have been told not to know people. Please go.",
  "requiresReputationMax": { "faction": "compact", "max": 0 },
  "options": [
    { "text": "Walk away.", "next": "safehouse-exit" }
  ]
}
```

---

## 3. Kheth-vel-Muir — Golvari Ambassador, Gravenhold quarters

**Lines.**
- "I have been six months in a stone room that was not cut properly.
  The angles are *almost* right. It is the almost that has aged me."
- "My people are patient. We are famously patient. We are patient
  because we are bored of being patient. Do not mistake one for the other."
- "If Commander Drest will not see me, he must instead see my absence.
  That is harder to interview. I advise against it."

**Tells.**
1. Reads surface architecture by running one finger along mortar lines
   — always at a consistent height (his own, not the human eye level).
2. Counts in multiples of seven in his own head; if asked how long
   something has been, answers in sevens ("seven days, seven weeks…").
3. Never eats surface food in front of humans; accepts it, pockets it,
   returns it later courteously unopened.

**Voice by relationship.**
- **Low rep (Wary or worse — Golvari tracks this strictly):** The
  ambassador is formal beyond parsing. Third-person, archaic
  constructions. "This ambassador regrets that this conversation
  cannot serve the speaker's purpose."
- **High rep (Trusted+):** Drops two layers of protocol. Uses
  contractions. Jokes once per conversation, dry.
- **Faction (player Caervorn-leaning):** Distant but not hostile.
  Will not discuss the emergence points. Changes every topic to
  stonework.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "kheth-formal-refusal",
  "text": "This ambassador regrets that this conversation cannot serve the speaker's purpose. The Rider is thanked for their courtesy. The door is behind the Rider.",
  "requiresReputationMax": { "faction": "golvari", "max": -1 },
  "options": [
    { "text": "Bow and withdraw.", "next": "quarters-exit" }
  ]
}
```

---

## 4. Thenna Orys — Halven Orys's Ward, Age 17 (Seed-Conversion target)

Per `canon-deliberations.md` Q3 conservative ruling: Thenna is 17, i.e.
adolescent, not a child. Do not write her below 16 unless user overrules.

**Lines.**
- "I had a dream about a city made of light. It was not frightening. It
  was instructional. Do you know what I mean?"
- "My uncle does not ask what I dream. I think because he has one
  answer he does not want to hear."
- "The man in the dream says my name the way my mother used to. But my
  mother has been dead since I was nine, and he has the pronunciation
  *exactly*. He must be listening somewhere."

**Tells.**
1. Pauses mid-sentence to listen to something the player cannot hear —
   recovers smoothly, never apologizes for it.
2. Speaks her own name wrong on purpose about half the time ("Thenna,"
   "Tenna," "Thenna again") as if checking which one is hers today.
3. Draws spirals on any dusty surface with her index finger while
   talking. If the player points it out, she looks down, embarrassed,
   and scrubs it with her sleeve — always counter-clockwise.

**Voice by relationship.**
- **Low rep (Wary, or player has never spoken to her before):**
  Careful, polite, rehearsed. "Good day. I am well. I trust you are
  well. Please tell my uncle I was helpful to you." Zero signal.
- **High rep (Trusted+ OR Maerwyn-introduced):** The dream-talk starts.
  She is eager — not zealous — to be heard by someone who will not
  laugh. This is the window the player has to intercept.
- **Faction (player Thornwood-leaning):** Watches the player with
  open curiosity. Describes the dream-city in greater detail. Asks
  whether the Rootweave has a voice the player can describe.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "thenna-polite-wall",
  "text": "Good day. I am well. I trust you are well. Please tell my uncle I was helpful to you.",
  "requiresReputationMax": { "faction": "compact", "max": -1 },
  "options": [
    { "text": "Thank her and leave.", "next": "farewell-neutral" }
  ]
}
```

---

## 5. The Portmere Dockworker — "Old Ren"

A minor named NPC supporting Chain 4 Beat 2 social path. "Old Ren" is
his only name — a dockworker who has worked Warehouse #3 for twelve
years under three different unregistered Compact quiet-contracts.

**Lines.**
- "I don't know what you saw. I know what *I* saw, and what I saw was
  a slab of black stone that has never once needed a single repair.
  Nothing in this yard is like that. Nothing in the *city* is like that."
- "I'm owed a pension. That's all I want out of this conversation.
  Don't get me killed before I collect it."
- "If you tell Jena I opened the door, I will tell anyone who asks
  that you pushed me aside. Not because I dislike you. Because I have
  a daughter."

**Tells.**
1. Chews tobacco he is no longer allowed to chew by a doctor he no
   longer visits; spits over his left shoulder before every firm
   statement.
2. Calls the slab "the guest" and refuses to say "stone" while in the
   warehouse.
3. Writes nothing down, ever, including his own name on a receipt.

**Voice by relationship.**
- **Low rep (Wary with Compact):** Won't unlock the door. "The night
  watch is around. Go around." Walks away.
- **High rep (Compact Trusted+ OR Jena-contact flag):** Opens the door
  on the first knock. Does not pretend. Spits, nods, waits.
- **Faction (player Caervorn-leaning):** Hostile. "Your lot doesn't
  get this door." Threatens to whistle for the watch.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "ren-walk-away",
  "text": "The night watch is around. Go around. I never saw you.",
  "requiresReputationMax": { "faction": "compact", "max": -1 },
  "options": [
    { "text": "Leave the alley.", "next": "portmere-street" }
  ]
}
```

---

## 6. Senior Factor Lirya Vane — Compact Council, Portmere Branch

The "one specific Councilor she trusts" from Chain 4 Beat 5
`success-clean`. Named here because the chain demands a name and the
dockworker/Jena/Merin trio cannot collectively play this role.

**Lines.**
- "You brought me a problem. I dislike being brought problems. I
  dislike *more* being brought problems after they have ripened into
  catastrophes. So: thank you, on the early timing."
- "The Compact is not one thing. It is seventeen things in a polite
  coat. When one of the seventeen does something the other sixteen
  cannot know about, it is my job to notice. Tonight I noticed. Tomorrow
  I will act. The day after I will not remember your name. This is a
  kindness."
- "Do not ask me what happens to the slab. You will not like the verb."

**Tells.**
1. Signs her name twice on every document — once in black ink,
   once in green, on the same line; the green is her private mark.
2. Keeps a bowl of unshelled almonds on her desk and shells one for
   every five minutes of conversation; pushes the shells into a small
   neat pile she never sweeps away.
3. Never sits in the same chair twice in one meeting — stands, walks,
   returns to a different chair at each pause.

**Voice by relationship.**
- **Low rep (Wary):** Will not meet the player. Sends a clerk: "The
  Senior Factor is engaged indefinitely." Clerk is sincere; Lirya is
  literally engaged, she is busy shelling an almond.
- **High rep (Trusted+):** The second quoted line happens verbatim.
  She is indiscreet only when she has already decided to act.
- **Faction (player Thornwood-leaning):** Slightly warmer — the
  Compact and Thornwood have quiet alignment at senior levels. Offers
  the player an almond. Considered a minor honor.

**Low-rep variant (JSON, paste-ready):**
```json
{
  "id": "lirya-unavailable",
  "text": "The Senior Factor is engaged indefinitely. Please leave your request in writing with the outer clerk.",
  "requiresReputationMax": { "faction": "compact", "max": -1 },
  "options": [
    { "text": "Withdraw to the clerk's desk.", "next": "compact-antechamber" }
  ]
}
```

---

## Schema note on `requiresReputationMax`

All six low-rep variants above use
`"requiresReputationMax": { "faction": "<f>", "max": <N> }`.

Interpretation: **the option or node is visible only while the named
faction's reputation is ≤ `max`**. Typical values:
- `max: -1` — Wary only (actively disliked).
- `max: 0` — neutral-or-Wary (unknown + disliked).
- `max: 999` — Known-or-below (i.e. not-yet-Trusted).

This is the **inverse** of `requiresReputation.min`. Both should be
linter-supported; the linter should also warn if a node uses both on
the same gate and the resulting window is empty (e.g. `min: 100,
max: 50`).

For live use: see `tools/design/fixtures/dialogue-harken-vos.json`
node `greet-low-rep`, which uses `requiresReputationMax` for Harken's
Wary-tier opening and lints cleanly in the current tool build.
