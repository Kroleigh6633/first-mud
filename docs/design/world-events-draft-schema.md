# World-Events Draft Schema (Strawman)

Forward-looking spec shape for `content/events.json`. This is a **draft
proposal** for a future content-pipeline migration agent — not a tool
schema, not a binding contract. Three fixtures accompany this doc under
`tools/design/fixtures/event-*.json` so the shape is concrete.

Source material: `docs/design/world-events.md` (12 catalogued events).
Three of those events are translated here as exemplars: #1 The Red Market
(economic), #4 The Dream-Installation Tick (faction-long-arc), #8 The
Seed-Conversion Attempt (faction-long-arc, player-interceptable).

## Top-level shape

```jsonc
{
  "$schema": "world-event/v1",
  "id": "string",                     // unique slug
  "title": "string",                  // designer-facing
  "family": "economic|political|faction-long-arc|encounter|weather",
  "priority": 1,                      // 1=highest. Scheduler resolves conflicts.
  "trigger": { ... },                 // see Trigger types
  "duration": { "ticks": 6 },         // in-game days; null = permanent
  "concurrencyKey": "string?",        // events sharing key cannot stack
  "effects": {
    "worldState": [ ... ],            // see Effect entries
    "npcDialogueFlags": [ ... ],
    "zoneAmbient": [ ... ],
    "shopStock": [ ... ],
    "npcAvailability": [ ... ]
  },
  "onResolve": [ ... ],               // cleanup effects when duration expires
  "exclusiveWith": [ "event-id" ]     // mutually-exclusive events (optional)
}
```

## Trigger types

The scheduler evaluates triggers once per in-game day at dawn. Five
trigger families:

```jsonc
// 1. cron — fixed calendar cadence
{ "type": "cron", "everyDays": 7, "atDay": null }

// 2. game-tick — relative offset since last fire (or world start)
{ "type": "gameTick", "minDaysSinceLast": 14 }

// 3. rep-threshold — fires when player crosses a faction tier boundary
{ "type": "repThreshold", "faction": "thornwood", "minTier": "trusted", "withinDays": 10 }

// 4. quest-completion — fires N days after a quest beat resolves
{ "type": "questCompletion", "questId": "lost-expedition", "beatId": "beat-4",
  "anyBranch": true, "delayDays": 60 }

// 5. compound — AND/OR composition of the above
{ "type": "all", "of": [ {...}, {...} ] }
{ "type": "any", "of": [ {...}, {...} ] }
```

A trigger may also include `"and": { ... }` to mix a primary condition
with secondary gates (e.g. cron AND a player-presence requirement).

## Effect entries

```jsonc
// worldState: durable flags / numeric drift
{ "type": "setFlag", "key": "market.red", "value": true }
{ "type": "addReputationDrift", "faction": "compact", "perTickDelta": -2 }
{ "type": "shopPriceMultiplier", "scope": "zone:eastmile", "category": "grain", "factor": 1.4 }

// npcDialogueFlags: drives dialogue tree gating
{ "type": "setNpcFlag", "npcId": "senna-orrick", "flag": "redmarket.line", "value": true }

// zoneAmbient: cosmetic + soft mechanical
{ "type": "setAmbient", "zoneId": "eastmile", "mood": "wary", "weatherOverride": null }

// shopStock: inventory drift
{ "type": "stockShift", "shopId": "bent-mare", "itemId": "stew",
  "stockDelta": 0, "priceDelta": 1 }

// npcAvailability: presence/absence in a zone
{ "type": "setPresence", "npcId": "halven-orys", "zoneId": "compact-house",
  "available": true, "schedule": "always" }
{ "type": "spawn", "npcId": "thenna-orys", "zoneId": "compact-house",
  "lifetimeTicks": 5, "voiceProfile": "dream-fragmented" }
```

## Cleanup (`onResolve`)

A list of effects that must fire when the event expires. The scheduler
runs them at the next dawn after `currentDay > firedDay + duration.ticks`.
Cleanup must be **idempotent** — if the player loaded an old save the
scheduler may run cleanup twice. Use `clearFlag`, `removeReputationDrift`,
`clearShopPriceMultiplier`, `clearAmbient`, `despawn`.

## Scheduler discipline

- Per `world-events.md`, **at most two events from different families
  may be active at once**. The scheduler evaluates priority (lower
  number wins) and `exclusiveWith` to resolve.
- Triggers that *miss* their window (player offline, conflicting event
  active) are **not** queued — they wait for next eligibility.
- Permanent events (`duration: null`) do not occupy a concurrency slot
  after their initial reveal day.

## Player-visibility note

Events with `"family": "faction-long-arc"` and `"playerInterceptable":
true` should surface a soft hint via at least one NPC line in the
trigger zone. The fixture for event #8 (Seed-Conversion) demonstrates
this with Auld Maerwyn's "There's a girl thinking" line — without that
hook the 5-day window is unwinnable for a player who hasn't been told.

## Open questions for content pipeline

1. Do `setFlag` keys in events share namespace with quest-chain flags? If
   yes, a collision audit is needed (event flag stomps quest flag).
2. Should `concurrencyKey` be auto-derived from `family`, or always
   author-specified? Strawman: optional override, defaults to family.
3. Is `repThreshold` evaluated on cross *or* on dwell? The fixture
   assumes cross (one-shot fire), with `withinDays` as a freshness gate.

## Hand-off

Three sample fixtures: `event-red-market.json`, `event-dream-tick.json`,
`event-seed-conversion.json`. A content-migration agent can validate this
schema with a future `event-lint` tool (not yet built) and then promote
all 12 events from `world-events.md` into a single `content/events.json`.
