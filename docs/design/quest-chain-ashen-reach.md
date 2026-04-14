# Quest Chain — **The Glass That Remembers** (Ashen Reach)

Zone: `aeldran-6-ashen-reach` (danger 8). First chain written for the Reach. Designed to be accessible only after the player has at least Known status with two of {Gravenguard, Thornwood, Caervorn} AND has completed Chain 1 Beat 4 ("Translation") — the Reach's ambient menace is pitched against mid-arc players, not starters.

**Synopsis.** A Compact caravan is paying triple rate to go *into* the Reach — unheard of — to recover an Ardweld naming-stone that has started answering voices it shouldn't know. The player escorts the caravan in, discovers the stone is being *fed* rather than silenced, and decides whether the thing answering back gets named, unmade, or left to speak.

**Agenda (multi-party).**
- **Compact** (Factor Halven Orys, or his successor if Chain 2 removed him) — wants the stone recovered for auction before Caervorn or Gravenguard claims it.
- **Gravenguard** (Commander Drest, via Kesh) — wants the stone *unmade*, per Ardweld funerary logic.
- **Ashen thread (hidden)** — wants the stone to keep speaking. One caravan member is already Dream-Walked.

**Quest-giver:** *Factor's Courier Rhena Stell* — mid-30s, Compact tattooed-forearms, speaks in ledger terms. Intercepts the player at the Bent Mare or Portmere dockside (whichever the player visits first after trigger conditions are met).

---

## Beat 1 — "A Contract Nobody Will Quote Twice"

Rhena offers the contract in writing only; she will not speak the caravan's destination aloud in the common room. Triple rate (600 gold + equipment stipend). The player is asked to sign a non-disclosure clause — uncommon for Rider work.

- **Stakes:** Accepting signals to the Compact that the player is willing to take Ashen-adjacent work. +100 Compact (Known tier). -50 Gravenguard if word reaches Kesh *before* the player tells him (see Beat 2 fork).
- **Reward:** 200 gold on-signing. Remaining on completion.
- **Branching choice:** Accept, Refuse, or Accept-and-inform-Gravenguard (requires Gravenguard Known+). The third option opens Beat 2b.

## Beat 2 — "Escort to the Glass Line" (escort sub-step)

**This beat is built to be ready to ship the day the escort-template blocklist is partially lifted.** Until then, it runs as a scripted travel montage (see "Fallback implementation" below).

The caravan is four wagons, six teamsters, and Rhena herself. They travel from Portmere → Starting Road → Eastmile → through a Caervorn-ignored byway → into the Reach. Four days of in-game travel. Four encounter rolls.

**Escort spec (for the day the blocklist lifts):**
- **Escort targets:** 6 teamsters (HP-light, DPS-zero), 1 Rhena (HP-medium, utility), 4 wagon objects (HP-heavy, immobile-on-damage-threshold).
- **Win condition:** ≥4 teamsters alive AND Rhena alive AND ≥2 wagons intact on arrival at the Glass Line waypoint.
- **Loss condition:** Rhena dead, OR fewer than 2 wagons intact, OR all teamsters dead.
- **Soft-loss condition (continues chain with penalty):** Rhena alive but ≤1 wagon intact — chain proceeds, but Beat 4's "extraction" option becomes impossible (no wagon to carry the stone out in).
- **Encounter mix (data-driven from `content/monsters.json` by biome):** Day 1 plains bandit skirmish (low), Day 2 Thornwood verge ambush IF Thornwood rep is Hostile (medium), Day 3 Gravenmarsh wight swarm on the crossing (medium-high), Day 4 Reach entry: a Dream-Walked teamster attempts to sabotage the wagons at night — this is the *hook* to Beat 3, not just an encounter.
- **Party scaling:** Player party (3) + 2 teamster combatants (auto-controlled, flee at 30% HP). Escort targets count as protected entities, not party members — no XP contribution.

**Fallback implementation (today):**
Skip the per-encounter escort sim. Run a travel-montage narrative: 4 rolls against a single escort-survival table (data-driven from `content/monsters.json` biome-keyed spawns). Result is a single severity tier (Clean / Bruised / Bloody / Catastrophic), which maps to the branch state Beat 3 reads from.

- **Stakes:** Narrative only at fallback; see spec above for true-escort stakes.
- **Reward on arrival (any non-Catastrophic):** +200 Compact. +100 if Gravenguard was pre-informed (they know the player kept word).

## Beat 2b — "Kesh's Shadow" (branch: pre-informed Gravenguard)

