# Performance work

The record of the performance passes over this repository: what each iteration measured, what it
proposed, what it changed, and what the change was worth on a stated machine. It is the log of the
*work*; the instrument the work is measured with is [benchmarks.md](benchmarks.md), and the figures
quoted here are taken from that report unless a row says otherwise. This page does not restate what
a step costs or how the report is read — it says what was done about it and how each claim was
checked.

> The rules argued here are stated canonically in [rules.md](rules.md): `M4` `M5` `M6` `M7`
> `D7` `D8` `W5`, and the principles P1, P4 and P6 they follow from.

| Looking for | Go to |
| --- | --- |
| The report itself, and how to read a comparison | [benchmarks.md](benchmarks.md) |
| What a grid costs as it grows, and what makes it stutter | [load-and-hitching.md](load-and-hitching.md) |
| Why a handful of light fittings sets a capital ship's cost | [stiffness.md](stiffness.md) |
| Where the model is going at a million blocks | [scale-design.md](scale-design.md) |
| Open performance rows | [backlog.md](backlog.md), section D |

## How a pass is run

Every iteration takes the same seven steps, and the order is the point: the measurement comes
before the idea, and the oracle before the change.

1. **Measure.** `bench report` at the pass's own starting commit and at its tip, minutes apart, on
   one machine, inside `heavy run` (`M7`, `W5`). Where the report cannot see the thing in question,
   a lab is written first and the report is extended to carry the figure.
2. **Identify.** A candidate is a place the numbers accuse, and the counts are a place to look
   rather than a verdict (`D7`).
3. **Branch.** One branch per iteration, off the working branch's tip, merged back with a merge
   commit once the change has passed every check below.
4. **Validate in a lab.** The candidate is measured on one hull with the change flipped between
   timed blocks, because two hulls are two allocations with two cache colourings.
5. **Implement only if it pays and holds.** A change is kept when its saving is outside the noise
   floor (`M5`) and it breaks neither the three invariants (`C6`) nor a stated intent.
6. **Pin it.** An optimisation is asserted bit-identical to the code it replaced on a fixture that
   proves it exercised something (`D8`), and a defect it found is pinned so it cannot return.
7. **Clean up.** The code and the pages it replaced go; what stays describes the present.

**The machine.** Every figure on this page was taken on the repository's 32-core development
machine, which is shared with three other projects (`W5`). It was not idle on 2026-08-26: two
editor language servers held a core each for the whole day and the swap was full. Measured rather
than assumed: the committed baseline's own commit, rebuilt and re-run unoptimised on this day,
read a calibration of 146 ms against the 106 ms it recorded on 2026-08-22, so the machine is
about **1.4×** slower than the baseline's — and nothing more. Every larger gap on this page is the
code, and each one is named. The comparisons here are taken minutes apart in one held window,
which is what makes the ratios readable when the absolutes are not.

## The iterations

