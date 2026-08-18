# Load and hitching

What the simulation costs as a grid grows, measured rather than extrapolated, and what was
changed to stop a large grid stuttering.

Companion to [scale-design.md](scale-design.md), which is the design for a million blocks and
still mostly unbuilt, and to [bugs-and-performance.md](bugs-and-performance.md), which is the
record of an earlier investigation at eight thousand.

---

## The distinction the whole exercise rests on

Two costs, and confusing them is how a performance problem gets fixed in the wrong place.

**Steady cost** is what a tick costs when nothing has changed. It sets how much of a frame the
mod takes. It degrades *gracefully*: twice the cost is half the simulation rate, which a player
experiences as heat moving more slowly, and a server operator can trade away with `Frequency`.

**Spike cost** is what a tick costs when something *has* changed — a block welded, a section
shot away, a door cycled, a blueprint pasted. It sets whether the game stutters. It degrades
*catastrophically*: a player does not perceive a one-second frame as a slow simulation, they
perceive it as the game breaking.

A million-block grid that costs 100 ms every tick is playable at a low simulation rate. The same
grid stalling for one second when a block is placed is not, and no amount of lowering the rate
helps, because the stall is not the rate.

So the target for this work was never "make it fast". It was: **make the spike proportional to
what changed, and let the steady cost be whatever the arithmetic says it is.**

---

## Measuring it

Three tools, in `sim/`, described in [sim/README.md](../sim/README.md).

```bash
cd sim
dotnet run --project Thermodynamics.Sim -- bench scale                 # the ladder
dotnet run --project Thermodynamics.Sim -- bench spike --size 125000  # one block placed, split by stage
dotnet run --project Thermodynamics.Sim -- bench weld  --size 125000  # a block every tick
dotnet run --project Thermodynamics.Sim -- bench hitch --size 125000  # per-tick distribution
dotnet run --project Thermodynamics.Sim -- bench load  --size 1000000 # what world load costs
```

> **The harness runs on .NET 9; the game runs .NET Framework 4.8.** Absolute milliseconds here are
> optimistic against the game by a factor this project has not measured directly, but which the
> field data suggests is several. Ratios, counts and shapes of curve carry across; a millisecond
> figure does not. Treat the tables below as a floor and the telemetry report as the truth.

**These benchmarks run one grid.** A world runs hundreds, and the largest single finding of this
work — every grid ticking on the same frame — was invisible to all of them and came from a
telemetry report instead. Benchmarks bound what one grid costs; only the report says what a frame
costs.

