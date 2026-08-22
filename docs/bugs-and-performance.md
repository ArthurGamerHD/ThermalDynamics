# Bugs and performance findings

> **Superseded on performance.** This page records an investigation at eight thousand blocks. The
> same exercise repeated at a million is in [load-and-hitching.md](load-and-hitching.md), which
> supersedes the performance section below: the "cost of scale" item at the end of the suggested
> order of work has since been measured, and most of it fixed. The correctness findings here still
> stand.

Findings from extracting the simulation into [`tests/`](../tests) and putting it under test. Each
correctness item says whether it is confirmed by a test, and where the fixed behaviour lives.

Severity is about impact on a running game, not on how hard it is to fix.

> **Status.** The live mod now runs the rewritten model, so the model defects below are fixed in
> the shipping path rather than only in `tests/`. What is still open is measurement: the questions
> these findings raise — how often real grids clamp, what the room mapper costs on a real ship —
> are answered by switching telemetry on (`/thermal telemetry on`) and reading the cost and
> substep sections of the report. See [telemetry.md](telemetry.md).
>
> This document is a record of an investigation, not a description of the current code. Legacy
> file names below — `ThermalCell.cs`, `ThermalGridMapper.cs`, `ThermalGridSolar.cs`,
> `ThermalGridStorage.cs` — name the pre-rewrite sources the findings were made against and are
> deliberately not links: those files no longer exist. For what the simulation does now, read
> [thermal-model.md](thermal-model.md).

---

## Findings from the first field run

Twenty 44,632 cell capital ships plus sixty projections, run to a world close: 741 frames,
4,802 grid steps, 43.8 million block updates, 12.3 % of real time inside the mod. The report is
the evidence for everything in this section; the numbers quoted are from it.

The single most useful thing it established is that **block placement is not a main-thread-only
path**. The game builds pasted and projected grids on worker threads, so everything reachable
from `ThermalGrid.AddBlock` runs concurrently with itself. Three of the four defects below are
that one fact, arriving at three different unguarded collections.

### F1. The definition catalog's scratch list was shared across threads — **critical** — *fixed*

442 blocks were lost to `ArgumentOutOfRangeException` thrown out of `List.EnsureCapacity`, inside
`ThermalBlockCatalog.BuildSurfaces`. That is what a `static List<MountRect>` looks like when two
worker threads clear and fill it at once. The exception aborted the whole `AddBlock`, so each one
is a block the simulation never had — silently absent from conduction, radiation and damage for
the life of the world.

**Fixed** — the mount list is a local, and the model cache is behind a lock: the probe and the
insert are guarded, the expensive build is not, and a duplicate build races to the same answer
and discards the loser. Both costs are once per block *type* per session, so neither is on a hot
path. `BlockSurfaceBuilder.BuildSurfaces` is pure given its own list; *confirmed by*
`StressFindingsTests.BuildingSurfacesConcurrentlyMatchesTheSingleThreadedAnswer` and
`BuildingSurfacesDoesNotMutateOrRetainTheCallersMounts`.

### F2. The telemetry registries were shared across threads — **high** — *fixed*

The same cause, twice more. Ten `ArgumentException`s out of `Telemetry.GetBlockType` for a key a
`TryGetValue` had reported missing one line earlier, and two `NullReferenceException`s raised
inside `Dictionary.Insert` from `RegisterGrid` — a torn bucket array, not a null key.

**Fixed** — one lock over the grid, block-type and anomaly registries. `Anomaly` is included
because the adapter's own exception handlers call it from whichever thread threw.

### F3. Reading electric power off a component that has none — **high** — *fixed*

`ThermalBlock.AttachSource` asked every source component for its electricity output. A hydrogen
tank, an oxygen farm and an ice-fed generator all carry a source component with no electric type
in it, and the game's by-type accessors index a dictionary rather than probing it, so the call
threw `KeyNotFoundException` out of `MyResourceSourceComponent.GetTypeIndex`. Being thrown from
inside `Attach`, it aborted binding — another class of block quietly missing from the simulation.

**Fixed** — `AttachSource` and `AttachSink` check `ResourceTypes` / `AcceptedResources` before
asking. The subscription is kept either way, because a sink can gain a type after it is built and
the change event already filters on the resource id.

### F4. A fresh solver's substep estimate was infinity — **medium** — *fixed*

