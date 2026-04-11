# Reputation System

## Overview

Reputation is how the world decides who you are. It gates quest complexity, merchant access, NPC trust, companion availability, and portal unlock chains. It is earned through action and lost through action. It cannot be purchased.

---

## Tier Definitions

| Tier | Name | Points Range | NPC Behavior |
|------|------|-------------|--------------|
| -2 | **Hostile** | Below -500 | Attacked on sight in faction territory |
| -1 | **Wary** | -499 to -1 | No quests, merchants price-gouge 40%, members avoid the player |
| 0 | **Unknown** | 0 | Default start. Basic civil interaction. No special access. |
| 1 | **Known** | 1–999 | Fetch quests, basic merchant access, faction gossip available |
| 2 | **Trusted** | 1000–2999 | Named quests, merchant discounts, faction safe houses |
| 3 | **Honored** | 3000–5999 | Complex quest chains, faction abilities/spells, inner circle access |
| 4 | **Bound** | 6000+ | Wyrd-level quests, deepest lore, faction artifacts, mentor access |

**Tier thresholds are not displayed directly.** The player sees their reputation tier and a qualitative sense of direction ("rising", "steady", "slipping") but not the exact number. The haze is intentional — it makes reputation feel like a real relationship, not a progress bar.

---

## Earning Reputation

### Quest Completion
- Quest difficulty scales with current tier — Known-tier quests award Known-tier rep gains
- Quest failure awards nothing; abandonment is remembered (mild penalty applied)
- Some quests offer faction choice: which faction gets the credit for an outcome

### Material Donations
- Every faction has materials they value
- Donating above a minimum threshold grants reputation
- Diminishing returns per session — bulk dumping is less efficient than regular contributions

### Combat Support
- Defending faction members from attack
- Clearing hostile forces from faction territory
- Escorting faction NPCs

### Wyrd-Aligned Choices
- Choices that align with a faction's values give bonus reputation
- The player doesn't always know which choices qualify — alignment is discovered through play

### Passive Standing
- Active Honored/Bound players accumulate tiny passive reputation gains with factions they're regularly seen with
- "Regularly seen with" means: completing quests, using their merchant, traveling their roads

---

## Losing Reputation

### Direct Actions
- Killing faction members (always significant)
- Working against faction interests (quest choices, side-taking in conflicts)
- Breaking faction-specific oaths
- Being caught using magic in Caervorn territory (as a mage with Caervorn rep)

### Neglect (Honored and Bound only)
- Going extended in-game time without faction interaction causes slow drift back toward Trusted
- This represents: people forget, politics shift, the player is no longer current
- Drift is slow — designed to be noticed before it becomes a problem, not to punish absence

### Faction Tension Costs
- Some rep gains automatically cost another faction points
- See Faction Tension Web below

---

## Faction Tension Web

Some factions are in direct tension. Gaining reputation with one automatically affects another:

### Direct Tensions

**Thornwood Covens ↔ House Caervorn**
- Every Thornwood tier gained costs 1 Caervorn tier
- Every Caervorn tier gained costs 1 Thornwood tier
- *Exception*: The late-game reconciliation quest chain can break this link — but requires Honored in both simultaneously, which means the player navigated the tension carefully rather than committing to one side

**Golvari ↔ Gravenguard**
- Gaining Golvari Trusted costs Gravenguard Known
- Gaining Golvari Honored costs Gravenguard Trusted
- *Exception*: The mid-game Reconciliation Quest (*The Ambassador's Answer*) allows both to be maintained at Honored simultaneously

**Fairgean ↔ Emerald Compact**
- Competitive tension — less severe than the above
- Gaining Fairgean Trusted reduces Compact prices advantage by half
- Gaining Compact Honored reduces Fairgean's initial hostility rate

### Ashen Court Special Case
- Positive Ashen Court reputation is visible to all other factions
- Reaching Ashen Court Known causes mild suspicion from Thornwood, Gravenguard
- Reaching Ashen Court Trusted causes active distrust from Thornwood, Gravenguard, Compact
- The world notices. NPCs comment. Some doors close.

---

## Reputation and Quest Gates

### Quest Quality by Tier

| Tier | Quest Type | Example |
|------|-----------|---------|
| Unknown | Basic utility | "Deliver this package to Portmere" |
| Known | Local problem | "Bandits have been raiding our supply road — deal with it" |
| Trusted | Named story quest | "The Compact Factor in Veldann is dead. Officially: fever. Unofficially: find out" |
| Honored | Faction-changing | "Aldric Caervorn is moving on the Thornwood. We need you to ensure a specific meeting doesn't happen" |
| Bound | Wyrd-level | "The Lost Expedition found something. Commander Drest will only tell you. It changes everything." |

### Quest Complexity
- Unknown quests have no story consequence. They are transactions.
- Known quests begin to have NPCs who remember the player
- Trusted quests have branching outcomes — how you complete them matters
- Honored quests have faction-wide consequences — other factions hear what happened
- Bound quests alter the world state permanently and visibly

### World-Unique Reputation
Each world beyond Aeldran has its own reputation tracks with its own factions. Aeldran reputation **does not transfer** to other worlds directly.

However:
- High Aeldran faction rep can give a starting advantage in related world factions (Thornwood rep → slight starting bonus with Wyrd-Paths Anchored faction)
- Actions in other worlds can affect Aeldran faction rep if news travels (Honored+ factions have intelligence networks)

---

## AI Player Reputation

AI players in the world navigate the same reputation system:

- They pursue quest chains, build faction relationships, and compete for quest slots
- If an AI player reaches Honored with a faction first, certain quests in that faction's chain become "taken" until that player advances or fails
- AI players can be neutral, friendly, or hostile to the human player depending on their faction alignment
- An AI player Bound to House Caervorn and encountering a Thornwood Honored human player will react accordingly
- AI players can form temporary alliances on specific quests if reputation and faction alignment support it

---

## Placation

When reputation drops below a threshold (Wary or Hostile), standard quests cannot restore it. Placation requires:

1. **Acknowledgment**: Find the faction NPC who was wronged or witnessed the offense. Not always easy.
2. **Offering**: A specific material, action, or service the faction values — faction-specific, not generic.
3. **Time**: Some placation quests have a cooldown. You cannot rush the mending of a relationship.
4. **Consequence**: Placated reputation returns to 0 (Unknown), not to the previous tier. You rebuild from there.

**Exception**: Killing a named faction NPC creates a permanent reputation floor — that faction will not advance you above Trusted regardless of subsequent actions, unless a specific Bound quest chain from another faction intervenes as mediator.
