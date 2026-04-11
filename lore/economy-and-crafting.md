# Economy and Crafting

## The Engine

The game's economy is a **resource engine that requires active feeding**. Automation accelerates processing but never replaces acquisition. You cannot sit at your base and grow. You must be on the road.

```
Road → Loot/Quest → Resources → Automation → Crafting → Advancement → Better Road
                        ↓                                      ↓
                  Feed NPCs/Golems                    Stronger Companions
                        ↓                                      ↓
                  Base Produces                       Harder Quests
                        ↑                                      ↓
                  Back on Road ←─────────────────────────────┘
```

Neglect the road: automation starves, production stops, NPCs leave, research halts.
Neglect the base: loot piles up unprocessed, opportunities are missed, no craft advantage.

---

## Resources

### Tiers

| Tier | Availability | Examples |
|------|-------------|---------|
| **Common** | Aeldran surface | Iron ore, timber, herbs, beast hides, basic reagents |
| **Uncommon** | Deep ruins, faction merchants | Dravenite dust, rare herbs, monster components |
| **Rare** | World-specific | Tidal Iron (Deep), Deepstone (Deeps), Sundering Shards (Remnant) |
| **Legendary** | Quest reward, boss drop | Wyrd-Thread, Ardweld Alloy, Waking Stone |

Resources are **not guaranteed per run**. Loot tables have randomness. Some resources only drop from specific monster types, only in specific world areas, only at certain times (in-world time cycles matter in the Deep and the Wyrd-Paths).

### Resource Decay

Organic reagents (herbs, monster components, certain Wildfolk-adjacent materials) have a shelf life. Crafting with aged components produces lower Workmanship output. Storage solutions (Herb Tender, preservation wards) extend shelf life — they don't eliminate decay.

---

## Crafting System (Asheron's Call Architecture)

### Workmanship

Every crafted item and every component has a **Workmanship score** (1–10).

- Components are salvaged from looted items or gathered directly
- Salvage skill determines what you recover and its Workmanship score
- Higher Workmanship components produce higher Workmanship items
- Workmanship affects: durability, base stats, enchantment capacity

A masterwork sword (W9) from a skilled crafter is meaningfully better than a functional sword (W5) from a beginner. The gap matters.

### The Combine System

Crafting is combination: component + component (+ optional taper) → result.

- Items are combined in a crafting interface
- No recipe book tells you exact ratios
- You know the ingredients needed (learned through experimentation, quest rewards, NPC teaching)
- You do **not** know the exact quantities — these are seeded per player

### Per-Player Recipe Seeds

At character creation, each player receives a hidden **crafting seed**. This seed shifts:
- Ingredient ratios within a band (±20% of baseline)
- Yield quantities (±15%)
- Occasional "resonance windows" — specific non-standard combinations that work for this player and no other

Consequences:
- Players cannot share exact recipes — "I use 3 iron shards and 1 fire reagent" may not work for another player
- Forums are useless for exact quantities
- Players must experiment within their own seed space
- Discovering a resonance window produces a unique item variant — named for the player, logged in the world

### Failure States

Failed combines do not simply fail. Results:
- **Near-miss** (70%): Produces a degraded version of the intended item — usable but lower Workmanship
- **Unexpected result** (20%): Produces something different entirely. Sometimes useful. Sometimes not.
- **Component loss** (8%): Components are consumed with no output — genuine loss, not common
- **Discovery** (2%): Produces something new that wasn't in any known recipe — logged as a first discovery if no player has found it before

---

## The Taper System (Magical Imbuing)

Tapers are magical preparation materials that add elemental properties to crafted items during the combine process.

### Taper Types

| Taper | Source | Magical Effect |
|-------|--------|---------------|
| **Shaping Taper** | Crafted from Wildfolk essence + reagents | Adds Shaping elemental property to item |
| **Unmaking Taper** | Rare drop (Ashen Court enemies, Remnant) | Adds Unmaking property — dangerous to craft with |
| **Wyrd Taper** | Quest reward only, non-tradeable | Adds a fate-touched property unique to the crafter |
| **Ardweld Taper** | Remnant world drops | Predecessor magic — effect is powerful but unpredictable |
| **Deep Taper** | Golvari Deeps crafting masters | Earth/Fire hybrid property — stable, strong |
| **Tide Taper** | Fairgean Deep merchants | Water property with pressure-resistance bonus |

### Taper Quality

Each taper type comes in three quality levels:
- **Pristine**: Full intended effect, no warp risk
- **Flawed**: 70% effect, minor instability in the output
- **Spent**: 40% effect, higher chance of unexpected result

Taper quality degrades with age if not stored properly.

### Imbuing Formula

```
Item Workmanship × Taper Quality × Crafting Skill → Magical Output Quality
```

A W9 item + Pristine Shaping Taper + Crafting 8 produces exceptional magical gear.
A W4 item + Spent Taper + Crafting 3 produces something functional but unremarkable.