`RequiredSubsteps` is the question a host asks *before* stepping — whether this grid is
affordable this frame. It read the mirrored node arrays, which only `Step` fills in, so on a
solver that had never stepped it divided conductance by a thermal mass of zero and answered
`+Infinity`.

**Fixed** — the public entry point synchronises the state it reads; `Step` calls the private
form so a step still syncs once. *Confirmed by*
`StressFindingsTests.SubstepEstimateBeforeTheFirstStepIsUsable`, which previously asserted only
"not NaN" — the assertion that let this through.

### F5. Cold start reported a 0 K sky — **low** — *fixed*

Every grid reported a minimum ambient of 0.0 K and a minimum solar figure of 0 W, exactly once
each, on its first sample: the host reads the environment the solver used, and before the first
step that was a default-constructed state. It dragged the reported mean ambient from 2.700 K to
2.645 K and made the coldest sky of the session a physical impossibility.

**Fixed** — the solver starts in vacuum at `VacuumTemperature`.

### F6. Rate of change reported one substep instead of the whole step — **medium** — *fixed*

The field ships spent every step at six substeps. `LastDeltaTemperature` was written once per
*substep*, so it held the last substep's movement, not the step's — while all three of its
consumers treat it as the step's. The HUD multiplies it by the step rate to display K/s, the
anomaly classifier recovers the previous temperature by subtracting it, and the per-block-type
distribution records it. For a decaying exchange the last substep is a fraction of the total, so
every one of those under-reported by roughly the substep count.

**Fixed** — the temperature at the top of the step is kept, and the reported change is measured
against it in the same write-back loop that publishes the new temperature. That also removes one
heap write per node per substep from the innermost loop. *Confirmed by* `SolverReportingTests`.

### F7. Doors never sealed, so no room with one in it was ever a room — **critical** — *fixed*

A field grid of one 3x3x3 armour shell with a single sliding door mapped as **zero sealed rooms**
for its whole life. Its numbers say why on their own: a padded search box of 150 cells came back
125 external, and 150 − 125 = 25 is the armour count exactly. The door cell, the reactor and the
interior were all outdoors.

The cause is that `ThermalBlockCatalog.Seals` read only the definition's pressurisation table.
That table is derived from mount points that *fully cover* a cell face, and an airtight sliding
door's mount points along the way through are the 5 % frame either side — so the table marks the
face not pressurised. The game does not stop there: `MyGridGasSystem.IsAirtightBlock` falls
through to `IsDoorAirtight`, which seals a closed door by a rule per door family and never
consults the table. Reading half the rule made every door a hole.

**Fixed** — the adapter now mirrors both halves: the table, then the door rule (sliding doors
seal local forward, generic airtight doors forward and backward, other doors every face that
carries no mount point). Two smaller divergences went with it. An explicit `<IsAirTight>false</IsAirTight>`
now settles the question the way the game settles it, instead of falling through to the table.
And an open door no longer stops sealing altogether: the model carries a second surface set for
the open state, so a door keeps the sides it is bolted in by and opens only the way through.
*Confirmed by* `DoorSealingTests` and `IncrementalRoomTests`, and reproducible offline as the
`first-room` scenario.

The report gained the diagnostic that would have said this in one line rather than in arithmetic:
per grid, how the mapper classified every cell of the search box and which blocks it left
outdoors, and per block definition, how much of each of its six faces seals. See
[telemetry.md](telemetry.md).

---

## Model defects

These are wrong physics rather than wrong code. They are the reason a rewrite is worth doing
rather than a patch.

### M1. Conduction ignores thermal mass entirely — **critical**

`ThermalCell.PrecalculateVariables` derives two coefficients:

```
C = 1 / (SpecificHeat * Mass * gridSize) * TimeScaleRatio
k = Conductivity * (SpecificHeat * Mass * gridSize) / (5 * Area * largestFace)
```

and the update multiplies them together. `SpecificHeat`, `Mass` and `gridSize` cancel exactly:

```
ΔT = Conductivity * Σ(contactArea) / (5 * Area * largestFace) * TimeScaleRatio * (Tn − T)
```

A 500 kg light armour block and a 50 000 kg one heat at the same rate from the same neighbour.
Thermal inertia only affects radiation and waste heat, never conduction — so `SpecificHeat`
does about half the job a mod author would expect it to.

*Confirmed by* `ConductionTests.OriginalConductionIgnoredThermalMass`.

### M2. Conduction does not conserve energy — **critical**

