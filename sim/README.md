# Isolated simulation environment

Runs the thermal simulation outside Space Engineers, so it can be built, tested, profiled and
debugged in seconds instead of by loading a world.

## Layout

| Project | What it is |
| --- | --- |
| `Thermodynamics.Core` | The simulation itself. Sources live in [`../Data/Scripts/Thermodynamics/Core`](../Data/Scripts/Thermodynamics/Core) so the game compiles them as part of the mod; this project links the same files. |
| `Thermodynamics.Harness` | Synthetic block catalogue, grid builder, canned environments, scenario library. |
| `Thermodynamics.Sim` | Command line front end for the scenarios. |
| `Thermodynamics.Tests` | xUnit suite. |

## What makes it isolated

The core references exactly one Space Engineers assembly: `VRage.Math.dll`, for `Vector3I`,
`Vector3`, `Matrix` and `Base6Directions`. That is pure managed maths and loads fine on .NET on
Linux. There is no `Sandbox.*`, no `VRage.Game`, no `MyAPIGateway`, no session, no entity.

Everything the game would supply crosses one of three boundaries:

| Boundary | Type | Supplied by the game as |
| --- | --- | --- |
| Block layout | `GridModel` / `BlockInstance` / `BlockModel` | `MyCubeGrid` and `IMySlimBlock` |
| Environment | `EnvironmentSample` | planets, sun raycasts, weather, grid velocity |
| Output | `OverheatEvent`, node temperatures | damage calls, HUD, block colouring |

A test fills those in with plain numbers; the game adapter fills them in from the session. The
solver cannot tell the difference, which is the point.

`LangVersion` is pinned to 6 on the core project — the same version the in-game script compiler
accepts — so the language features used here are ones the game accepts.

That is not the whole contract, though: the game also applies a **type whitelist** that this
project cannot check. `IndexOutOfRangeException` and `ArgumentOutOfRangeException` are both
prohibited in game and both compile happily here, so a green test run is necessary but not
sufficient. See [development.md](../docs/development.md#repo-conventions) for the list.

## Running it

```bash
cd sim

dotnet test                                    # the whole suite (679 tests)
dotnet run --project Thermodynamics.Sim -- list
dotnet run --project Thermodynamics.Sim -- run reactor
dotnet run --project Thermodynamics.Sim -- run all --csv out/
```

If `VRage.Math.dll` is not found, point at your install:

```bash
SE_BIN=/path/to/SpaceEngineers/Bin64 dotnet test
```

The probe order is `$SE_BIN`, the default Steam path, then the mod's own
`bin/Release/net472` output.

## Scenarios

| Name | Question it answers |
| --- | --- |
| `vacuum-soak` | How fast does a hot block shed heat into empty space? |
| `reactor` | Where does a buried reactor settle in vacuum? |
| `atmosphere` | How much does sea-level convection change that? |
| `daynight` | How far does an airless hull swing over a rotation? |
| `reentry` | Does aerodynamic heating reach the leading face? |
| `coolant` | Does a pumped ring actually move heat out of a reactor? |
| `sealed-room` | Do sealed interiors stop radiating? |
| `meltdown` | When does overheating start doing damage, and how much? |
| `radiator` | Do panels earn their mass — and what does bolting them flat against the hull cost? |
| `airlock` | Does opening a door actually let a sealed room start radiating? |
| `coolant-failure` | What happens to a cooled reactor when the pump is destroyed? |
| `welding` | Does an unfinished block swing further than a finished one? |
| `first-room` | The shape a player builds first — a shell with a door, welded a block at a time. Does it map as a room? |
| `stiff` | How many substeps does a very light block bolted to a very heavy one force, and where does it clamp? |
| `units` | The same block at three thermal clocks: what `HeatTimeScale` actually buys. |
| `perf` | Throughput on a settled solid cube, and what coalesced room mapping saves while welding. |
| `capital` | A 40,000 cell bulkheaded ship: what the one-shot rebuilds cost against a step. |
| `fleet` | Twenty ships stepped together — is the cost per cell or per grid? |
| `interior` | A hot appliance with no exposed face at all, against the same one on the skin. |
| `solver` | What a step actually costs when every link carries a gradient and the grid substeps. |
| `self-shadow` | Which faces of a solid hull are lit, and by how much, when the grid shadows itself. |
| `shadow-cost` | What keeping a self-shadow map costs on a large grid, and how often a pass runs. |
| `weather` | Does a storm reach the temperature model — the air, the sun and the convection, not just the wind? |
| `underground` | How far down does the day survive, and where does the rock start warming toward the core? |

> **`perf` and `solver` are not the same measurement.** `perf` steps a settled cube of one block
> type, where nearly every link joins two cells at the same temperature — and the conduction
> loop's first act is to skip a link whose ends agree, so most of its headline figure is the cost
> of *not* conducting. It also solves in one substep, so it never exercises the loop that
> dominates a real ship. `solver` seeds a 250-750 K spread across a hollow ship built of two
> block types and reports **nanoseconds per link visit**, which is comparable across grid sizes
> and substep counts. Where the two disagree, `solver` is the one describing the game.

Scenarios are deterministic: no clock, no randomness, no dependence on iteration order. The same
scenario produces byte-identical output on every run, which is what lets them double as
regression tests (`ScenarioTests`).

## Load benchmarks

Scenarios answer what the simulation does. The `bench` command answers what it costs, at sizes no
scenario would sit through — up to a million blocks in one grid.

```bash
dotnet run --project Thermodynamics.Sim -- bench scale                 # the ladder
dotnet run --project Thermodynamics.Sim -- bench spike --size 125000   # one block placed, split by stage
dotnet run --project Thermodynamics.Sim -- bench weld  --size 125000   # a block welded every tick
dotnet run --project Thermodynamics.Sim -- bench hitch --size 125000   # per-tick distribution
dotnet run --project Thermodynamics.Sim -- bench load  --size 1000000  # world load, before the first tick
```

| Benchmark | Question it answers |
| --- | --- |
| `scale` | What does each stage cost at 8k, 32k, 125k, 500k and 1M blocks? |
| `spike` | A block is placed. Which stage stalls, on which tick, and how many things did it touch? |
| `weld` | A block welded every tick for 120 ticks — sustained construction, which never gets a quiet tick to recover in. |
| `hitch` | 300 ticks of a settled grid with one block welded and one ground off. Reports median, p95, p99, max and the spike ratio. |
| `load` | Building the simulation for a grid this size, which a player sees as the loading screen or as a blueprint paste. |

`--shape ship|cube|truss` picks the shape, `--max N` stops the ladder early, `--ticks N` sets the
run length, `--csv <dir>` writes the ladder as a table.

**The harness runs on .NET 9 and the game runs .NET Framework 4.8**, so absolute milliseconds here
are optimistic against the game. Ratios and shapes of curve carry across; a millisecond figure
does not. `--diagnostics` turns on the per-node watt figures that switching telemetry on turns on
in a live world, so a benchmark can be compared against a field report that includes the cost of
being measured — about 2 % on a 42,000-block hull.

**The ship is the default shape on purpose.** A solid cube is the best case on nearly every axis
the simulation cares about — see [scale-design.md §10](../docs/scale-design.md#10-grid-shape-changes-the-arithmetic).

Two figures matter and they are not the same figure. The **steady cost** is what a tick costs when
nothing changed; it degrades gracefully, because twice the cost is half the simulation rate. The
**spike cost** is what a tick costs when something did; it degrades catastrophically, because a
player does not perceive a one-second frame as a slow simulation. What each benchmark measured,
and what was changed because of it, is in
[load-and-hitching.md](../docs/load-and-hitching.md).

## Writing a scenario

```csharp
GridBuilder builder = GridBuilder.Large();
builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
builder.Place(Catalog.Reactor(), new Vector3I(3, 0, 0))
       .Producing(15f * ThermalConstants.MegawattsToWatts);

ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

ScenarioRunner runner = new ScenarioRunner(simulation);
runner.Environment = t => Worlds.PlanetSurface(1f, timeOfDay: 0.5f);
runner.Track("reactor", builder.Last);
runner.Run(seconds: 3600f, sampleIntervalSeconds: 60f);

Console.WriteLine(runner.ToCsv());
```

Coolant rings are laid out by `PipeFitter`, which searches the 24 block orientations for one
whose ports face the right way — building rings by hand means deriving rotations by hand, and
that is how you end up with a scenario that silently tests nothing.

```csharp
var ring = PipeFitter.RectangleXZ(Vector3I.Zero, width: 4, depth: 3);
PipeFitter.BuildRing(builder, ring);      // pump goes on the first straight run
```

## Test coverage

679 tests across:

* position keys and block geometry maths
* face indexing, the colour ramp, occlusion
* orientations, block instances, grid model bookkeeping
* surface bit flags and their symmetry
* room flood fill, including incremental vs one-shot equivalence
* conduction: conductance, symmetry, energy conservation, order independence
* radiation, convection, solar, aerodynamic heating
* waste heat, critical damage and its rate independence
* integrator stability under absurd step sizes
* coolant loop topology and heat transport
* the save format, both current and legacy
* scheduling, settings and definition clamping
* end-to-end simulation, save/load, and every scenario
* the claims each scenario's summary line makes, so a headline conclusion cannot quietly invert
* the climate: latitude, ground, lag, altitude, thin air, weather and depth
* room air coupling — that a pressurised room gains links and takes the temperature of its walls
* the compartments the game seals and this model does not
* real specific heat against the `HeatTimeScale` clock, including that scaling capacity is
  exactly running time faster
* every mechanism switched off one at a time, and switches changed mid-session taking effect
  without a rebuild
* temperature thresholds: direction, no double reporting on the boundary, survival across a
  multi-step update
* registered point heat sources: gain, additivity, buffer bounds, and no gain on a buried block
* room air: pressurisation, links to the surfaces bounding a room, heat carried between walls that
  do not touch, energy conservation, and air surviving a map rebuild
* block identity by minimum cell, and overheat events surviving a multi-step update
* the stage-timing hook, and that instrumenting a run does not change its results
* the incremental conduction graph: that building a grid one block at a time, and grinding one
  down, produce the same graph a full rebuild does — including a 400-step run that builds and
  grinds in a generated order and compares against a rebuild after every single change
* what an update costs, asserted on work counters rather than a stopwatch: a settled grid rebuilds
  nothing, a placed block links the block and not the grid, a removed one unpicks the block and not
  the grid, every budgeted pass respects its budget, and observing the simulation does not change it
* the rolling sweep's slice arithmetic, including that it never rounds down to nothing
* the per-frame cost tracker that finds hitches in a real session

Several tests compare against `LegacyFormulas`, a verbatim copy of the original mod's equations,
to pin down exactly how the rewritten model differs.
