# Tapers Content Proposal — First `content/tapers.json`

**Status:** draft. Primes the next content-migration pass.
**Scope:** enum → JSON migration for `TaperType` and (optionally) `TaperQuality`.
**Source of truth (current):** `src/FirstMud.Domain/Enums/TaperType.cs`.

## Current inventory (from enum)

`TaperType` — 6 entries:

| Id | Numeric | Canonical biome/source (inferred from lore) |
|----|--------:|---------------------------------------------|
| `shaping`  | 1 | Generalist taper; the "workshop default." Used for Shaping-school recipes. |
| `unmaking` | 2 | Destructive reshaping; paired with Unmaking-school in `magic-system.md`. |
| `wyrd`     | 3 | Wyrd-aligned taper; scarce, Ashen-adjacent. |
| `ardweld`  | 4 | Ardweld-era artifact-taper; pre-Sundering, only recovered. |
| `deep`     | 5 | Deep-zone / Gravenmarsh taper; chthonic. |
| `tide`     | 6 | Fairgean / Drowned Coast taper; water-coupled. |

`TaperQuality` — 4 entries (Pristine, Fine, Common, Cracked — from code).

Tapers are referenced by `Recipe.RequiredTaperType` (nullable). In
`content/recipes.json` today all entries have `"requiredTaperType": null`,
so the migration lands cheap — no existing fixture data needs rewriting.

## Proposed JSON shape

```json
{
  "$schema": "./schemas/tapers.schema.json",
  "tapers": [
    {
      "id": "shaping",
      "name": "Shaping Taper",
      "school": "Shaping",
      "tier": 0,
      "description": "The generalist workshop taper. Paired with low-skill recipes.",
      "origin": { "source": "crafted", "zone": null },
      "rarity": "common",
      "aligned": false
    },
    {
      "id": "unmaking",
      "name": "Unmaking Taper",
      "school": "Unmaking",
      "tier": 1,
      "description": "Resolves what should not hold. Scorches the caster.",
      "origin": { "source": "crafted", "zone": null },
      "rarity": "uncommon",
      "aligned": false
    },
    {
      "id": "wyrd",
      "name": "Wyrd Taper",
      "school": "Wyrd",
      "tier": 2,
      "description": "Fringe-woven, drawn from Wyrd seams. Risks Ashen contamination.",
      "origin": { "source": "foraged", "zone": "aeldran-9-maw-borderlands" },
      "rarity": "rare",
      "aligned": true,
      "alignmentHazard": "ashen"
    },
    {
      "id": "ardweld",
      "name": "Ardweld Taper",
      "school": "Ardweld",
      "tier": 3,
      "description": "Recovered, never made. Pre-Sundering artifact-grade.",
      "origin": { "source": "recovered", "zone": "aeldran-6-ashen-reach" },
      "rarity": "legendary",
      "aligned": true,
      "alignmentHazard": "ardweld-remnant"
    },
    {
      "id": "deep",
      "name": "Deep Taper",
      "school": "Deep",
      "tier": 2,
      "description": "Gravenmarsh bog-wood, aged under cold water for a century.",
      "origin": { "source": "foraged", "zone": "aeldran-4-gravenmarsh" },
      "rarity": "rare",
      "aligned": false
    },
    {
      "id": "tide",
      "name": "Tide Taper",
      "school": "Tide",
      "tier": 2,
      "description": "Fairgean-gift or Drowned-Coast find. Moody in dry hands.",
      "origin": { "source": "foraged", "zone": "aeldran-5-drowned-coast" },
      "rarity": "rare",
      "aligned": true,
      "alignmentHazard": "fairgean-debt"
    }
  ],
  "qualities": [
    { "id": "pristine", "name": "Pristine", "multiplier": 1.25 },
    { "id": "fine",     "name": "Fine",     "multiplier": 1.10 },
    { "id": "common",   "name": "Common",   "multiplier": 1.00 },
    { "id": "cracked",  "name": "Cracked",  "multiplier": 0.80 }
  ]
}
```

Design notes:
- `id` is lowercase hyphenated to match `content/monsters.json` and
  `content/zones.json` conventions.
- `school` is a *string* matching the enum symbol; loader can map string
  → enum via standard JSON enum conversion.
- `tier` keyed 0–3 parallels `content/monsters.json` tier column. Tier
  governs which recipes can consume it (defers to recipe taper
  requirement).
- `origin.source` ∈ {`crafted`, `foraged`, `recovered`, `gifted`}. Lets
  herbology/resource pipeline pull the right spawn context.
- `aligned: true` + `alignmentHazard` flags the three "risky" tapers —
  Wyrd (Ashen drift), Ardweld (Remnant flag), Tide (Fairgean debt).
  Consumers use this to gate high-rep / faction-cost effects.

## Migration scope

**Affected files:**
- `src/FirstMud.Domain/Enums/TaperType.cs` — keep; serves as strong-typed
  backstop. Loader maps JSON id → enum.
- `src/FirstMud.Domain/Enums/TaperQuality.cs` — same.
- `src/FirstMud.Application.Content/ContentProvider` (or equivalent) —
  add `Tapers` IReadOnlyList accessor, mirror `GetMonster(id)` pattern.
- `content/schemas/` — new `tapers.schema.json` validating the shape.
- `content/recipes.json` — no data change today (all null).
- Tests: round-trip test `TaperContentLoaderTests` — 6 entries, 4
  qualities, all enum-mapped.

**Not affected:**
- DB schema (tapers live only in content, same as monsters today).
- Any combat code (tapers are crafting-only).

## Test matrix

1. Load `tapers.json` → verify 6 entries, all enum-map to `TaperType`.
2. Each aligned taper's `alignmentHazard` resolves to a known faction/
   hazard string (lint).
3. Origin `zone` (when non-null) exists in `content/zones.json`.
4. Recipe lookup: given a recipe with `requiredTaperType = "wyrd"`,
   consumer can resolve the taper's tier + alignment without a DB hit.
5. Quality multipliers round-trip numerically (double precision).

## Open questions for the user

- Is a **seventh taper** warranted? Canon has hinted at a "Sealed" /
  Compact-Cities taper used by the Emerald Compact's commercial
  artificers. Not in the current enum — if the user confirms, adding it
  is a one-line enum + JSON addition.
- Should `qualities` live in a separate `taper-qualities.json`, or
  inline as above? Inline is simpler; split is extensible. Recommend
  inline until there are >10 qualities.
- Is `Shaping` tier 0 correct? It's the default but feels plot-light.
  Could promote to tier 1 with a new "rough" taper at tier 0.

## Hand-off

Promote to `content/tapers.json` on the day the content-migration pass
runs. No fixtures reference tapers yet, so no fixture edits needed.