Each cell computes its own coefficient from its own geometry, so the two ends of a joint
disagree about how much heat crossed it. For a 1×1×1 block bolted to a 3×3×4 block, one update
takes 8 333 J out of the large block and puts 5 000 J into the small one. The missing 3 333 J
leaves the simulation, every update, at every asymmetric joint.

The sign of the error depends on which block is larger, so a grid can gain energy as easily as
lose it.

*Confirmed by* `ConductionTests.OriginalConductionDidNotConserveEnergy`.

**Fixed** in [`ThermalLink`](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalLink.cs): one
symmetric conductance per joint, computed as two conductors in series
(`G = A / (L₁/k₁ + L₂/k₂)`), applied equally and oppositely.

### M3. Aerodynamic friction never heats anything — **high** — *fixed in the live mod*

`ThermalCell.cs:452`:

```csharp
DeltaTemperature += DeltaRadiation + DeltaFriction;   // friction included here
Temperature      += DeltaRadiation;                   // but not here
...
DeltaTemperature  = (C * deltaTemperature);           // and this overwrites the line above
Temperature      += DeltaTemperature;
```

`DeltaFriction` is computed, briefly stored, then discarded. `FrictionAtSpeedsAbove` has no
gameplay effect at all; only the debug colouring responds, because it reads the raw watts inside
`CalculateFriction`.

**Fixed** — friction is applied to `Temperature` alongside radiation, and the dead
`DeltaTemperature +=` line is gone. `DeltaTemperature` keeps its existing meaning, the
conduction delta, which is what the HUD's "Peak dT" and the debug overlay already displayed.

*Confirmed by* `ConvectionSolarFrictionTests.FrictionActuallyHeatsTheBlock`.

### M4. Damage scales with the update rate — **high** — *fixed in the live mod*

`HandleCriticalTemperature` applies the full overshoot as damage on every cell update, so
`Frequency = 4` destroys blocks four times faster than `Frequency = 1` at the same temperature.
A performance setting silently changes the difficulty.

*Confirmed by* `DamageTests.DamagePerSecondDoesNotDependOnStepRate`. The fixed model multiplies
by the step length; `ThermalSettings.DamageIsPerSecond = false` restores the old behaviour.

**Fixed** — `HandleCriticalTemperature` multiplies the overshoot by `TimeScaleRatio`, the length
of one update in seconds, so `OverheatDamagePerKelvin` now reads as damage per kelvin per
second. At the default `Frequency = 4` this is a **4× reduction** in damage rate; scale
`OverheatDamagePerKelvin` in `Cubes.xml` if the old pace was the intended one.

### M5. Specific heat is roughly 250× below physical — **medium** — *fixed in the live mod*

The definitions use `SpecificHeat` values of 1–6 while radiation uses the real Stefan-Boltzmann
constant and real areas in m². Steel is about 500 J/(kg·K). The result is that heat capacity is
two to three orders of magnitude too small relative to radiated power, so blocks respond almost
instantly: a 3 300 kg heavy armour block at 800 K falls below 300 K in about 100 seconds of
simulated time, and every scenario reaches equilibrium within a few minutes.

That may well be the intended game feel, but it is worth making deliberate. Either move
`SpecificHeat` to real J/(kg·K) and scale the radiation term for pace, or keep the values and
document that they are a game unit. Right now the two halves of the equation are in different
unit systems.

*Observable in* the `vacuum-soak` scenario.

**Fixed** — made deliberate rather than removed. `SpecificHeat` is now real J/(kg·K) in every
definition (steel 450, copper 385, aluminium 900, water-glycol 3400), so both halves of the
equation are in the same unit system and a definition reads as a description of the material.
The game's pace comes from one setting, `HeatTimeScale` (default 225), which divides every heat
capacity — blocks and coolant alike. Dividing capacity is exactly running thermal time faster,
so equilibrium temperatures and every ratio between mechanisms are untouched; `HeatTimeScaleTests`
holds that claim to account, including against the quartic radiation term.

Worth knowing when retuning: because the old values were flat 1–6 while real materials are not,
the *relative* pace between block types has moved. Against the default block, reactors now
respond 2.25× faster, thrusters 1.5× faster, radiators 4× slower, and the coolant fluid 3×
slower.

### M6. Solar occlusion is all-or-nothing per grid — **medium**

