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
| `/thermal settings` | Every setting and its current value; `set` changes one live. |
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
| Per-frame cost | nothing is accumulated and no frame is closed; one test |
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
* the frame's running total — one add per grid per tick, and one closed frame per rendered
  frame, into a histogram and a list of sixteen structs;
* structure — link, room, loop and queue counts — once in eight steps. Every figure there is a
  collection count; the link count in particular is now maintained by the solver, where the old
  code had to walk every cell for it.

Memory is bounded by construction. There are no sample buffers and no per-block history: every
figure is a streaming count, sum, sum of squares, min and max, or a fixed-bucket histogram. The
dictionaries are bounded by the number of block definitions (4096), grids that have existed
(2048) and anomaly kinds (64); overruns are counted and reported rather than allowed to grow.

## Frame cost and hitching

The section to read first when someone reports stuttering.

Every other cost figure in the report is per grid, and a stutter is not per grid. Grids tick on
the ten-frame cadence and the engine calls them all on the same frame, so twenty ships each
taking a tolerable two milliseconds are a forty-millisecond frame — and every per-grid row still
looks fine. `FrameCostTracker` adds up what the mod spent on each frame across every grid, and
keeps the **worst sixteen frames of the session in full**.

The report gives, under `Cost`:

| Figure | What it says |
| --- | --- |
| `frames with work` | frames on which any grid updated |
| `mean` / `worst` | the mod's cost per frame, all grids together |
| `over a 60 fps frame` | frames where the mod alone exceeded 16.7 ms, and what share that is |
| `worst over mean` | how spiky the session was — see below |
| distribution | the same fixed-bucket histogram the other timings use |
| worst frames | sixteen samples, worst first |

Each worst-frame sample names the frame, when in the session it happened, the total, the split
across topology, room mapping, exposure and solver, how many grids ran, which single grid was
worst and how large it is — and **what the one-shot stages touched**: nodes visited for topology,
nodes refreshed for exposure, cells flooded. Those counts are what turns "a 90 ms frame" into "a
300,000-block station rebuilt its whole conduction graph", which is a line to change rather than
a number to worry about.

**`worst over mean` is the number to quote about smoothness.** A mod that is uniformly expensive
has a ratio near one and costs frame rate, which players tolerate; one that is cheap on average
and occasionally enormous has a ratio in the hundreds and costs a stutter, which they do not. The
two want completely different fixes, and a mean alone cannot tell them apart.

Frames costing under 4 ms are never considered for the list. Without that floor a quiet session
reports its sixteen most ordinary frames as though they were hitches.

The frame is closed at the top of the *next* frame rather than at the end of its own, because the
order the engine runs session components and entity components in is not something a mod
controls, and a frame closed before its grids have run records nothing.

## Work counters

Alongside the millisecond figures, the simulation counts what its one-shot stages *touched*:
rebuilds, node visits, links built, cells flooded, loop searches, substeps. See
`SimulationWork` in the core.

The two kinds of number answer different questions, and the counts are the ones that travel. A
topology stage with a worst call of 90 ms and a node-visit count equal to the grid size is a
global rebuild — an algorithm problem, reproducible anywhere. The same stage with a count of
forty is a slow machine. Milliseconds alone cannot distinguish them, which is why a report that
carried only timings could say a session stuttered without saying why.

They are also what the load tests in `sim/` assert against, so a claim proved on a benchmark and
a claim observed in a real world are the same claim.

## Output

Three files land in the world's storage folder
(`%AppData%/SpaceEngineers/Saves/<world>/Storage/<mod>/`), stamped with the session start time:

