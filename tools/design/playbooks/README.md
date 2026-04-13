# Balance playbooks

Each `*.json` in this folder is a sweep spec the `playbook-runner`
design-tool command executes against `CombatSimulationService`. Run via:

```bash
dotnet run --project tools/design/FirstMud.DesignTools -- playbook-runner --playbook <id>
```

## Axis → CombatContext mapping (CP7 Fix #2)

The playbook engine builds a `CombatSimulationService.CombatContext` per
cell. Axes and holdouts map to context fields as follows:

| axis / holdout   | field                  | effect on combat                                                                   |
| ---------------- | ---------------------- | ---------------------------------------------------------------------------------- |
| `playerLevel`    | `PlayerLevel`          | scales every player stat (HP, speed, ability BasePower via `actorLevel*2` bonus).  |
| `playerElement`  | `PlayerElement`        | chooses archetype stats and element-matchup table entry.                           |
| `gearTier`       | `GearTier`             | **+15% BasePower on Strike per tier** + **+3 Agility per tier** (dodge proxy).     |
| `imbueLevel`     | `ImbueLevel`           | **+20% BasePower per level on magical attacks** + **flat +4 per level** to every ability base. |
| `companionCount` | # entries in Companions| recruits N wildfolk companions at `companionLayer`.                                |
| `partySize`      | # entries in Companions| partySize = 1 ⇒ solo, partySize = N ⇒ player + (N-1) companions. Overrides companionCount. |
| `packSize`       | # enemies              | replicates the scaled monster N times (1 = solo enemy, 4 = 4-pack).                |
| `companionLayer` | each Companion.Layer   | wildfolk companion layer (also sets level = 2 * layer).                            |
| `dangerLevel`    | monster scaling        | `MonsterScaling.Apply` inflates HP / speed / damage; ≥7 → monsters get 2 actions/turn; ≥9 → 3. |
| `monsterId`      | holdout only           | overrides the default tier-picker; fixes which content monster is used.            |

### Why gear and imbue apply distinctly

Prior to CP7 Fix #2, `gearTier` and `imbueLevel` were folded into
`playerLevel` (`+2 effective levels per gear tier`, `+1 per imbue`). That
made `gear-only` and `imbue-only` playbooks indistinguishable from a
raw-level sweep. Now:

- **Gear** buffs the physical weapon path (Strike) and adds mitigation.
  Does **not** scale HP pool.
- **Imbue** buffs magical abilities (Weave Bolt, elemental bolts) and
  adds a small flat floor to every ability base. Does **not** scale HP.

The auto-sim player picks Strike by default, so gear has a stronger
in-sim effect than imbue. A caster-focused playbook (if added) would
see the ranking reverse.

## Cell seed determinism (CP7 Fix #1)

The per-cell salt used to diversify RNG across cells is derived
deterministically from the cell's **index tuple** — i.e. the position of
each axis's value in its `values` array — via a prime-weighted positional
mix. No `string.GetHashCode()` is involved, so the same playbook + seed
produces bit-identical results across runs, across processes, and across
.NET host versions. See `PlaybookEngine.DeterministicCellSalt`.

## Current playbooks

| id                     | purpose                                                                              |
| ---------------------- | ------------------------------------------------------------------------------------ |
| `nude-character`       | player at varying levels, no gear/imbues, no companions. Raw level curve.            |
| `gear-only`            | gear tier × danger sweep. Shows gear-dps curve distinct from level.                  |
| `imbue-only`           | imbue level × danger at baseline gear tier 3. Shows magical-power curve.             |
| `companion-contribution` | companion count × layer × danger. Shows party contribution.                        |
| `full-party`           | mid-game stack regression baseline; should sweep d=0-10 at 100%.                     |
| `pack-viability`       | partySize × packSize × danger. Probes pack-cap / party-scaling asymmetry.            |
