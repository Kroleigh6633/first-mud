# World-Tick Events — Aeldran

A catalogue of world-tick events that can fire during idle play to keep
Aeldran *moving* when the player isn't driving a chain forward. These are
raw material for a future `content/events.json` migration.

**Shape of each event.** Each entry specifies:
- **Trigger condition (prose)** — what the world-state machine checks each tick.
- **Duration** — how long the event persists once fired.
- **World-state effect** — the durable consequences (prices, garrisons, roads, rep drift).
- **NPC-dialogue-flag effect** — which NPCs get new options, new opening lines,
  or withhold old ones while the event is live.

**Tick cadence (proposed):** world events evaluate once per in-game day at
dawn. At most **two** events from different families may be active at once;
the scheduler picks by priority (combat > political > economic > weather).

**Cross-references:** named NPCs from `npc-voices.md`, zones from
`aeldran-zones.md`, factions from `lore/factions.md`, reputation thresholds
from `lore/reputation.md`. No canon is contradicted.

---

## 1. **The Red Market** (economic)

- **Trigger:** 14+ in-game days without a raid event anywhere AND the player
  has Trusted+ with any single faction. Scarcity is the trigger; surplus
  NPCs price-up.
- **Duration:** 6 in-game days.
- **World-state effect:** Eastmile market raises grain by +40%, herbs by
  +60%. Gravenmarsh causeway toll +2 copper. Wytchwood poach enforcement
  halved (guards reassigned to grain escort).
- **NPC-dialogue-flag effect:**
  - Senna Orrick: "The road's hungry this week. Drinks stay the same, stew's a copper up, don't look at me like that."
  - Harken Vos: opens a new bounty line — "food-cart ambush, third this month."
  - Auld Maerwyn: offers barter-only, refuses coin.

## 2. **Rider Dust-Up at the Bent Mare** (political / social)

- **Trigger:** Two Riders from opposed factional sympathies lodge the same
  night at the Bent Mare. Computed as: ≥2 Rider NPCs within Eastmile node
  on a given tick with reputation-lean values of opposite sign > 500.
- **Duration:** 1 night.
- **World-state effect:** One random road-event (ambush/encounter) in
  adjacent zone is **skipped** for the next 3 days — the Riders handled it
  between drinks. Senna's till is short by 15 silver (she'll mention it).
- **NPC-dialogue-flag effect:**
  - Senna: "You missed the show. Two of yours. I mean, two like you. The broken stool is yours to replace, by the way."
  - Captain Hollemar: curt for a week. He heard about it.

## 3. **Caervorn Assay Sweep** (political / oppression)

- **Trigger:** Any player action that raises `thornwood` reputation into
  Trusted tier **while** that player has visited Marchkeep within the last
  10 days. Ewan Truce files a report.
- **Duration:** 10 in-game days.
- **World-state effect:** Every entry to Marchkeep requires an on-the-spot
  assay. 10% of random Thornwood-coded items on NPCs are confiscated
  (including Auld Maerwyn's nephew's ring — see cameo below). Thornwood
  ↔ Caervorn tension drifts +1 tick.
- **NPC-dialogue-flag effect:**
  - Captain Hollemar: "My orders are my orders, Rider. I recommend a different road this month."
  - Auld Maerwyn: "They took my nephew's ring. I will be cold about it. Tea?"
  - Ewan Truce (not yet voiced): cold, formal, never meets eyes.

## 4. **The Dream-Installation Tick** (faction-long-arc)

- **Trigger:** Every 20 in-game days **unconditionally** from world start.
  This is the Ashen Court's build-out cadence — it does not care what the
  player is doing. See `canon-deliberations.md` Q2: Maren's return is
  story-gated, but the infrastructure is not.
- **Duration:** 3 in-game days visible (the installation is "fresh" and
  detectable).