`bench scale` builds a hull at each rung of a ladder — 8k, 32k, 125k, 500k, 1M blocks — and times
every stage of an update on its own. The shape is a ship, not a cube, for the reasons in
[scale-design.md §10](scale-design.md#10-grid-shape-changes-the-arithmetic); `--shape cube` and
`--shape truss` are there to compare against.

`bench spike` is the one that says what to change. It places a single block on a settled grid and
reports the **worst call to each stage** across every tick until the grid settles, with the tick
each landed on, and the counts of what each touched. The stages do not land together — the
conduction rebuild is the tick after the placement, the room map converges hundreds of ticks
later, the exposure pass rides on the tick that publishes it — so a single "worst tick" figure
hides two of the three stalls behind whichever happened to be largest.

Everything is deterministic. The temperature spread the benchmarks seed is written out rather than
taken from `Random`, so two runs on the same machine are comparable and two machines differ only
by their speed.

---

## What it measured, and what changed

All figures from this machine, release build, single thread, ship shape.

### The ladder

| blocks | links | bbox | build | topology | rooms | exposure | full step | tick | cap | resident |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 8,904 | 20,779 | 68,800 | 124 ms | 11.8 | 36.1 | 5.1 | 0.56 | 0.57 | 48 | 22 MB |
| 32,800 | 73,787 | 328,640 | 238 ms | 27.8 | 76.2 | 5.9 | 2.84 | 1.56 | 13 | 98 MB |
| 126,731 | 277,967 | 1,499,616 | 890 ms | 53.3 | 383.9 | 26.2 | 16.05 | 16.58 | 3 | 345 MB |
| 505,566 | 1,079,559 | 6,838,104 | 5,552 ms | 220.1 | 3,191.3 | 133.1 | 52.63 | 22.16 | 1 | 1,639 MB |
| 1,000,294 | 2,114,111 | 14,278,796 | 11,192 ms | 461.0 | 6,212.1 | 150.7 | 102.50 | 42.41 | 1 | 2,545 MB |

The topology, rooms and exposure columns are each stage run **whole**, which is what a one-shot
rebuild or a world load costs. They are not what a tick costs; every one of them is now spread.

**`full step` and `tick` are not the same number, and the gap is the point.** `full step` is a
step of the whole configured length with as many substeps as the grid's stiffness asks for. `tick`
is what a tick actually pays once `MaxLinkVisitsPerStep` has shortened the step to fit, and `cap`
is how many substeps that leaves. Below about a hundred thousand blocks the budget never binds and
the two agree. Above it they diverge, and that divergence is the trade being made: at a million
blocks a tick pays 42 ms instead of 103, and simulated time advances more slowly to pay for it.

**The solver scales.** Cost per link visit is 9.0 ns at 8k and 16.2 ns at a million — it doubles
across a working set that grows from half a megabyte to two gigabytes, which is a cache effect
and not an algorithmic one. Nothing in the conduction pass is superlinear.

### The spike, before and after

Worst tick after a single block is placed:

| blocks | before | after |
| ---: | ---: | ---: |
| 8,904 | 9.8 ms | **1.6 ms** |
| 32,800 | 23.4 ms | **3.9 ms** |
| 126,731 | 121.0 ms | **13.3 ms** |
| 505,566 | 387.3 ms | **69.8 ms** |
| 1,000,294 | **1,086.3 ms** | **156.9 ms** |

What is left in those figures is almost entirely the solver step that happens to share the tick,
not the placement: the topology stage itself is under a millisecond at every size.

Worst call per stage, on a 127k hull, one block placed:

| stage | before | after | why |
| --- | ---: | ---: | --- |
| topology | 90.5 ms | **0.6 ms** | a placed block is linked, not the grid |
| rooms | 77.2 ms | **12.2 ms** | the interior scan is charged against the budget |
| exposure | 29.7 ms | **2.6 ms** | the pass is resumable |
| solver | 22.5 ms | 22.5 ms | the steady cost, unchanged and now the largest thing left |

### Per-tick distributions

`bench weld` places a block on every tick for 120 ticks — sustained construction, the worst
realistic case for the rebuild path, and the one that never gets a quiet tick to recover in.
`bench hitch` ticks a settled grid for 300 ticks with one block welded a quarter of the way in
and one ground off half way.

| run | blocks | median | p95 | p99 | max | spike | over budget |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| weld | 32,800 | 1.73 | 3.08 | 7.62 | 11.97 | 6.9x | 0 / 120 |
| weld | 126,731 | 7.25 | 8.47 | 16.97 | 20.93 | 2.9x | 2 / 120 |
| weld | 505,566 | 34.27 | 36.71 | 44.53 | 55.47 | **1.6x** | 80 / 120 |
| hitch | 32,800 | 1.97 | 5.70 | 22.28 | 30.33 | 15.4x | 6 / 300 |
| hitch | 126,731 | 10.38 | 17.42 | 49.10 | 78.69 | 7.6x | 27 / 300 |
| hitch | 505,566 | 49.33 | 56.27 | 173.40 | **303.59** | 6.2x | 201 / 300 |

**Welding is smooth, and gets smoother as the grid grows.** A spike ratio of 1.6 on a
half-million-block grid means sustained construction is essentially flat: the ticks over budget
are the steady cost being over budget, not stalls. That is the incremental topology working — the
same run before it would have paid a 200 ms rebuild on every one of the 120 ticks.

The `hitch` rows above were taken before findings 6 to 8. Afterwards, with the worst tick in each
run attributed by stage:

| run | blocks | median | p95 | p99 | max | worst tick is |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| hitch | 126,731 | 11.28 | 19.34 | **26.47** | 60.83 | the first step |
| hitch | 505,566 | 23.96 | 26.36 | **28.71** | 208.68 | the first step |

The tail is what moved. At 500k the p99 went from 173 ms to 29 ms against a median of 24, and the
median itself halved because a step is now bounded. What is left is a very tight distribution and
one outlier: **the first step**, which pays for the first touch of every flat array and the first
fill of every mirrored row. It is a warm-up, it happens once, and it happens immediately after a
world load that took eleven seconds — so it is the least interesting stall on the page.

The worst call to each stage on those runs is topology 10.5 ms, rooms 10.4 ms, exposure below the
timer's resolution, and the solver everything else. Nothing but the solver is above ten
milliseconds on a half-million-block grid, and the solver is doing arithmetic rather than
bookkeeping.

---

## The findings

### 1. A block placed rebuilt the whole conduction graph — *fixed*

Placing one block marked the graph stale and the next tick rebuilt every link on the ship. On a
million blocks that is 456 ms for one block.

A block arriving is the one topology change that needs no demolition: nothing links to a block
that was not there, so its links can be appended and every existing link left as it was. Placed
blocks are queued and the queue is drained by linking each new block's own six faces.

The parts that were proportional to the grid had to go with it — the flat arrays the substep loop
reads are appended to rather than rewritten, and conductance totals are added to rather than
recomputed. See `ThermalSolver.LinkPendingNodes`, and `IncrementalTopologyTests` for the proof
that the graph it builds is the same graph a full rebuild produces.

**Still global:** anything that can invalidate a link that already exists — a block *removed*, a
block whose mounting changed, a new adjacency source. Removal is the one that matters and is
listed under [what is still open](#what-is-still-open).

### 2. The room mapper's budget did not bound its scan — *fixed*

The flood fill is budgeted so no tick pays for the whole grid. The walk between one room and the
next was not: it counted as a single unit of budget however far it went, and across a pass that
walk covers the whole bounding box. The tick that finished a pass on a 127k ship swept the tail of
a 1.5-million-cell box in one call and cost 77 ms.

The scan is charged cell by cell now. A pass takes about twice as many ticks and every one of them
is bounded.

The load test asserting the budget was respected passed throughout, because it read the counter
that was not counting the scan. **A budget test is only as good as what it counts.**

### 3. The exposure refresh had no budget at all — *fixed*

It walked every node on the grid on the tick a room pass published — the same tick as the
mapper's own worst call, which is how one tick came to cost 109 ms on a grid whose steady cost is
twenty. It is resumable now, in slices scaled off the node count.

Some nodes read the previous map for a few ticks. That is not a new inaccuracy: it is the map they
had been reading for the several hundred ticks the pass took to build.

### 4. Two searches walked the grid to find nothing — *fixed*

The coolant loop search and the heat pump rebuild each walked every block asking a question almost
every block answers no to, on every topology change. The grid counts them as they are placed, so a
ship with no plumbing — nearly every ship — skips two passes over a million blocks for an integer
test.

### 5. The mass sweep walked every block every eight steps — *fixed*

Block mass changes with build progress and damage and the game raises no event for either, so the
only way to notice is to look. It looked at every block on the grid, every eight steps, asking the
game for each block's mass. It is a rota now, capped per tick; a grid under the cap is still swept
whole every eight steps.

This is the one finding the synthetic benchmarks cannot see — a harness has no game blocks to ask
— which is why the load numbers are not the whole story and telemetry from a real session is.

### 6. Removing a block rebuilt the whole conduction graph — *fixed*

The companion to finding 1, and the harder direction. A removed block's links have to be *found*
before they can be dropped, and scanning the link list for them is proportional to the grid. Its
node also has to leave a list whose indices every link refers to, and taking it out by shifting
everything after it moves every one of those indices at once.

Both are solved by two decisions. Links are indexed per node as an intrusive chain — three `int`
arrays, no per-node collections and no managed references, which is the shape
[scale-design §6](scale-design.md#6-data-structures) asks for — so a node's links are walked in
time proportional to its degree. And a node leaves by having the last node moved into its place,
so exactly one index changes and only the links touching that one node are rewritten.

Everything else holding a node index is repaired for that one change: the coolant loops, the heat
pumps, and the room air. The room air is the one that mattered — it is not rebuilt until a room
mapping pass completes, which on a large grid is thousands of ticks after the block was removed,
so a stale index there would have poured a room's heat into whichever block inherited it.

`IncrementalTopologyTests` proves it against the global builder, including a 400-step fuzz run
that builds and grinds in a generated order and compares the graph against a rebuild after every
single change.

### 7. A step's cost varied five-fold with nothing visible changing — *fixed*

A step's cost is its substep count times its links, and the substep count is set by the stiffest
node on the grid, which moves as the grid heats. On a 127k hull that produced a step costing 15 ms
most of the time and 70 ms occasionally, from the same grid doing the same thing.

`MaxLinkVisitsPerStep` bounds it. When a step would exceed the budget, the step is made
**shorter** rather than its substeps coarser — and that distinction is the whole point.
Coarsening substeps takes steps too large for the stiffness and leans on the overshoot clamp,
which is an accuracy loss. Shortening the step advances less simulated time at exactly the same
accuracy: heat moves more slowly and nothing else changes.

**This is the setting that trades simulation rate for smoothness**, and it is what makes a grid
too large to simulate at full rate run at a lower rate smoothly rather than at full rate in
lurches. `ThermalSimulation.SimulationRate` reports how much of real time a grid is keeping up
with, so a slow grid says so rather than being mysterious. The default is about one 60 fps
frame's worth of link visits; grids below roughly a hundred thousand blocks never reach it.

### 8. Publishing the shadow map walked every node — *fixed*

The self-shadow walk was budgeted. Publishing its answer was not: a completed pass called a loop
over every node on the grid, six faces each — 760,000 shadow lookups on a 127k hull — from inside
the step. That was the 69 ms step whose conduction loop accounted for a fifth of it.

The same shape of mistake as the room mapper's, and the same fix: the refresh is spread over
steps. It is advanced inside the pass rather than at the top of a step, so a grid small enough for
the budget to cover in one go still finishes in the same substep that completed the pass.

### 9. Every grid in the world ticked on the same frame — *fixed*

The one the benchmarks could never have found, because they run a single grid.

A field run of a 203-grid save: of 13,915 frames, **1,392 did any thermal work at all** — one in
ten, exactly — and each of the worst frames carried **183 grids**. Those frames averaged 117 ms,
worst 611 ms, and two in three exceeded a 60 fps frame. The session total was 20 % of real time,
which is a throughput number and a survivable one. Arriving in one lump every tenth frame is what
made it a stutter.

`ThermalGridScheduler` gives each grid one of ten phases and ticks one phase per frame. The
interval per grid is unchanged — every grid still ticks once per ten frames — only which ten. The
phase is chosen by the blocks already on it rather than by grid count, because that world held one
42,051-block capital ship and two hundred craft of a few hundred blocks each, and balancing by
count would have left the capital's frame carrying its share of the rest as well.

**This is a correction, not a discovery.** Earlier in the same session this was investigated by
reading `MyDistributedTypeUpdater<MyEntity>(10)` in the engine assemblies, which computes
`m_step = ceil(Count / UpdateInterval)` and walks a slice per frame — and concluded, in a
committed document, that the engine already staggered and there was nothing to do. The reading was
plausible and the conclusion was wrong. The measurement settled it in one line: 1,392 of 13,915.

The wrong version of that conclusion was the more dangerous kind, too. Had the fix been written on
the original belief it would have been *invisible* — a stagger applied on top of a stagger that
was not there would simply have worked, and nobody would have learned that the premise was false.

---

## Catching it again

The load tests in `sim/Thermodynamics.Tests/LoadTests.cs` run with the ordinary suite and assert
these properties rather than leaving them to a benchmark nobody runs. Almost every assertion is
on a `SimulationWork` counter rather than a stopwatch, because a millisecond threshold is a claim
about the machine that ran it and a claim that placing one block must not visit every node is a
claim about the algorithm.

| Test | Property |
| --- | --- |
| `ASettledGridRebuildsNothing` | an unchanged grid pays for no rebuild of any kind |
| `PlacingOneBlockLinksTheBlockAndNotTheGrid` | fewer than sixteen node visits, whatever the grid size |
| `WeldingABurstCostsTheBurstAndNotTheGrid` | a hundred placed blocks visit a hundred nodes |
| `ManyPlacementsInOneTickCoalesceIntoOneRebuild` | one rebuild per burst, not one per block |
| `SearchesForPlumbingSkipAGridThatHasNone` | no cells scanned for loops or pumps |
| `RoomMappingNeverExceedsItsBudgetInOneTick` | the flood fill respects its budget |
| `ExposureRefreshNeverExceedsItsBudgetInOneTick` | the exposure pass respects its budget, and still covers every node |
| `ARebuildIsBilledToTopologyAndNotToTheSolver` | stage attribution is honest |
| `ReadingTheLinkCountDoesNotRebuildTheGraph` | observing does not change what is observed |
| `SolverCostPerLinkStaysProportional` | nothing quadratic crept into the conduction pass |
| `ASettledTickFitsInAFrame` | a wall-clock tripwire, loose on purpose |

The timed ones run in a collection that disables parallelisation and take the best of three runs.
They were reporting 1.7x and then 3.2x for the same binary before that, and all of the difference
was thirty other tests on the same cores.

Every test writes its figures to the test output, so a suite run is also a performance report.

---

## From a real session

Benchmarks measure a synthetic hull with no game blocks behind it. What a real world does is a
telemetry question, and the report now has a **frame cost** section: what the mod spends per
frame across every grid, the share of frames where it alone exceeded 16.7 ms, how spiky the
session was, and the worst sixteen frames in full — each with its stage split and the counts of
what those stages touched. See [telemetry.md](telemetry.md#frame-cost-and-hitching).

That last part is what makes a report actionable. "A 90 ms frame" is a number to worry about; "a
300,000-block station rebuilding its conduction graph, 300,000 nodes visited" is a line to
change.

---

## Calibrating the step budget against a real world

`MaxLinkVisitsPerStep` bounds a step at a number of link visits, and its default of 1,000,000 was
chosen as roughly one 60 fps frame at the harness's measured 16 ns per visit. A field report says
what that actually buys in game.

Three 42,051-block ships, 90,136 links each, in a 203-grid world:

| | |
| --- | --- |
| substeps per step | **11.00 / 11.00 / 11.00** (sd 0.00, n 928) |
| steps clamped | 0 |
| simulation rate | 35.4 % — 150.3 s of simulated time not advanced |
| that grid's tick | 85–153 ms |

The budget is working exactly as designed: 1,000,000 ÷ 90,136 = 11, the substep count is pinned
there with zero variance, nothing clamped, and the step is shortened instead — the grid runs at
35 % of real time rather than in lurches.

What it also says is that **the default is calibrated against the harness and the harness is
optimistic**. 991,000 link visits cost 85–150 ms in game against the ~17 ms the harness's rate
predicts. Some of that is the game's .NET 4.8 runtime against the harness's .NET 9, some is that
the environment pass is per *node* per substep and the budget only counts links — this grid has
2.14 links per node, so the node work the budget cannot see is a large share of what a step does.

Practical consequence: **on a world with grids this size, the default is about five times too
generous.** A value nearer 200,000 would put those ships at 2 substeps and a tick nearer 20 ms, at
the cost of simulation rate they are already trading away. The right value is a judgement about
that trade, and it is per world, which is why it is a setting.

The counting itself is worth fixing rather than only documenting — see
[known-issues.md](known-issues.md).

## What is still open

Roughly in order of how much a million-block grid would notice.

**A solver step is atomic, and at a million blocks it is 104 ms.** This is now the largest single
thing that lands in one tick, and unlike everything above it is not an accounting mistake — a step
must touch every node, and 16 ns per link visit is near the memory-bandwidth floor. Lowering
`Frequency` makes the spike *less frequent* without making it smaller, so it does not help. Making
it smaller means not touching every node: activity tracking, chunking and multirate stepping, all
designed in [scale-design.md](scale-design.md) and none of it built.

**Removing a block still rebuilds the whole graph, and it is now the largest spike there is.**
304 ms against a 49 ms median on a half-million-block hull, and it is a common event: grinding,
combat damage, a section breaking off. Additions are incremental; removals are not, because every
node index after the hole moves and every link referring to one of them becomes wrong. Repairing that incrementally needs a per-node index of the links touching a node — the
intrusive adjacency chains in [scale-design.md §6](scale-design.md#6-data-structures) — after
which removal is O(degree) like addition. Until then, grinding or combat damage on a very large
grid costs a full rebuild per burst. It coalesces, so a section shot away is one rebuild rather
than one per block.

**Memory is 2.5 GB at a million blocks**, against the ~110 MB budgeted in
[scale-design.md §6](scale-design.md#6-data-structures). The gap is object-per-node and
object-per-block layout plus the room map's hash sets over the whole bounding volume — the
structure-of-arrays and blittable-only changes in that section, none of which are built. This may
well bind before the solver does.

**The room map floods the bounding volume**, which is 14 times the block count on a hull and worse
on a station. It is budgeted now, so it costs ticks rather than a stall — 7,237 of them to
converge at a million blocks, which is twenty minutes of a stale map. Bounded and wrong is better
than unbounded, but it is still wrong.

**World load is 11 seconds at a million blocks**, in one call, before the first tick. `RebuildAll`
is deliberately one-shot because it is far cheaper than replaying the incremental path per block,
and a loading screen is a better place for a stall than a session — but a blueprint pasted
mid-session takes the same path.

**`SweepRoomPressure` is still per room per cadence**, with two game API calls each. Bounded by
compartment count rather than block count, so it is small on a ship and unmeasured on a station
with thousands of rooms.