| # | Date | Subject | Verdict | Where |
| ---: | --- | --- | --- | --- |
| 1 | 2026-08-26 | The harness measured unoptimised code | **kept** — every configuration now compiles optimised | [Iteration 1](#iteration-1--the-harness-measured-unoptimised-code) |
| 2 | 2026-08-26 | The ladder's `build` column measured the hull generator | **kept** — generator 9× cheaper, and outside the clock | [Iteration 2](#iteration-2--the-ladders-build-column-measured-the-hull-generator) |
| 3 | 2026-08-26 | An orientation is a signed permutation | **kept** — block construction halved at every size | [Iteration 3](#iteration-3--an-orientation-is-a-signed-permutation) |
| 4 | 2026-08-26 | The room mapper reads a snapshot of the sealing | **kept** — the room map 3× cheaper at half a million blocks | [Iteration 4](#iteration-4--the-room-mapper-reads-a-snapshot-of-the-sealing) |

## Iteration 1 — the harness measured unoptimised code

**What was found.** Nothing in `tests/` set `<Optimize>`, and every command this repository runs —
`dotnet run --project Thermodynamics.Sim`, `dotnet test` — builds the Debug configuration. A Debug
assembly carries `DebuggableAttribute(DisableOptimizations)`, and the JIT honours it: it compiles
minimal-optimisation code with no register allocation across statements, no bounds-check
elimination and no inlining. **Every millisecond this repository had ever published was taken on
code the game never runs.**

**What it was worth, measured.** The same commit, the same machine, the same held window, four
minutes apart; the committed report at `--size 32000 --max 125000`, fastest of three (`M4`).

| figure | unoptimised | optimised | ratio |
| --- | ---: | ---: | ---: |
| ladder 32,000, step | 18.33 ms | 5.21 ms | **0.28** |
| ladder 125,000, step | 58.27 ms | 18.36 ms | 0.32 |
| features, everything on | 17.90 ms | 5.22 ms | 0.29 |
| features, everything off | 0.959 ms | 0.248 ms | **0.26** |
| step shape, fixed per step (fit) | 2.07 ms | 0.84 ms | 0.41 |
| step shape, per substep (fit) | 0.602 ms | 0.163 ms | 0.27 |
| step shape, prologue and estimate | 0.718 ms | 0.169 ms | **0.23** |
| step shape, write-back | 0.261 ms | 0.071 ms | 0.27 |
| step shape, row fill, clamp live | 1.485 ms | 0.300 ms | **0.20** |
| overshoot clamp, resolved, always clamped | 30.24 ms | 11.49 ms | 0.38 |
| diagnostics, cost of being measured | 5.04 ms | 0.65 ms | **0.13** |
| fleets, 100 grids, whole fleet | 53.50 ms | 14.66 ms | 0.27 |
| noise, spread of five identical runs | 0.353 ms | 0.092 ms | 0.26 |

**The ratio is not a constant, and that is the finding.** A uniform 3.5× would have left every
comparison this repository ever made intact, since a benchmark exists for ratios. It is not
uniform: the row fill is 5× and the cost of diagnostics 7.7×, where the clamped conduction loop is
2.6×. So the published *shares* were wrong too — the row fill was quoted as a fifth of a step and
the diagnostics as a 15 % surcharge, and both were mostly the price of unoptimised code around a
field store. Any figure on [benchmarks.md](benchmarks.md) that is a *share* of a step, and any
optimisation whose case rested on hoisting a load out of a loop, was measured on an instrument
that exaggerated exactly that kind of work.

**What changed.** `tests/Directory.Build.props` sets `<Optimize>true</Optimize>` for every project
and every configuration. `dotnet run` and `dotnet test` still build Debug; Debug is now optimised.
Pinned by `OptimisedBuildTests`, which reads `DebuggableAttribute` off the built core, harness and
test assemblies and fails if any of them says the optimiser is off — a line dropped from a props
file fails nothing else.

**What it is not.** It is not the game. The game compiles `Data/Scripts` with its own compiler and
runs it on .NET Framework 4.8; the harness is .NET 9. The 6–8× gap between a field dump and the
harness recorded on [load-and-hitching.md](load-and-hitching.md) was measured against the
unoptimised harness and is now larger, not smaller — the harness got faster and the game did not.
Absolute nanoseconds still do not transfer, which [benchmarks.md](benchmarks.md#what-it-measures)
already says; what transfers is the shape of a curve, and the shapes are now taken on an optimising
JIT, which is what the game has.

**The fast lane** ran 1,662 cases green on the optimised build before the change was committed.

## Iteration 2 — the ladder's `build` column measured the hull generator

**What was found.** `PerformanceReport.Build` started its stopwatch before `PlaceCensus`, which
since `C26` bolts every partial-mount block by trying all 24 orientations: each candidate rotated
each face through a freshly built matrix and probed a dictionary for the neighbour, with the 24
orientations themselves re-enumerated through `Enum.GetValues` per block — 144 matrices and 144
probes a block. Split on a 32,800-block hull, optimised: the generator was **1.0 s** and the
simulation's own build — surfaces, links, loops, rooms, exposure — **0.10 s**. The ladder's `build`
column reported the sum under a name that reads as the second, and the calibration row carried the
same generator inside its 4,000-block build. `LoadBenchmarks.MeasureBuilt` had always dealt its
hull outside the clock, so the repository's two `build` figures disagreed in scope (`P1`).

**Two changes.** The search resolves the 24 orientations' rotated faces once, into a static table
in `PipeFitter.AllOrientations`'s order, and probes each block's six neighbours once; same
candidates in the same order with the same tie-break, so the chosen orientations are the same
ship. The report deals the hull before the clock starts, in the ladder and the calibration alike.
`FacingItsNeighbours`, a second orientation search reached by nothing, is gone (`D2`).

**Pinned.** `CensusBoltTests` runs the original search — kept verbatim in the test — beside the
table-driven one over four hulls and requires the same orientation at every cell, after checking
that more than a twentieth of the blocks were turned at all (`D8`, `E8`). It failed on its first
run: the table's first entry is not the identity, and producers had been given index zero. That is
the pin doing the one thing it is for.

**What it was worth.** The tip before and after this iteration, optimised, in one held window,
each twice, fastest kept (`M4`):

| figure | before | after | ratio |
| --- | ---: | ---: | ---: |
| machine, calibration | 285.4 ms | **94.0 ms** | 0.33 |
| ladder 8,000, build | 303.3 ms | **27.2 ms** | 0.09 |
| ladder 32,000, build | 1,193.6 ms | **170.6 ms** | 0.14 |
| ladder 125,000, build | 5,314.7 ms | **1,143.5 ms** | 0.22 |
| ladder 8,000 / 32,000 / 125,000, step | 1.19 / 5.21 / 18.49 ms | 1.15 / 5.22 / 18.39 ms | 0.96 / 1.00 / 0.99 |
| features, everything on | 5.16 ms | 5.40 ms | 1.05 |
| step shape, prologue and estimate | 0.169 ms | 0.167 ms | 0.99 |

And the generator on its own, dealt into a `GridBuilder` with nothing simulated, old search against
new in the same window, fastest of five:

| blocks | old `PlaceCensus` | new `PlaceCensus` | ratio | the simulation's build, for scale |
| ---: | ---: | ---: | ---: | ---: |
| 8,904 | 285.0 ms | **18.6 ms** | 0.065 | 18.3 ms |
| 32,800 | 977.9 ms | **57.3 ms** | 0.059 | 93.6 ms |
| 126,731 | 3,797.2 ms | **240.4 ms** | 0.063 | 622.1 ms |

Fifteen to seventeen times cheaper, and now under the build it feeds rather than ten times over
it. Every `Hulls.Driven` in the suite dealt a hull through the old search, so the suite's own
duration is where the rest of this saving lands.

The `step` rows are the control: the hull is the same ship, so nothing a step does may move, and
nothing did beyond the noise row. The `build` rows now describe the mod's load path, which is the
figure they were always read as. **The calibration row moved by 3×**, which means every
cross-machine comparison [benchmarks.md](benchmarks.md) invited through it was, since `C26`,
mostly comparing two copies of the generator.

**What it does not say.** The ladder's `build` is still the harness's simulation build and not the
game's world load — the adapter's mirror of a `MyCubeGrid` is outside this instrument, as it always
was.

## Iteration 3 — an orientation is a signed permutation

**What was found.** Reading the load path for the same shape as iteration 2 found it in shipped
code: `BlockOrientation.Rotate`, `Unrotate` and `RotateFace` each built a `Matrix` with
`Matrix.CreateWorld` and transformed through it, and a `BlockInstance` calls them about ten times
per cell at construction — every cell's grid position, and every face's rotated surface bits. That
is the path a world load and a blueprint paste take, per block.

**What changed.** The struct holds a table, built once by its static constructor *from its own
matrix*, of where each local axis and each local face lands per orientation; a rotation is three
multiplies by ±1 and three adds. The twelve forward/up pairs that are not orientations keep the
matrix path unchanged. Exact on integers either way, so the two forms are identical rather than
close.

**Pinned.** `BlockOrientationCacheTests` compares the table against the matrix over every integer
vector of a 7×7×7 cube for all thirty-six pairs — forward, back and per face — and checks
separately that every legal orientation rotates and unrotates to where it started, so a table built
from a degenerate matrix could not agree its way through. `FleetParallelTests` records why the three
static tables may be shared by grids on different threads: built once, never written after.

**What it was worth.** `bench load` is the one instrument here that constructs blocks inside its
clock — a heavy-armour hull placed block by block, registered, then rebuilt — and its `adding
blocks` figure is the block construction and registration; `RebuildAll` is the control, since it
constructs no block. Before and after, optimised, one held window, each twice, fastest kept:

| blocks | adding blocks, before | after | ratio | `RebuildAll`, before → after |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 310 ms | **176 ms** | 0.57 | 999 → 989 ms |
| 1,000,294 | 1,779 ms | **862 ms** | 0.48 | 9,672 → 9,629 ms |

Half of the cost of placing a block was building matrices to rotate a one-cell block at the
identity. The control moved by less than its own repeat-to-repeat spread (±3 %). A world load at a
million blocks is 11.2 s on this machine and the block half of it is now under a second; the ten
seconds left are `RebuildAll`, which is where iteration 4 goes.

## Iteration 4 — the room mapper reads a snapshot of the sealing

**What was found.** `bench scale` split the build at 505,566 blocks: **2,954 ms of 4,388 ms was
the room map**, against 392 ms for the links and 345 ms for exposure. The flood fill visits every
cell of a bounding volume fourteen times the block count, asks two questions per face — is this
face sealed on either side, is that cell solid — and each was answered from `SurfaceMap`'s
dictionaries: two hash probes per face, twelve per cell, eighty million probes a pass. It is also
what [backlog.md](backlog.md) `D2` measures at 7,207 ticks to converge at a million blocks, and
what a world load waits on.

**What changed.** When a pass begins, the mapper copies the structural self-airtight bits of every
occupied cell in its search box into one byte per cell, and answers both questions from that with
array reads. The copy is exact because a change to what a block seals already requests a restart
(`ThermalSimulation.RefreshBlock` marks the topology dirty on any structural change; `AddBlock` and
`RemoveBlock` always do), so a pass never read a surface map that had moved under it — and now it
reads one that cannot. The buffer is the bounding volume in bytes, 6.8 MB at 505k blocks and 14 MB
at a million, retained between passes and regrown only when the box outgrows it; a box past
`int.MaxValue` cells falls back to the dictionaries rather than allocating. The dictionary path
stays behind `SnapshotSealing` for the pin.

**Pinned.** `RoomMapSnapshotTests` publishes the map both ways and requires the same room count,
solid count, external count, the same cells in the same order in every room, the same venting, and
the same portals — on a census hull with compartments, on a shell with a door (the portal case), and
then steps the pressurised hull both ways to identical temperatures through `SolverAb`. Each fixture
asserts it found rooms and portals first (`E8`).

**What it was worth.** `bench scale`, ship shape, before and after in one held window, twice each:

| blocks | bounding cells | rooms, before | rooms, after | ratio | build, before → after | links, exposure (control) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 32,800 | 328,640 | 35 / 43 ms | **28 / 28 ms** | 0.65 | 130 → 165 ms | 15 / 10 → 18 / 19 ms |
| 126,731 | 1,499,616 | 271 / 328 ms | **176 / 185 ms** | 0.54 | 592 → 525 ms | 92 / 81 → 101 / 82 ms |
| 505,566 | 6,838,104 | 2,065 / 2,874 ms | **893 / 913 ms** | 0.31 | 3,917 → 2,328 ms | 370 / 329 → 399 / 334 ms |

The fastest of the two is the figure; the pairs are printed because the *before* rows at 505k
disagree with each other by 40 %, which is the dictionary path's own sensitivity to the state of the
cache on a shared machine, and the *after* rows by 2 %. The control columns did not move beyond
their spread. What is left of the room map's cost is the bitset, the frontier queue and the
published map's own per-cell writes; the `settle` column — ticks to converge after a placement — is
unchanged at every rung, because the mapper's budget is counted in cells and this iteration made
each cell cheaper rather than fewer.

**The next rung, measured rather than guessed.** Split further at 126,731 blocks after this change:
`SurfaceMap.Rebuild` 173–254 ms, links 107–178, rooms 156–188, exposure 82–152, room air 3–7. The
surface map's two per-cell dictionaries — built, then refreshed at seven probes per cell per layer —
are now the largest single term of a load.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-26 | Opened, with the first four iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |
