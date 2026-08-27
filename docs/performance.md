# Performance work

The record of the performance passes over this repository: what each iteration measured, what it
proposed, what it changed, and what the change was worth on a stated machine. It is the log of the
*work*; the instrument the work is measured with is [benchmarks.md](benchmarks.md), and the figures
quoted here are taken from that report unless a row says otherwise. This page does not restate what
a step costs or how the report is read — it says what was done about it and how each claim was
checked.

> The rules argued here are stated canonically in [rules.md](rules.md): `M4` `M5` `M6` `M7` `M13`
> `D7` `D8` `W5`, and the principles P1, P4 and P6 they follow from.

| Looking for | Go to |
| --- | --- |
| The report itself, and how to read a comparison | [benchmarks.md](benchmarks.md) |
| What a grid costs as it grows, and what makes it stutter | [load-and-hitching.md](load-and-hitching.md) |
| Why a handful of light fittings sets a capital ship's cost | [stiffness.md](stiffness.md) |
| Where the model is going at a million blocks | [scale-design.md](scale-design.md) |
| Open performance rows | [backlog.md](backlog.md), section D |

## How a pass is run

Every iteration takes the same eight steps, and the order is the point: the measurement comes
before the idea, and the oracle before the change.

1. **Measure.** `bench report` at the pass's own starting commit and at its tip, minutes apart, on
   one machine, inside `heavy run` (`M7`, `W5`). Where the report cannot see the thing in question,
   a lab is written first and the report is extended to carry the figure.
2. **Identify.** A candidate is a place the numbers accuse, and the counts are a place to look
   rather than a verdict (`D7`).
3. **Branch.** One branch per iteration, off the working branch's tip, merged back with a merge
   commit once the change has passed every check below.
4. **Validate in a lab, on an instrument that can resolve the claim.** The candidate is measured on
   one hull with the change flipped between timed blocks, because two hulls are two allocations
   with two cache colourings — and on a *stage timed by itself*, best of many, because a figure
   read off the whole build or the whole report carries every other stage's noise. This pass found
   both errors that follow from getting it wrong: a change kept would have been dismissed at a 6 %
   floor (iteration 10, really worth 10–18 %), and a change dismissed had to be re-judged at a 1 %
   floor before the dismissal meant anything (iteration 6, really worth nothing).
5. **Implement only if it pays and holds.** A change is kept when its saving is outside the noise
   floor (`M5`) and it breaks neither the three invariants (`C6`) nor a stated intent.
6. **Pin it.** An optimisation is asserted bit-identical to the code it replaced on a fixture that
   proves it exercised something (`D8`), and a defect it found is pinned so it cannot return.
