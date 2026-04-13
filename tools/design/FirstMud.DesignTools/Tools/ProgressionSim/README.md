# progression-sim

Full implementation. Simulates an N-hour fresh-character play session under a
named playstyle, emits per-hour progression snapshots, and flags bottlenecks
with candidate data tunes.

See `tools/design/README.md` for full CLI + playstyle tables. Core model
lives in `ProgressionSimulator.cs` — CLI glue in `ProgressionSimCommand.cs`.

Tests: `tools/design/FirstMud.DesignTools.Tests/ProgressionSimTests.cs`.