`FrameSolarOccluded` is a single flag for the whole grid, so one asteroid clipping the sun line
shades an entire station including faces nowhere near it. There is no self-shadowing at all: the
dark side of a ship absorbs exactly as much as the lit side, weighted only by face direction.

A per-block implementation exists, fully commented out, in
`ThermalGridSolar.cs`.

### M7. Convection ignores still air on unfavourable faces — **low**

`DirectionalIntensity` returns 0 for a face pointing away from the airflow, and convection is
multiplied by it directly, so a face in the lee of the wind convects nothing at all. In still
air (`windSpeed == 0`) the direction vector is zero and *no* face convects, which is why a
becalmed ship in atmosphere behaves like one in vacuum.

The rewritten model blends a floor into the directional term
(`0.5 + 0.5 * directional`), so still air still carries heat away.

---

## Code defects

### C1. `LargestFace` under-reports for most non-cubic blocks — **high** — *fixed in the live mod*

`ToolHelper.LargestFace` seeded both running maxima at 1 and only updated the runner-up when a
new maximum arrived:

```csharp
int s1 = 1, s2 = 1;
for (int i = 0; i < 3; i++)
    if (vector[i] >= s1) { s2 = s1; s1 = vector[i]; }
```

For 1×5×2 — the shipped radiator — it returns 5 instead of 10. Ascending inputs like 2×3×4
happen to work, which is why it survived. The value is the denominator of `k`, so affected
blocks conduct roughly twice as fast as intended.

Worse than that: the extents are read in grid space (`(Block.Max + 1) - Block.Min`), so
**rotating a block changed its conductivity**. A radiator laid out along one axis conducted at
twice the rate of the same radiator turned 90°.

*Confirmed by* `GridMathTests.LargestFaceAreaFixesTheOriginalOrderDependentResult`.

**Fixed** — [`GridMath.LargestFaceArea`](../Data/Scripts/Thermodynamics/Core/Util/GridMath.cs)
divides the volume by the smallest dimension, which is order independent by construction, and
`ToolHelper` has since been deleted; the original is kept verbatim as
[`LegacyFormulas.LargestFace`](../tests/Thermodynamics.Tests/LegacyFormulas.cs) so the test can
still demonstrate the difference. Of the blocks this mod ships only the two radiators (1×5×2)
change: they conduct at half their previous rate, in every orientation. Vanilla and third-party non-cubic blocks are
affected wherever their largest dimension precedes their second largest.

### C2. `RemoveNeighbor` can throw — **high** — *fixed in the live mod*

`ThermalCell.cs:409` tests the wrong variable:

```csharp
int j = n2.Neighbors.IndexOf(this);
if (i != -1)                    // should be j
    n2.Neighbors.RemoveAt(j);   // RemoveAt(-1) when the link is one-sided
```

Any asymmetric neighbour list — which grid merges and mechanical detachment can produce — turns
a skip into an `ArgumentOutOfRangeException` inside a block-removal event handler.

**Fixed** — the guard now tests `j`.

### C3. Door handlers double on every door cycle — **critical** — *fixed in the live mod*

`ThermalGridMapper.cs:111`:

```csharp
public void OnBlockAddOrUpdate(IMySlimBlock block)
{
    if (block.FatBlock is IMyDoor)
        ((IMyDoor)block.FatBlock).DoorStateChanged += (state) => OnBlockAddOrUpdate(block);

    CalculateBlockSurfaceStates(block);
    BeginCrawl();
}
```

The handler calls the method that subscribes it. C# snapshots the invocation list, so a state
change fires the *n* existing handlers, each of which subscribes one more: the count **doubles**
on every open or close. `OnBlockRemoved` tries to detach with a newly allocated lambda, which
removes nothing.

After ten cycles one door carries 1 024 handlers, and every subsequent state change runs 1 024
full surface calculations and 1 024 whole-grid room crawl restarts (see P1 and P2, which this
multiplies). A busy airlock will stall a server inside a few minutes of play, and the handlers
keep the block alive after it is removed.

**Fixed** — `ThermalGrid.DoorStateHandlers` maps a door's `EntityId` to the delegate that was
attached to it. `OnBlockAddOrUpdate` subscribes only when the door has no entry, so re-entry
from the handler no longer adds one, and `OnBlockRemoved` detaches the stored delegate instead
of a newly allocated lambda.

### C4. Save data quantises and perturbs the live simulation — **medium**

`ThermalGridStorage.cs:46` stores
temperature as a `short` and writes the truncated value back onto the running cell. Every
autosave snaps the whole grid to whole kelvin. Both `Pack` and `PackLoops` carry a `TODO`
acknowledging it.

*Confirmed by* `StorageCodecTests.TheLegacyFormatLosesTheFraction`. The
[v2 format](../Data/Scripts/Thermodynamics/Core/Storage/ThermalStorageCodec.cs) keeps the
fraction and never touches live state; v1 still loads.

### C5. Position keys alias beyond ±512 blocks — **medium**

`Flatten` packs X, Y and Z with a 1024 stride into a 32-bit int, so `(0,0,0)` and `(1024,−1,0)`
produce the same key. Grid coordinates that far out are rare but reachable on large stations,
and the collision silently merges two blocks' temperatures in both the runtime map and the save
file. `Unflatten` is also wrong for negative coordinates: it uses truncating division.

*Confirmed by* `GridMathTests.LegacyKeyAliasesOutsideItsSafeRange`. Fixed by a 64-bit key with a
21-bit stride, plus a corrected legacy decoder for reading old saves.

### C6. Loop temperatures are saved by list index — **medium**

`PackLoops` keys on a loop's position in `ThermalLoops`, which the crawler rebuilds in whatever
order blocks happen to be added. Two loops on one grid can swap temperatures across a reload.
The index is also a single byte, capping a grid at 256 loops.

Fixed by an order-independent signature hashed from the ring's block positions
([`CoolantLoop.RefreshSignature`](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoop.cs)).

### C7. Coolant loops are not deduplicated — **medium**

`OnAddDoCoolantCheck` runs a fresh crawl for every pipe placed and appends any closed ring it
finds without checking whether those blocks already belong to a loop. `OnRemoveDoCoolantCheck`
removes only the first loop containing the block. The crawler also special-cases the small grid
pump by subtype name, multiplying its step by three — any new multi-cell coolant block needs the
same hack.

Fixed by making ports carry the cell they live on, and by keying discovered rings on their member
set. *Confirmed by* `CoolantLoopTests.ARingIsFoundOnlyOnceNoMatterWhereTheSearchStarts` and
`AMultiCellPumpIsWalkedEndToEnd`.

### C8. Block variant group references a subtype that does not exist — **low**

[BlockVarientGroups.sbc](../Data/CubeBlocks/BlockVarientGroups.sbc) lists
`Gauge_LG_CoolantPipe_Straight_Sink`; the definition is `..._SingleSink`. The large-grid
single-sink straight pipe is missing from its variant group. The small-grid group is correct.

### C9. `RecentlyRemoved` grows without bound — **low**

Every removed block's temperature is recorded so a grid split can restore it, but entries are
only ever removed when a split claims one. On a long-lived world the dictionary grows for the
lifetime of the grid.

### C10. Config file is written but never read — **low**

`Settings.Load()` and `Save()` are complete and have no callers; `ThermalGrid.Init` always
assigns `GetDefaults()`. `ThermodynamicsConfig.cfg` has no effect. Combined with four debug
toggles defaulting to `true`, a fresh install ships with block recolouring, on-screen text and
drawn rays enabled, and no supported way to turn them off.

---

## Performance findings

Measured on this machine, release build, 8 000 blocks (20×20×20 light armour), single thread.