7. **Clean up.** The code and the pages it replaced go; what stays describes the present.
8. **Refresh the lanes.** Take the runner's per-class durations and tag what crossed two seconds,
   because nothing checks that rule and it has rotted twice in two days.

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
| 2 | 2026-08-26 | The ladder's `build` column measured the hull generator | **kept** — generator 15–17× cheaper, and outside the clock | [Iteration 2](#iteration-2--the-ladders-build-column-measured-the-hull-generator) |
| 3 | 2026-08-26 | An orientation is a signed permutation | **kept** — block construction halved at every size | [Iteration 3](#iteration-3--an-orientation-is-a-signed-permutation) |
| 4 | 2026-08-26 | The room mapper reads a snapshot of the sealing | **kept** — the room map 2.3× cheaper at half a million blocks | [Iteration 4](#iteration-4--the-room-mapper-reads-a-snapshot-of-the-sealing) |
| 5 | 2026-08-26 | The surface map's two layers in one dictionary | **kept** — the surface map 2× cheaper | [Iteration 5](#iteration-5--the-surface-maps-two-layers-in-one-dictionary) |
| 6 | 2026-08-26 | One row per link in the conduction loop | **dropped** — ±1.2 % at a 1 % floor, sign changing | [Iteration 6](#iteration-6--one-row-per-link-in-the-conduction-loop-tried-and-dropped) |
| 7 | 2026-08-26 | The room map's solid set is a bitset | **kept** — exposure 2× cheaper | [Iteration 7](#iteration-7--the-room-maps-solid-set-is-a-bitset-over-the-search-box) |
| 8 | 2026-08-26 | The flood fill steps an index, not a vector | **kept** — another fifth off the room map | [Iteration 8](#iteration-8--the-flood-fill-steps-an-index-not-a-vector) |
| 9 | 2026-08-26 | The fast lane had rotted to 37 s | **kept** — 4 s again, eleven classes tagged | [Iteration 9](#iteration-9--the-fast-lane-had-rotted-to-37-s) |
| 10 | 2026-08-26 | The interior scan steps its index | **kept** — the room pass 10–18 % cheaper | [Iteration 10](#iteration-10--the-interior-scan-steps-its-index) |
| 11 | 2026-08-26 | The cell tables are keyed on `GridMath.Key` | **kept** — links 2× cheaper, surfaces 3× | [Iteration 11](#iteration-11--the-cell-tables-are-keyed-on-gridmathkey) |

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
and every configuration, and it is `M13` in [rules.md](rules.md) so the next harness cannot repeat it. `dotnet run` and `dotnet test` still build Debug; Debug is now optimised.
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
| 32,800 | 328,640 | 35 / 43 ms | **28 / 28 ms** | 0.80 | 130 → 165 ms | 15 / 10 → 18 / 19 ms |
| 126,731 | 1,499,616 | 271 / 328 ms | **176 / 185 ms** | 0.65 | 592 → 525 ms | 92 / 81 → 101 / 82 ms |
| 505,566 | 6,838,104 | 2,065 / 2,874 ms | **893 / 913 ms** | 0.43 | 3,917 → 2,328 ms | 370 / 329 → 399 / 334 ms |

> **The ratio column is fastest against fastest, and it read 0.65 / 0.54 / 0.31 for a few minutes
> after this section was written** — the fastest *after* divided by the slowest *before*, which is
> the arithmetic `M4` exists to prevent and which flatters every row. Corrected in place (`E10`).

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

## Iteration 5 — the surface map's two layers in one dictionary

Every occupied cell was
held twice, in two `Dictionary<Vector3I, int>` keyed on the same cell — the live layer and the
structural one — and refreshed in the same call, so a refresh probed each of six neighbours twice and
a rebuild inserted every cell twice. The two states are packed into one `long` now, live in the low
half and structural in the high: seven probes a cell rather than fourteen, and one table rather than
two, which is also the surface map's retained memory halved. `SurfaceMapPackingTests` keeps the
two-dictionary map verbatim and holds the packed one to the same answer on every cell and every
neighbour of a census hull and of a shell with a door — rebuilt, added block by block, with blocks
removed, and with the door open, which is the only state in which the two layers differ and the test
asserts that they do.

**What it was worth.** The `RebuildAll` split, each stage on its own clock on a dealt hull, at the
commit before and after, one held window, fastest of two:

| blocks | `SurfaceMap.Rebuild`, before | after | ratio | links, rooms (controls) |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 86 ms | **32 ms** | 0.37 | 76 / 138 → 95 / 181 ms |
| 505,566 | 653 ms | **291 ms** | 0.45 | 426 / 863 → 428 / 866 ms |

Better than the halving the probe count predicts at the small rung, because the second table was
also the second set of cache lines; and the controls at 505k did not move.

## Iteration 6 — one row per link in the conduction loop: tried and dropped

**What was tried.** The innermost loop gathered a link from three parallel arrays — two node
indices and a conductance — beside the node rows it scatters into. A `LinkRow` struct put the three
in one array: one stream and one bounds check where there were three of each, the same arithmetic in
the same order, so every bit-identity suite and every byte-identical scenario passed unchanged.

**What it measured.** The full report at the commit before and after, optimised, one held window,
twice each, fastest kept:

| figure | before | after | ratio |
| --- | ---: | ---: | ---: |
| ladder 8,000 / 32,000 / 125,000, step | 1.146 / 5.252 / 18.325 ms | 1.209 / 5.190 / 19.366 ms | 1.06 / 0.99 / 1.06 |
| features, everything on | 5.213 ms | 5.217 ms | 1.00 |
| features, conduction, isolated | 0.458 ms | 0.478 ms | 1.05 |
| features, conduction, marginal | 1.675 ms | 1.539 ms | 0.92 |
| overshoot clamp, resolved, always clamped | 11.109 ms | 11.305 ms | 1.02 |
| noise, spread of five identical runs | 0.063–0.128 ms | 0.110–0.186 ms | — |

**Not kept** (`M5`). Nothing moved outside the noise floor, in either direction, on any hull.

**And the report was the wrong instrument to decide it on, which iteration 10 is what proved.** Two
nominally identical step measurements inside one report disagree by two to five per cent, so
"inside the noise" there means *under about five per cent* — which is a floor wide enough to hide a
real saving. Iteration 10 was dismissed on that same instrument and turned out to be worth 10 to
18 % when the room pass was timed on its own, so this was re-judged the same way: the substep loop
alone, one settled 32,800- and 126,731-block hull, twenty steps a repeat, fifteen repeats, fastest
kept.

| hull | with link rows | with three arrays | ratio |
| --- | ---: | ---: | ---: |
| 32,800 blocks, 64,964 links | 5.4736 ms | 5.4098 ms | **1.012** |
| 126,731 blocks, 247,350 links | 18.7644 ms | 18.8290 ms | **0.997** |

Repeating each leg puts the best-to-best variation within one tree at 0.6 to 1.3 %, so this
instrument resolves about a per cent — and the effect is ±1.2 % with the sign changing between
rungs. The substeps run are identical on both sides (8,485 and 7,272), so the two are the same
arithmetic at the same count.

**So the revert stands, and now it stands on evidence rather than on a floor that was too wide.**
The finding is about the loop: the three link streams were already sequential and prefetched, so
folding them bought nothing, and what the loop pays for is the two gathers and two scatters into
the *node* rows, which no layout of the link side can touch. The commit is reverted in the same
branch, so the attempt is in the history and the tree carries no code that measured as nothing.

## Iteration 7 — the room map's solid set is a bitset over the search box

The published map held its solid cells in a
`HashSet<Vector3I>` — about forty bytes a member on a hull that is a third to three quarters
structure — and every exposure face asks `IsExternal`, which asked that set first. It is a
`CellBitset` over the search box now: an eighth of a byte a cell, and a bit read where there was a
hash. `RoomMapSolidTests` checks every cell of the box, inside and out, against the surface map's
own sealing rather than against the map (`E7`), and that a second pass starts from an empty set.

**What it was worth.** The same split, at the commit before and after, fastest of two:

| blocks | exposure, before | after | ratio | rooms, before → after | resident MB (ladder) |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 126,731 | 61 ms | **32 ms** | 0.52 | 181 → 136 ms | 88 → 90 |
| 505,566 | 317 ms | **143 ms** | 0.45 | 866 → 769 ms | 450 → 391 |

Exposure halves because most of its faces are exposed ones, and each of those asked the hash set.
The room pass gains too, from writing bits rather than hashing cells into the set. The resident
column is the ladder's coarse `GC.GetTotalMemory` difference and moves by more than this change
between repeats of one tree, so it is printed and not claimed.

## Iteration 8 — the flood fill steps an index, not a vector

With the sealing snapshot live, a flood step still
derived each neighbour's index in the visited bitset from its coordinates — three subtractions and
three compares — and then again for the snapshot. The cell's index is derived once and each face
adds a precomputed delta; the box test is the only per-face geometry left. Same faces in the same
order, so `RoomMapSnapshotTests`, which runs the dictionary path beside it, is the pin.

**What it was worth.** The same split, at the commit before and after, fastest of two:

| blocks | rooms, before | after | ratio |
| ---: | ---: | ---: | ---: |
| 126,731 | 136 ms | **111 ms** | 0.82 |
| 505,566 | 769 ms | **626 ms** | 0.81 |

Another fifth off, and the room pass now costs about **92 ns per bounding cell** at half a million
blocks — 626 ms over 6,838,104 — against **302 ns** before iteration 4 on this same instrument, and
432 ns in the pass's first reading of it.

**The ladder taken in the same window disagrees, and it is the ladder that is wrong.** Its rows for
this commit carry a *step* column 28 % above the rows before it — a control no change in this pass
can touch — which is another project's work landing on the machine while the window was held (`W5`
is cooperative, and holding it does not stop anyone). The split above was taken first, its own
controls (links, surfaces) held, and it is the figure quoted.

**Where the build stands at 505,566 blocks after iterations 4, 5, 7 and 8**, on one clock: rooms
626 ms, links 503, surfaces 418, exposure 173, block registration 56 — **1.6 s** where the pass
began at 4.4. Links are the next largest term and were not touched.

## Iteration 9 — the fast lane had rotted to 37 s

**What was found.** The suite's own duration is a number no test inside it can read, which is why
[tests/README.md](../tests/README.md#the-two-lanes-and-the-rule-that-sorts-them) says the lane rule
rots. Timed for this pass's iteration log: the whole suite **1 m 22 s** over 2,001 cases on the
optimised build, from 2 m 34 s over 1,884 — and the fast lane **37 s**, where the page said 4.
Per class, from the runner's own log: `DesignedHullTests` 35 s of test time on its own, and ten
more classes past two seconds — `DialReachTests` 11.8 s, `ModHardwareRetestTests`,
`SettingsDialReachTests`, `LoopDialReachTests`, `LoopCoolantMassTests`, `ScenarioTests`,
`ScriptWhitelistTests`, `HeatTimeScaleTests`, `CoolantLoopTests` and `DocumentationTests`, each
between 2.4 and 4.1 s — none tagged, all written or grown in the two days since the lanes were last
sorted.

**What changed.** The eleven carry `[Trait("speed", "slow")]`; thirty classes do now. The fast lane
is **4 s over 1,582 cases** — 5.5, 5.6, 6.2 s of wall clock across three runs (`M4`). Nothing about
the tests moved; what moved is which lane a developer waits for.

**What it does not fix.** The rule is still unchecked, and this is the second time in two days it
has been found rotten by measuring rather than by a test. The honest check would read the runner's
durations back, which is a tool outside the suite; until one exists the refresh is a step of every
performance pass, which this page now lists.

## Iteration 10 — the interior scan steps its index

**What was found.** The interior scan walks every cell of the bounding volume — 3.1 million at
126,731 blocks and 13.7 million at 505,566 — and derived each cell's index twice, once for the
visited bitset and once for the sealing snapshot: three subtractions, six compares and a multiply,
each time. **The scan order is the index order exactly**, x fastest then y then z, so a step of one
cell is a step of one index, wraps included. And `IsStructure` asked a `HashSet<Vector3I>` whether
every sealed cell belonged to a door, on grids that mostly have no door at all.

**What changed.** The scan carries its index and increments it; the door probe tests the count
first.

**Pinned.** `RoomMapSnapshotTests` — which publishes the map both ways and compares it cell for
cell — is the pin, and it was proven against a deliberate off-by-one on the wrap: six of its nine
cases fail. `WalkingABoxInScanOrderAdvancesTheIndexByOne` states the ordering claim where it is
made rather than through the map, over three box shapes including one a single row deep.

**The ladder could not resolve it, and a tighter instrument could.** On the build split the room
pass read 571 ms before and 551 after at 505,566 blocks, against a repeat-to-repeat spread of 6 to
12 % — a figure inside the noise floor has not moved (`M5`), and on that instrument this change had
not. So the room pass was measured **on its own**, on one prebuilt grid, fifteen passes, fastest
kept:

| blocks | cells a pass | rooms, before | after | ratio |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 3,072,469 | 69.4 ms | **57.0 ms** | 0.82 |
| 505,566 | 13,720,674 | 498.3 ms | **446.2 ms** | 0.90 |

Repeated, the same pair reads 71.0 → 61.9 and 510.5 → 450.9: the direction is the same in all four
pairs, and the gap is several times the best-to-best variation between rounds of one tree. **The
cells-visited counter is identical on both sides** — 3,072,469 and 13,720,674 — which is what says
the two are the same walk at a different price rather than two different walks, the same way
`LoadTests` holds a claim on work counters rather than on milliseconds.

**And the spread is the reason this needed its own instrument.** Fifteen passes of the same code
range from 69 ms to 263: on a machine three other projects share, the *worst* of N is about the
machine and only the best of N is about the code (`M4`, `W5`).

## Iteration 11 — the cell tables are keyed on `GridMath.Key`

**What was found.** Links were the largest term never touched, so the link build was timed alone:
at 505,566 blocks the neighbour query is **80 % of it** (282 of 338 ms), and a neighbour query is
six dictionary probes per cell. Probed directly — the hull's own cells, six face neighbours each,
the same hits in the same order — a `Dictionary<Vector3I, …>` with `Vector3I.Comparer` costs **51–58
ns a probe** and a `Dictionary<long, …>` keyed on `GridMath.Key` costs **9–13**. A struct key goes
through a comparer object for its hash and its equality; a `long` does both inline. And the same
table shape sat under the surface map, the room index a pass writes, and the mapper's door cells.

**What changed.** All four are keyed on `GridMath.Key`. That is the key `BlockInstance.Key` and the
frozen room arrays already used — a 64-bit packing injective over any grid the game can hold, which
`GridMathTests` pins — so no new assumption about a grid's extent is made. A dictionary's answers do
not depend on how it hashes, so every existing pin holds unchanged: the packed surface map against
its verbatim two-dictionary reference, the snapshot mapper against the dictionary mapper, the
byte-identical scenarios.

**What it was worth.** Best of fifteen on one prebuilt grid, before and after interleaved in one
window, three rounds; and the build split beside it:

| blocks | neighbour query, before | after | ratio | link build, before | after | ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 126,731 | 44.6 ms | **30.1 ms** | 0.67 | 58.5 ms | **39.1 ms** | 0.67 |
| 505,566 | 276.1 ms | **154.1 ms** | 0.56 | 355.5 ms | **184.4 ms** | 0.52 |

| build stage, 505,566 blocks | before | after | ratio |
| --- | ---: | ---: | ---: |
| surfaces | 308 ms | **100 ms** | 0.32 |
| links | 436 ms | **232 ms** | 0.53 |
| rooms | 513 ms | **323 ms** | 0.63 |
| exposure | 167 ms | **95 ms** | 0.57 |

Every stage that probes a cell table moved, which is what a change to the tables and nothing else
should do. The room pass moves through the room index it writes and the door probe, not through
the flood fill, which already reads the snapshot.

**And the first two attempts to measure this compared the tip with itself.** The "before" probe is
a scratch project whose reference the harness of a worktree, and a `sed` meant to repoint it
matched nothing and said nothing — so two pairings read *no change* with pleasing consistency. The
pairing above is preceded by a check that the two probes carry different core assemblies, and it
refuses to time anything if they do not. That is the same defect the pass keeps finding, in the
instrument rather than the mod: a comparison that cannot fail is not a comparison (`E8`).

---

## What the pass moved

Every figure below is the pass's starting commit against its tip, both built optimised, interleaved
in one held window, each leg twice, fastest kept (`M7`, `M4`). The starting commit is measured
*optimised* on purpose: iteration 1's saving is a property of how the harness was built rather than
of the mod, so building both legs the same way is what leaves the code changes on their own.

| Figure | start | tip | ratio |
| --- | ---: | ---: | ---: |
| **World load, 1,000,294 blocks** | | | |
| block construction and registration | 1,749 ms | **677 ms** | 0.39 |
| `RebuildAll` | 10,126 ms | **2,481 ms** | 0.25 |
| whole load | 12,025 ms | **3,158 ms** | **0.26** |
| **The build ladder** | | | |
| 8,904 blocks | 325.1 ms | **16.8 ms** | 0.05 |
| 32,800 blocks | 1,188.8 ms | **101.8 ms** | 0.09 |
| 126,731 blocks | 5,129.9 ms | **537.4 ms** | 0.10 |
| calibration (4k hull, built and stepped) | 286.2 ms | **83.1 ms** | 0.29 |
| **Memory at 126,731 blocks** | | | |
| retained | 857 B/block | **797 B/block** | 0.93 |
| peak | 1,086 B/block | **999 B/block** | 0.92 |
| **The suite** | | | |
| fast lane | 37 s | **4 s** | 0.11 |
| whole suite | 2 m 34 s, 1,884 cases | **1 m 22 s, 2,001 cases** | — |

**The build ladder's ratio is not all mod code**, and the table would mislead without saying so:
most of the 8,000-block rung is iteration 2 taking the census generator out of the clock, which is
harness. The figure that is all mod is the world load — `bench load` places heavy armour itself and
times only construction and `RebuildAll` — and it is **3.8× faster**, from iterations 3, 4, 5, 7,
8, 10 and 11. The load and memory rows were re-taken after iteration 11 in the same way, against the
commit before it: 910 → 677 ms registering and 4,007 → 2,481 ms rebuilding, interleaved, twice.

**What did not move, which is the control.** Nothing in this pass touched the substep loop, and the
step columns say so: 1.194 → 1.143 ms at 8,904 blocks, 5.381 → 5.373 at 32,800, 19.650 → 19.784 at
126,731, every feature's marginal and isolated cost unchanged, and a bare configuration identical to
three decimal places. The scenario and environment rows read 3 to 6 % higher and the ladder rows
1 % lower — two measurements of the same thing disagreeing by that much is this instrument's own
repeatability on a shared machine, not a change.

**Where the next pass starts.** At 505,566 blocks the build split reads rooms ~323 ms, links
~232, surfaces ~100, exposure ~95, registration ~43 after iteration 11 — about 0.8 s of stage time
where the pass found 4.4 s. The room pass is the largest term again, and what is left in it is the
frontier queue, the visited bitset and the room index's own writes. Below that, `RebuildAll` at a million
blocks is still 4 s of a world load, and the room map still floods a bounding volume fourteen times
the block count — cheaper per cell, and the same number of cells ([backlog.md](backlog.md) `D2`).

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-26 | Opened, with the first four iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |
