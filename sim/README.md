# Isolated simulation environment

Runs the thermal simulation outside Space Engineers, so it can be built, tested, profiled and
debugged in seconds instead of by loading a world.

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

dotnet test                                    # the whole suite (1,026 tests)
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

## Block balance

`balance` is neither a scenario nor a benchmark: it costs every block the mod ships and measures it
against the vanilla blocks it competes with, reading the shipped `.sbc` and `Cubes.xml` at run time
rather than from a hand-written catalogue.

```bash
dotnet run --project Thermodynamics.Sim -- balance
dotnet run --project Thermodynamics.Sim -- balance --csv out/
```

It reports five tables — every block costed and measured, what the vanilla heat sources put in, what
a panel delivers end to end, which dial actually moves that number, and the heat pump and coolant
rings across their ranges. The conclusions are pinned by `BalanceTests`. See
[balance.md](../docs/balance.md); the short version is that a coolant sink face couples six times
harder than a bolt joint, and no surface property comes close to being worth as much.

## Reactor waste heat

`reactors` answers the one balance question `balance` cannot: what fraction of a reactor's output
should become heat. The fraction spans three orders of magnitude of rated output — 0.5 MW on a
small-grid small generator against 300 MW on a large-grid large one — so it cannot be picked by
analogy with the thruster's.

```bash
dotnet run --project Thermodynamics.Sim -- reactors
```

Every vanilla reactor at six candidate fractions and three loads, in two rigs: **bare** in shadow
with every face on a 2.7 K sky, which is the coolest a reactor can possibly run, and **skinned**
under one cell of light armour, which is how one is actually installed. The shipped 0.02 is the
fraction where the bare column survives everywhere and the skinned column does not. Pinned by
`ReactorWasteHeatTests`; argued in [balance.md](../docs/balance.md#reactor-waste-heat).

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

**`--linear` matters.** Balance collection runs concurrently, because a settling problem is a pure
function of a ship and a scenario and nothing about a temperature changes because another core was
busy. Anything whose figure is a *duration* has to run one at a time, because there every other
core is contention rather than speed — and no number carries a note saying which it was. The two
modes produce identical matrices; only the clock differs. See
[balance-lab.md](../docs/balance-lab.md#running-the-lab-parallel-and-linear).

## Balance profiles

`profiles`, `sweep` and `features` are the whole-system counterpart to `balance`: not "is this block
worth building" but "is this world configuration worth running". They live in the harness and change
nothing that ships.

```bash
dotnet run --project Thermodynamics.Sim -- profiles          # one rig, four worlds
dotnet run --project Thermodynamics.Sim -- sweep --csv out/  # every scenario and worst case
dotnet run --project Thermodynamics.Sim -- features          # mechanism switches in combination
```

Realism turns out to be the cheapest configuration and the least responsive; the substep estimate
explains nearly every failure; and both the arcade profile and the shipped default diverge on cases
a player can build. See [profiles.md](../docs/profiles.md).

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
| `report` | Everything at once, as a diffable CSV — including the worst cases a plain hull never reaches: air in the compartments, plumbing, blocks past their rating, three hull shapes in three worlds, and fleets of up to a hundred grids. Every scenario row carries what it actually built, so one that builds nothing cannot report a cost of zero. Also: the ladder, every feature measured both marginally and in isolation, every profile, and the substep cap across its range — plus a noise floor so a reader can tell a small cost from no cost. `--baseline <csv>` compares against an earlier run. See [benchmarks.md](../docs/benchmarks.md). |
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

1,026 tests across:

> **The bit-identity suites share one fixture.** Five of them pin an optimisation against the
> thing it replaced — the precomputed environment rows, the fixed source row, the gated
> conduction clamp, the batched diagnostics, a step spread across frames — and all five build from
> `Hulls.Driven`: a census hull, its producers running, its temperatures spread across 250-750 K.
> That fixture checks its own postconditions, because two runs of a hull that built nothing agree
> perfectly and agreement is the whole assertion. `SolverAb` holds the capture and the comparison.

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
* block balance: that every shipped block is priced, weighs something, gets its own thermal entry
  and carries plumbing exactly when it should; and that the conclusions drawn from the balance pass
  — the radiator beating the armour it displaces, a coolant sink beating every surface dial, the
  heat pump passing through all three of its limits — cannot quietly invert
* the lab runner: that results keep the order of their inputs in both modes, that one item
  throwing does not lose the rest, and — the invariant parallel mode rests on — that parallel and
  linear produce the same matrix
* the load model: that only a thruster carries thrust, since gyros spell torque with the same
  element and reading it drove a real hull to 342,000 K
* blueprint reading: that a ship comes back with its name and every block, that an empty
  SubtypeName is the base armour cube, that one modded block disqualifies a ship, that a block of
  the wrong grid size is refused, and that a real subscribed ship builds a simulation that steps
* the block derivation: that specific heat is the exact mass-weighted mean, that a window comes out
  glass and an armour block steel, that a battery is more fragile than a reactor, that a plushie is
  fabric, that an unknown component falls back to steel, and that no invented material sits outside
  the range the real ones span
* reactor waste heat: that every block type delivering power through the source component converts
  some of it, that no reactor cooks itself with every face on open space, and that a large one
  buried in hull at full rating does — so the fraction still means what it was chosen to mean
* the profile machinery: that the settings and material hooks reach a scenario, that the material
  override is applied exactly once, that a profile's conduction pace lands on the number the solver
  uses, and that no two profiles derive to the same world
* two divergence defects, pinned as present so that fixing either fails a test rather than moving a
  number nobody is watching
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
