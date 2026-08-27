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

# Pass 2 — 2026-08-26, evening

The second pass, started from `84e3be5`, the tip the first one left. It uses the first pass's
lessons as its procedure: every candidate is judged on `bench stages`, and every before/after script
proves its two legs differ before it times anything.

**Where it started.** The same machine, the same shared window; every figure below is the fastest of
fifteen on one prebuilt grid unless the row says otherwise:

| stage, 505,566 blocks | start of pass 2 | work | ns per unit |
| --- | ---: | ---: | ---: |
| surfaces | 81.8 ms | 505,566 cells | 162 |
| links | 196.2 ms | 952,523 links | 206 |
| rooms | 292.4 ms | 13,720,674 cells visited | 21.3 |
| exposure | 95.2 ms | 505,566 nodes | 188 |
| a settled step, granted its substeps | 93.9 ms | 28 substeps | — |
| world load at 1,000,294 blocks | 638 + 2,538 ms | — | — |

*`bench stages --size 500000`, taken at `3296cfd` with the instrument itself in the tree; the scratch
probe read the same commit at 121 / 177 / 311 / 93 / 94.7 ms a few minutes earlier, which is the
same ordering and the same shape with the surface and link rows inside their own spread.*

## Pass 2, iterations

| # | Subject | Verdict | Where |
| ---: | --- | --- | --- |
| 1 | The stage instrument lives in the tree | **kept** — `bench stages`, `StageLabTests` | [Iteration 1](#pass-2-iteration-1--the-stage-instrument-lives-in-the-tree) |
| 2 | A one-cell block's neighbours are six probes | **kept** — links 0.77–0.88 | [Iteration 2](#pass-2-iteration-2--a-one-cell-blocks-neighbours-are-six-probes) |
| 3 | A one-cell block's exposure is one state and six tests | **kept** — exposure 0.65 | [Iteration 3](#pass-2-iteration-3--a-one-cell-blocks-exposure-is-one-cell-state-and-six-face-tests) |
| 4 | The interior scan skips visited cells a word at a time | **kept** — room-map ticks to converge 0.54–0.62 | [Iteration 4](#pass-2-iteration-4--the-interior-scan-skips-visited-cells-a-word-at-a-time) |
| 5 | The environment pass reads one row per node | **dropped** — 0.99 at a 1 % floor | [Iteration 5](#pass-2-iteration-5--the-environment-pass-reads-one-row-per-node) |
| 6 | A one-cell block is built without walking its cells | **kept** — place 0.64–0.83 | [Iteration 6](#pass-2-iteration-6--a-one-cell-block-is-built-without-walking-its-cells) |
| 7 | The flood's box test is one compare on the axis that moved | **dropped** — slower, 1.25–1.30 | [Iteration 7](#pass-2-iteration-7--the-floods-box-test-is-one-compare-on-the-axis-that-moved) |
| 8 | The freeze sorts by radix, after two forms that walked a bitset and lost | **kept** — the publish tick 0.72, rooms 0.85–0.90 | [Iteration 8](#pass-2-iteration-8--the-freeze-walks-a-bitset-in-key-order-instead-of-sorting) |
| 9 | A block's two surface layers share one array unless it is a door | **kept** — place 0.85–0.87 | [Iteration 9](#pass-2-iteration-9--a-blocks-two-surface-layers-share-one-array-unless-it-is-a-door) |
| 10 | What the pass moved | world load 0.70, exposure 0.63, rooms 0.78, links 0.83, the step 1.00 | [Iteration 10](#pass-2-iteration-10--what-the-pass-moved) |

## Pass 2, iteration 1 — the stage instrument lives in the tree

The first pass judged two changes wrongly on the build ladder and had to re-judge both on a scratch
probe that timed one stage alone. That probe is `StageLab` and `bench stages` now: placing blocks,
registering them, surfaces, links, rooms, exposure and a settled step, each on one prebuilt grid, fastest of fifteen, with the stage's
own work counter beside the time — and a work figure that moves between repeats aborts the row,
because two readings of different walks are not a comparison (`P6`). `StageLabTests` holds that
every stage reports work on a hull that exercised it and that the work is the same figure asked
twice (`E8`). It reports nanoseconds per unit of work, which is the figure that transfers between
sizes.

## Pass 2, iteration 2 — a one-cell block's neighbours are six probes

**What was found.** The neighbour query is 80 % of the link build, and at ~50 ns per probed cell
only ~10 of it is the dictionary since iteration 11 of the first pass: the rest is the slab walk
through `BoxGeometry`'s per-axis switches and a `List.Contains` dedupe per candidate. A one-cell
block — nearly every block on a hull — needs neither: it has one candidate cell per face, and a
neighbour is a box, so a box can touch a unit cube on at most one face. Its six answers are
distinct by construction.

**What changed.** `GridModel.GetNeighbours` answers a one-cell block with six probes in face order
and hands everything else to the boundary walk, which stays as `GetNeighboursWalkingTheBoundary`.
Same faces in the same order, so the link list — and with it the order the conduction sum
accumulates in — is unchanged. `GridModelAdjacencyTests` holds the two paths to the same neighbours
in the same order over every block of a census hull and of a grid that mixes unit blocks with bars
and a cube, so both shapes the dedupe exists for are on the fixture.

**What it was worth.** `bench stages --stages links`, the commit before against the tip, the two
cores proven different first, interleaved, two rounds, fastest kept:

| blocks | links, before | after | ratio |
| ---: | ---: | ---: | ---: |
| 126,731 | 47.96 ms | **36.80 ms** | 0.77 |
| 505,566 | 192.53 ms | **169.78 ms** | 0.88 |

The same direction in all four pairs, and the link count identical on both sides. Less at the
large rung, where the probes' cache misses are a larger share of the query and the arithmetic
around them a smaller one.

## Pass 2, iteration 3 — a one-cell block's exposure is one cell state and six face tests

**What was found.** The exposure refresh is the same shape as the neighbour query one stage later:
for every block, every cell of every face of its box, through the same per-axis switches, asking
the surface map for the cell's state on each face. For a one-cell block that is one state read six
times and six face tests.

**What changed.** The one-cell path reads the state once and asks the same two questions per face
in the same order — is this face sealed from the other side, does the space beyond reach the
outside — and hands every larger block to the boundary walk, which stays as a public overload so a test can
reach it. `ExposureFastPathTests` holds the two to the same count on
every face of every block of a mapped census hull and of the mixed grid.

**What it was worth.** `bench stages --stages exposure`, before against after, cores proven
different, interleaved, two rounds, fastest kept:

| blocks | exposure, before | after | ratio |
| ---: | ---: | ---: | ---: |
| 126,731 | 17.74 ms | **11.88 ms** | 0.67 |
| 505,566 | 97.11 ms | **63.43 ms** | 0.65 |



| Date | Change |
| --- | --- |
| 2026-08-27 | Pass 4, iteration 5: the air rebuild and the room-side exposure refresh walk neighbours by key arithmetic — roomair 0.74 at 126k blocks but only 0.95 at 505k, which says the stage is bound by the dictionary probes, not the arithmetic around them. |
| 2026-08-27 | Pass 4, iteration 4: room air is canonical — sorted by node, so a room's links and its starting temperature no longer depend on the path the flood took. The span flood's precondition is met. The iteration also gave the air rebuild its first instrument, and it is **the largest stage on the load path** — 185 ms at 505,566 blocks against the room pass's 134 in a quiet window — and previously unmeasured. |
| 2026-08-27 | Pass 4, iteration 3: the room map answers from a rank over the set it already fills, so the sorted key and room arrays and the radix sort are gone. Exposure 0.45, rooms 0.89, and the pass's allocation is a tenth of what it was when the pass opened. |
| 2026-08-27 | Pass 4, iteration 2: the room cells are one store, hinted from the pass before. Half the remaining allocation, the worst pass a tenth quicker, the best unmoved — and the same commit reading 30 % apart in two windows. |
| 2026-08-27 | Opened pass 4 on the figure pass 3 ended with: the room pass allocated 253 MB per execution. Its first iteration removed 149 MB of that, and a quarter of the stage. |
| 2026-08-26 | Opened, with the first four iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |

## Pass 2, iteration 4 — the interior scan skips visited cells a word at a time

**What was found.** A room pass is two walks over the bounding volume: the floods, which reach every
cell of air and every solid cell beside it, and then the interior scan, which walks every cell of
the box once more to find the ones no flood reached — the start of each enclosed room. By the time
the scan reaches a cell it has almost always been visited, so the scan's whole job is to skip, and
it skipped one cell at a time: half of every pass's charged work, and the half a budgeted tick
spends most of.

**What changed.** The visited bitset walks whole words while they are all ones — sixty-four cells a
step — and the scan's cursor is derived from the index it lands on, by the inverse of the order the
cursor advances in, which the first pass's `WalkingABoxInScanOrderAdvancesTheIndexByOne` holds. The
charge is per word looked at rather than per cell skipped: still cell by cell where cells are
unvisited, never more than one unit per sixty-four. **The map is the same map** —
`RoomMapSnapshotTests` runs the cell-by-cell path beside it — and the test now also asserts that the
skip removed more than half the box's walk, since a skip that skipped nothing would agree perfectly
(`E8`). `NextClearIndex` is pinned at every offset shape an off-by-one could survive the map tests
on: a clear bit at the start of a word, mid-word, past a run of full words, and none before the
bound.

**What it was worth — ticks, which is the figure `D2` is about.** The mapper's budget is counted in
the units the scan charges, so charging per word is what turns a cheaper pass into a *shorter*
one: `bench scale`, before against after, cores proven different, two rounds each:

| blocks | bounding cells | settle ticks, before | after | ratio | room map ms, before → after |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 8,904 | 68,800 | 168 | **106** | 0.63 | 15.8 → 16.7 |
| 32,800 | 328,640 | 211 | **131** | 0.62 | 43.0 → 10.5 / 42.2 |
| 126,731 | 1,499,616 | 794 | **438** | 0.55 | 70.5 → 43.7 |
| 505,566 | 6,838,104 | 3,482 | **1,890** | 0.54 | 368.6 → 313.5 |

The tick count is deterministic — identical between rounds on each side — and it is the count a
world waits on a stale exposure map after a block is placed ([backlog.md](backlog.md) `D2`). The
`+1 spike` column, the worst tick after one block is placed, fell from 145.5 to 96.3 ms at 505k for
the same reason: the first tick of the remap does more of the scan. The milliseconds per pass are
the ladder's and are read for direction only; the stage instrument's figure is this:

| blocks | rooms, before | after | ratio | units charged, before → after |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 50.49 ms | **43.60 ms** | 0.86 | 3,072,469 → 1,622,649 |
| 505,566 | 286.22 ms | **258.39 ms** | 0.90 | 13,720,674 → 7,216,527 |

`bench stages --stages rooms`, before against after, interleaved, two rounds, fastest kept. The
milliseconds move by a tenth because the scan was already the cheap half per cell; the *units* halve,
and the units are what a tick's budget is spent in, which is why the ticks halved and the
milliseconds did not.

## Pass 2, iteration 5 — the environment pass reads one row per node

**What was tried.** The pass read four parallel arrays per node every substep — exposed faces,
the radiation coefficient, the convection row and the source row — beside the temperature it reads
and the watts it writes. One `EnvironmentRow` struct per node puts the four in one array: one
stream and one bounds check where there were four of each, with the fill writing the last two
fields once a step. Same arithmetic in the same order, so the five bit-identity suites and the
byte-identical scenarios pass unchanged. The first pass's iteration 6 tried the same layout on the
*link* side and measured nothing; the node side is the gather the loop actually pays for, which is
why it is worth asking again. Judged on `bench stages --stages solver`, the settled step alone.

**What it measured, and why it is dropped.** Before against after, cores proven different,
interleaved, two rounds, twenty steps a repeat, fifteen repeats:

| hull | step, before | after | ratio |
| --- | ---: | ---: | ---: |
| 126,731 blocks, 24 substeps | 18.50 ms | 18.40 ms | 0.99 |
| 505,566 blocks, 28 substeps | 88.94 ms | 88.24 ms | 0.99 |

Inside the instrument's own floor, with the substep count identical on both sides. Together with
the first pass's link rows this is a finding about the loop rather than about a layout: **neither
side's stream layout is what the substep loop pays for.** The link streams and the node rows are
both sequential and prefetched either way; what is left is the arithmetic, the two gathers of a
temperature per link, and the stores — and the work on the elements is already about two
nanoseconds each at half a million blocks. Reverted in the same branch (`M5`); a change that
measures as nothing is not left in.

## Pass 2, iteration 6 — a one-cell block is built without walking its cells

**What was found.** With the instrument grown two stages, placing a block read **250 ns** at
505,566 blocks — four times registering it — and a one-cell block's construction ran an iterator
over its one cell, re-anchored a rotated box that cannot move off its corner, and rotated surface
bits that are the same on every face through six face lookups.

**What changed.** The one-cell path writes the cell — the block's minimum corner in every
orientation — and rotates only what a rotation can change: a state uniform across all six faces is
returned as it is, since a rotation permutes faces. The cell walk stays as a public method and
`BlockInstanceOneCellTests` holds the two together over all twenty-four orientations for a solid
block, a partly mounted one, and a door shut and open — the uniform short cut is taken by the first
and cannot be by the second, so both sides of it are on the fixture.

**What it was worth.** `bench stages --stages place,register`, before against after, cores proven
different, interleaved, two rounds, fastest kept; register is the control, since nothing in this
change touches it:

| blocks | place, before | after | ratio | register (control), before → after |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 23.31 ms | **14.84 ms** | 0.64 | 7.08 → 5.83 / 7.34 ms |
| 505,566 | 109.59 ms | **90.84 ms** | 0.83 | 26.49 → 24.02 ms |

What is left of placing a block at the large rung is the grid's own index — three dictionary
operations a block — and the block's allocations, which iteration 9 takes one of.

## Pass 2, iteration 7 — the flood's box test is one compare on the axis that moved

**What was found.** Every flood step tested each of six neighbours against all six edges of the box
after constructing the neighbour, when the cell it stepped from is inside the box by construction
and only the axis a face moves along can leave it.

**What changed.** One compare per face, against that edge, before the neighbour is built. The face
order the switch assumes is pinned beside the map tests (`FaceOffsetsAreTheOrderThisSwitchAssumes`),
because a reordering of `Face.Offsets` would pass every map test on a hull whose flood never met an
edge from the wrong side; and a small closed shell, whose padded box the flood meets on every edge
in both directions of every axis, maps the same by both paths.

**What it measured, and why it is dropped.** `bench stages --stages rooms`, before against after,
cores proven different, interleaved, two rounds:

| blocks | rooms, before | after | ratio |
| ---: | ---: | ---: | ---: |
| 126,731 | 46.61 ms | 57.11 ms | **1.23** |
| 505,566 | 245.08 ms | 317.51 ms | **1.30** |

Slower, by a quarter, in all four pairs, on identical work. Six compares against a box in one
expression are six well-predicted branches the JIT keeps in registers; a six-way `switch` that
writes an out-parameter on every path is a jump table and a store per face, and the flood makes
that choice eighty million times a pass. The change is reverted in the same branch (`M5` in the
other direction: a change that is measurably worse is a defect, whatever it was meant to save), and
the two pins written for it — the face order and the closed shell — stay, because they were
statements about the flood that were worth making anyway.

## Pass 2, iteration 8 — the freeze walks a bitset in key order instead of sorting

**What was found.** `bench spike` splits the worst tick after a block is placed by stage, and at
505,566 blocks it is **91 ms, of which 80 is the room map on one tick** — the tick the pass
publishes on. Measured at the commit before iteration 4 as well, to be sure the charging change had
not made it: 81.4 ms there. The mapper is budgeted per cell for the whole of its walk and then, when
the walk is done, sorts every room cell's key in one call — 1.2 million of them — so the one
unbudgeted call in the pass lands on the frame a player was already waiting on.

**What changed.** Room cells are marked in a bitset over the search box as the flood reaches them,
and the freeze walks that bitset in index order. Box-index order is `GridMath.Key` order — both are
z, then y, then x, and a key is `z·2^42 + y·2^21 + x`, monotone in that order for any coordinate a
grid can hold — so the keys come out sorted and the arrays need no sort: one dictionary lookup per
room cell for its room, and a word-skipping walk for the order. **The order claim is checked, not
trusted**: the freeze verifies the keys strictly increase and falls back to sorting if they do not,
recording that it did, and `RoomMapFreezeTests` asserts the fallback is never taken on a hull with
tens of rooms. The bitset walk is pinned at word edges against the cells that were added, in key
order. The bitset is retained per map at an eighth of a byte a bounding cell.

**As first written it was slower, and the pairing said so.** The walk derived each room cell's
coordinates from its index by two long divisions and found each set bit by shifting one at a
time — 1.2 million cells' worth of both — which cost more than the sort it replaced: rooms 252 →
320 ms and the publish tick 80 → 155 ms at 505,566 blocks, in all four pairs. The walk is per word
now: one coordinate derivation per sixty-four cells, an increment with row and plane wraps per set
bit, and a de Bruijn multiply to find each bit without a loop (the game's framework and whitelist
offer no intrinsic for it).

**And corrected, it still lost to the sort — which is the finding.** Rooms 258.9 → 256.5 ms at
505k, level, and the publish tick **80 → 155 ms**. The walk needed one dictionary probe per room
cell to learn its room, and 1.2 million probes into a 1.2 million-entry table are 1.2 million cache
misses; the sort's input was the same dictionary *enumerated*, which is sequential. So the third
form keeps the enumeration and replaces the comparison sort with a radix sort on the offset of
each key from the box's first key — eleven-bit digits, passes that skip themselves where every
digit is zero, two scratch arrays dropped after. The bitset, its walk and the bit finder went with
the version they served (`D2`'s defect class in reverse: code that measured worse is not left in).
`RoomMapFreezeTests` holds the radix order against `Array.Sort` on random keys spanning every digit,
with the rooms carried along, and on sorted, reversed and wide inputs.

**What it was worth, in the third form.** `bench stages --stages rooms`, before against after,
cores proven different, interleaved, two rounds, fastest kept; and `bench spike` for the tick the
change is about:

| blocks | rooms, before | after | ratio | publish tick, before → after | worst tick after a placement |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 126,731 | 47.18 ms | **40.04 ms** | 0.85 | 16.4 → **12.1 ms** | 31.7 → 29.0 ms |
| 505,566 | 246.17 ms | **222.80 ms** | 0.90 | 80.1 → **57.7 ms** | 91.3 → 69.6 ms |

The sort was a third of the pass at 505k and is a tenth now. What is left of the publish tick is the
enumeration, the radix passes' own memory traffic, and the portal and venting work that follows.

## Pass 2, iteration 9 — a block's two surface layers share one array unless it is a door

**What was found.** A `BlockInstance` holds its live surface bits and its structural ones in two
arrays, and the two differ only on a door that stands open. Every other block — and every door
while shut — allocated the same bits twice: a third of a block's surface allocations, on the load
path and on every paste.

**What changed.** The two are one array whenever they cannot differ; a refresh still allocates
afresh, so `ThermalSimulation.RefreshBlock`'s old references remain the snapshot it compares
against. Nothing writes into either array after construction — checked by reading every reader of
`SelfSurfaces` and `StructuralSurfaces`. `BlockInstanceOneCellTests` pins when the layers share,
when they do not, and that a door opening leaves the structural layer where it was.

**What it was worth.** `bench stages --stages place,register`, before against after, cores proven
different, interleaved, two rounds, fastest kept:

| blocks | place, before | after | ratio | register (control) |
| ---: | ---: | ---: | ---: | ---: |
| 126,731 | 19.74 ms | **17.10 ms** | 0.87 | 7.07 → 6.37 / 7.93 ms |
| 505,566 | 107.30 ms | **91.09 ms** | 0.85 | 25.26 → 23.61 ms |

One allocation fewer per block, and a third less surface memory per block — the memory table at
the pass's end carries the retained figure.

, with its start figures taken by the instrument the pass begins by putting in the tree. |
| 2026-08-26 | Opened, with the first four iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |

## Pass 2, iteration 10 — what the pass moved

The pass's first commit with the instrument in the tree (`3296cfd`) against its tip, built the same
way, interleaved in one held window, each stage on its own clock, fastest of fifteen, two rounds;
the world load against the true start (`84e3be5`). The step is the control: nothing kept in this
pass touches it.

| stage, 505,566 blocks | start | tip | ratio | what did it |
| --- | ---: | ---: | ---: | --- |
| surfaces | 84.2 ms | **75.2 ms** | 0.89 | fewer allocations per block (9) |
| links | 177.6 ms | **147.5 ms** | 0.83 | the one-cell neighbour path (2) |
| rooms | 285.8 ms | **222.5 ms** | 0.78 | the word skip (4) and the radix freeze (8) |
| exposure | 92.3 ms | **57.9 ms** | 0.63 | the one-cell exposure path (3) |
| a settled step | 89.6 ms | 89.6 ms | **1.00** | nothing — the control |
| **World load, 1,000,294 blocks** | | | | |
| placing and registering | 648 ms | **500 ms** | 0.77 | iterations 6 and 9 |
| `RebuildAll` | 2,519 ms | **1,713 ms** | 0.68 | iterations 2, 3, 4, 8 |
| whole load | 3,167 ms | **2,213 ms** | **0.70** | |
| **Memory, 126,731 blocks** | | | | |
| retained | 797 B/block | **765 B/block** | 0.96 | one surface array per block (9) |
| peak | 999 B/block | **993 B/block** | 0.99 | |
| **Ticks for a room pass to converge, 505,566 blocks** | 3,482 | **1,890** | 0.54 | the word skip (4) |
| **Worst tick after a placement, 505,566 blocks** | 93.0 ms | **69.6 ms** | 0.75 | the radix freeze (8) |
| **The suite** | 2,014 cases | 2,016 cases, 1 m 21 s | | |

**One row is not claimed.** At 126,731 blocks the surface stage read 8.4 ms at the start and 13.3
at the tip, both rounds, while at 505,566 it improved; nothing in the pass touches
`SurfaceMap.Rebuild`, and the tip's run has two more stages before it that leave the heap in a
different state. It is recorded as unexplained rather than as a regression or a saving, and it is
the first thing the next pass should measure on its own, with the stage list held to one.

**What the pass learned that the first one did not know.**

* **The substep loop is not layout-bound.** Two AoS layouts, one per side of the loop, both inside a
  one-per-cent floor. At about two nanoseconds an element the loop is at the floor of this design,
  and the way down is structural — activity tracking, `D1` — not a cheaper stream.
* **A jump table can lose to six compares** on a path taken eighty million times a pass, by a
  quarter. Measured, not reasoned.
* **A sort of sequential input beats a walk with a random probe per element**, even when the walk
  is linear and the sort is not; and the cure was a better sort, not no sort.
* **The tick a pass publishes on is where its unbudgeted work lands**, and `bench spike`'s per-stage
  worst tick is the instrument that sees it; the stage's total milliseconds did not.
* **Charging per word is what turned a cheaper scan into a shorter pass.** The budget is spent in the
  units the scan charges, so the ticks halved where the milliseconds moved a tenth (`D2`).

**Where pass 3 starts.** Rooms ~223 ms, links ~148, surfaces ~75, exposure ~58, place ~92 at
505,566 blocks. The flood is what is left of the room pass — every air cell of a box fourteen times
the block count, six faces each — and only fewer cells can move it now.

---

# Pass 3 — 2026-08-27

Started from `a81ee0b`, the tip pass 2 left. Its first act is the same as pass 2's: fix the
instrument, because pass 2 ended owing an explanation for a figure it could not account for.

## Pass 3, iterations

| # | Subject | Verdict | Where |
| ---: | --- | --- | --- |
| 1 | The stage lab settles between stages and reports what each allocated | **kept** — and `C4` is asserted now | [Iteration 1](#pass-3-iteration-1--the-stage-lab-settles-between-stages) |
| 2 | The flood reads a cell's own sealing byte once, not once a face | *measuring* | [Iteration 2](#pass-3-iteration-2--the-flood-reads-a-cells-sealing-byte-once) |
| 3 | A room-membership bit in front of the search `IsExternal` makes | *measuring* | [Iteration 3](#pass-3-iteration-3--a-room-membership-bit-in-front-of-the-search) |
| 4 | A pair's touching face is found once, and the link list is sized | *measuring* | [Iteration 4](#pass-3-iteration-4--one-touching-face-per-pair-and-a-sized-link-list) |
| 5 | One box index for both of the sets `IsExternal` reads | *measuring* | [Iteration 5](#pass-3-iteration-5--one-box-index-for-both-sets) |
| 6 | A neighbour's key is the cell's key plus a constant | **kept** — surfaces 0.51 | [Iteration 6](#pass-3-iteration-6--a-neighbours-key-is-the-cells-key-plus-a-constant) |
| 7 | A block carries its grid slot, and a dictionary goes | *measuring* | [Iteration 7](#pass-3-iteration-7--a-block-carries-its-grid-slot) |
| 8 | The flood's frontier is a ring buffer kept between passes | *measuring* | [Iteration 8](#pass-3-iteration-8--a-retained-ring-frontier) |
| 9 | The neighbour walk reports the face it found a neighbour across | **kept** — links 0.80 | [Iteration 9](#pass-3-iteration-9--the-walk-reports-the-face-it-found) |
| 10 | The allocation counter is per thread, not per process | **kept** — the check was wrong in the one place it mattered | [Iteration 10](#pass-3-iteration-10--the-allocation-counter-is-per-thread) |

## Pass 3, iteration 1 — the stage lab settles between stages

Pass 2 ended owing an explanation: at 126,731 blocks the surface stage read 8.4 ms at its start and
13.3 at its tip, in both rounds, and nothing in that pass touched the surface map. The cause is the
instrument. `place` constructs a block instance and a grid entry per block, fifteen repeats over —
two million objects — so every stage the list ran after it measured a different heap.

The lab collects twice between stages now, and reports **what one execution of each stage
allocated**, which is the column that makes such a row explainable rather than odd. The figure is
also a check: a settled step of a census hull must allocate under four kilobytes, which is `C4` —
*nothing allocates on the stepping path* — asserted rather than read off a benchmark. For a rule
whose correct value is zero, a report was never a check.

*Measured alone, the surface stage reads 7.1 ms at 126,731 blocks on the pass-2 tip against the
8.4 ms that pass recorded, and its own spread across rounds on that code is 7.1 to 13.5 ms — so the
row pass 2 could not explain was the instrument's, and this is the correction (`E10`).*

## Pass 3, iteration 2 — the flood reads a cell's sealing byte once

`Reaches` re-read `sealing[index]` — the same byte for all six faces of the cell it was asked about —
on every face, and took the neighbour as an out-parameter the caller had already built. The byte is
read once per cell now, the sealing test that can reject without touching the box runs first, and the
interior flood asks `IsStructureAt` with the index it already holds. Faces in the same order, so a
room's cells arrive in the same order, so its air links and the sum over them are bit for bit what
they were — which is the constraint every change to this flood is under.

## Pass 3, iteration 3 — a room-membership bit in front of the search

Exposure asks `IsExternal` once per unsealed face of every block, and `IsExternal` answered by binary
searching every room cell on the grid — about twenty dependent loads through 1.5 million keys at half
a million blocks — to learn what is nearly always *in no room at all*. A bit per bounding cell
answers that outright; the search stays for callers that want the room's index. The check was written
first, passes against the old code, and was proven to fail on an inverted membership test.

## Pass 3, iteration 4 — one touching face per pair, and a sized link list

The link builder tested for a touching face and then called `CountContactFaces`, which found it
again: a million redundant box overlaps at half a million blocks, on the full rebuild and the
incremental one alike. And the link list was doubled into from empty, which on a million links is
several million struct copies of pure growth; it is sized at two a block first.

## Pass 3, iteration 5 — one box index for both sets

The solid set and the room-membership set cover the same box, so a cell has one index in both, and
`IsExternal` was deriving it twice — three subtractions, six compares and two multiplies, per
unsealed face, per block.

## Pass 3, iteration 6 — a neighbour's key is the cell's key plus a constant

**A key is a sum of the components rather than a packing of bit fields**, so it is linear:
`Key(v + d) == Key(v) + Key(d)`, across zero and across a negative component alike. The surface
rebuild converted a cell to a key seven times per cell and back once; it snapshots keys and self
states now, adds a per-face constant for each neighbour, and writes each cell's derived half exactly
once rather than asking the dictionary for what it had just put there.

**What it was worth.** `bench stages --stages surfaces`, alone and first, before against after,
cores proven different, two rounds, fastest kept:

| blocks | surfaces, before | after | ratio |
| ---: | ---: | ---: | ---: |
| 126,731 | 7.14 ms | **7.11 ms** | 1.00 |
| 505,566 | 52.32 ms | **26.50 ms** | **0.51** |

At the small rung the stage fits in cache either way and the probes are not what it pays for; at the
large one it halves. The *before* leg also swings 52 to 80 ms between rounds where the after leg
reads 26.5 twice — the dictionary path is the sensitive one, which is the same fact from the other
side.

## Pass 3, iteration 7 — a block carries its grid slot

`GridModel` held a second dictionary, from block key to list slot, beside its cell index: an insert
per block at load, a probe per removal, and about thirty bytes a block, to answer what the block can
hold in four. The block carries it now on the same terms as `NodeIndex` — a hint the grid verifies
before use — and `GetByKey` reads the cell index, which answers the same question because a block's
key is the key of a cell it occupies.

## Pass 3, iteration 8 — a retained ring frontier

A pass enqueues every air cell of the bounding volume — 6.6 million at half a million blocks — into
a `Queue` grown from empty, so about twenty reallocations and thirteen million struct copies went on
growth every pass, having already been that large the pass before. The frontier is an array-backed
ring the mapper keeps. What a retained buffer risks is a pass reading what the last one left, so that
is the pin: the same hull mapped after a flood over a box forty cells larger in every direction
publishes the same map.

## Pass 3, iteration 9 — the walk reports the face it found

The neighbour walk iterates faces to find neighbours and threw the face away, so the link builder
asked `ContactFace` to work it out again from two boxes. It reports it now, through an overload
rather than a change to `IBlockAdjacency` — a port an adapter outside this repository implements
(`W2`) — and a host supplying its own adjacency still gets the worked-out face.

## Pass 3, iteration 10 — the allocation counter is per thread

`C4`'s new assertion — a settled step allocates nothing — **passed alone and failed in the suite**,
reading half a megabyte. `GC.GetTotalAllocatedBytes` counts the whole process, and the suite runs
eight classes at once, so the stage's delta collected whatever the other seven allocated meanwhile.
`GC.GetAllocatedBytesForCurrentThread` is the figure the lab wanted. A check that is right in
isolation and wrong under the conditions it will actually run in is the worst shape available, and
it is worth recording that the suite caught it within an hour of the check being written.

## Pass 3 — what the pass moved, and the one thing it moved the wrong way

Start (`a81ee0b`) against tip, both built the same way, interleaved in one window, four rounds at
505,566 blocks, fastest kept. **The solver stage is the control** — nothing in this pass touches the
substep loop — and it reads 90.8 ms against 89.6, flat within 1.5 %, which is what says the rest of
the column can be read.

| stage, 505,566 blocks | start | tip | ratio |
| --- | ---: | ---: | ---: |
| surfaces | 55.4 ms | **27.5 ms** | **0.50** |
| place | 67.3 ms | **52.0 ms** | 0.77 |
| links | 105.4 ms | **84.4 ms** | 0.80 |
| exposure | 49.4 ms | **46.5 ms** | 0.94 |
| register | 17.6 ms | 18.7 ms | 1.06 |
| rooms | 192.9 ms | 207.2 ms | 1.07 — *inside this stage's floor; see below* |
| a settled step (control) | 90.8 ms | 89.6 ms | 0.99 |
| **Memory, 126,731 blocks** | | | |
| `GridModel` indexes | 77 B/block | **43 B/block** | 0.56 |
| `BlockInstance` | 208 B/block | 216 B/block | 1.04 |
| retained | 765 B/block | **739 B/block** | 0.97 |
| **Worst tick after a placement, 505,566 blocks** | 69.6 ms | **49.4 ms** | 0.71 |

**The room pass read seven per cent worse, and that reading does not survive being attributed.**
This section said for half an hour that the regression was the finding and named iteration 3 as the
suspect — a membership bit set 1.5 million times inside the room pass to make exposure cheaper.
Measured at each of the pass's commits instead of reasoned about, best of fifteen, two rounds each
at 505,566 blocks:

| commit | rooms, round 1 | round 2 | best |
| --- | ---: | ---: | ---: |
| `a81ee0b` start | 201.6 ms | 196.2 ms | 196.2 |
| iteration 2 | 208.1 | 208.9 | 208.1 |
| iteration 3 | 201.9 | **186.2** | 186.2 |
| iteration 5 | 195.7 | 209.0 | 195.7 |
| iteration 8 | 220.4 | 208.1 | 208.1 |

**The spread within one commit is as large as the difference between any two** — 186 to 202 at
iteration 3, 196 to 202 at the start — so nothing here is convicted and iteration 3, the accused,
reads *faster* than the commit before it. The stage's floor at this size is about seven per cent,
which is exactly the size of the end-to-end reading, so the honest statement is that **the room
pass did not measurably move in this pass, in either direction** (`M5`, applied to my own claim,
corrected in place per `E10`).

**Why that stage is the noisy one is now visible, and it is the next pass's opening.** The
allocation column added in iteration 1 says the room pass allocates **253 MB per execution** at
505,566 blocks — the per-room cell lists, the cell-to-room dictionary, the frozen arrays and the
radix scratch — against links and exposure and a settled step, which allocate nothing at all. A
stage whose cost is dominated by allocation and collection is a stage whose timings will keep
refusing to resolve a few per cent, and cutting that 253 MB is worth more than any instruction in
the flood. The column earned its place within the pass that added it.

## Pass 3 — what is designed and not built

**A span flood.** The room pass is what is left, and its cost is the flood: every air cell of a box
fourteen times the block count, six faces each. Counted on this hull (a structural count, not a
timing): at 505,566 blocks the external air is **5,103,739 cells in 60,681 maximal x-runs — 84 cells
a run** — and the room air is 1,503,815 cells in 22,445 runs. A flood that enqueued *runs* rather
than cells would push about eighty thousand entries where this one pushes six and a half million,
and could mark whole words of the visited set at a time.

**And it is not the first thing to try any more.** The allocation figure above says the room pass
is bound by what it allocates before it is bound by how many cells it walks, so a pass aimed at this
stage should cut the 253 MB first and count cells second.

**What stops a span flood being this pass's work is bit-identity, and it is worth writing down.** A span flood
reaches a room's cells in a different order; `BuildRoomLinks` walks a room's cells to build its air
links, and `AccumulateRoomAir` sums over those links in order — so a different cell order is a
different sum order and a different last bit on a temperature. Landing it means first making the air
links canonical (sorting them by node index), which is itself a change that moves the last bits once
and needs the byte-identical scenario baselines re-recorded in its own commit. That is a designed
change with a measured prize, and it belongs to a pass that can start with it rather than reach it
ninth.

## Pass 4, iterations

Pass 3 ended by naming this pass's subject: the room pass allocates 253 MB per execution where every
other stage allocates nothing, and a stage bound by allocation is a stage whose timings will not
resolve a few per cent. So this pass counts bytes first and instructions second, and the iteration
table carries an allocation column for that reason.

| # | Subject | Verdict | Where |
| ---: | --- | --- | --- |
| 1 | The room map's cell-to-room dictionary is gone, not merely rebuilt | **kept** — rooms 0.75, and 149 MB of the 253 with it | [Iteration 1](#pass-4-iteration-1--the-room-maps-cell-to-room-dictionary-is-gone) |
| 2 | Every room's cells in one store, sized from the pass before | **kept** — another 50 MB, and the worst pass 0.89; the best did not move | [Iteration 2](#pass-4-iteration-2--every-rooms-cells-in-one-store) |
| 3 | The map answers from a rank, so the sorted arrays and the sort go | **kept** — exposure **0.45**, rooms 0.89, another 28 MB | [Iteration 3](#pass-4-iteration-3--the-room-map-answers-from-a-rank) |
| 4 | A room's air is a function of the room, not of the flood's path | **kept** — for the property, at a cost the instrument cannot resolve; and it found the load path's largest stage, unmeasured | [Iteration 4](#pass-4-iteration-4--a-rooms-air-is-a-function-of-the-room) |
| 5 | The air rebuild walks neighbours by key arithmetic | **kept** — roomair 0.74 at 126k, 0.95 at 505k, and the gap says what the stage is bound by | [Iteration 5](#pass-4-iteration-5--the-air-rebuild-walks-neighbours-by-key-arithmetic) |

## Pass 4, iteration 1 — the room map's cell-to-room dictionary is gone

`RoomMap` held every room cell twice: once in the room's own list, and once as a key in
`Dictionary<long, int> roomIndexByCell`. At half a million blocks that second copy is **1.5 million
hash inserts taken during the flood** — inside the hot loop, growing and rehashing as it goes — and
pass 2 had already established that a probe of it per cell is a cache miss per cell.

What makes it removable is not a cheaper structure but a fact about when it is read: **nothing asks
a room map which room a cell is in while the pass that builds it is running.** The flood asks the
membership *bitset* (iteration 3 of pass 3), never the index; the solver, the audit and exposure all
ask after `Freeze()`. The dictionary was written 1.5 million times, read zero times, and then
enumerated once at the end to build the sorted arrays that answer every real query.

So it is deleted. `Freeze()` builds `frozenKeys`/`frozenRooms` by walking the surviving rooms —
which hold the same cells, with each cell's room already known by which list it is in — and radix
sorts them, the same sort iteration 8 of pass 2 measured. `RoomAt` returns −1 before a freeze, which
is the honest answer and is now the documented contract: *a working map is private until it is
published*. `DropEmptyRooms` no longer renumbers anything, because there is no longer a second table
of indices to keep in step with the first.

**What it was worth.** `bench stages --stages rooms,exposure`, before against after, the two core
DLLs proven different, two rounds, best of fifteen within a round, fastest round kept. **Exposure is
the control**: it runs on the frozen map, this change does not touch how it reads, and it must not
move.

| stage, blocks | before | after | ratio |
| --- | ---: | ---: | ---: |
| rooms, 126,731 | 45.31 ms | **35.57 ms** | **0.79** |
| rooms, 505,566 | 257.99 ms | **193.21 ms** | **0.75** |
| exposure, 126,731 (control) | 13.17 ms | 11.42 ms | 0.87 |
| exposure, 505,566 (control) | 52.55 ms | 51.55 ms | 0.98 |
| **allocated, one room pass** | | | |
| 126,731 | 34,844 KB | **17,717 KB** | **0.51** |
| 505,566 | 258,882 KB | **106,095 KB** | **0.41** |

Cell-visit counters are identical on both legs at both sizes (1,622,649 and 7,216,527), which is
what says the two legs did the same work; the scenario baselines are byte-identical, which is what
says they got the same answer.

**Read the allocation rows before the millisecond rows.** A quarter of a gigabyte per execution
became a hundred megabytes, and the timing moved by a quarter — that is the shape pass 3 predicted,
and it is also why this stage's *spread* is still 178 % at the large rung: 104 MB is still 104 MB.
The remaining bytes are the per-room cell lists and their doubling copies, the frozen arrays, and
the radix scratch, in that order of size, which is the order the rest of this pass takes them in.

## Pass 4, iteration 2 — every room's cells in one store

A room was a `List<Vector3I>` of its own. The flood fills one room to exhaustion before it opens the
next — only the current room is ever added to — so a room's cells are **contiguous by
construction**, and a room can be a start and a length into one shared array instead. `AddToRoom`
refuses any room but the open one, because the failure that would otherwise follow is silent: a cell
filed under a closed room's index lands at the end of the store and is read as the current room's.

What that buys is not the room objects. This hull has a few hundred rooms, not thousands. It is that
**a rebuild can be told its size in advance**: the pass before it found a number of room cells, and a
hull that gained or lost a block finds very nearly the same number, so `HintRoomCells` sizes the
store once and the pass neither doubles into it nor trims it back. A list per room could not be
hinted, because the flood does not know how large any *one* room will be until it has finished it.

**What it was worth.** Same instrument, same rules, exposure as the control:

| stage, 505,566 blocks | before | after | ratio |
| --- | ---: | ---: | ---: |
| rooms, best of 15 | 149.47 ms | 146.31 ms | 0.98 — *inside the floor* |
| rooms, **worst of 15** | 445.87 ms | **400.60 ms** | **0.90** |
| rooms, worst of 15 (round 2) | 445.45 ms | **388.88 ms** | **0.87** |
| rooms, spread | 198 % | **174 %** | |
| exposure (control) | 38.12 ms | 36.76 ms | 0.96 |
| **allocated, one room pass** | 106,095 KB | **54,616 KB** | **0.51** |
| allocated, 126,731 blocks | 17,717 KB | **10,026 KB** | 0.57 |

**The best case did not move and the worst case did, and that is the finding.** Iteration 1 cut
allocation by 59 % and the stage got a quarter faster; iteration 2 cut it by another 49 % and the
stage did not move at all. The difference is that iteration 1 also removed 1.5 million hash inserts —
*work* — where this removes only *garbage*. Garbage does not lengthen the pass that makes it; it
lengthens whichever pass the collector happens to land in, which is why it shows up in the worst of
fifteen and in the spread rather than in the best. A load path that runs repeatedly while a player
waits is judged by its worst tick as much as its best, so this is kept — but on the tail and the
allocation column, not on a claim that the room pass got faster, which by `M5` it did not.

**A caution the two iterations together make unavoidable: milliseconds do not survive leaving their
window.** Commit `321c062` is the *after* leg of iteration 1 and the *before* leg of iteration 2. It
read **193 ms** in the first window and **149 ms** in the second — the same code, the same size, the
same instrument, thirty per cent apart. Both pairings are interleaved and both rounds within each
agree, so both ratios stand; what does not stand is any comparison of an absolute figure in one
table with an absolute figure in another. That is what `M7` means by measuring a pass against its own
start, and this is the clearest example of it the project has produced.

## Pass 4, iteration 3 — the room map answers from a rank

The published map held a sorted `long[]` of cell keys and a parallel `int[]` of rooms, and sorted
them — 1.5 million keys, by radix, on the tick that publishes the map. Twelve bytes a cell to hold
twelve bytes of answer, and a query that is a binary search: about twenty dependent loads through
twelve megabytes.

**None of that is needed, because the pass already fills a set that knows the answer's shape.**
`roomCells` is one bit per cell of the search box, marking the cells that are in rooms — the set
`IsExternal` asks first. It knows *which* cells are in rooms and, being indexed by the box's own
ordering, it already **orders** them. What it does not say is which member a given cell is. Ranking
it does: a prefix count of set bits per 64-cell word, four bytes a word, built in one pass over a
sixty-fourth of the box. Then

- the room of a cell is `roomByRank[rank(cell)]` — **four bytes a cell**, no keys held at all;
- a query is two loads and a popcount, with no search and no comparison anywhere;
- publishing is one walk over the set's words and one over the room cells. **There is no sort.**

`BitOperations.PopCount` is not on the script whitelist, so the popcount is written out as the usual
SWAR halving. The rank index is put away by any change to the set it describes, because a rank read
from a stale prefix is a plausible number that indexes the wrong thing.

**What it was worth.** `bench stages --stages rooms,exposure,solver`. Exposure cannot be the control
here — it is the biggest reader of this lookup — so **the solver stage is**, and it reads 1.01.

| stage, 505,566 blocks | before | after | ratio |
| --- | ---: | ---: | ---: |
| **exposure** | 38.20 ms | **17.06 ms** | **0.45** |
| rooms | 149.04 ms | **132.33 ms** | **0.89** |
| a settled step (control) | 80.10 ms | 80.73 ms | 1.01 |
| **allocated, one room pass** | 54,616 KB | **25,670 KB** | **0.47** |
| **at 126,731 blocks** | | | |
| exposure | 10.19 ms | **6.33 ms** | **0.62** |
| rooms | 33.67 ms | **31.74 ms** | 0.94 |
| a settled step (control) | 17.51 ms | 17.16 ms | 0.98 |
| allocated, one room pass | 10,026 KB | **4,768 KB** | 0.48 |

**Exposure more than halved, and that is where the reading is.** Pass 3's iteration 3 put a
membership *bit* in front of this search precisely because the search was expensive; what is left
after that bit are the cells that really are in rooms, and every one of them was still paying twenty
dependent loads. At half a million blocks that was more than half of the exposure refresh. The room
pass's own 0.89 is the sort no longer happening on the publish tick.

**And the ledger is closed on this pass's opening figure.** The room pass allocated **253 MB** per
execution when pass 4 began and allocates **25 MB** now — a tenth — in three changes that each
removed a structure rather than tuning one: a dictionary nothing read, per-room lists that could be
one hinted store, and sorted keys that a rank makes unnecessary. What remains is the cell store at
twelve bytes a cell and the room-by-rank at four, both of which the map is holding *because they are
its answer*, not as working space.

## Pass 4, iteration 4 — a room's air is a function of the room

`BuildRoomLinks` counts how many faces each bounding block presents to a room into a
`Dictionary<int, int>` and then enumerates it. A dictionary enumerates by insertion, so **a room's
links came out in the order the flood happened to reach its cells** — and the mean wall temperature
a new room's air starts at is a sum of floats over that same enumeration, so it did too. Two floods
agreeing exactly about which cells are in a room disagreed about how warm its air is: on the test
hull, **349.66922 forwards and 349.66916 backwards**. Not a rounding curiosity — a visible difference
in the fourth decimal of a temperature.

Sorting the contacts by node index makes both the links and the sum a function of the room's
*contents*. `RoomAirCanonicalTests` builds two maps holding the same rooms with each room's cells
offered in opposite orders and compares the resulting air bit for bit — links, conductances,
starting temperature — and both of its checks fail when the sort is removed.

**Why it is worth an iteration on its own.** This is the condition the span flood needs. A flood
that enqueues runs of cells rather than single cells reaches a room's cells in a different order,
and until now that would have moved every player's air temperatures in the last bits, which is why
[the design note](#pass-3--what-is-designed-and-not-built) said it needed the air links made
canonical first. That is done, and it was done in its own commit so that if it *had* moved a pinned
figure, the move would have been attributable to it alone. It moved none: 2,027 tests pass unchanged.

**What it cost.** `bench stages --stages roomair,rooms,solver`, with the room pass and a settled step
as controls:

| stage, 505,566 blocks | before | after | ratio |
| --- | ---: | ---: | ---: |
| roomair | 417.71 ms | 442.49 ms | 1.06 |
| rooms (control) | 141.11 ms | 140.17 ms | 0.99 |
| a settled step (control) | 112.03 ms | 109.39 ms | 0.98 |
| roomair, 126,731 blocks | 44.19 ms | 46.73 ms | 1.06 |

**Read that as "at most a few per cent", not as six.** The *before* leg alone read 437.24 and 417.71
in its two rounds — 4.7 % apart on identical code — and the first round's pairing was 1.01 where the
second's was 1.06. A difference the same leg produces against itself is not a difference between
legs (`M5`). The honest statement is that a sort of a few hundred integers per room, per air rebuild,
costs no more than a few per cent of a stage, and it buys a property that a whole class of future
change depends on.

**And the iteration found something bigger than itself.** There was no instrument for the air
rebuild at all: the stage lab measured place, register, surfaces, links, rooms, exposure and a
settled step, and `RebuildRoomAir` — which runs every time a map republishes — was in none of them.
It is a stage now, and it is **the largest single thing on the load path**, in every window it has
been measured in. It had been that all along.

*The absolute figure needs a window to mean anything, and this section first said 437 ms — the
reading in the window above, where a settled step read 112 ms. Measured again the next window, on the
same commit, with a settled step at 85.5 ms: the air rebuild reads **185 ms** against the room pass's
**134**. The machine was about 30 % slower in the first window and the air rebuild was 2.4× slower,
which is worth its own sentence: this stage is memory-bound, so it is the one that suffers most from
whatever else is running. Corrected in place, `E10`; the ratios in the table are unaffected, being
interleaved within one window.*

The
first draft of the stage timed an empty outer loop, because a room at zero pressure has no air and
therefore no links, so the rooms are filled before the clock starts — which is the same failure as
[a switch wired to nothing](#pass-2-iteration-5--the-environment-pass-reads-one-row-per-node), caught
here by the stage reporting a suspiciously small number rather than by a check.

## Pass 4, iteration 5 — the air rebuild walks neighbours by key arithmetic

`BuildRoomLinks` and `RefreshExposureAround` both walk a room's cells and ask the grid what stands
across each of the six faces. Both did it by adding the face offset to the cell and calling
`GetAtCell`, which converts a cell to a key — **seven conversions a cell**, two multiplies and three
adds each. A key is a sum of the components, so a neighbour's key is this cell's key plus a per-face
constant: one addition. This is pass 3's iteration 6 applied to the two loops it did not reach.

`GetAtKey` is `GetAtCell` without the conversion. It is deliberately *not* `GetByKey`, which answers
only for a block's lowest cell — a neighbour walk that reached for that one would find every
one-cell block and miss every multi-cell one except at its corner, a hole nothing else in the model
would report. The two are pinned apart by a test on a 3×3×3 block.

| stage | before | after | ratio |
| --- | ---: | ---: | ---: |
| roomair, 126,731 blocks | 41.09 ms | **30.58 ms** | **0.74** |
| roomair, 505,566 blocks | 184.88 ms | **175.61 ms** | 0.95 |
| rooms, 505,566 (control) | 134.30 ms | 133.42 ms | 0.99 |
| a settled step, 505,566 (control) | 85.55 ms | 84.36 ms | 0.99 |

**The gap between the two rungs is the finding, and it names the next iteration.** The same change
is worth a quarter of the stage at 126,731 blocks and a twentieth at 505,566. Arithmetic does not
get cheaper with grid size, so what grew is everything else: at half a million blocks `blocksByCell`
holds five hundred thousand entries, the six neighbour keys of a cell land in six unrelated buckets,
and the stage is waiting on memory rather than computing. Removing five conversions from a cell that
then stalls on a cache miss anyway recovers little.

That is worth stating as a measurement rather than a hunch, because it changes what to do next:
**the probes that cost are the ones that find nothing.** A room's interior cells have six air
neighbours and pay six full dictionary probes to be told so, and interior cells are most of a room.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-27 | Opened pass 3, whose first iteration explains the figure pass 2 could not: the instrument, not the surface map. |
| 2026-08-27 | Closed pass 2: ten iterations, six kept, three dropped with their measurements, one the pass summary. World load at a million blocks 3.17 → 2.21 s. |
| 2026-08-26 | Opened pass 2 on the page, with its start figures taken by the instrument the pass begins by putting in the tree. |
| 2026-08-26 | Opened, with the first four iterations of the 2026-08-26 pass: the harness had measured unoptimised code for its whole life, and the ladder's `build` column had been measuring the census generator since `C26`. |
