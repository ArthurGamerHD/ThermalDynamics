# Isolated simulation environment

Runs the thermal simulation outside Space Engineers, so it can be built, tested, profiled and
debugged in seconds instead of by loading a world.

> The rules argued here are stated canonically in [rules.md](../docs/rules.md): `E7` `D2` `D4`
> `D5` `D8` `C1` `C2` `C5` `M11` `R10`.

| Looking for | Go to |
| --- | --- |
| What the simulation does | [thermal-model.md](../docs/thermal-model.md) |
| Building and deploying the mod | [development.md](../docs/development.md) |
| The performance report and the scale ladder | [benchmarks.md](../docs/benchmarks.md) |
| The balance lab this harness runs | [balance-lab.md](../docs/balance-lab.md) |

## Layout

> **Hulls are built from a measured block census.**
> [`Census.cs`](Thermodynamics.Harness/Census.cs) holds the block population of a real ship —
> eight bands by heat capacity, plus the share that generate waste heat — read out of a telemetry
> dump's block-type table. Every benchmark builds from it, because a step takes as many substeps
> as the *stiffest* block needs, so a hull's cost is decided by its lightest block. The old mix of
> heavy armour and gratings asked for three substeps where a real ship asks for twenty-one to
> thirty-one, which made every scale figure about six times too cheap. `CensusFidelityTests`
> fails if the census drifts away from the field observations recorded beside it in
> `Census.Field`; refresh both when a new dump arrives.

> **A field dump is kept beside the census.**
> [`benchmarks/field-dump/`](benchmarks/field-dump) holds three CSVs of one real telemetry dump —
> 264 climate rows, 242 grids, 560 block types — under the names the mod writes them with.
> `DumpAuditTests` audits them on every run, so the claims the model makes are checked against
> numbers a world produced rather than numbers a test invented. It found two faults on its first
> pass: a grid's stages exceeding the update they nest inside, and a block type's peak temperature
> sitting below its own maximum. Both are fixed and the fixture still carries them, because a dump
> records what the mod was when it was taken. Refresh it when a dump arrives, from `-- dump`.

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

## Where the defects have actually been

Three faults found in one pass over a field dump had the same shape, and it is worth stating
because it says where to write the next test rather than where the last one was.

| Fault | Why the suite missed it |
| --- | --- |
| A grid's substep demand was conduction-only until it had stepped | every test measures a grid *after* stepping it |
| A grid's one-off build was charged to no row in the cost table | nothing read the report the mod writes |
| A block type's peak temperature could sit below its own maximum | nothing read the report the mod writes |

**The suite tests the simulation, and the things around it were untested**: the state a grid is in
before its first step, and the artefacts the mod produces about itself. Both are exactly where a
fault survives longest, because both look like output rather than behaviour and neither changes a
temperature. `DumpAuditTests` and `FieldDumpTests` close the second; the first is a habit — when a
figure is read outside a step, test it outside a step.

The adapter under `Game/` remains the largest uncovered surface; see F1 in the
[backlog](../docs/backlog.md).

`LangVersion` is pinned to 6 on the core project — the same version the in-game script compiler
accepts — so the language features used here are ones the game accepts.