- **World-state effect:** One new Dream-relay node appears in a zone the
  player has not visited recently. Zones cycle in a fixed order: Eastmile,
  Thornwood Verge, Gravenmarsh, Marchkeep outskirts, Ironspire environs,
  Ashen Reach periphery. The node is a defaced shrine or milestone.
- **NPC-dialogue-flag effect:**
  - Brother Velm: mentions a "bad dream" if the node is in Eastmile.
  - Auld Maerwyn: sends the player to inspect ("Mabon's fur is up, which is *never* a compliment").
  - Rhianne Moss (post-chain): can *feel* the node if within 2 zones.

## 5. **Market-Day at Gravenmarsh Landing** (economic / opportunity)

- **Trigger:** Every 7 in-game days (fixed calendar).
- **Duration:** 1 day.
- **World-state effect:** Dravenite and bog-iron prices halved for 24 hours.
  Smuggler NPCs spawn at a 3x rate — the crowd is cover. Lieutenant Varn is
  always on duty this day (useful for players trying to *avoid* him).
- **NPC-dialogue-flag effect:**
  - Harken Vos: "Market day. I shall be here. I shall be, in point of fact, insufferable, because I will have had breakfast."
  - Unnamed factor (Halven Orys, Compact): appears on this day if not otherwise dead or arrested.

## 6. **Wildfolk Migration** (encounter / weather-adjacent)

- **Trigger:** First frost of the season (seasonal tick; fires once per year
  at day 200 by convention).
- **Duration:** 14 in-game days.
- **World-state effect:** Wildfolk encounters in the Thornwood Verge
  triple in frequency but are 70% non-hostile (they're passing through,
  not hunting). Wolves move down out of Thornwood into Eastmile farmland.
  Farmers lose livestock; bounty board at the Bent Mare swells with
  wolf-pelt requests (see `harken-wolf-pelts` fixture — this is the
  event that supplies it).
- **NPC-dialogue-flag effect:**
  - Senna Orrick: "The Autumn's in. You'll want the back-room chair."
  - Auld Maerwyn: "The wood is travelling. Don't *startle* it."

## 7. **Ashen Court Silence Breaks** (faction-long-arc, one-time)

- **Trigger:** Canon-deliberations Q2 conservative ruling — story trigger
  (Trusted+ with one of the Three, at least one Ashen thread touched) AND
  60-day floor since Chain 1 Beat 4 resolution.
- **Duration:** Permanent; this is the reveal tick that unlocks the Ashen
  Court arc.
- **World-state effect:** Maren re-emerges. Ashen-Reach access becomes
  possible for Trusted+-aligned players (conversion dialogue becomes
  available, not forced). All Dream-installation nodes become readable
  instead of merely detectable. Thornwood and Caervorn both announce
  closed-council sessions within the week.
- **NPC-dialogue-flag effect:**
  - *Every* voiced NPC gets a new opening line for 3 days ("Have you
    heard…"). Each voice's version follows `npc-voices.md` conventions
    strictly — Drest says "I have heard. Sit." Maerwyn says "Yes. Tea."
  - Harken Vos: shuts up entirely. This is the tell. Players who know him
    should notice.

## 8. **The Seed-Conversion Attempt** (faction-long-arc, player-interceptable)

- **Trigger:** 30 days after any player touches the Compact faction, AND
  canon-deliberations Q3 conservative ruling holds (target is adolescent).
- **Duration:** 5-day window.
- **World-state effect:** A named ward of a Compact house — **Thenna Orys**,
  17, Halven Orys's niece — begins receiving Dream-contact. She is
  traceable if a player spends time at the Compact house. If untouched at
  the 5-day window's end, she joins the Ashen Reach voluntarily (flagged
  `thenna.converted`) and appears as an Ashen agent in later threads.
  If intercepted, she can be placed in Thornwood hedge-school protection
  (+150 Thornwood, +50 Compact, -200 Ashen).
- **NPC-dialogue-flag effect:**
  - Halven Orys (if still alive): unusually generous. Something is on his mind.
  - Thenna herself (new NPC, voice TODO): speaks in half-remembered dream-phrases.
  - Auld Maerwyn: "There's a girl *thinking* in the Compact house. Thinking at the wrong frequency. Go listen."