| File | Contents |
| --- | --- |
| `Thermodynamics_Telemetry_<stamp>.log` | the full report |
| `Thermodynamics_BlockTypes_<stamp>.csv` | one row per block definition |
| `Thermodynamics_Grids_<stamp>.csv` | one row per grid |
| `Thermodynamics_Rooms_<stamp>.csv` | one row per compartment — see [Room dump](#room-dump) |

A summary line always goes to `SpaceEngineers.log`. If world storage cannot be written — the
failure mode most likely during shutdown — the whole report goes to the game log instead, so a
run is never lost silently.

## Surface dump

`Thermodynamics_Surfaces_<stamp>.csv` is written with every report: **one row per block face**, six
rows per live block, on every grid in the world.

| Column | Meaning |
| --- | --- |
| `grid`, `grid_id`, `block`, `subtype` | which block, on which grid |
| `cell_x/y/z`, `size_x/y/z` | the block's minimum cell and its extents |
| `face` | which of the six sides the row is about |
| `face_cells` | cell faces on that side — the four counts below sum to this |
| `exposed` | cell faces the model counts as open to the sky |
| `sealed` | rejected: something airtight on the other side |
| `mounted` | rejected: two mount surfaces bolted together |
| `interior` | rejected: the space beyond is a sealed room, not outdoors |
| `sun_dot` | how square the face is to the sun, −1..1 |
| `sun_lit_fraction` | the share of *this face* the sun reaches, from the shadow map — per face, since the far layer of a wall is dark toward the sun and lit on the flank |
| `solar_w`, `temperature_k`, `exposed_area_m2` | what the block is doing with all that |

The three rejection columns are the point of the file. A face that looks open in game and reports
`exposed 0` is answered by whichever of them is non-zero, and they are indistinguishable from the
exposure total alone. The same breakdown is on the crosshair readout, per face, for the block being
looked at — see `DebugTextOnScreen`.

Rows are captured per grid at its final snapshot — when it closes, or when the report is written —
rather than read from the live grids at report time. That is the difference between a full file and
a header: the report is normally written as the world closes, and by then no grid is live. A record
outlives its grid, so a ship destroyed mid-session still contributes its faces.

Limits are 50,000 rows per grid and 300,000 for the session, about fifty thousand blocks. Hitting
either logs a line and stops; it never truncates silently.

## Room dump

`Thermodynamics_Rooms_<stamp>.csv` is written with every report: **one row per compartment per
grid**, both the rooms this model found and the ones only the game has.

Both kinds are in one table on purpose. The question a reader has is "what rooms does this ship
have and which of them work", and answering it out of two tables that have to be joined by hand is
how this went unnoticed in the first place.

| Column | Meaning |
| --- | --- |
| `grid`, `grid_id` | which grid |
| `kind` | `mapped` when this model found the room, `lost` when only the game holds it |
| `index` | index within its kind, ordered by anchor, stable while the grid's shape is |
| `anchor_x/y/z` | the lexicographically smallest cell, which is the room's name in grid space |
| `cells`, `volume_m3` | how big it is |
| `vented` | mapped rooms: standing open to the sky through a door |
| `pressure`, `air_kg`, `temperature_k` | what the simulation is actually running with |
| `links` | blocks the air is coupled to. **Zero beside a non-zero `air_kg` means the air is inert** — it has mass and touches nothing |
| `game_airtight` | `MyCubeGrid.IsRoomAtPositionAirtight` at the anchor — the game's own verdict |
| `vents` | the air vents opening onto it, by terminal name |
| `vent_pressurised` | whether a vent in it reports `IsPressurized` — the game's answer about its own room |
| `oxygen_level` | the highest level any vent in it reports, or −1 when none did |
| `game_oxygen` | the game's own oxygen level in the room, 0..1, or −1 when it could not be asked. Read from the grid's gas system, independently of our map and of any vent |
| `disagreement` | mapped rooms: **the game has air in it and this model runs none** — a compartment detected and not filled, which is a different failure from one not detected at all. Note `game_airtight` means *sealed*, not *full*: a sealed empty cupboard is correct in both models and is not flagged |
| `leak_faces` | lost rooms: faces this model leaves open that the game seals |
| `leaking_blocks` | lost rooms: the block subtypes across those faces, worst first |

**`leaking_blocks` is the fix list.** A compartment losing forty faces to one subtype names that
definition's surface bits as the thing to correct, where a cell coordinate would only say that
something somewhere does not seal.

**`vents` is the identity.** A player reports a room by the vent whose terminal says pressurised
while the overlay shows nothing, not by its coordinates, so that is what the row is named by.

The report carries the same table per grid under **compartments**, capped at 60 rooms before it
defers to the CSV, with a headline count of how many compartments the game holds that this model
does not — and how many of those have a vent reporting pressurised.

### What produces the `lost` rows

Every cell the room map calls external is offered to `IsRoomAtPositionAirtight`, and the ones the
game calls airtight are grouped into connected regions
([UnmappedRooms](../Data/Scripts/Thermodynamics/Core/Surfaces/UnmappedRooms.cs)). Each region is a
compartment this model lost: the flood fill walked in from outside, no room was created, and
because pressurisation is only ever asked about rooms the map already found, nothing ever compared
the two.

The scan costs one call into the game per external cell. It runs only when the room overlay is up
or telemetry is on, on a 240-step cadence, and is forced once at dump time so a report written
moments after a wall was welded describes the ship as it is. It is capped at 200,000 cells and says
so in the report when it hits that rather than truncating silently.

The same regions are drawn by the room overlay in red, one hue per region, brightest where a vent
in them reports pressurised — so a hole in the model reads as a hole rather than as an absence.

## Climate dump

`Thermodynamics_Environment_<stamp>.csv` is written with every report: one row per grid every ten
seconds of play, describing the world at that grid rather than the grid itself. It is the file for
balancing a planet's climate.

| Column | Meaning |
| --- | --- |
| `time_s`, `grid`, `grid_id`, `planet` | when, where, and on what |
| `altitude_surface_m` | height above the ground directly below |
| `altitude_sealevel_m` | height above the planet's mean radius |
| `latitude_deg` | against the planet's own axis: −90 at one pole, +90 at the other |
| `sun_elevation_deg` | the sun's height above the horizon, negative at night |
| `air_density`, `atmosphere_factor` | what the game reports, and what this mod makes of it |
| `ambient_k`, `ambient_c` | the ambient this mod produced |
| `underground` | 1 when the game says the grid is below the surface |
| `depth_m` | metres of ground over the grid — the figure the underground model actually uses. Zero or less is open air |
| `solar_w`, `solar_occlusion` | irradiance after atmosphere, weather and shadow, and the share shadowed |
| `convection_coeff` | the coefficient in force, W/(m²·K), with the wind and weather terms already in it |
| `wind_speed`, `wind_bearing_deg` | the wind the model uses, and where it is going: 0 north, 90 east |
| `wind_ceiling` | the game's own figure, which is the maximum the field scales |
| `weather` | the game's name for the weather standing over the grid — `RainHeavy`, `SnowLight` — empty in clear air |
| `weather_intensity` | the game's weather intensity at that point |
| `weather_ambient_k` | what that weather did to the air, K. The column that says the model reacted to it at all |
| `game_temperature` | the game's own comfort figure at that point, 0..1 — its model, for comparison |
| `surface_material` | the voxel material under the grid: snow, sand, grass, ice |
| `grid_mean_k`, `grid_peak_k` | what the grid itself did about all of it |

The rows are raw on purpose. A climate question is the *shape* of ambient against altitude, against
latitude and around a day, and an average has none of that in it. The report also carries a
**Climate** section summarising each planet — ambient by day and by night, air density, altitude,
solar, wind, convection, depth where anything was underground, the weathers seen and what they were
worth, and the ground materials — for the headline without opening the file.

Depth and weather are summarised only over the rows they happened on. Averaging a storm against the
clear days either side of it reports a drizzle that never fell, and averaging a mine shaft against
the surface reports a hole nobody dug.

Capped at 4000 rows per grid, about eleven hours of play at the default cadence, and logged when hit.

## What is collected

**Session** — world name and path, online mode, server/dedicated/multiplayer, real and in-game
elapsed time, frame count, and every setting that produced the numbers. The settings section is
generated from the same name table the chat commands and the mod API use, so a setting added to the
config cannot go missing from the report meant to explain a run.

**Per grid** (kept for the life of the grid, and retained after it is destroyed) — node count,
block count, conduction links, sealed rooms, exterior cells, surface entries, coolant loops,
`RecentlyRemoved` size and room-mapper queue depth, as min/mean/max; block add/remove/ignore
counts, splits, merges, door state changes, surface refreshes, mapper passes completed, saves
and loads with payload sizes and blocks and rooms restored; simulation steps, node updates, sampled nodes,
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

The audit runs once per completed mapper pass, never while one is in flight, and never before the
first pass has finished. Both exclusions are there because both fired. A map that predates the
grid reports every block placed since as a disagreement it is not; and a grid that closes inside
its first few seconds — a paste preview, a subgrid — never maps at all, and still holds the
all-external map the mapper hands out until it has built one. Audited against that, a sound hull
reported all 27 of its cells as unaccounted for. Such a grid now prints `mapped never (no pass
completed)` and no figures, rather than zeroes that look like measurements.

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
