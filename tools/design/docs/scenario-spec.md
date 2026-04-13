# Scenario (Quest-Chain) Spec — v1

Input format consumed by `scenario-player`. JSON, one fixture per file.
Companion to `dialogue-spec.md`.

## Top-level shape

```json
{
  "$schema": "quest-chain/v1",
  "id": "harken-wolf-pelts",
  "title": "Harken's Wolf Pelt Request",
  "description": "Optional flavor blurb shown on stdout.",
  "startState": { "...": "..." },
  "root": "intro",
  "allowUnderflow": false,
  "beats": [ { "...": "..." } ]
}
```

| field            | type     | required | notes                                                          |
|------------------|----------|----------|----------------------------------------------------------------|
| `id`             | string   | yes      | stable identifier; used in log filenames                        |
| `title`          | string   | yes      | human label                                                     |
| `description`    | string   | no       | flavor / design intent                                          |
| `startState`     | object   | yes      | initial flags / inventory / reputation                          |
| `root`           | string   | yes      | id of the first beat                                            |
| `allowUnderflow` | bool     | no       | default `false`. When `true`, removeItem underflow → warning   |
| `beats`          | array    | yes      | list of `Beat` objects                                          |

## `startState`

```json
{
  "player": "fresh-farmer",
  "flags": ["intro.complete"],
  "inventory": [ { "key": "gold", "amount": 50 } ],
  "reputation": { "wytchwood": 0, "gravenguard": 100 }
}
```

- `flags`: array of strings.
- `inventory`: array of `{ key, amount }` rows. Missing keys = stock 0.
- `reputation`: faction → integer map.

## `Beat`

```json
{
  "id": "intro",
  "description": "Harken explains he needs 3 wolf pelts.",
  "requires": { "flags": ["..."], "items": [], "reputation": {} },
  "terminal": false,
  "outcome": null,
  "choices": [ { "...": "..." } ]
}
```

| field         | type      | notes                                                        |
|---------------|-----------|--------------------------------------------------------------|
| `id`          | string    | unique inside the spec                                        |
| `description` | string    | shown in stdout + markdown log                                |
| `requires`    | Requires? | currently emits a *warning* if unmet (does not block entry)   |
| `terminal`    | bool      | when true, ends the run with `outcome`                        |
| `outcome`     | string?   | label written to `finalOutcome` when terminal                 |
| `choices`     | Choice[]  | omitted/empty + non-terminal = run ends with `dead-end`       |

## `Choice`

```json
{
  "id": "bribe-scribe",
  "text": "Bribe the night-scribe.",
  "next": "drest-reads",
  "requires": { "items": [ { "key": "gold", "amount": 25 } ] },
  "effects": [
    { "type": "removeItem", "key": "gold", "amount": 25 },
    { "type": "setFlag", "key": "scribe.bribed" }
  ]
}
```

| field      | type      | notes                                                        |
|------------|-----------|--------------------------------------------------------------|
| `id`       | string    | unique within the beat                                        |
| `text`     | string    | player-facing line                                            |
| `next`     | string?   | next beat id; absent → run ends with `choice-had-no-next`     |
| `requires` | Requires? | **gates traversal** — see "Choice gating" below               |
| `effects`  | Effect[]  | applied in order on selection                                 |

### Choice gating

If a choice has a `requires` block:

- **Default traversal** picks the first *eligible* choice. An ineligible
  choice is silently skipped.
- **Forced traversal** (`--choose`) into an ineligible choice produces a
  hard error (`branch not traversable (...)`) and aborts the run.

Eligibility uses the same predicate as `Beat.requires` (flags AND items AND
reputation), but unlike beat-requires the result is *blocking*, not advisory.

## `Requires`

```json
{
  "flags": ["sealed.accepted"],
  "items": [ { "key": "sealed-letter", "amount": 1 } ],
  "reputation": { "gravenguard": 250 }
}
```

All three sub-clauses are AND-ed. Items use `>=` against current stock;
absent keys count as stock 0. Reputation uses `>=` against the current value
(default 0).

## `Effect`

```json
{ "type": "removeItem", "key": "gold", "amount": 25 }
```

| `type`          | semantics                                                                 |
|-----------------|----------------------------------------------------------------------------|
| `setFlag`       | adds `key` to the flag set                                                 |
| `clearFlag`     | removes `key` from the flag set                                            |
| `addItem`       | `inventory[key] += amount` (creates entry if absent)                       |
| `removeItem`    | `inventory[key] -= amount` — **see underflow rules**                       |
| `addReputation` | `reputation[key] += amount` (creates entry at 0 if absent; amount may be ±)|

### `removeItem` underflow semantics

When `amount > current stock` (an absent key counts as 0):

- **Default**: hard error. The runner appends an error of the form
  `ERROR at beat '<beatId>' choice '<choiceId>': removeItem '<key>' x<amount>
  exceeds stock (<n> available)` and exits non-zero. The stock is clamped to
  0 in the snapshot so downstream effects keep running, but the run is
  considered failed.
- **`--allow-underflow` CLI flag** OR **`"allowUnderflow": true` in the
  fixture**: downgrades the error to a per-step warning of the form
  `WARN at beat '<beatId>' choice '<choiceId>': removeItem ... (clamped to
  0)`. Run still reports `success`.

The fixture-level flag is OR-ed with the CLI flag (it can only enable
permissiveness, never disable it).

## Sample (minimal)

```json
{
  "$schema": "quest-chain/v1",
  "id": "demo",
  "title": "Demo",
  "startState": { "flags": [], "inventory": [], "reputation": {} },
  "root": "intro",
  "beats": [
    {
      "id": "intro",
      "description": "Pick.",
      "choices": [
        { "id": "go", "text": "Go.", "next": "end",
          "effects": [ { "type": "setFlag", "key": "went" } ] }
      ]
    },
    { "id": "end", "description": "Fin.", "terminal": true, "outcome": "ok" }
  ]
}
```
