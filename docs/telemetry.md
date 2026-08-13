# Telemetry

A data collection module for the live mod. It observes the running simulation and writes a
report on demand or when the world closes.

It exists to answer the questions in [bugs-and-performance.md](bugs-and-performance.md) with
measurements from real play rather than from a synthetic benchmark: what temperature blocks
actually sit at, which definitions are mistuned, what each stage of the update costs, and
whether real grids are stiffer than the integrator can follow.

**It is off by default.** `EnableTelemetry` defaults to `false`, so a shipped world pays a
handful of static bool reads and nothing else.

## Turning it on

Either set `<EnableTelemetry>true</EnableTelemetry>` in `ThermodynamicsConfig.cfg` before
loading the world, or switch it on in chat during one:

| Command | Effect |
| --- | --- |
| `/thermal status` | Collection state, sample stride, live grids, block models built, rotor bridges. |
| `/thermal telemetry on` | Starts collecting. Attaches records and stage profilers to grids that already exist, so no reload is needed. |
| `/thermal telemetry off` | Stops. Every hook goes back to a single bool read. |
| `/thermal stride <n>` | Blocks sampled per step is `1/n` of the grid. Default 4. |
| `/thermal dump` | Writes a report without closing the world. |
| `/thermaldump` | The same thing; kept from the previous version. |

Repeated dumps are safe: the "final state" sections are rebuilt each time rather than appended
to.

## What it costs when it is off

| Path | Cost with collection off |
| --- | --- |
| Grid update | one `Telemetry.Enabled` test |
| Environment sample | one test |
| Block placed / removed | one test (`GetBlockType` returns null and the record is never made) |
| Overheat damage | one test |
| Simulation stages | `Profiler` is null; one null check per stage per update |
| Per-mechanism watts | `CollectDiagnostics` is false, so the solver does not compute or store them |

The last row is the one that used to be unavoidable. Radiation, convection, solar and friction
watts per block are diagnostics that nothing in the simulation reads; they are now produced only
for a report or for a client with the crosshair readout on. A dedicated server in ordinary play
writes none of them.

## What it costs when it is on

The solver steps a whole grid at once, so there is no per-block callback to pay for. Per step,
per grid:

* solver-level figures — substeps, whether the step clamped, node count, critical blocks;
* a **rotating slice** of nodes, `1/stride` of the grid, feeding the per-definition statistics
  and the anomaly detector. Every node is seen once per `stride` steps, so full per-block
  coverage costs one pass spread over four steps rather than a pass every step;
* structure — link, room, loop and queue counts — once in eight steps. Every figure there is a
  collection count; the link count in particular is now maintained by the solver, where the old
  code had to walk every cell for it.

Memory is bounded by construction. There are no sample buffers and no per-block history: every
figure is a streaming count, sum, sum of squares, min and max, or a fixed-bucket histogram. The
dictionaries are bounded by the number of block definitions (4096), grids that have existed
(2048) and anomaly kinds (64); overruns are counted and reported rather than allowed to grow.

## Output

Three files land in the world's storage folder
(`%AppData%/SpaceEngineers/Saves/<world>/Storage/<mod>/`), stamped with the session start time:

| File | Contents |
| --- | --- |
| `Thermodynamics_Telemetry_<stamp>.log` | the full report |
| `Thermodynamics_BlockTypes_<stamp>.csv` | one row per block definition |
| `Thermodynamics_Grids_<stamp>.csv` | one row per grid |

A summary line always goes to `SpaceEngineers.log`. If world storage cannot be written — the
failure mode most likely during shutdown — the whole report goes to the game log instead, so a
run is never lost silently.

## What is collected

**Session** — world name and path, online mode, server/dedicated/multiplayer, real and in-game
elapsed time, frame count, and the full `Settings` snapshot that produced the numbers.

**Per grid** (kept for the life of the grid, and retained after it is destroyed) — node count,
block count, conduction links, sealed rooms, exterior cells, surface entries, coolant loops,
`RecentlyRemoved` size and room-mapper queue depth, as min/mean/max; block add/remove/ignore
counts, splits, merges, door state changes, surface refreshes, mapper passes completed, saves
and loads with payload sizes and blocks restored; simulation steps, node updates, sampled nodes,
**solver substeps and steps clamped by the substep cap**, critical block counts, damage events
and total damage, peak temperature with the block that reached it, and the final temperature
histogram; ambient temperature, air density, atmosphere factor, wind speed, convection
coefficient, effective solar energy, grid speed, the fraction of time the sun was occluded, the
fraction of time in atmosphere, and which planets were visited.