> **The shape of that benchmark flatters the results.** A solid cube has the smallest exposed
> fraction, the densest bounding box and the shortest conduction path of any shape a grid actually
> takes. Against a ship-shaped grid the room mapper does roughly **8× more work per block**, and
> three times as many blocks are exposed to the environment. See
> [scale-design.md §10](scale-design.md#10-grid-shape-changes-the-arithmetic) for the measured
> table, and `GridShapes` / `GridMetrics` in the harness for reproducing it.
>
> **And the solver figure in it is not a measurement of the solver.** The cube is one block type
> left to settle, so nearly every link joins two cells at the same temperature — and the
> conduction loop skips a link whose ends agree before doing any arithmetic. It also solves in a
> single substep. The field run's ships needed six substeps every step with a gradient across
> almost every joint, which is between six and forty times the work per step that this row
> implies. The `solver` scenario measures that case instead, and reports **nanoseconds per link
> visit** so the number survives a change of grid size or substep count.

| Metric | Value |
| --- | --- |
| Conduction links | 22 800 |
| Build + full surface map + room map | 51 ms |
| Solver step | 0.128 ms |
| Cost per simulated second at `Frequency = 4` | 0.512 ms |
| Welding 400 blocks, room map coalesced | 2 ms |
| Welding 400 blocks, room map per block | 105 ms |

### P1. The room map restarts from scratch on every block change — **critical**

`OnBlockAddOrUpdate` and `OnBlockRemoved` both call `BeginCrawl`, which clears every queue and
restarts the flood fill over the grid's whole bounding volume. Welding, grinding, pasting a
blueprint or a ship taking fire therefore triggers one full-volume flood fill per block.

The cost is O(bounding volume) per block, so it grows with the *cube* of ship size while the
work being done is a single block.

Measured above at 50× for 400 blocks in a small volume; on a real ship, where the bounding
volume is far larger than the number of blocks being welded, the gap is much wider.

**Fixed** by `RoomMapper.RequestRestart`, which only marks the map stale. Any number of requests
before the next `Step` collapse into one pass. *Confirmed by*
`RoomMapperTests.ManyRestartRequestsCollapseIntoOnePass`.

### P2. Per-cell logging in the block placement path — **critical** — *fixed in the live mod*

`ThermalGridMapper.cs:321` writes a log
line for every *cell* of every block placed, unguarded by any debug setting:

```csharp
MyLog.Default.Info($"[CalculateBlockSurfaceStates] {cell} - {DebugSurfaceStateText(state)}");
```

`DebugSurfaceStateText` builds a `StringBuilder` and appends 30 formatted values per call, and
each line goes through synchronous file IO. Pasting a large grid produces tens of thousands of
lines and a visible stall. This is the single cheapest fix on the list.

**Fixed** — that line and the per-room line in `CreateRoom` are commented out, matching the rest
of the debug logging in the file.

### P3. Allocation per surface calculation — **high**

`CalculateBlockSurfaceStates` allocates a `Queue<Vector3I>` and two `List<MountPoint>` per block,
and `FindSurfaceArea` allocates two more lists per neighbour pair. `CalculatekA` allocates a new
`float[]` every time a neighbour is added or removed. On a grid being welded this is continuous
garbage in the game's main thread.

The rewritten conduction graph builds once per topology change into pooled buffers.

### P4. `GetNeighbours` and mount-point transforms run per pair — **medium**

`FindSurfaceArea` re-transforms both blocks' mount points on every call, for every neighbour of
every block. Mount transforms depend only on the definition and the orientation, so they can be
computed once per placed block.

The rewritten model precomputes grid-space surface bits once in `BlockInstance` and reads them
as a packed int.

### P5. Update order forces a scheduling workaround — **medium**

Because cells are updated in sequence and read their neighbours' current temperature, the result
depends on iteration order — which is why the original alternates sweep direction every pass and
carries `Frame`/`LastTemprature` bookkeeping on every cell.

Accumulating watts from the start-of-step temperatures and applying them together removes the
order dependence, deletes the direction-flipping machinery, and makes the pass trivially
parallelisable if that is ever wanted. *Confirmed by*
`ConductionTests.ConductionResultDoesNotDependOnBlockOrder`.

### P6. Static pooled lists shared across grids — **low**

`_overlapResultPool` and `_gridPool` in `ThermalGrid` are `static` but used as per-grid scratch
space. It is safe only because everything runs on one thread, and it silently blocks any future
attempt to thread grid updates. The `_gridPool` loop in `PrepareSolarEnvironment` also does
nothing but `continue` — presumably meant to skip occlusion by physically connected grids.

### P7. Every grid raycasts to the sun every simulation frame — **low**

`PrepareSolarEnvironment` casts a 15 000 km line and walks every overlapping entity per grid per
simulation frame. With many grids in a world this is the most expensive thing the mod does that
is not the solver. The sun direction changes slowly; the result can be cached and refreshed on a
longer cadence, or shared between grids that are physically connected.

### P8. No stability bound on the integrator — **medium**

The original has no guard beyond `Temperature = max(0, Temperature)`. A light block with a
high-conductivity neighbour and a low `Frequency` can overshoot and oscillate with growing
amplitude; the clamp at zero turns that into a one-sided runaway rather than stopping it.

The rewritten solver derives the number of substeps from the stiffest node
(`RequiredSubsteps`) and clamps each pairwise exchange so neither side can overshoot the other.
*Confirmed by* `StabilityTests.AnAbsurdStepStaysBoundedAndConservesEnergy` and its companion
`WithoutTheClampAnAbsurdStepBlowsUp`, which shows the same grid diverging past 10⁵ K with the
clamp disabled.

### P9. The substep loop, measured properly — **done, ~8 %**

Baseline from the `solver` scenario: 40,008 cells, 90,519 links, seeded across 250-750 K, best of
seven runs.

| | ms per step (3 substeps) | ns per link visit |
| --- | --- | --- |
| Before | 2.532 | 10.1 |
| After | 2.323 | 8.7 |

Three changes, all of them removing work rather than approximating it:

* **The stability estimate was computed twice per step.** `Step` asked `RequiredSubsteps` for the
  substep count and then asked again to decide whether the cap had bound. It walks every node
  cubing a temperature; the second walk produced no new information.
* **The conduction loop read its links out of a `List<ThermalLink>`**, copying a sixteen-byte
  struct through a bounds-checked indexer to use twelve bytes of it. The three fields the loop
  needs are mirrored into flat arrays at rebuild time, alongside the node state that was already
  mirrored. `ContactFaces` is diagnostic and stays in the list.
* **`LastDeltaTemperature` was written per node per substep** — see F6. Correcting it to describe
  the whole step also takes the write out of the innermost loop.

**Measured and rejected: partitioning nodes by exposure.** The environment pass tests every node
for exposed faces once per substep, and it looked worth keeping a dense list of the exposed ones
so buried blocks are never visited. It measured within noise on both a hollow ship (88 % of cells
are exposed — a ship is mostly skin) and a solid cube (where the buried path is already two array
reads and an add). It was reverted: the hottest loop in the mod is the wrong place to keep state
that does not pay for itself.

**Still open.** At six substeps a 44,632 cell ship costs 23 ms a step, and twenty of them share no
budget — the fleet is twenty times one ship, which `fleet` confirms is per cell and not per grid.
Nothing here changes that shape; it is 8 % off a number that needs to come down by more than
that. The larger wins are structural and are described in
[scale-design.md](scale-design.md): activity tracking so settled regions stop stepping, lumping,
and multirate stepping so cold structure steps at a fraction of the rate its hot neighbours do.

---

## Suggested order of work

1. ~~**P2** and **C3**~~ — **done**. The two hot-path log lines are commented out and door
   handlers are now tracked per `EntityId` and detached on removal. **C2** went with them: the
   guard in `RemoveNeighbor` tests `j`.
2. ~~**C1, M3, M4**~~ — **done**. `LargestFace` is order independent, friction is applied to the
   block, and damage is scaled by the update length. All three shift balance — radiators conduct
   at half their old rate, fast atmospheric flight now actually heats the leading face, and
   overheating destroys blocks 4× slower at `Frequency = 4` — so the retune of
   `Cubes.xml` these imply is still outstanding. (`Frequency` was the default when this was
   written and is now 8, which halves that figure again; the rate is part of the claim.)
3. ~~**P1**, **M1**, **M2**, **C4**–**C7**, **C10**, **M5**~~ — **done** with the rewrite. Room
   mapping coalesces, conduction is one symmetric conductance per joint, the v2 save format keeps
   the fraction and reads v1, loops are keyed by signature, and `Settings.Load` is called from
   `Session.Init`.
4. ~~**F1**–**F6**~~ — **done**. The first field run's four crash classes and two reporting
   defects. F1 and F3 were both losing blocks from the simulation outright, which makes them the
   most consequential things on this page.
5. ~~**P9**~~ — **done**, ~8 % off a solver step, and a benchmark that measures the loop rather
   than the path around it.
6. **The retune of `Cubes.xml`** that C1, M3 and M4 imply — radiators conduct at half their old
   rate, fast flight now heats the leading face, and overheating destroys blocks 4× slower at
   `Frequency = 4` — which is no longer the default, and the shipped 8 halves it again. Still
   outstanding, and it is a balance decision rather than a defect.
7. ~~**The cost of scale**~~ — **mostly done**, and measured properly rather than extrapolated.
   See [load-and-hitching.md](load-and-hitching.md). The one-shot rebuilds got what this asked
   for and more: placing and removing a block are now incremental rather than budgeted, the room
   mapper's scan and the exposure and shadow publishes are budgeted, and a step's substep count is
   bounded so a large grid runs at a lower rate smoothly instead of at full rate in lurches. What
   is left of this item is the steady state — activity tracking and multirate stepping, still
   only designed, in [scale-design.md](scale-design.md).
