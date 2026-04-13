# Content configification plan

**Status:** pilot complete (consumables migrated). This doc is the survey + roadmap.

## Why

Today content (what potions do, what buildings cost, what monsters drop, what NPCs say) is scattered across `Handlers/` and `Services/` as hardcoded dictionaries and switch statements on string substrings. Designers can't tune numbers without a recompile. The same table is often duplicated in two files (see the old `ResolveEffect` that existed in *both* `UseConsumableCommandHandler` and `ConsumableHelper`).

The goal of this initiative is to move static / tunable content into data files (JSON for flat tables, Neo4j for graph-shaped content, SQL for runtime-mutable state). Code becomes the *engine*; JSON becomes the *content*.

## Principles

- **Format chosen per cluster.** Flat lookup → JSON. Graph-shaped (quest dependencies, dialogue trees, zone connections) → Neo4j. Runtime mutable → SQL. Procedural → stays code, but parameters in JSON.
- **Validate at startup, fail fast.** Typos in JSON should throw before a player logs in, not at the moment a potion is quaffed.
- **One loader, one provider.** `IContentProvider` is the single DI point. Adding a new cluster = new property on the provider, not a new service.
- **Schema files document intent.** Every JSON file has a sibling `*.schema.json` in `content/schemas/` — that's the designer's reference and the future hot-reload validator.

## Survey

| # | Cluster | Current location | Shape | Target format | Effort | Payoff |
|---|---------|------------------|-------|---------------|--------|--------|
| 1 | **Consumable effects** (pilot, shipped) | `UseConsumableCommandHandler.ResolveEffect`, `ConsumableHelper.ResolveEffect` (duplicated) | Flat table: name-match → effect | JSON (`content/consumables.json`) | S | High — was duplicated in 2 files |
| 2 | **Building construction costs** | `BuildingService.ConstructionCosts` + `BuildingDuty` + `WorkerCapacity` (3 dictionaries keyed on same enum) | Flat per-enum | JSON (`content/buildings.json`) | S | High — designers adjust costs constantly |
| 3 | **Loot tables** | `LootService` (biome/danger → item rolls) | Weighted table per (biome, danger) | JSON, one file per biome | M | High — core tuning dial |
| 4 | **Crafting recipes** | `CraftingService` hardcoded recipe map | Flat table: output → list of (material, qty) | JSON (`content/recipes.json`) | M | High |
| 5 | **Salvage yields** | `SalvageService` | Flat table: input → yield rolls | JSON | S | Medium |
| 6 | **Smelt recipes** | `SmeltService` | Flat ore→ingot table | JSON | S | Medium |
| 7 | **Imbue effects** | `ImbueService` | Flat table | JSON | S | Medium |
| 8 | **Monster templates** | `MonsterFactory.cs` | Per-monster stat block + loot ref | JSON (`content/monsters/*.json`) | M | High |
| 9 | **Starter seed data** | `StartupSeeder.cs` | Procedural + static | JSON for static seed, keep procedural logic in code | M | Medium |
| 10 | **Zone grid layout** | `ZoneGridLayout.cs` | Hand-authored grid | JSON (`content/zones.json`) | M | Medium |
| 11 | **Biome / danger params** | `BiomeService.cs` | Enum-driven constants | JSON | S | Medium |
| 12 | **Quest definitions** | `QuestService` + `StartupSeeder` | Graph (prerequisites, rewards, triggers) | **Neo4j** — already the right substrate | L | High |
| 13 | **NPC dialogue + companion barks** | hardcoded strings in handlers | Tree of `(context, line)` pairs | JSON now, Neo4j later for branching | M | High (writing-friendly) |
| 14 | **Encounter triggers** | scattered in handlers | Event → condition → outcome | JSON rules table | L | Medium |
| 15 | **Handler string literals** ("harvested Wood x6" etc.) | everywhere | Format strings | Resource file or JSON `strings.json` | L | Low-Medium (i18n prep) |

**Classification legend:**
- Effort: S = <½ day, M = 1–2 days, L = 3+ days
- Payoff: based on frequency of tuning + duplication factor + designer accessibility

## Pilot: consumables (shipped)

- `content/consumables.json` — 8 consumables
- `content/schemas/consumables.schema.json` — JSON Schema documenting the format
- `FirstMud.Application/Content/IContentProvider.cs` + `ContentProvider.cs` — loader/validator/cache
- `FirstMud.Application/Content/ContentRootResolver.cs` — walks up from `bin/` to find repo `content/` at runtime, `FIRSTMUD_CONTENT_ROOT` env var override for containers
- Consumers refactored: `UseConsumableCommandHandler`, `ConsumableHelper`, `FarmingOrchestrator`
- Tests in `tests/FirstMud.Tests/Content/ContentProviderTests.cs` — regression fence proving legacy switch behavior is preserved byte-for-byte, plus validation-failure cases

**Net:** two copies of an 8-branch switch + 3 hardcoded priority arrays replaced by one JSON file and one provider.

## Recommended next steps (ranked)

1. **Building costs (#2)** — same S-effort pattern as pilot, three dictionaries collapse into one JSON file, designers want this daily.
2. **Crafting recipes (#4)** — medium effort but probably the single most-edited content cluster once crafting expands.
3. **Monster templates (#8)** — will block / accelerate everything combat-flavored (new zones, new encounters). Natural follow-on to loot tables.

Deferred for good reason: quests (#12) should go to Neo4j, not JSON — the graph shape is the whole point. Dialogue (#13) is writing-heavy; wait until we have writers.

## Future work

- **Hot reload in dev.** `ContentProvider.Reload()` exists; wire a `FileSystemWatcher` + SignalR broadcast so designers edit JSON and see changes without a server restart.
- **Schema validation at CI time.** Run the JSON files through a schema validator in the build pipeline so typos fail the PR, not startup.
- **Admin UI.** Once 3+ clusters are JSON, a minimal React panel on top of the files beats a text editor.
