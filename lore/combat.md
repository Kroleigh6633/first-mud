# Combat System

## Overview

Combat is **turn-based and party-based**, drawing from Summoners War's elemental skill matrix. The player fields a party of up to 3 companions alongside their own character. Encounters are initiated on the world map or triggered in dungeons.

The player is always in the party. They cannot sit out a fight.

---

## Party Composition

| Slot | Occupant |
|------|----------|
| 1 | Player character (always) |
| 2 | Companion (any type) |
| 3 | Companion (any type) |
| 4 | Companion (any type) |

Companions not in active slots wait at base or travel passively. Active slot companions gain usage toward layer advancement in every combat. Passive companions do not.

---

## Turn Structure

Combat is turn-based with a **speed-modified initiative system**:
- Each character and enemy has a Speed stat
- Turn order is determined by Speed + a small random variance (±5%)
- Faster characters act more often in longer fights — not just sooner

On each turn, the acting character can:
1. **Use a skill** (costs AP — see below)
2. **Attack** (free action, basic damage)
3. **Use an item** (costs AP, 1 per turn limit)
4. **Defend** (reduces incoming damage next hit, free action)

---

## Action Points (AP)

Every character has an **AP pool** that regenerates each turn at a rate modified by their stats.

- Skills have varying AP costs — basic skills are cheap, powerful skills are expensive
- AP does not carry over fully between turns (partial carry-over based on a passive stat)
- Running out of AP forces basic attacks only
- Some companion abilities passively generate AP for others (support builds)

---

## Elemental Matrix

Every character, companion, monster, and many abilities have an **elemental alignment**. Element vs element interactions apply:

| Attacker Element | Strong Against | Weak Against |
|-----------------|----------------|--------------|
| Fire | Earth | Water |
| Water | Fire | Air |
| Earth | Air | Fire |
| Air | Water | Earth |
| Aether | All (slightly) | All (slightly) |

**Strong Against**: 150% damage, may apply bonus effect
**Neutral**: 100% damage
**Weak Against**: 65% damage

Aether deals 110% to all elements and takes 90% from all elements — powerful but not dominant.

**Polarity also applies**:
- Shaping abilities deal full damage to physical/mundane targets
- Unmaking abilities deal bonus damage to Shaping-warded targets (ward bypass)
- Twice-Born abilities can choose polarity per skill use

---

## Status Effects

Status effects stack (up to 3 of the same type). Duration is in turns.

| Effect | Source Element | Effect |
|--------|---------------|--------|
| **Burning** | Fire | Damage over time, removes buffs on target |
| **Soaked** | Water | Increases Air/Water damage against target, slows speed |
| **Rooted** | Earth | Prevents position change abilities, reduces speed to 0 |
| **Confused** | Air | Target acts randomly on their turn |
| **Wyrd-Touched** | Aether | Target's next action has a fate-modified result |
| **Soul-Touched** | Aether/Unmaking | Target loses one action — they stand, unmoving |
| **Warded** | Any Shaping | Damage reduction (element-specific) |
| **Unmade** | Any Unmaking | Warding stripped, next hit double damage |

---

## Skills

Each character/companion has skill slots based on their layer:

- **Layer 1**: 2 skills
- **Layer 2**: 3 skills
- **Layer 3**: 4 skills + passive
- **Layer 4**: 4 skills + 2 passives
- **Layer 5**: 5 skills + 2 passives
- **Layer 6**: 5 skills + 3 passives + signature move

Skills have:
- AP cost
- Cooldown (in turns)
- Target type (single, cleave, all enemies, self, single ally, all allies)
- Elemental alignment
- Polarity (Shaping, Unmaking, or neutral)
- Effect (damage, heal, buff, debuff, status)

### Player Skills (Magic-Based)
Player skills are drawn from their magical research. A Fire/Shaping mage has:
- Forge-bolt (damage, single target, Fire/Shaping)
- Warmth Ward (buff, all allies, Fire/Shaping — reduce incoming damage)
- Battle-Fire (buff, self, Fire/Shaping — increase damage for 3 turns)

As research advances, new skill slots unlock. Tier 4+ research unlocks signature-class skills.

---

## Dungeons

Dungeons are **procedurally generated** tile-based environments with:
- Room templates (selected from a pool based on dungeon type and world)
- Room connections (randomized layout per run)
- Encounter rooms, treasure rooms, puzzle rooms, boss room
- Persistent state within a session — explored rooms stay cleared
- Boss rooms lock until all encounter rooms in a wing are cleared

### Dungeon Types

| Type | World | Character |
|------|-------|-----------|
| Ardweld Ruin | Aeldran | Automata enemies, trapped rooms, artifact rewards |
| Thornwood Depths | Aeldran | Fae creatures, poison/illusion heavy, coven-related lore |
| Caervorn Fortress | Aeldran | Human enemies, no magic traps, political lore |
| Golvari Warrens | Golvari Deeps | Ambush-focused, Earth element heavy, mining tie-ins |
| Drowned Ruin | Fairgean Deep | Water element, pressure mechanics, Fairgean lore |
| Remnant Pocket | Ardweld Remnant | Sundering-era enemies, Unmaking dominant rules |
| Dream Chamber | The Dream | Nightmare enemies, Dream-Walking mechanics, Ashen Court agents |

### Dungeon Scaling
Dungeons scale to player level within a band — a Trusted-tier player encounters Trusted-tier challenges. However, there are **fixed-tier dungeons** that do not scale — an Ardweld Ruin near the Ashen Reach is always brutal regardless of player level. These are discoverable and avoidable, but the loot tables reflect the risk.

---

## Boss Encounters

Boss enemies have:
- Named identity with lore relevance
- Phase system (behavior changes at 50% HP and 25% HP)
- Unique mechanics not found in regular combat
- A specific weakness that can be discovered through lore, NPC hints, or in-fight experimentation

Bosses do not respawn immediately. Most have a respawn timer of multiple in-game days. Some are single-instance — once killed, they are gone. Their location is sometimes filled by a different enemy, sometimes left empty.

---

## Death

Player death results in:
- Loss of unbanked resources carried that session
- Companions survive but may be shaken (temporary layer drift acceleration)
- Return to last visited safe point (inn, base, faction waypoint)
- A "death record" — visible in the world lore as a minor note, flavor only

There is no permanent death. This is an adventure game, not a roguelike. The consequences are real but not game-ending.

Companions *can* die permanently — this is extremely rare, requires very specific circumstances (a boss-level ability hitting a low-health companion with no protective gear), and the game warns the player clearly before the final hit. There is a recovery quest for recently-dead companions in the Wyrd-Paths, available within a time window.