Kesh slips the player a sealed envelope before the caravan leaves Portmere. Inside: a single Ardweld un-naming glyph on parchment, and a note — *"If it speaks a name, put this on it. Then leave."*

- **Adds a new option to Beat 4's climax.** Does not alter Beat 2 mechanically.
- **Reward:** +150 Gravenguard. Silent. Kesh does not acknowledge the handoff in public for the rest of the arc.

## Beat 3 — "The Teamster Who Dreams"

On the third or fourth night (depending on Beat 2 severity tier), one of the caravan's teamsters — named **Old Farrow** (sixties, limps, was supposedly a Gravenguard deserter in his youth) — goes to the wagons in his sleep and chalks an Ashen sigil on each axle.

The player can:
- **Catch him and confront:** Farrow is lucid enough to explain. He's been Dream-contacted for weeks and thought he was dreaming it. He will come peacefully back to the camp and can be questioned. Reveals: the stone has been *named* already — it answers to "Soren-Who-Was-Summer," a name not in any known register. Kills the caravan's plausible deniability. Unlocks Beat 4 option "unmake."
- **Catch him silently and conceal:** Player wipes the sigils, Rhena never learns. Farrow finishes the trip. Beat 4 proceeds as if clean. The chain's hidden Ashen progression tick fires anyway at +1 (world-event consequence: Thenna Orys's conversion advances).
- **Kill Farrow:** -200 Compact (Rhena saw him as crew). +0 Gravenguard (they'd have preferred him alive). Closes the "unmake" option in Beat 4 unless Beat 2b happened.
- **Miss it entirely:** The sigils persist. At Beat 4, the stone responds differently — it knows the caravan is coming.

## Beat 4 — "The Glass That Remembers"

The caravan reaches the **Glass Line** — a ridge of fused sand where the Reach proper begins. A hundred yards in, a waist-high Ardweld naming-stone stands upright, untouched by wind, clean of ash. It is warm. When the player approaches within ten feet, it says, in no particular voice: *"You came. I thought you would."*

**Player options:**
- **Extract (Compact win):** Wagon-load it and haul it out. Requires ≥2 wagons (Beat 2 soft-loss forbids this). +500 Compact, +300 gold bonus. Long-tail: the stone is auctioned in Portmere six weeks later; a Golvari ambassador buys it; Chain arc resumes in a later Portmere-focused pass. **Ashen progression +2.**
- **Unmake (Gravenguard win):** Use Kesh's glyph from Beat 2b, OR Brother Velm's glyph from Chain 1 if the player retrieved it and has it on-person. Requires the player to *speak* the stone's true name back to it (learned in Beat 3 if Farrow was questioned lucidly). The stone cracks. The Reach goes cold for a mile. **+700 Gravenguard. -300 Compact** (contract failed). **Ashen progression -1** (the only chain-branch that actively sets back the Ashen tick).
- **Leave it (null ending):** Walk away. +0 all factions. The stone watches the player leave and says, audibly to all present: *"I will remember that you chose not to choose."* This line triggers a flag: every Ashen NPC in later chains recognizes the player on sight without introduction. No mechanical benefit or penalty — atmospheric.
- **Feed it (hidden/Ashen-aligned, requires Dream-contact flag on player):** The stone accepts a named offering (an NPC the player has traveled with, by name). This option is **content-locked until user approval** — see canon-deliberations Q4 below.

**Reputation summary:** +500 to +700 in chosen faction; -300 in the opposing; up to +300 gold; one world-event tick in either direction; one permanent flag (the stone's memory, if "Leave" was chosen).

**Success state:** Unlocks Ashen Reach as travel-safe for this arc (the Reach's ambient menace drops one tier in the zone around the Glass Line).

**Failure state:** Rhena dead or Farrow-killed badly handled → Compact blacklists the player from Reach contracts for the arc. Gravenguard still respects the attempt.

---

## Ashen progression ticks referenced

Beat 3 silent-cover → `ashen.installations.progress +1`
Beat 4 Extract → `ashen.installations.progress +2`
Beat 4 Unmake → `ashen.installations.progress -1`
Beat 4 Leave → flag only, no tick.

These ticks feed the world-events system described in `docs/design/world-events.md` and Q2 of `canon-deliberations.md` (the 60-day floor on Maren's re-emergence).

---

## Canon question surfaced by this chain

**Q4 (new, for user ruling):** Can a player actively *feed* an Ashen-aligned artifact by offering a named NPC? The "Feed it" option is spec'd above but content-locked. Conservative reading: **forbidden until explicit user approval**, same as the Q3 minors question. The machinery is written so the option can be dropped in with a single flag flip when ruled on.
