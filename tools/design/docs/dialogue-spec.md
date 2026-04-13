# Dialogue Spec (v1)

Schema: `dialogue-tree/v1`. Loaded by `FixtureLoader` (case-insensitive, comments
and trailing commas allowed) and validated by `dialogue-lint`.

## Top-level

| Field                | Type                     | Required | Notes |
|----------------------|--------------------------|----------|-------|
| `$schema`            | string                   | no       | `"dialogue-tree/v1"` for forward-compat. |
| `npcId`              | string                   | yes      | Stable id of the NPC owning this tree. |
| `roots`              | string[]                 | yes      | Entry-point node ids. One tree can have multiple roots (e.g. first-meet vs. repeat). |
| `startingReputation` | map<string, int>         | no       | Faction -> rep value assumed when the player arrives. Missing factions default to 0. |
| `nodes`              | node[]                   | yes      | All dialogue nodes in this tree. |

## Node

| Field       | Type              | Required | Notes |
|-------------|-------------------|----------|-------|
| `id`        | string            | yes      | Unique within the tree. |
| `text`      | string            | yes      | NPC's line. |
| `options`   | option[]          | no       | Player choices leaving this node. |
| `terminal`  | bool              | no       | If `true`, the conversation ends here. Non-terminal leaves with no options are warned. |
| `effects`   | map<string, int>  | no       | Reputation deltas applied on node entry, keyed by faction. Used by reachability analysis to predict whether downstream rep-max gates remain satisfiable. |

## Option

| Field                    | Type          | Required | Notes |
|--------------------------|---------------|----------|-------|
| `text`                   | string        | yes      | The button/choice text shown to the player. |
| `next`                   | string        | no       | Next node id. Options with no `next` are warned (they "go nowhere"). |
| `requiresReputation`     | `RepReq`      | no       | Floor gate: player's rep with faction must be `>= min`. |
| `requiresReputationMax`  | `RepLimit`    | no       | Ceiling gate: player's rep with faction must be `<= max`. Used for low-rep / hostile / stranger branches. |

### `RepReq` / `RepLimit`

```json
{ "faction": "wytchwood", "min": 25 }
{ "faction": "wytchwood", "max": 10 }
```

If an option sets both `requiresReputation` and `requiresReputationMax` on the
same faction and `min > max`, the linter emits an **error** — the gate is
unsatisfiable.

## Lint rules

Errors (fail the tool):

- Blank node id.
- Duplicate node id.
- Root not found among nodes.
- Option `next` refers to an unknown node id.
- On the same option, `requiresReputation.min > requiresReputationMax.max` for the same faction.

Warnings (do not fail):

- Option with no `next` and whose parent is not terminal.
- Non-terminal leaf with no options (dead-end).
- Orphan nodes (unreachable from any root).
- `requiresReputation` gate that cannot be satisfied by the best reputation
  reachable at that node (starting rep + prior `effects` along any path).
- `requiresReputationMax` gate whose ceiling is exceeded by the worst
  reputation reachable at that node (typically a prior node applied a rep gain
  that pushes the player above the ceiling on every path).

## Example

```jsonc
{
  "$schema": "dialogue-tree/v1",
  "npcId": "harken",
  "roots": ["greet"],
  "startingReputation": { "wytchwood": 0 },
  "nodes": [
    {
      "id": "greet",
      "text": "Harken eyes you. 'What brings you to the Wytchwood?'",
      "options": [
        {
          "text": "Who are you, old timer?",
          "next": "stranger-reply",
          "requiresReputationMax": { "faction": "wytchwood", "max": 10 }
        },
        {
          "text": "I'm a friend of the wood.",
          "next": "insider",
          "requiresReputation": { "faction": "wytchwood", "min": 25 }
        }
      ]
    },
    { "id": "stranger-reply", "text": "'Call me Harken. Mind your step.'", "terminal": true },
    { "id": "insider", "text": "'Ah — one of ours.'", "terminal": true }
  ]
}
```