That is not the whole contract, though: the game also applies a **type whitelist** that this
project cannot check. `IndexOutOfRangeException` and `ArgumentOutOfRangeException` are both
prohibited in game and both compile happily here, so a green test run is necessary but not
sufficient. See [development.md](../docs/development.md#repo-conventions) for the list.

## Running it

```bash
cd tests

dotnet test                                    # the whole suite, under a minute
dotnet test --filter "speed!=slow"             # the fast lane, about fifteen seconds
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

The nine scenario batteries — each stepping whole ships for tens of seconds — carry
`[Trait("speed", "slow")]`; the fast lane is everything else. The suite spent months at five
minutes because one test read the whole blueprint corpus on every run despite its own class's
opt-in rule — 4 m 57 s of a 5 m 4 s suite. It is behind `THERMAL_CORPUS_TESTS` now, where the rest
of its class already was.

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
| `cooling-plant` | **The general case.** Loop, pumps and radiators as one chain on a loaded ship — how much does the whole plant buy over the same ship without it? |
| `loop-faults` | Every way a ring fails to become a loop, on one grid, each named with its reason. |
| `loop-dry` | A loop that formed correctly and cools nothing, because no sink face meets anything. Every figure looks healthy. |
| `heatpump-backwards` | A pump installed the wrong way round — a rotation, not an obvious mistake. What does it cost? |
| `heatpump-limits` | One pump across four gap widths: which of its three limits binds, and what the coefficient costs. |
| `cooling-runaway` | More heat than the radiators can shed. Where is the knee, and does it reach damage? |
| `loop-stiffness` | A long ring is the stiffest thing a player can build cheaply. Does the substep estimate see it? |
| `loop-layout` | One ring or several, and where to put the sinks? Splitting buys nothing; spreading the sources buys 41 K. |
| `air-conditioning` | Can a heat pump cool a room? Yes, through a wall, and only a pressurised one. |

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

## The descent

```
dotnet run --project Thermodynamics.Sim -- descent
dotnet run --project Thermodynamics.Sim -- descent --csv out/
```

A ship digging from the surface to the core, read at every depth: the sun, the wind, the day
damping out in the rock, and the rock's own heat below the deadzone. It is the one place the four
hand over to each other, and each of them was written on its own.

The columns match the environment dump's, so a descent flown in game can be laid against the
modelled one. It found the wind fault A15 — a buried grid was still in the ground-level wind — and
it is where the unmodelled rock contact (A16) is visible.

## The balance commands

Five commands, none of them a scenario or a benchmark. Each answers one balance question, each
reads the shipped definitions at run time rather than a hand-written catalogue, and each has its
conclusions pinned by a test and argued on a page. **The findings are on those pages** — this table
is what to run and where to read the answer.

| Command | The question | Pinned by | Argued in |
| --- | --- | --- | --- |
| `balance [--csv out/]` | What is every block this mod ships worth against the vanilla blocks it competes with? Five tables: every block costed and measured, what the vanilla heat sources put in, what a panel delivers end to end, which dial moves that number, and the heat pump and coolant rings across their ranges. | `BalanceTests` | [balance.md](../docs/balance.md#block-balance) |
| `coolers` | Which block in the whole game is the best cooling in it, stacked one to thirty-two against the largest reactor at plate rating, in shadow? | `CoolingLadderTests` | [balance.md](../docs/balance.md#the-same-question-asked-of-the-whole-game) |
| `reactors` | What fraction of a reactor's output should become heat, across three orders of magnitude of rating? Every vanilla reactor at six fractions and three loads, in two rigs — **bare** in shadow on a 2.7 K sky, and **skinned** under one cell of light armour. | `ReactorWasteHeatTests` | [balance.md](../docs/balance.md#reactor-waste-heat) |
| `retrofit --ships 500 [--csv out/]` | Can cooling be fitted to ships people actually built? A real hull is parsed, run under load to find where its heat is, and the mod's blocks go into the cells that hull left free — bolted and plumbed. | `RetrofitTests` | [balance-lab.md](../docs/balance-lab.md#0-define-good-balance-before-collecting-anything) |
| `stiffness [--csv out/]` | What does a ship's stiffest block demand of a step, asked of 8,102 workshop hulls rather than one save? Nothing is stepped: stiffness is a property of a built grid and the world it is asked about. | `DecorativeStiffnessTests` | [stiffness.md](../docs/stiffness.md#the-same-question-asked-of-eight-thousand-real-ships) |

```bash
dotnet run --project Thermodynamics.Sim -- balance --csv out/
dotnet run --project Thermodynamics.Sim -- coolers
dotnet run --project Thermodynamics.Sim -- reactors
dotnet run --project Thermodynamics.Sim -- retrofit --ships 500 --csv out/
dotnet run --project Thermodynamics.Sim -- stiffness --csv out/     # ~4 min
```

Two things about how these read the world are the harness's business rather than the finding's.

**`coolers` carries a top-K column**, because a stack goes flat two ways and they read identically
without it: a stack that saturated is hot at its far end, and a stack the heat never reached is
*colder than it was built*, having radiated to the sky instead.

**`stiffness` streams the corpus in batches and counts what it skipped.** A blueprint is held in
memory as every grid and every block in it, so a fifty-gigabyte corpus parsed at once is how an
uncapped run takes a machine down. Blueprints over 64 MB of XML are skipped and **counted**, as is
everything else that does not reach the table (`O5`).

## The block catalog

`blocks` is the authoring side of the definition pass: what every block in the game derives to from
its build components, and which subtypes deviate far enough from their type to be worth naming.

```bash
dotnet run --project Thermodynamics.Sim -- blocks
```

It reads the installed game's own `CubeBlocks/*.sbc` rather than a transcription — there is no
honest way to hand-copy 1,503 definitions — and prints nothing useful without an install. The
derivation it reports is the same one that runs in game, in `BlockThermalDerivation`, so this is a
view of live behaviour rather than of a generator. See
[definitions.md](../docs/definitions.md#where-a-blocks-properties-come-from).

## The blueprint corpus

`corpus` reads real ships out of Space Engineers blueprint files and reports what came back.

```bash
dotnet run --project Thermodynamics.Sim -- corpus                 # subscribed workshop items
dotnet run --project Thermodynamics.Sim -- corpus --path <dir>    # any directory of bp.sbc files
```

Everything this repository measures, it measures on a hull it built itself, and `Census` says so
in its own summary: "one ship is one ship". A corpus replaces that hypothesis with a population.
`corpus-fetch` builds one from the workshop, ranked by subscriptions:

```bash
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --top 10000 --list-only
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --user <account> --top 10000
```

It never handles a password — log the account in once with `steamcmd +login <user> +quit` and
SteamCMD caches it. See [balance-lab.md](../docs/balance-lab.md) for what the lab is for and how it
is staged.

## Screening and the battery

`screen` measures every ship in a corpus without stepping it, and cuts the population to a panel of
specimens chosen to cover the design space rather than to be typical of it. `battery` puts that
panel through every scenario.

```bash
dotnet run --project Thermodynamics.Sim -- screen  --panel 24
dotnet run --project Thermodynamics.Sim -- battery --panel 6
dotnet run --project Thermodynamics.Sim -- battery --panel 6 --linear
```

`hotspot` answers why one block is the hottest thing on a ship, which a matrix cannot:

```bash
dotnet run --project Thermodynamics.Sim -- hotspot --ship Atlas --scenario full-electrical
```

It dumps the blocks at the top of the distribution with the three figures that decide where each
landed — what it generates, what it can radiate through its own faces, and what it can conduct into
its neighbours. See
[balance-lab.md](../docs/balance-lab.md#a-block-with-no-exit-is-the-labs-own-failure-mode) for what
those three separate, and why a block with no exit is the lab's own failure mode rather than a
physics result.

**`--linear` matters.** Balance collection runs concurrently, because a settling problem is a pure
function of a ship and a scenario and nothing about a temperature changes because another core was
busy. Anything whose figure is a *duration* has to run one at a time, because there every other
core is contention rather than speed — and no number carries a note saying which it was. The two
modes produce identical matrices; only the clock differs. See
[balance-lab.md](../docs/balance-lab.md#running-the-lab-parallel-and-linear).

## How far the model is from physics

`profiles`, `sweep` and `features` are the whole-system counterpart to `balance`: not "is this block
worth building" but "how far is this model from physics, and what does each departure cost". They
live in the harness, change nothing that ships, and are the only thing in the repository still called
a profile.

```bash
dotnet run --project Thermodynamics.Sim -- profiles          # one rig, four worlds
dotnet run --project Thermodynamics.Sim -- sweep --csv out/  # every scenario and worst case
dotnet run --project Thermodynamics.Sim -- features          # mechanism switches in combination
```

Realism turns out to be the cheapest configuration and the least responsive; the substep estimate
explains nearly every failure; and both the arcade end of the comparison and the shipped default
diverge on cases a player can build. See [realism.md](../docs/realism.md).

## Load benchmarks

Scenarios answer what the simulation does. The `bench` command answers what it costs, at sizes no
scenario would sit through — up to a million blocks in one grid.

```bash
dotnet run --project Thermodynamics.Sim -- bench scale                 # the ladder
dotnet run --project Thermodynamics.Sim -- bench spike --size 125000   # one block placed, split by stage
dotnet run --project Thermodynamics.Sim -- bench weld  --size 125000   # a block welded every tick
dotnet run --project Thermodynamics.Sim -- bench hitch --size 125000   # per-tick distribution
dotnet run --project Thermodynamics.Sim -- bench load  --size 1000000  # world load, before the first tick
dotnet run --project Thermodynamics.Sim -- bench floor --size 42000    # what a per-block substep cap buys, and costs
dotnet run --project Thermodynamics.Sim -- bench steppath              # a step at the solver, against a step through the host
dotnet run --project Thermodynamics.Sim -- bench smallgrids            # what one grid costs before any of its blocks do
dotnet run --project Thermodynamics.Sim -- bench wattsclear            # what zeroing the watts row costs, up a size ladder
dotnet run --project Thermodynamics.Sim -- bench rowfill               # what the first substep of a step pays over a later one
dotnet run --project Thermodynamics.Sim -- bench floor --frequency 4   # the substep cap swept; the rate is part of the answer
dotnet run --project Thermodynamics.Sim -- bench report --csv benchmarks              # the full report
dotnet run --project Thermodynamics.Sim -- bench report --baseline benchmarks/performance.csv   # and the diff
```

| Benchmark | Question it answers |
| --- | --- |
| `scale` | What does each stage cost at 8k, 32k, 125k, 500k and 1M blocks? |
| `spike` | A block is placed. Which stage stalls, on which tick, and how many things did it touch? |
| `weld` | A block welded every tick for 120 ticks — sustained construction, which never gets a quiet tick to recover in. |
| `hitch` | 300 ticks of a settled grid with one block welded and one ground off. Reports median, p95, p99, max and the spike ratio. |
| `load` | Building the simulation for a grid this size, which a player sees as the loading screen or as a blueprint paste. |
| `report` | Everything at once, as a diffable CSV — including the worst cases a plain hull never reaches: air in the compartments, plumbing, blocks past their rating, three hull shapes in three worlds, and fleets of up to a hundred grids. Every scenario row carries what it actually built, so one that builds nothing cannot report a cost of zero. Also: the ladder, every feature measured both marginally and in isolation, and the substep cap across its range — plus a noise floor so a reader can tell a small cost from no cost. `--baseline <csv>` compares against an earlier run. See [benchmarks.md](../docs/benchmarks.md). |
| `floor --driven` | The same sweep on a ship held at temperature by forty 250 kW sources instead of by a seeded spread — the case that says whether a player would notice, and the one that reports the peak temperature overheat damage is decided by. |
| `coolant` | The segmented fluid model against the well-mixed one it replaced, on the same grid, at rising amounts of pipe. |
| `steppath` | The same step driven straight at the solver and then through the host's entry point, at three substep caps. Everything else here drives the solver, so work the host does around a step is invisible to it — which is how a duplicate stability estimate survived a section written to attribute fixed cost. Both timed runs re-seed the temperature spread; conduction skips a link whose ends agree, so whichever runs second on a flatter grid reads cheaper for no reason but its order. |
| `smallgrids` | A 200-grid fleet swept from one block a grid upwards, each row run whole, again paced the way the host drives it, and a third time on a zero-length frame to measure the per-grid visit on its own. The report's fleet rows stop at eight hundred blocks a grid because that is the smallest the ship generator builds; a real world is mostly smaller than that, and the per-grid fixed cost is what decides its price. Phases alternate order between repeats — whichever ran second inherited a settled grid and read five times faster than the phase it is a superset of. |
| `floor` | `MaxSubstepsPerBlock` swept: what each cap does to the substep count and the clock, how many blocks it moves, and how far it moves them. Builds from the block census, because the population's *shape* is what decides how far a cap reaches. |

`--shape ship|cube|truss` picks the shape, `--max N` stops the ladder early, `--ticks N` sets the
run length, `--csv <dir>` writes the ladder as a table.

### What segmenting the coolant cost

`bench coolant` runs both fluid models on one grid. Best of five passes per model per row, because
the first version of it reported a four-ring grid as faster than a one-ring grid — a mean was
measuring the machine rather than the work.

```
  rings  pipes  loops   blocks    links   segmented   well-mixed   ratio  substeps
      1     24      1    1,253    2,835      0.2306       0.2307    1.00   23 / 23
      4     96      4    1,349    2,931      0.2532       0.2528    1.00   23 / 23
     16    384     16    1,733    3,315      0.3421       0.3400    1.01   23 / 23
     48   1152     48    2,757    4,339      0.5901       0.5908    1.00   23 / 23
```

**Within one percent, on a grid that is 42 % coolant pipe** — against a field ship measured at 21 %.
The ratio does not climb with the plumbing, which was the thing worth checking: a fixed multiple is a
tuning question, a multiple that grows with what a player builds is a design problem.

It is that cheap because **the link count is identical**. One link per pipe plus one per sink face
exists in both models; what changed is which temperature each link reads. The cost of a coolant loop
was always its links, and there are exactly as many as before.

> **The comparison is only honest because the well-mixed path is genuinely single-mass.** The first
> version of `WellMixedCoolant` reproduced the old *behaviour* by carrying a parcel per pipe and
> levelling them after every substep — so a setting whose only purpose is to be cheaper kept the whole
> cost of the thing it was meant to avoid, and the benchmark was comparing the new model against
> itself. A well-mixed ring is now literally a ring with one parcel holding all of its coolant, so it
> costs one accumulator and one integration however long it is. `TheWellMixedRingIsOneParcelHoldingEverything`
> pins that.

The substep column is the other half of the answer: 23 either way, set by the hull's light blocks, so
loops are nowhere near the stiffest thing on the grid. And they cannot become so by circulating
faster — carrying the fluid is a rotation of which parcel sits in which pipe, which is exact at any
speed. `FlowSpeedDoesNotCostSubsteps` asserts that at 5,000 parcels per second.

`WellMixedCoolant` therefore exists for the choice rather than for the cost.

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

> **It had that failure itself.** A pump carries no sink ports; every rectangle's first straight run
> is index 1; `BuildRing` put the pump there by default, and a sink requested on index 1 was dropped
> without a word. Every ring in this repository asked for exactly that, so **no test or scenario had
> ever exercised a sink face** — they passed on ordinary block-to-block conduction between the hot
> block and the pipe above it, which happens whether a sink exists or not.
> `BuildRing` now moves an automatically chosen pump aside, and an explicitly named pump index that
> collides with a sink is an error. `PipeFitterTests` pins both, and the loop tests now assert the
> link count they expect instead of assuming the sink arrived.

```csharp
var ring = PipeFitter.RectangleXZ(Vector3I.Zero, width: 4, depth: 3);
PipeFitter.BuildRing(builder, ring);      // pump goes on the first straight run
```

## Test coverage

1,769 tests. **What each class is for is stated in its own summary, not here** —
the index below says where to look, and `EveryTestClassSaysWhatItIsFor` fails when a class arrives
without saying. This table is checked by `EveryTestClassIsInTheIndex`, so a suite cannot be added
and left off it.

| Subject | Suites |
| --- | --- |
| **Block geometry and the grid model** | `FaceTests` `BoxGeometryTests` `GridMathTests` `CellBitsetTests` `BlockOrientationTests` `BlockInstanceTests` `GridModelTests` `BlockSurfaceBuilderTests` `Se2LatticeTests` `ShapeTests` |
| **Surfaces, rooms and air** | `SurfaceMapTests` `RoomMapperTests` `DoorSealingTests` `RoomPortalTests` `IncrementalRoomTests` `RoomMapCompletionTests` `RoomCellStorageTests` `RoomAuditTests` `UnmappedRoomTests` `RoomAirTests` `RoomAirCouplingTests` `RoomPressureTests` `RoomAirPressureTests` `ExposureAuditTests` |
| **Conduction and the integrator** | `ConductionTests` `StabilityTests` `ConductionClampGateTests` `CoupledConductanceCacheTests` `SubstepDemandTests` `SubstepFloorTests` `SubstepScaleTests` `HeatTimeScaleTests` |
| **Environment: air, climate, weather** | `EnvironmentSolverTests` `RadiationTests` `ConvectionSolarFrictionTests` `FrictionIsolationTests` `ClimateModelTests` `GroundRoughnessTests` `DayLengthTests` `WeatherAndDepthTests` `UndergroundContactTests` `PlanetThermalTests` `PlanetReferenceTests` `PlanetPropertyMergeTests` `DescentTests` |
| **Sun, shadow and occlusion** | `SunShadowMapTests` `SolarSelfShadowingTests` `SunLitSliceTests` `SolarOcclusionTests` `SolarOcclusionSamplerTests` `OcclusionLadderTests` `OcclusionMathTests` `SolarSymmetryTests` `GridShadowTests` `TerrainHorizonTests` `SelfShadowScenarioTests` `FaceWeightPairingTests` |
| **Wind** | `WindFieldTests` `WindProfileTests` `GradientHeightTests` `WindSlopeTests` `WindTerrainTests` `WindCompassTests` `StormHeatingTests` `WindScenarioTests` `WindSolverContractTests` `WindLabTests` |
| **Heat sources, damage and thresholds** | `HeatGenerationTests` `DamageTests` `CriticalTemperatureTests` `CriticalTemperatureMirrorTests` `OverheatEventTests` `SuitThermalTests` `IncandescenceTests` `HeatWarningTests` `HeatCueScanTests` `ThresholdTests` `HeatSourceTests` `HeatSourceMathTests` `HeatSourceCommandTests` `CustomHeatSourceTests` `MultiCellAndDamageTests` `ReactorWasteHeatTests` `GridHeatBalanceTests` `HottestNodeTests` `GlowGeometryTests` |
| **Coolant loops and heat pumps** | `CoolantLoopTests` `PumpPowerTests` `CoolantFlowTests` `CoolantFaultTests` `PipeFitterTests` `HeatPumpTests` `CoolingScenarioClaimTests` |
| **What a step costs, and what it must not change** | `LoadTests` `StepBudgetTests` `StepFixedCostTests` `StepPacingTests` `StepTermsTests` `SpreadStepTests` `PaceEquivalenceTests` `SweepSliceTests` `BufferGrowthTests` `IncrementalTopologyTests` `BlockRefreshTests` `CostRollupTests` `SolverReportingTests` `StressFindingsTests` |
| **Bit-identity: an optimisation against what it replaced** | `PrecomputedEnvironmentTests` `FixedSourceRowTests` `WattsClearFusionTests` `ConductionClampGateTests` `DiagnosticBatchingTests` |
| **Settings, storage and definitions** | `SettingsTests` `SettingsDefaultsTests` `SettingsWiringTests` `ValidationReportingTests` `StorageCodecTests` `SchedulerTests` `DefinitionTests` `DefinitionFileTests` `ShippedDefinitionTests` `AuthoredMaterialTests` `BlockDerivationTests` `SolarAbsorptivityTests` `MaterialOverrideTests` `FeatureToggleTests` `DefaultSettingsTests` `ProfileSuiteTests` `WorldSettingsTests` |
| **Readouts a player sees** | `TemperatureScaleTests` `UnitsTests` |
| **Telemetry, reports and overlays** | `RunningStatTests` `HistogramTests` `TimingStatTests` `TelemetryFormatTests` `TelemetryAnomalyTests` `SampleGateTests` `GridHealthTests` `AnomalyRegistryTests` `FrameCostTests` `ProfilerTests` `RescanGateTests` `OverlayBudgetTests` `PerformanceReportTests` `BenchmarkBaselineTests` |
| **Field dumps: the mod checked against a world** | `DumpAuditTests` `FieldDumpTests` `CensusFidelityTests` |
| **End to end, and the host boundary** | `SimulationIntegrationTests` `ScenarioTests` `ScenarioClaimTests` `HostAdapterTests` `CoreIsolationTests` |
| **Balance, and the ships it is decided on** | `BalanceTests` `CoolingLadderTests` `RetrofitTests` `BlockHeatIndexTests` `TimeToLossTests` `CatalogDriftTests` `ModHardwareRetestTests` `RetestSetTests` `SettleReadingTests` `DecorativeStiffnessTests` `ElementCostFitTests` `ScreeningTests` `BlueprintTests` `SubgridBridgeTests` `PrefabWalk` `CorpusGuardTests` `CorpusArchiveTests` `ClientDriftTests` `ClientInputTests` `HotTailTests` `HotTailSyncTests` `AirCostTests` `ConductionPaceTests` `LoadDialTests` `WorstCaseTests` `LabRunTests` `LabInvariantTests` |
| **Corpus walks** (opt-in, `THERMAL_CORPUS_TESTS`) | `CorpusSurvey` `CorpusCensus` `KnobSweep` `ConductanceRetestWalk` `PairSweep` `SunlightPanelWalk` `BlockAccountingWalk` `DeterminismWalk` |
| **The documentation itself** | `DocumentationTests` `ModApiShapeTests` `ConfigurationDocTests` `SimCommandTests` `CredentialScanTests` `ScriptWhitelistTests` |

> **The bit-identity suites share one fixture.** Five of them pin an optimisation against the
> thing it replaced — the precomputed environment rows, the fixed source row, the gated
> conduction clamp, the batched diagnostics, a step spread across frames — and all five build from
> `Hulls.Driven`: a census hull, its producers running, its temperatures spread across 250-750 K.
> That fixture checks its own postconditions, because two runs of a hull that built nothing agree
> perfectly and agreement is the whole assertion. `SolverAb` holds the capture and the comparison.

> **Where a claim is checked against something other than the model.** `LegacyFormulas` is a
> verbatim copy of the original mod's equations, so a test can show exactly how the rewrite
> differs rather than asserting that it differs. `Reference` works sun visibility out the slow,
> obvious way — intersect the ray with every cell's cube — so the shadow map is compared against
> geometry rather than against itself. `benchmarks/field-dump/` is three CSVs a real world
> produced. A test that asks the model the same question twice agrees with whatever the model
> happens to do.

> **Two divergence defects are pinned as present**, so that fixing either fails a test rather than
> moving a number nobody is watching.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | Extended `LoadDialTests` to the charge duration, which is the number `G8` is now scored against: a drive holds 3 MWh, draws 32 MW and keeps 80 % of it, so it fills in 421.9 s. Every figure is read off the game's own definition, because a duration invented here would be the criterion being scored against an assumption. |
| 2026-08-23 | Indexed `LoadDialTests`, which checks that the pair grid's third axis reaches the blocks and reaches nothing else — a sweep dial that reached nothing would report *no change* in exactly the shape of one that reached everything and changed nothing, which is the failure the retest set's `reach.csv` exists for. Conductivity above all: the whole point of that axis is that it is not transport. |
| 2026-08-23 | Indexed `ConductionPaceTests`, which guards a ratio rather than a number: the game has two conduction paces — one for solids, one for the coolant loop's fluid coupling — and nothing made them agree. Moving one alone weakens every loop relative to the structure it competes with, and the only thing that said so when it happened was four balance tests failing for what read like unrelated reasons. |
| 2026-08-23 | Indexed `AirCostTests`, which pins the arithmetic under `C19`: demand is a conductance times the step over a capacity, so it is exactly proportional to the step — which is why every atmospheric figure taken at `Frequency` 8 was half — and lowering the clock divides every stiffness term while raising conductivity restores only conduction. Two rigs, one conduction-limited and one convection-limited, because a hull mixes the two in proportions nobody chose. The population figures it stands beside cannot be pinned here and are not; they are scored by `tools/corpus/air.py`. |
| 2026-08-23 | Indexed `HotTailSyncTests`, which covers the protocol around the packet rather than the packet: which grid a message is about, when a server sends one, when a client asks again, and what a ledger drops when a player leaves. It is here because a session is the one place none of it can be checked — registration and addressing are host code, the policy is not, and two of its cases are failures that are silent by construction (`P2`). |
| 2026-08-23 | Indexed `HotTailTests` and `ClientInputTests`, which cover the two halves of `B4`'s fix: the packet a server sends a client about the blocks that are about to fail, and the claim that a degraded client *input* is a standing bias rather than a perturbation. Both carry their own rig guards as tests — an undegraded client must agree exactly, and a run where nothing on the server ever failed must say so rather than report that the readout agreed (`E8`). |
| 2026-08-23 | Indexed `ScriptWhitelistTests`, which is the one class here that judges the mod against the *game* rather than against the model: it reads `Data/Scripts` for framework types the game's script whitelist refuses, which a green mod build cannot tell you. It is also why Roslyn is a package reference — syntax only, to tell a type from a field of the same name. |
| 2026-08-23 | Indexed the three natural-feedback classes. `IncandescenceTests` is the unusual one: it carries a numerical integration of Planck's law against the CIE observer and re-derives every constant the shipped glow uses, so nothing in it compares the mod to a figure the mod produced (`E7`). |
| 2026-08-22 | Condensed the five balance commands from a section each to one table. Each of them restated a conclusion argued in full on [balance.md](../docs/balance.md), [balance-lab.md](../docs/balance-lab.md) or [stiffness.md](../docs/stiffness.md), which is how the reactor fraction below came to be wrong here and right there. This page is now what to run and where the answer is argued, and keeps only what is the harness's own business rather than the finding's. |
| 2026-08-22 | Corrected the reactor's shipped waste fraction, quoted here and on [thermal-model.md](../docs/thermal-model.md) as 0.02 against the 0.01 in `Cubes.xml` and on [balance.md](../docs/balance.md#reactor-waste-heat). |
| 2026-08-22 | Added the standard header and this change log. |
| 2026-08-22 | Fitted cooling to ships people actually built, closing the retrofit gap the balance criteria depended on. |
| 2026-08-21 | Brought the quoted suite size onto something the suite checks, so a count in prose cannot silently become a historical curiosity. Gated the one corpus test that was running ungated — 4 m 57 s of every run since its fixture landed. |
| 2026-08-20 | Opened the page as the suite's index: every class of tests filed under a subject, checked by `EveryTestClassIsInTheIndex`, with what each suite is *for* living in its own summary where it cannot drift from the code. |