## 9. **Ironspire Dispatch** (political / quest-seeding)

- **Trigger:** 3 days after Chain 3 Beat 4 resolves (any branch).
- **Duration:** 1 day (dispatch window).
- **World-state effect:** A sealed Caervorn dispatch rides to all Marchkeep-
  adjacent garrisons. Contents vary by Chain 3 branch:
  - comply-truthfully → strike order on the coven stone.
  - comply-falsely → reconnaissance-repeat order.
  - refuse → internal review order (Captain Hollemar is interviewed).
  - warn-then-comply → counter-intelligence order (Aldric hunts the leak).
- **NPC-dialogue-flag effect:**
  - Captain Hollemar: moody for a week. If the player caused his interview, refuses to share tea.

## 10. **The Merchant's Guild Audit** (economic / player-vs-rep)

- **Trigger:** Player has sold goods worth over 500 silver total in any
  single zone's market within 30 days, regardless of faction lean.
- **Duration:** 7 days.
- **World-state effect:** Compact factors audit the player's next sale.
  Legitimate → +25 Compact rep. Dodgy (smuggled/stolen items in inventory)
  → -100 Compact rep and a fine of 20% of the audit's sale value.
- **NPC-dialogue-flag effect:**
  - Compact NPCs generally: politer.
  - Any NPC who sold the player stolen goods: nervous, evasive, may flee the zone.

## 11. **First Frost Weather-Tick** (weather / pure world-color)

- **Trigger:** Seasonal, day 200.
- **Duration:** 30 days.
- **World-state effect:** Travel times on non-King's-Road routes +25%.
  Cold-weather gear gains mechanical value (when mechanics exist). Bogs
  in Gravenmarsh freeze unevenly — random causeway closures.
- **NPC-dialogue-flag effect:**
  - Senna: reports the weather as the road's mood ("the road's grumpy today").
  - Drest: more terse than usual; fewer meetings, shorter.
  - Harken Vos: complains about ink freezing. Does not stop writing.

## 12. **The Gorsewitch Circle Gathering** (faction / optional discovery)

- **Trigger:** Once per lunar cycle (every 28 in-game days), if the player
  has Known+ with Thornwood AND has visited the Thornwood Verge within 7
  days.
- **Duration:** 1 night.
- **World-state effect:** The Gorsewitch Circle holds an open gathering.
  Rhianne Moss (if alive, post-chain) attends. A rare Working is performed
  — player with Thornwood Trusted+ may witness, with Honored+ may contribute.
  Contribution grants a rotating minor benefit (depending on season: frost-
  ward, weave-sight, path-silence). Missed gatherings are missed forever.
- **NPC-dialogue-flag effect:**
  - Auld Maerwyn: the day *before* — "Come tomorrow. Or don't. Don't knock."
  - Rhianne: exists as a reachable NPC on that night only (unless post-chain
    other access is unlocked).

---

## Tick-state shape (forward-looking)

For a `content/events.json` migration, each entry needs:
```
{
  "id": "...",
  "family": "economic|political|faction-long-arc|encounter|weather",
  "priority": 1-5,
  "trigger": { "type": "...", ... },
  "duration": { "ticks": N },
  "effects": { "worldState": {...}, "dialogueFlags": {...} },
  "onResolve": [...]
}
```
Flags touched by these events that should be durable world-state (not
chain-local):
- `market.red` (1)
- `assaySweep.active` (3)
- `dreamNode.<zoneId>` (4)
- `marketDay.gravenlanding` (5)
- `wildfolk.migration` (6)
- `ashen.silence.broken` (7)
- `seedConversion.thenna.status` (8)
- `ironspire.dispatch.<branch>` (9)
- `compact.auditing` (10)
- `season.frost` (11)
- `gorsewitch.gathering.<month>` (12)