**Room mapping, per grid** — how the mapper classified the padded search box: exterior cells,
structure cells and room cells, which sum to the box exactly, plus the two numbers that explain a
room that will not seal. *Block cells left outdoors* counts cells holding a block that the map
treats as open space; that is normal for anything the game does not consider airtight — a
reactor, a lattice, the way through an open door — and it is the first thing to read when a
sealed-looking interior maps as no room. *Sealed structure read as open* counts cells that seal on
all six faces and are still not structure, which nothing can reach and so is always zero unless
the map and the grid disagree; it raises an anomaly. The last completed pass also reports its
search volume, its block cell count, how many block cell faces do not seal, how many blocks seal
on no face at all, and a bounded list naming the blocks left outdoors with their surface bits and
which of their faces are open.

The audit runs once per completed mapper pass, never while one is in flight — a map that predates
the grid would report every block placed since as a disagreement it is not.

**Per block definition** (aggregated across every grid) — how much of each of the six faces the
definition seals and mounts, and how often a block of that type was observed with its sealing
switched off by an open door; the thermal properties in force, block
size, placed/removed/live/peak counts, mass, thermal mass, exposed surfaces and area, and
min/mean/max/sd for temperature, per-step ΔT, conduction, radiation, convection, solar and
friction watts, heat generation, power produced and consumed and thrust draw; critical updates
and heat damage dealt; and both a sampled and a final temperature histogram.

**Cost** — call count, total, mean, worst and a distribution for the grid update, and nested
inside it the four stages the simulation reports through `ISimulationProfiler`: topology
rebuild, room mapping, exposure refresh and the solver itself, plus the solar occlusion raycast.
Save and load are measured separately. Rows marked "of which" are nested and are not double
counted in the total.

Splitting the update by stage is what makes the cost numbers actionable: topology and room
mapping run on block changes and are the expensive pair, while the solver runs every step and is
usually not the problem.

**Anomalies** — NaN, infinite and implausibly high temperatures, temperatures clamped to zero
from a positive value (the signature of an unstable step), and any exception caught inside the
module. Each is recorded once per kind with a count and its first and last example.

An exception's example carries its type, its message and the top six stack frames. The message
alone does not say which call threw, and the throw is often inside game code the mod only reaches
indirectly — "capacity was less than the current size" is not a diagnosis, but the same line with
`ThermalBlockCatalog.BuildSurfaces` two frames below it is. The bound is what makes that
affordable: only the first and last example of each kind are kept, however many times it fires.

Sections of the report are written from whichever thread caught the event. The game builds pasted
and projected grids on worker threads, so the registries behind grids, block types and anomalies
are locked; see F1 and F2 in [bugs-and-performance.md](bugs-and-performance.md) for what happened
before they were.

## Tests

Most of the module reads `Sandbox.*` and `VRage.Game` types, which cannot load in [`sim/`](../sim).
Its decision logic does not, and lives in three files that reference nothing but `System`:

| File | What it holds |
| --- | --- |
| `TelemetryStats.cs` | `RunningStat`, `Histogram`, `TimingStat` |
| `TelemetryAnomalies.cs` | anomaly classification, the sampling gate |
| `TelemetryFormat.cs` | report and CSV formatting |

`Thermodynamics.Tests` links those three directly — the same files the game compiles, not a
copy — and covers them in `TelemetryStatsTests.cs`, `TelemetryAnomalyTests.cs` and
`TelemetryFormatTests.cs`.

The instrumentation hook itself is tested in `ProfilerTests.cs`, on the model side: every stage
is bracketed, an idle update reports no solver stage, a block placement reports a topology
rebuild, and — the two that matter most — an instrumented run and an uninstrumented one produce
**identical temperatures**, with and without diagnostics.

What is not covered is the wiring: which game fields each hook reads, and whether the report is
written successfully during shutdown. That is the same boundary the rest of `sim/` accepts, and
it can only be checked by loading a world.

## Settings

```xml
<EnableTelemetry>false</EnableTelemetry>
<TelemetrySampleStride>4</TelemetrySampleStride>
```