Stacking multiple tapers in a single combine is possible at higher crafting skill. Interactions between taper types are not always predictable.

---

## Automation Assets

Automation is force multiplication. It processes what you bring home, manages the mundane, and creates passive income. It does **not** replace you — it all requires upkeep.

### Base Assets

| Asset | Function | Upkeep Cost |
|-------|----------|-------------|
| **Sorting Engine** | Processes raw loot — salvages, categorizes, routes to storage | Dravenite dust (weekly) |
| **Herb Tender** | Maintains reagent garden, slows decay, produces basic herbs | Water, soil components (daily) |
| **Scribe Ward** | Copies scrolls, advances research passively (slow) | Ink, candles, quiet (no combat nearby) |
| **Trade Golem** | Manages buy/sell orders at Compact markets | Gold (weekly), maintenance parts |
| **Guard Ward** | Defends base from NPC thieves and faction raiders | Charged focus stones (weekly) |
| **Taper Refinery** | Converts raw components into usable tapers (upgradeable) | Component stock + Weave charge (player provides) |

### Automation Tiers

Assets upgrade through Ardweld research. A Tier 1 Sorting Engine handles basic salvage. A Tier 3 Sorting Engine (requires Ardweld Remnant research) can identify Workmanship scores, route components by taper compatibility, and alert the player to rare finds.

### Hired NPCs

Hired NPCs perform tasks that golems cannot — social, faction-specific, and judgment-based work.

| NPC Type | Source | Function |
|----------|--------|----------|
| **Thornwood Apprentice** | Thornwood Trusted | Research assistance — speeds Aether and Water research |
| **Golvari Smith** | Golvari Honored | Crafting assistance — improves Workmanship output |
| **Compact Factor** | Compact Trusted | Trade management — better prices, market intelligence |
| **Gravenguard Scout** | Gravenguard Known | Base defense, ruin mapping |
| **Fairgean Diver** | Fairgean Trusted | Retrieves specific undersea materials passively |

**NPC personality matters**: A Thornwood Apprentice will not work efficiently if the player's Thornwood rep drops. A Golvari Smith will leave if the player completes significant anti-Golvari quests. NPCs are not equipment — they are people with conditions.

**NPC upkeep**: Coin (all NPCs), faction rep maintenance (most NPCs), and periodic interaction — completely ignoring an NPC for extended periods reduces their efficiency before they eventually leave.

---

## The Research System

Research is personal magical advancement. It requires: time, reagents, texts/scrolls, and often a mentor NPC.

### Research Mechanics

- Research runs passively (Scribe Ward speeds this)
- The player must initiate each research project and provide materials
- Some research requires in-world experimentation — go find the thing, use it, report back
- Research failure is possible — consumes materials, provides a partial result or a clue

### Research Tree Structure (Summary)

```
Tier 1 (Available at start)
├── Element Basics (primary affinity)
├── Weave Management
├── Focus Stone Use
└── Salvage Fundamentals

Tier 2 (Known reputation with any magic faction)
├── Secondary Affinity Introduction
├── Wildfolk Communication
├── Coven-Working Basics
├── Intermediate Crafting
└── Base Asset: Tier 1 unlocks

Tier 3 (Trusted with Thornwood or Gravenguard)
├── Deep Element Expression
├── Wyrd-Sight
├── Advanced Golem-Craft
├── Taper Crafting
└── Portal Theory (introduces portal system)

Tier 4 (Honored with two factions)
├── Cross-Element Working
├── Ardweld Technique Fragments (requires Remnant access)
├── Bound Shade binding
├── Advanced Automation (Tier 2 base assets)
└── Dream-Walking Defense (passive — reduces Ashen Court dream attacks)

Tier 5 (Bound with one faction + specific quest unlock)
├── Aether/Unmaking Schools (forbidden — special unlock required)
├── Soul-Touch Defense (passive)
├── Lost Ardweld Techniques (full)
└── Advanced Automation (Tier 3 base assets)

Tier 6 (Twice-Born only — if applicable)
└── Twice-Born Techniques (unique to polarity combination)
```

---

## Economy Flow: A Typical Session

1. **Depart base**: Automation is running. Trade Golem has orders placed. Herb Tender is tending.
2. **Travel to quest zone**: Encounter roll — possible resource nodes, random encounters, faction events.
3. **Quest/dungeon**: Loot drops (randomized within loot table). Quest reward (materials + reputation).
4. **Return**: Sorting Engine processes loot. New components in storage.
5. **Craft**: Use session's haul + stored components to advance a crafting goal.
6. **Research**: Check passive research progress. Initiate next project. Provide materials.
7. **Automation upkeep**: Recharge Guard Ward. Resupply Herb Tender. Check NPC morale.
8. **Next quest**: Better equipped, slightly higher rep, one layer closer on a companion.

Nothing in this loop is skippable without consequence. Nothing is grinding for its own sake — every step feeds the next.
