# The performance report

One command that measures what the simulation costs — by size, by feature, and by configuration —
and knows how to compare itself against an earlier run.

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `E6` `M4` `M5`
> `M6` `M11` `C4` `C7`.

| Looking for | Go to |
| --- | --- |
| What a grid costs as it grows, and what makes it stutter | [load-and-hitching.md](load-and-hitching.md) |
| Why a handful of light fittings sets a capital ship's cost | [stiffness.md](stiffness.md) |
| Where a grid's memory goes | [memory.md](memory.md) |
| The equations being timed | [thermal-model.md](thermal-model.md) |

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
dotnet run --project Thermodynamics.Sim -- bench report --baseline benchmarks/performance.csv
```

The first writes `benchmarks/performance.csv`. The second measures again and prints what moved.
[`benchmarks/performance.csv`](../tests/benchmarks/performance.csv) is committed, so a change can be
measured against the tree it was made on rather than against somebody's memory.

---

## Why it exists

The benchmarks that came before it answer "how does this scale". None of them answer "did the
change I just made cost anything", because every figure lived in a different command, in a
different format, and was compared by reading two terminals side by side. A regression that did not
happen to show up in the one number somebody looked at was invisible.

This produces one artefact per run: a table for a person and a CSV keyed on
`section / case / metric` for a diff. The key is the contract — renaming a case silently turns a
regression into two unrelated rows, which is the one way a benchmark suite lies about itself, so
`PerformanceReportTests` asserts the sections, the keys, the CSV round trip and the comparison's
own behaviour.

---

## What it measures

### The machine, and a noise floor

Milliseconds are a property of the machine as much as of the code, so the report opens with the
processor count and a **calibration** figure — a fixed 4,000-block simulation run a fixed number of
times — that two machines can divide by.

More important is the **noise floor**: the same case measured five times, and the spread between
fastest and slowest. On the machine these figures came from that is about 2 % of a step. Every
number below it is noise, and knowing that is what makes the feature table readable — several
features cost less than the spread, and they show up as small negative numbers rather than as
zero. A report that hid that would be inviting its reader to believe a feature made the solver
faster.

Every case is timed three times and the **fastest** kept, not the mean. A timing sample is the true
cost plus whatever else the machine was doing, and that noise is one-sided; averaging it in
measures the operating system. `--repeats N` changes it, which is how the claim above is put to the
question rather than assumed.

**That sentence was not true of the `build` column until 2026-08-27**, and it was the column least
able to afford it: the ladder's build was one stopwatch from the day the report was written, so the
one figure a reader compares between two runs to say a load-path change worked carried a whole
sample's noise while the page said it did not. It is `Repeats` builds of one dealt hull now, fastest
kept, with the repeats asserted to have built the same graph.

### The ladder

Blocks, links, build time, step time, substeps granted, substeps demanded, cost per simulated
second and cost per element visit, at each rung. The two substep columns matter separately:
*granted* is what `MaxSubsteps` allowed, *demanded* is what the grid asked for, and the gap between
them is accuracy given up. In flight the gap is widest — see
[the environments section](#the-environments).

| rung | blocks | links | step | demanded | per element visit |
| --- | ---: | ---: | ---: | ---: | ---: |
| 8,000 | 8,904 | 18,333 | 1.13 ms | 23.2 | 1.73 ns |
| 32,000 | 32,800 | 64,964 | 4.79 ms | 27.5 | 1.75 ns |
| 125,000 | 126,731 | 247,350 | 16.7 ms | 23.6 | 1.86 ns |

*The `build` column is the simulation's own build — surfaces, links, loops, rooms and exposure —
on a hull dealt before the clock starts. From `C26` until 2026-08-26 it also carried the census
generator's bolt search, at about ten times the build it fed; see
[performance.md](performance.md#iteration-2--the-ladders-build-column-measured-the-hull-generator).*

*Re-run 2026-08-24 at `C24`'s pair on the hull `C26` refreshed. **A hull has about a tenth fewer
links** — the tiers carry the mount points of the blocks they stand for, so a shaped armour block
joins on three or four faces rather than six — and the demand is what the two changes leave: four
times the conduction pace against two and a half times the capacity, on a hull whose lightest band
no longer carries six joints. Cost per element visit is unmoved, which is what says the step got
smaller rather than slower.*

**Cost per element visit is now flat across the ladder** — 1.82, 1.87, 1.91 ns — where it used to
climb from 2.8 to 6.1. The climb was the two passes that reached through the node objects to the
heap on every visit; both now read flat arrays, and what is left scales with the elements rather
than with how far apart they landed in memory.

### The environments

Every shape in every world, because which is worst depends on which world:

| | step ms | substeps demanded |
| --- | ---: | ---: |
| ship, vacuum | 0.37 | 6.7 |
| ship, atmosphere | 0.83 | 16.3 |
| ship, flight at 300 m/s | 1.13 | **23.2** |
| cube, vacuum | 0.33 | 6.8 |
| cube, flight | 0.87 | 19.3 |
| truss, vacuum | 0.25 | **7.2** |
| truss, atmosphere | 0.50 | 16.7 |
| truss, flight | 0.69 | **23.6** |

**A truss is the cheapest hull in vacuum and the stiffest in flight** — 7.2 substeps to 23.6, a
threefold inversion. One link per node and nothing to convect with makes it trivial in space;
every node being exposed makes convection a per-node term on all of them once there is air.

*Re-run 2026-08-24. The vacuum column halved and the flight column barely moved, which is the same
fact the rest of this pass keeps finding: `C24` divides every capacity by 0.4 and multiplies
conduction by four, so a convection-limited demand falls and a conduction-limited one rises, and
vacuum is the column made of conduction. The truss is no longer the softest hull in vacuum — a ship
is — because a truss's demand was always its exposure and the ship's was its joints.*

Until this section existed, every benchmark in this repository ran a **ship in vacuum**, which is
the cheapest of those nine on the axis that matters. Worse, `AtmosphereFactor` is zero in vacuum,
so convection was switched off *by the world* rather than by its setting — the report dutifully
recorded convection and friction as costing nothing, having never run either. Both are now
measured: 1.62 ms and 0.87 ms isolated, which puts convection above solar.

Everything else in the report is now measured in flight, deliberately the worst of the nine.

> **Cost and accuracy have different worst cases.** Where `MaxSubsteps` refuses the demand, flight
> does not cost much more per step — it is *further from being resolved*, and the extra demand is
> paid in accuracy through the overshoot clamps rather than in milliseconds.

### The features, two ways

Every switch a world can turn off, measured twice:

* **marginal** — everything on, this one off. What removing it from a working configuration gives
  back.
* **isolated** — everything off, this one on. What the feature does on its own.

They differ whenever features interact, and the gap is usually the interesting part. From the
committed baseline, on a 32,800-block hull in flight where a whole step is 3.55 ms and a hull with
every feature off is 0.37 ms:

| feature | marginal | isolated |
| --- | ---: | ---: |
| convection | **1.21 ms** | **1.62 ms** |
| conduction | 1.22 ms | 0.79 ms |
| damage | 0.20 ms | 0.10 ms |
| radiation | 0.09 ms | 0.89 ms |
| environment clamp | 0.08 ms | 0.00 ms |
| solar | 0.07 ms | 0.91 ms |
| conduction clamp | 0.04 ms | 0.00 ms |
| friction | −0.00 ms | 0.87 ms |
| waste heat | 0.01 ms | 0.13 ms |
| heat sources, coolant loops, room air, heat pumps, self shadow | within the noise | within the noise |

Three things fall out of that table that no single-number benchmark would have shown.

**`ClampConductionOvershoot` used to be the second most expensive thing in the simulation**, at
2.84 ms of a 6.22 ms step, and later 4.17 ms of an 8.52 ms one. It is now 0.06 ms. Both halves of
the clamp are the same stability test — `h * G > C` for a node, `h * conductance >` the reduced
mass for a link — and neither reads a temperature, so both can be settled for the whole grid once
per step. On a grid granted the substeps it demands, which is what the substep count is chosen to
guarantee, neither holds anywhere and every clamped branch was computing a value it then discarded.
See [the clamp A/B](#the-overshoot-clamp-ab) for what that is worth in each regime.

**Solar, friction and waste heat cost almost nothing to keep, and about a millisecond to
introduce.** All three are fixed for the length of a step, so they are resolved once and carried as
a single row the substep loop adds; removing one of them takes a term out of a sum that is read
either way. The isolated column — what each does on a grid with nothing else running — is close to
a millisecond, because introducing any of them is what makes the pass run at all. Reading that
column alone would predict a saving from disabling them that is not there.

**Damage used to cost 0.83 ms marginal** for a check that fired zero times across two field
sessions. It is now 0.20 ms. The check reads a critical temperature once per node per substep, and
reaching that through the node object was three dependent loads — the node, its block, its model —
into memory scattered across the heap. The rating is now mirrored into a flat array beside the
node's mass and emissivity, and the block is reached only once a node is actually over its limit.
That took the 125,000-block rung from 30.8 ms to 17.0 ms on its own, because at that size the
scattered loads were most of the pass.

The worst case for that is a hull where the check *always* passes, which is what the `scorched`
scenario is for — see below.

> **A caveat on the marginal column.** Switching a feature off does not only remove its cost, it
> changes the state the grid reaches — a hull with convection disabled runs hotter, which changes
> how often the clamps engage and what the other passes do. Convection, friction and waste heat all
> show *negative* marginals in flight for that reason: removing them makes the hull hotter and the
> rest of the step more expensive. The isolated column does not have this problem and should be
> preferred when the two disagree.

### The configurations

The substep cap across its useful range, reported as cost per simulated second — the unit that does
not move when the step length does. There is one shipped configuration, so there is nothing else in
this section: the cap is the only dial that trades accuracy for throughput on a stiff grid, and every
other row of the report is already measured on the defaults.

The shipped configuration is **resolved** — it is granted every substep the stability estimate asks
for, because `MaxSubsteps` is 64 and `MaxSubstepsPerBlock` is off — so the overshoot clamps never
engage and nothing is being driven towards ambient rather than integrated towards it. What a refused
grid costs instead is [the overshoot clamp A/B](#the-overshoot-clamp-ab) below, and
[realism.md](realism.md#failure-and-what-actually-causes-it) is where a deliberately starved
configuration is measured.

> `BenchmarkBaselineTests` fails when the committed baseline's keys drift from the report's, which is
> the class of rot that made older figures on this page hard to place.

### The shape of a step

A step is one lot of per-step work — mirroring the node state, estimating the substep count,
applying the mass floor, publishing the result — plus one lot of per-substep work for each substep.
Those two scale differently, and a single millisecond figure hides which of them a change moved.
There is a third term the fit cannot see, and [below](#the-fitted-intercept-is-not-the-prologue-and-was-read-as-if-it-were)
is what it turned out to be.

The report fits them through the `cap 4` and `cap 16` rows, both of which are clamp-free because the
per-block mass floor raises the stiff blocks rather than refusing them substeps. On a 32,800-block
hull in flight:

| | |
| --- | ---: |
| fixed per step | **0.49 ms** |
| per substep | 0.17 ms |
| fixed share at the default 19 substeps | 14 % |

Measured on the solver path, which until recently paid one prologue where the host paid two. See
[the two step paths](#the-two-step-paths) below.

**At one substep the fixed part is three quarters of a step.** A grid soft enough to need only one —
which in the field is debris rather than ships — pays almost nothing but that fixed part, so a change
that halves the per-substep cost does nothing for it. Worth knowing before optimising for the wrong
end of the population.

> Fitting through `cap 1` instead would put a clamped point against an unclamped one and attribute
> the difference to the fixed term.

#### The fitted intercept is not the prologue, and was read as if it were

**A step has three terms.** The work before and after it integrates; the first substep, which fills
the per-step environment rows every later substep reads; and every substep after that. A two-point
fit has only two terms to spend, so it charges the first substep's extra to the intercept — and the
intercept is labelled `fixed per step`, which reads as the prologue and the write-back.

The report now measures the two ends directly instead of leaving them inside the fit. At `cap 1`
the step is one substep and the three terms separate without any fit at all: the prologue is what
the solver charges to answer how many substeps it needs, the write-back is the step machine's last
stage driven on its own clock, and what is left is one substep with the fill in it.

| term | ms | what it is |
| --- | ---: | --- |
| prologue and estimate | 0.18 | mirroring the node objects, the conductance totals, the mass floor, the stability walk |
| write-back | 0.05 | the step's results onto the node objects |
| **row fill** | **0.22** | **the first substep's share, and none of any later one's — measured here at `cap 1`, which is a refused grid, so it is [the clamped fill](#the-fill-is-two-different-fills-and-the-report-measured-the-expensive-one) and about twice what a grid granted its substeps pays** |
| later substep | 0.17 | one substep once the rows are filled |

The three add to 0.45 against a fitted intercept of 0.49, which is the model reconstructing the fit
to within the noise floor. **Half of what the fit calls fixed is the row fill**, and it is neither
irreducible nor where anyone was looking.

Two things came out of measuring it. The relaxation row was filled on every step and read only by
the clamped conduction loop, which on a grid granted its substeps never runs — a store per node per
step that nothing looked at. And the wind weighting, a six-face sum, was computed twice per node in
the fill: once for the convection factor and once for the friction row, both of them live in air at
speed. Removing the two took a one-substep configuration down about a tenth — 2.10 ms to 1.87 ms a
simulated second on the cheapest of them, and 4.20 to 3.92 on the dearest.

`StepTermsTests` pins the structural fact a stopwatch cannot: the rows are filled once a step
however many substeps it is cut into and however many frames it is spread over, and once a substep
when the cache is switched off.

**Mirroring the block's real heat capacity for the mass floor was tried here and reverted.** The
floor reads `nodes[i].ThermalMass` once per node per step, which is the same scattered-load shape
that cost 0.66 ms in the damage check and 3.58 ms in the diagnostics — and it measured as nothing,
twice, inside a 0.04 ms noise floor. The difference is cadence and company: those two run once per
*substep* and interleave with the streaming arrays of the environment and apply passes, while the
floor runs once per step immediately after `SyncNodeState` has walked the same objects in the same
order. The pattern is not the cost; the pattern plus a cold cache is. A change with no measured
benefit and a real four bytes a block was not kept.

### The two step paths

Everything above drives `ThermalSolver.Step` directly. The game does not: it asks how long a step
it can afford before starting one, and until this was fixed that question and the step it produced
each ran their own full prologue over every node — the node mirror, the conductance totals, the
mass floor, and the stability estimate that cubes a temperature per node.

`bench steppath` runs the same grid down both paths, at three substep caps, with
`MaxElementVisitsPerStep` left active but out of reach so neither column is a shortened step. The
overhead column is the host's question expressed as a share of the step:

| blocks | 1 substep | 3 substeps | 12 substeps |
| --- | ---: | ---: | ---: |
| 8,904 | 9.9 % | 6.4 % | — |
| 32,800 | 7.9 % | 4.3 % | 2.4 % |
| 126,731 | 9.6 % | 6.9 % | 4.5 % |

After the fix every one of those nine cells reads within ±1 % of zero, which is the noise floor of
this measurement. The field runs at about three substeps, so the saving in play is the middle
column.

> The 8,904-block row at 12 substeps is omitted: both runs land near 0.7 ms and the pair moved by
> more than the effect between repeats.

**Two runs of the same grid are not interchangeable.** Conduction skips a link whose ends already
agree, so whichever path runs second inherits a flatter grid and measures cheaper. Unseeded, that
alone made the host path read 26 % *faster* than the solver path it is a superset of. Both timed
runs re-seed the spread.

This is the general form of a gap worth watching for: **a benchmark that drives the component
rather than the caller cannot see work the caller does.** The fixed-cost fit above was written to
attribute exactly this cost and attributed half of it, because both of its points were measured on
the path that pays half.

### The overshoot clamp A/B

The clamp is skipped on any step where it cannot bind. That is a saving in one regime and a cost in
the other, so the report measures both, each twice — with the test and without it:

| regime | always clamped | gated | change | clamp live |
| --- | ---: | ---: | ---: | --- |
| resolved — granted the substeps it demands | 7.99 ms | **3.55 ms** | **−56 %** | no |
| refused — 4 substeps against a demand of 20 | 1.96 ms | 1.94 ms | −0.9 % | yes |

The `clamp live` column is what says the two rows are in different regimes rather than being the
same measurement printed twice; `PerformanceReportTests` asserts it reads 0 and 1 respectively.

**The worst case sits inside the noise floor.** Across repeated runs the `refused` row moved
between −0.3 % and +7.4 % while the noise row moved between 0.08 ms and 0.39 ms on a 2.2 ms step;
the two track each other, and the low-noise runs read as zero. The test returns on the first
element that can bind, so a grid where the answer is yes stops almost immediately — it is one
comparison, not a pass.

A configuration refused its substeps this badly is not one that ships — see
[realism.md](realism.md#failure-and-what-actually-causes-it), where the starved end of the
comparison is measured on purpose.

### What being measured costs

The per-mechanism watt figures are diagnostics nothing in the simulation reads, produced for a
telemetry report, a client with the crosshair readout up, or a debug overlay. Every field dump in
this repository was taken with them on, because taking a dump is what turns them on — so the
figures a dump reports are the expensive configuration, and this row is what makes the two
comparable rather than leaving a reader to assume they already are.

| | step |
| --- | ---: |
| off — a dedicated server in ordinary play | 3.55 ms |
| on, written once a step | **4.11 ms** |
| on, written every substep | 6.88 ms |

Each figure is overwritten by the next substep and read between steps, so only the last substep's
writes were ever observed; the other eighteen were five stores into a node object per node and two
into another per link, discarded immediately. Writing on the last substep alone took the cost of
being measured from 3.58 ms to 0.57 ms.

The worst case is a grid taking one substep, where the last substep is the only substep: 0.9654 ms
against 0.9682 ms, inside the noise floor.

> `bench report --diagnostics` set a flag nothing in `PerformanceReport` read, so the whole report
> ran in the cheap configuration and printed it under a name that said otherwise. The flag now
> reaches the solver, and this section measures both configurations whether or not it is passed.

### One overheat event per block per step

Damage is accumulated per node and filed once when the step ends, so `CriticalBlocks` is a count of
blocks.

The damage check runs in the apply pass and the apply pass runs once per substep, so filing there
instead multiplies every count by the substep demand: on a 2,223-node scorched hull taking twelve
substeps, 26,676 events for 2,223 burning blocks; on a census hull at twenty-six substeps, **29,224
events for 1,124 blocks**. The total damage stays right, because every consumer sums it. Three
things that read the list rather than summing it do not:

* **`CriticalBlocks` is `Overheats.Count`**, and it is reported as a count of blocks — in the debug
  panel, the cockpit HUD, the settings menu, the mod API and the telemetry report's `critical
  blocks` column. All of them were showing the block count multiplied by the substep count, which
  moves with grid stiffness and settings, so the figure was not even wrong by a constant.
* **`ApplyOverheatDamage` resolved the block and called `DoDamage` once per event.** A burning grid
  made twenty-six engine calls and twenty-six dictionary lookups a step for each block, where one
  would do — and filed twenty-six telemetry records.
* **The harness's batched step path keeps every step's events for a whole run**, so the substep
  factor multiplied a list already proportional to run length. `bench floor --driven --ticks 4000`
  on a 43,232-block hull was **OOM-killed at 14 GB**, twice, holding on the order of 440 million
  records.

Damage is now accumulated per node and filed once when the step ends. The sum a block takes is
unchanged — `OverheatEventTests` checks the batched total against the same run stepped one at a
time — and the count is a count of blocks.

Two smaller things fell out of it. The event carries **the hottest temperature the block reached
while over its rating** rather than whichever substep filed last, so a block that peaks and cools
inside one step reports the figure a player would recognise. And an **abandoned step now owes no
damage**: the substeps that ran had already filed their events, so a step whose temperatures were
discarded could still burn a block for a temperature nothing ever published.

### The row fill reads each face weight once

The first substep of a step fills the per-node environment rows every later substep reads. Two of
those rows are six-term sums over a node's faces — the wind weighting the convection factor and the
friction row share, and the lit weighting the solar row wants — and both read the same six face
weights.

**Reading them once needs the six hoisted into locals.** Written as two calls with a row store
between them, nothing can share loads across that store: `nodeFaceWeights`, `nodeConvectionRow` and
`nodeSolarRow` are all `float[]` fields, so a compiler has to assume a store to one may be a store
to another and reload the weights.

`bench rowfill` measures the fill directly, and the vacuum row is the control:

| world | before | after | change |
| --- | ---: | ---: | ---: |
| flight | 3.602 ns/node | 3.150 ns/node | **−12.5 %** |
| atmosphere | 3.578 ns/node | 3.178 ns/node | **−12.2 %** |
| vacuum | 2.586 ns/node | 2.53 ns/node | — |

**Vacuum does not move, and it is the reason to believe the other two.** With no air there is no
wind sum, so the lit weighting is the only reader of the face weights and there is no second read
to remove. The saving appears exactly where the mechanism says it should and vanishes exactly
where it says it should.

#### Measuring the fill by turning the cache off

The report already reports this term, by subtraction: a whole step at one substep, less a
separately measured prologue, less a separately measured write-back, less a separately measured
later substep. Four measurements, three subtractions, and the residue carries all four lots of
noise — fine for a term that is a fifth of a step, useless for judging a change worth a tenth of
that.

`bench rowfill` measures it directly by turning `PrecomputeEnvironment` off. A step of N substeps
then pays N fills instead of one, so the difference between the two configurations is N−1 fills:
the signal is **multiplied** by the substep count rather than divided by it. Both figures come
from one simulation with the flag flipped between timed blocks, which
`PrecomputedEnvironmentTests` is what makes legitimate — the flag does not change the answer, so
it cannot change the state the second block starts from.

#### The fill is two different fills, and the report measured the expensive one

| world | cap | substeps | clamp | one fill | of a step |
| --- | ---: | ---: | --- | ---: | ---: |
| flight | — | 19 | off | 3.150 ns/node | 2.9 % |
| flight | 4 | 4 | live | 4.502 ns/node | 9.9 % |
| atmosphere | — | 15 | off | 3.138 ns/node | 3.5 % |
| atmosphere | 4 | 4 | live | 4.058 ns/node | 8.7 % |
| vacuum | — | 12 | off | 2.622 ns/node | 3.7 % |
| vacuum | 4 | 4 | live | 3.591 ns/node | 7.0 % |

**A refused grid fills a row an unrefused one does not.** The relaxation row is read only by the
clamped conduction loop, so it is written only while that clamp is live — which happens when the
grid is denied the substeps it asked for. It costs a float divide per node, and the capped rows
measure it at **0.9 to 1.4 ns a node**, a third of the fill.

That is what the report's `row fill` figure of 0.22 ms is measuring. It is taken at `cap 1`, where
a grid demanding nineteen substeps is granted one, so the clamp is live and the relaxation row is
in the fill. The uncapped fill — what a grid granted its substeps actually pays — is 0.10 ms on the
same hull. **Both are right; they are answers to different questions**, and the report's label does
not say which one it is asking.

#### The relaxation row's cost is the row, not the divide — tried and dropped

The relaxation factor is `mass / (h * conductance)`, clamped to one, and it is computed for every
node while the clamp is live. The divide looked like the cost, and it can be skipped exactly:
`mass / (h*G) >= 1` precisely when `mass >= h*G`, since a true quotient at or above one cannot
round below one and a quotient just below one that rounds up to one is returned as one by either
form. So the comparison decides the unbound case without paying for the divide, bit for bit.

**Only 1.1 % to 4.8 % of nodes are bound**, which `bench rowfill` now reports — so more than
ninety-five per cent of the divides were being discarded, and the change should have been worth
about a nanosecond a node against a fill of six.

**It measured as nothing.** Against three runs of the unchanged tree, the gated version came out
1.8 % faster in flight, 2.3 % faster in an atmosphere and 0.6 % slower in vacuum, against a
run-to-run spread of 4 % to 6 % on those same rows. The change was not kept.

The uncapped rows are what make that a conclusion rather than a shrug. They do not fill the
relaxation row at all, so the change cannot reach them — and they moved by ±3 % across the same
runs. An instrument whose untouched control drifts as far as its treatment has not measured the
treatment.

**What the row does cost is about 1 ns a node**, which is the gap between the capped and uncapped
rows above. Since the divide is not it, what is left is the two loads and the store: reading
`nodeConductanceTotal[i]` and `nodeThermalMass[i]`, and writing `nodeRelaxation[i]`. That is worth
recording because it says where a future attempt should aim — **not at the arithmetic, but at not
writing the row.** It is all ones for ninety-five per cent of nodes, and the conduction loop reads
it per link end regardless.

> A caveat on that 1 ns. The capped and uncapped rows differ in substep count as well as in the
> relaxation row, so the gap is not purely that row. It is the right order and the wrong number to
> quote to two figures.

### The watts row is written, not cleared and then written

The environment pass reaches every node before anything else reads `nodeWatts`, so it writes the
row outright. Zeroing it with a memset first — then having the environment pass add into it, then
conduction scatter into it, then apply read it — makes the first of those four passes dead work by
construction.

`bench wattsclear` says what removing it is worth, up a ladder chosen so the row crosses each
level of the cache:

| blocks | nodes | row | cleared | fused | saved |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 2,000 | 2,223 | 8 KB | 0.251 ms | 0.251 ms | −0.0 % |
| 8,000 | 8,904 | 34 KB | 1.141 ms | 1.139 ms | 0.2 % |
| 32,000 | 32,800 | 128 KB | 3.640 ms | 3.624 ms | 0.4 % |
| 125,000 | 126,731 | 495 KB | 16.983 ms | 16.930 ms | 0.3 % |
| 500,000 | 505,566 | 1,974 KB | 69.016 ms | 68.611 ms | **0.6 %** |

**It is a small saving, and it grows with size, which is the signature of bandwidth rather than
instructions.** At 2,000 nodes the row is 8 KB and stays in L1 between the two walks, so the
second walk is nearly free and there is nothing to recover. At half a million it is 2 MB, does not
fit in L2, and the clear is a genuine second trip to memory.

**The first version of this lab measured the change at −1.1 % to +2.8 % and the ladder had no
shape.** It built two simulations, one per path, and timed each — so the two arms were two
allocations at two addresses with two cache colourings, and whichever hull happened to land better
carried a difference larger than the one being looked for. The lab now proves equivalence on two
hulls and takes its timings from **one** hull with the flag flipped between blocks: one layout,
one warm cache, the flag the only thing that moves. That is what turned a scatter into a ladder.

The saving is not the reason the change is worth having. A memset that is *nearly* redundant is a
correctness defect at any price, so `WattsClearFusionTests` pins the ordering claim it rests on —
that the environment pass reaches every node, including buried ones, including the case where the
environment is switched off and there is nothing to write, and including when the pass is sliced
across frames. Both halves of that were checked by breaking them: removing the
generation-only clear fails one test, and accumulating instead of assigning fails eight.

### The scenarios

A plain hull in a plain world does not reach air in the compartments, plumbing on the ship, or
blocks past their rating. Each of those is a section of the solver that had never been in a
measurement, and each row carries **what the scenario actually built** beside what it cost — a
scenario that quietly builds nothing reports zero and is indistinguishable from a feature that is
free, which is precisely what happened to convection.

| scenario | step | what it built |
| --- | ---: | --- |
| plain | 1.12 ms | — |
| pressurised | **1.57 ms** | 13 rooms with air |
| plumbed | 1.20 ms | 8 coolant loops, 8 heat pumps |
| burning | 1.15 ms | **411,651 overheat events** |
| scorched | 0.58 ms | **2,136,960 overheat events** — one per node per substep |

`scorched` is the bound on the damage check rather than its ordinary cost. Burning a ship through
its heat producers leaves most of the hull under its rating, so the check mostly fails; `scorched`
puts every node over every rating in the catalogue and holds it there, with the environment
switched off so the hull does not radiate itself cold in one substep. Its step is smaller than the
others because it has no environment pass, which is the price of holding the temperature —
`WorstCaseTests` asserts it reaches exactly one event per node per substep, so a scenario that
quietly stopped burning fails rather than reporting a cheap number.

Measured both ways, the mirrored rating costs **+1.9 %** on `scorched` (0.577 ms against 0.588 ms)
and saves 15–48 % everywhere else. That is the whole trade: one extra array read in a state a ship
does not survive, against three scattered loads per node per substep in every state it does.

Room air costs about 30 % of a step for thirteen compartments. Plumbing costs 5 %. Both shares grew
when the step itself halved; neither pass got more expensive.

**The burning row is still the finding, and it is unchanged.** Twenty steps raised 411,651 overheat
events — about 20,600 per step on a hull with 970 heat producers, against 22 substeps. That is one
event *per producer per substep*, where one per producer per step would do: the damage check lives
on the apply pass, which runs once per substep. The total damage is right, because
`DamageIsPerSecond` divides by the substep length — but the event count is twenty times what it
needs to be, and in game every one of those events becomes a `DoDamage` call on the host. A burning
ship is making twenty times the damage API calls it should.

### The fleets

A world is many grids and every benchmark here has been one. Each grid pays its own fixed per-step
cost, so a fleet of small grids is not obviously the same price as one large one:

| | step, whole fleet | blocks | per thousand blocks |
| --- | ---: | ---: | ---: |
| 1 grid | 0.88 ms | 8,904 | 0.099 ms |
| 10 grids | 1.25 ms | 11,240 | 0.111 ms |
| 100 grids | 9.71 ms | 83,500 | 0.116 ms |

**Flat, and that is the answer.** Splitting the same work across a hundred grids costs what one
grid costs per block: the solver's per-grid fixed work — state sync, the stability estimate, the
mass floor — does not dominate even at a hundred to one.

That is a narrower result than it looks. The harness has no adapter: no environment sample, no
entity callbacks, no telemetry, no scheduler. The per-grid cost that a real world pays lives almost
entirely in those, which is why the largest finding of this project — every grid in a world
stepping on the same frame — came from a telemetry dump and could not have come from here. What
this section rules out is the *solver* scaling badly with grid count. It says nothing about the
adapter.

**It is also narrow in a second way that took a field dump to see.** Eight hundred blocks a grid is
the *smallest* grid the ship generator builds, and a world is not made of them: the 2026-08-20 dump
holds 97 grids of five cells or fewer and 198 of five hundred or fewer, costing 10 % of the mod's
time for 5 % of its blocks. `bench smallgrids` reads the same question below eight hundred.

### Below eight hundred blocks

A 200-grid fleet on a planet surface, each row run twice: whole, and paced the way the host drives
it — every grid visited every frame, each frame doing its share of a step.

| blocks/grid | whole | paced | of which the visit floor | pieces per step, before the floor → after |
| ---: | ---: | ---: | ---: | --- |
| 1 | 0.116 ms | +35 % | 0.0282 ms — 70 % | 3.0 → 1.0 |
| 3 | 0.150 ms | +27 % | 0.0280 ms — 69 % | 7.4 → 1.0 |
| 8 | 0.100 ms | +63 % | 0.0175 ms — 28 % | 7.4 → 1.0 |
| 32 | 0.257 ms | +9 % | 0.0174 ms — 76 % | 7.4 → 1.0 |
| 128 | 2.05 ms | +2 % | 0.0177 ms — 56 % | 7.4 → 3.0 |
| 512 | 8.68 ms | +5 % | 0.0177 ms — 4 % | 7.4 → 7.4 |
| 2,048 | 35.1 ms | +13 % | 0.0177 ms — 0 % | 7.4 → 7.4 |

The step used to be sliced into whatever a frame's share came to, which on a three-block grid is one
element visit. Re-entering the resumable stage machine costs the same whatever it carries, so such a
grid paid fifteen entries a step to integrate three blocks. The per-frame budget now banks until it
reaches 2,048 element visits or the step's remainder, whichever is smaller; the rate is untouched
and the step lands in one piece.

**The visit floor is the fourth column, and it is measured rather than argued.** The same fleet is
driven a fourth time on a zero-length frame, which returns from `AdvanceSolver`'s first line: what
is left is `Update` entered and abandoned — the settings check, the three dirty branches, the
profiler scopes — once a frame per grid whether or not the frame does any solver work. It is flat
at 5.9 ns a grid a frame from eight blocks upwards, because none of it reads the grid.

**Two things the floor does not fix, and they are the interesting ones.**

The one-block row does not move, and 70 % of what it still pays is that visit. The remaining 30 %
is the credit arithmetic below it, which a banked frame runs before deciding it has nothing to
spend. Nothing about a step can reach either; only visiting fewer grids per frame can.

The 512 and 2,048 rows do not move either, and they carry 5 % and 13 % that the floor is deliberately
too small to touch. A large grid genuinely is sliced across its window, and between two of its
frames the other 199 grids in the fleet evict its arrays: every resume is a cold start. That is the
price of spreading a step rather than staggering whole steps across frames, and it is not a bug —
but it is a design question the ladder cannot ask, because the ladder runs one grid.

#### And asked of a fleet, the trade is smaller than it looks

`bench stagger` runs the same fleet twice: whole steps one grid at a time, and the same steps cut
into eight slices and interleaved. Identical arithmetic over the same nodes in the same order, so
what separates them is locality and nothing else.

| Grids | 1,004 nodes each | 2,223 nodes each |
| ---: | ---: | ---: |
| 4 | −0.0 % | 0.0 % |
| 16 | 1.0 % | 0.1 % |
| 64 | 1.7 % | 5.5 % |
| 242 | **12.2 %** | 5.0 % |

**The penalty is set by the fleet's working set, not by any grid's size.** Four grids interleaved
still fit in cache and spreading is free; two hundred do not and it costs 12 %. It appears once the
fleet's arrays stop fitting — 64 grids of 2,223 nodes reaches it where 64 of 1,004 does not — which
is why the single-grid ladder above could never see it.

**The lump is 0.53 ms for a 1,004-node grid and 1.19 ms for a 2,223-node one**, flat across fleet
sizes, because it is one grid's step and nothing else. That is the number the current design was
chosen to avoid, and it is worth reading against what staggering actually changes: a frame still
carries the same total work — `grids × Frequency / 60` steps' worth either way — so staggering does
not add work to a frame, it makes the work a frame carries belong to fewer grids. What it gives up
is *granularity*: a grid whose whole step will not fit the frame's budget can no longer be split.
At about half a microsecond a node, that is roughly seven thousand nodes against a four-millisecond
budget.

So the shape the measurement points at is not one schedule or the other but a threshold: step whole
the grids that fit, spread the ones that do not. `StaggerTests` pins that the penalty grows with the
fleet and that the lump does not. [backlog.md](backlog.md) `D14`.

**Every rung is timed over the same number of grid-steps, and that is a correction rather than a
detail.** The lab ran a fixed eight *rounds* at every fleet size until 2026-08-24, and a round
advances every grid once — so a timed repeat was 32 grid-steps at four grids and 512 at sixty-four,
one window of about four milliseconds and one of about seventy. Best-of-five over four milliseconds
is a reading a single scheduler hiccup lands inside all five times, and the two rungs were then
divided by each other as though the same instrument had taken both (`P6`). That is what made the
lump read 0.75 ms at four grids against 0.13 ms at sixty-four whenever something else was compiling.
The window is 512 grid-steps at every rung now, which leaves the largest one exactly as it was and
lengthens the rest to match, so no figure on this page moves.

**The penalty claim refuses a reading it cannot resolve rather than failing** (`M5`). Its floor is
measured from the repeats themselves; past 30 % — several times the largest locality effect it is
looking for, and the same figure the test already asserted on — it prints `REFUSED` with the floor
and asserts nothing about the ordering. A regression larger than the floor still fails. The
alternative was a suite that is red whenever the machine is busy, which is the failure mode that
teaches people to ignore a red suite.

**The lump claim needed something else, and finding out took an hour.** It was held on a *ratio* of
two timings taken at two fleet sizes, and that ratio is a claim about the machine's cache: 64 grids
of 600 nodes is a working set an order of magnitude larger than four of them, so a machine busy with
something else evicts the big one and not the small one. **That penalty is steady, and a floor
computed from repeat-to-repeat spread cannot see a steady bias** — it failed at a floor of 6 % with
the lump reading 0.103 ms at four grids against 0.289 ms at sixty-four, an hour after the refusal
gate was added to catch exactly this. So the regression it exists for — per-grid cost growing with
the fleet, which is what a per-fleet scan smuggled into the per-grid path would do — is held on the
solver's own work counters instead: **38,633 element visits a grid-step at four grids and at
sixty-four, identical rather than close**, on any machine. The milliseconds are printed beside it
and are read here. That is `LoadTests`' own principle, applied a second time: *a millisecond
threshold is a claim about the machine, and "placing one block must not visit every node" is a claim
about the algorithm.* [backlog.md](backlog.md) `A11`.

**The eight-block row is not a stable figure.** It has swung from +48 % to +74 % across repeats of
an unchanged tree, on a whole cost of a tenth of a millisecond for two hundred grids; its share
column swings with it. Read the rows at 32 blocks and above, and the visit column, which do not
move.

**Reading a paced row against a whole one takes care.** Both phases are the same grids in the same
state, so whichever runs second inherits what the first left behind — a settled temperature spread,
a completed room map — and conduction skips a link whose ends agree. Run in blocks, the paced phase
measured *five times faster* than the whole phase it is a superset of. The rows above alternate the
order between repeats and re-seed the spread before each.

---

## What each pass cost

A single comparison says whether one change was a regression. It cannot say whether a year of
changes has been a drift. [The iteration log](#the-iteration-log) below keeps one row per pass over
this repository — the suite's size and duration, and the headline step figures — so the trend is
readable without reconstructing it from commit messages.

## Reading a comparison

```
regressions (2)
  features/conduction/marginal                        4.7200 ->      5.9100  +25.2%
  ladder/125,000/step                                  82.18 ->       94.30  +14.7%

improvements and neutral moves (1)
  substep cap/cap 4/step                              0.7430 ->      0.6100  -17.9%
```

A figure is a regression when it grew and lower is better for it; substep counts and block counts
are marked neutral so they are reported as moves rather than as failures. The threshold is 5 % by
default — below the noise floor on most machines there is nothing to say.

**A figure that was inside the noise floor cannot have regressed**, and the comparison says so
rather than counting it. This is not a detail: making the solver leaner lifts several feature costs
out of the noise at once, and every one of them arrives in the diff as a large percentage increase
over a number that never meant anything. The first run of this against a real optimisation reported
sixteen regressions, fifteen of which were figures that had previously been unmeasurable.

Rows that appear or vanish are listed separately. A vanished row usually means a case was renamed,
which is worth knowing precisely because it breaks every future comparison silently.

`BenchmarkBaselineTests` is the guard on that. It compares the committed baseline's *keys* against
a fresh run's and fails when they diverge, so a rename or a new section fails a test rather than
quietly turning the baseline into a list of rows nothing joins to. It checks keys only — timings
belong to the machine that took them, which is what the calibration row is for. When it fails,
re-record:

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
```

Record it on a quiet machine and check the noise row before committing: this baseline's spread was
0.025 ms, and runs taken at four times that noise moved the smallest figures in the report by
several per cent.

---

## Keeping it honest

The report is only as good as the hull it measures, and for most of this project's life that hull
was wrong — see
[load-and-hitching.md](load-and-hitching.md#the-ladder-is-measured-on-a-census-hull-not-an-armour-cube). Everything
is now built from [`Census`](../tests/Thermodynamics.Harness/Census.cs), the block population of a
real ship read out of a telemetry dump, and `CensusFidelityTests` fails if it drifts away from the
field observations recorded beside it.

**Refresh both as dumps arrive.** The census tiers describe what a ship is made of; `Census.Field`
records what those ships were observed to do. When a new report lands, update the tiers to the new
population and the field constants to the new observations, and the fidelity tests will say whether
the synthetic ship still resembles the real one.

---

## What a substep costs

The step budget bounds a step at `links + 4 × nodes` element visits. That weighting is measured
rather than assumed, because a substep runs both a conduction pass that is per *link* and an
environment pass that is per *node*, and counting one of them grants two grids of the same link
count and different shapes the same allowance for different work.

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- bench elements --nodes 100000 --seconds 4
dotnet run --project Thermodynamics.Sim -- bench elements --nodes 250000 --seconds 4 \
    --shapes stick,comb,plate,hollow,box --csv out/bench
```

### Method, and two things it has to get right

Shapes are chosen for their **link-to-node ratio** rather than for realism: isolated blocks touch
nothing, a stick is a chain, a plate is two-dimensional, a hollow box is a shell, and a solid box
approaches three links per node. The whole substep is timed and the coefficients fitted by least
squares through the origin. The `ship` shape is held out of every fit and predicted from it, which
is the only honest check that the coefficients describe anything.

**A single fit does not work, and the reason is geometric.** Fitting `cost = a·nodes + b·links` with
the environment on gives 0.37 ns a link, a negative r², and a solid box measuring *cheaper* than a
stick with a third of the links. On a cube lattice every cell face is either bonded or exposed, so
`faces ≈ 6·nodes − 2·links`: exposure and link count are nearly collinear across any family of
shapes, and the box is cheaper because its interior blocks have no exposed faces to integrate. The
environment is therefore separated by **differencing** — each shape measured twice, with radiation,
convection and solar off and on.

**A substep figure has to be measured where substeps dominate.** At the shipped `HeatTimeScale`
these grids demand *one* substep a step, so a per-substep number is really the per-step overhead —
array syncing, environment sampling, write-back — which is an order of magnitude larger than the
per-element work and is charged once a step. The lab runs at `HeatTimeScale` 20,000, which puts
every connected shape at 27–81 substeps a step. The `dust` shape cannot be driven there at all:
with no links it has no conduction stiffness, so it stays at one substep and is excluded from the
fits. Leaving it in drives the per-link coefficient negative.

### What it measures

Best of three per condition, .NET 9.

| Nodes | ns per node | ns per link | Env adds, per node | **A node is worth** | An exposed face |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 4,000 | 2.63 | 1.45 | 1.37 | **2.8 links** | 0.14 links |
| 60,000 | 2.60 | 2.85 | 1.58 | **1.5 links** | 0.14 links |
| 100,000 | 6.1–6.6 | 2.6–2.9 | 1.8–3.4 | **3.3 links** (2.9–3.7, n=3) | ~0.19 links |
| 250,000 | 13.2–13.9 | 1.8–2.2 | 1.4–2.3 | **7.5 links** (6.6–9.0, n=3) | ~0.4 links |
| 500,000 | 15.0 | 1.67 | 7.0 | ~13 links (one noisy run) | ~0 |

Conduction fits well — r² 0.88 to 0.99, and the held-out ship predicted within 1–15%. The
environment fit is much weaker, r² 0.08 to 0.72, for a reason that is itself the third finding.

### Three conclusions

**1. A node is not free, and it is not one link either.** At the sizes where the budget binds it is
worth three to eight links. On the field ship's ratio of 2.4 links per node, counting links alone
under-charges a step by about 2.4× at 100k blocks and 4× at 250k.

**2. The weight rises with grid size, because nodes are the working set.** Per-link cost is flat at
1.5–2.9 ns across a hundredfold size range — links stream. Per-node cost goes 2.6 → 6.4 → 13.4 ns as
the node state stops fitting in cache. This is why one constant cannot serve every grid, and why it
does not need to: the weight only has to be right from 100k up, where it measures **3 to 8**.

**3. Exposure is not what the budget is missing.** An exposed face costs 0.1–0.5 of a link, and the
environment fit's poor r² says the same thing from the other side: most of the environment pass is
per node — sampling, the radiation terms' setup, the write-back — and only the loop inside it scales
with exposure. A budget that counts nodes and links and ignores faces loses very little.

**Four is chosen at the low end of the measured 3-to-8 range**, at the size where the budget first
binds. A grid past a quarter of a million blocks is still slightly under-charged, which errs toward
letting a large grid run rather than throttling it on a machine that could have kept up. See
[load-and-hitching.md](load-and-hitching.md#calibrating-the-step-budget-against-a-real-world) for
what the default buys in game.

> **Absolute nanoseconds here do not transfer to the game.** The harness is .NET 9 and the game is
> .NET 4.8; a field dump measured 34.8 ns per element against roughly 1.9–2.0 ns here — and both
> figures move with the solver, so the ratio is only meaningful between a dump and a harness run on
> the same tree. The *ratios* are arithmetic per element rather than throughput, which is why the
> lab reports link-equivalents, and `AWeightIsLinksPerNodeAndSurvivesAChangeOfUnits` pins that they
> do not move when the machine does.

---

## What the allowance is worth

`MaxElementVisitsPerStep` is the one shipped bound whose unit had never been priced. The section
above measures what a *node* is worth in links, which is how the unit was built; this measures what
the unit itself costs in milliseconds, and what the thing it trades away costs in kelvin.

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- bench allowance --csv out/bench
dotnet run --project Thermodynamics.Sim -- bench allowance --max 32000
```

### It is a per-frame budget wearing a per-step name

A step is spread across the frames of its own window — `ThermalSimulation.Update` banks
`StepWorkUnits × frameSeconds × StepsPerSecond` of credit a frame — so an allowance of `V` at
`Frequency f` bounds a frame at **`V × f / 60`** element visits, whatever the grid. At the shipped
4,000,000 and `Frequency` 4 that is **266,667 visits a frame** — it was 133,333 at the 2,000,000
that shipped until `C27` — and it is the only quantity the setting controls. `AnAllowanceIsAPerFrameBudgetScaledByTheStepRate` measures the ceiling off a
throttled grid rather than deriving it, and lands within one substep of it.

That is also why the setting moved with `Frequency` in 2026-08-22 instead of staying put, and why a
figure quoted per *step* is the wrong unit to argue about.

### The unit had no measured cost, because nothing divided by it

Every *ns per element visit* figure this repository publishes — [the ladder](#the-ladder), and the
`ns per element visit` field a telemetry dump prints — divides solver milliseconds by
`nodes + links`. The budget counts `links + 4 × nodes`. On a census hull those differ by very close
to **2×**, so converting the shipped allowance into milliseconds through either published figure
overstates its cost by about a factor of two. Both figures are correct for what they say; neither is
the setting's own currency, which is why the allowance could not be chosen against a frame.

Measured in the currency the setting counts in, on a driven census hull:

| blocks | vacuum | atmosphere | flight |
| ---: | ---: | ---: | ---: |
| 8,904 | 1.05 | 0.99 | 0.95 |
| 32,800 | 1.08 | 1.01 | 0.98 |
| 126,731 | 1.24 | 1.07 | 1.03 |

*ns per element visit, `links + 4 × nodes`, harness, best of three, 2026-08-24.* Multiplied by the
`nodes + links` ratio — 2.01 across these three rungs — that is **1.88 to 2.50 ns** in the ladder's
unit, against the ladder's own 1.73–1.86. The gap is the right size and in the right direction:
these rows are the *host* path, which asks how long a step it can afford before starting one, and
[the two step paths](#the-two-step-paths) is what that question costs.

### Where it binds, which is air rather than vacuum

The share of real time a settled, driven hull keeps at each allowance. **100 % means the bound was
never reached**, and a bound that is not reached costs nothing at all (`P8`).

| blocks | allowance | vacuum | atmosphere | flight |
| ---: | ---: | ---: | ---: | ---: |
| 8,904 | 2,000,000 | 100 % | 100 % | 100 % |
| 16,558 | 2,000,000 | 100 % | 100 % | **84.2 %** |
| 32,800 | 2,000,000 | 100 % | **52.7 %** | **36.2 %** |
| 64,463 | 2,000,000 | **68.9 %** | **26.3 %** | **18.1 %** |
| 126,731 | 2,000,000 | **27.2 %** | **11.9 %** | **8.5 %** |
| 32,800 | 4,000,000 | 100 % | 100 % | **72.5 %** |
| 64,463 | 4,000,000 | 100 % | **52.7 %** | **36.2 %** |
| 126,731 | 4,000,000 | **68.0 %** | **29.8 %** | **21.2 %** |

**The demand is three to four times larger in flight than in vacuum** — 23.6 to 27.5 substeps
against 6.8 to 7.3 — because air puts a convection term on every exposed node, and three times the
demand reaches the same ceiling at a third of the size. So at 2,000,000 the size where a hull stops
being covered is about 32,000 blocks in vacuum, 16,000 in atmosphere and **9,000 in flight**; at the
4,000,000 that ships since `C27`, 64,000, 32,000 and 16,000 — every one a rung of the sweep
rather than an interpolation between two.

[backlog.md](backlog.md) `C27` measured this in vacuum, which is the cheapest of the nine worlds on
the axis that matters and the same reading [the environments](#the-environments) exists to stop
anyone taking again.

### What the rate it trades away is worth

A grid whose step the allowance shortens is not made less accurate — each step is as faithful as it
was and there are fewer of them, so **its thermal clock runs slow**. That is the quantity `F23`
measured with `ClientInputLab`'s `SimSpeedError`, at one point: 2.52 K standing at a 10 % deficit on
the sweep's own rig, under a moving load, and 0.00 K under a steady one.

One point is a slope only if the curve through it is straight, and the deficits above reach past
60 %, so `bench allowance` runs the same mechanism up a ladder instead:

| deficit | peak K | standing K | K per unit |
| ---: | ---: | ---: | ---: |
| 5 % | 5.36 | 1.19 | 23.7 |
| 10 % | 10.99 | 2.52 | 25.2 |
| 20 % | 23.29 | 5.79 | 29.0 |
| 30 % | 36.99 | 10.16 | 33.9 |
| 45 % | 61.00 | 20.01 | 44.5 |
| 60 % | 90.84 | 36.98 | 61.6 |

*2,000-block census hull, planet, 600 s of thermal time, load alternating every 120 s.* The 10 % row
reproduces the degraded-input sweep's published `slow clock` figure exactly, which is the check that
the two are the same experiment (`P4`), and
`ThePriceLadderLandsOnTheSweepsOwnSlowClockRow` holds them to it.

**The curve is convex**, 23.7 K per unit at the shallow end and 61.6 at the deep one, so a straight
line through `F23`'s point under-reports the deep end by two and a half times. Rows past 60 % are
printed with a `>` rather than extrapolated (`P2`).

**The scope the kelvin column carries** (`P1`): it is a hull under a *moving* load, which is the only
place a clock error shows at all. It is an upper bound on a ship whose load is steadier and says
nothing about a settled grid, because two hulls heading to the same equilibrium at different speeds
agree once they arrive.

### What that makes it, beside the approximations the mod has already priced

| approximation | what it costs | what it is |
| --- | --- | --- |
| the substep ceiling `C19` accepted | 0.028 K on the hottest block | shipped, and recorded as a limit |
| `MaxSubstepsPerBlock 6` (`C3`) | 0.607 K on the worst-placed block | **not** a default — a switch, on that figure |
| the allowance at 2,000,000 | 4.4 K at 16,558 blocks in flight, >37 K at 32,800 | was the shipped default |
| the allowance at 4,000,000 | nothing to 16,558, 9.1 K at 32,800 in flight | the shipped default since `C27` |

Same hull family, same rules, three orders of magnitude apart. The allowance was by a wide margin
the largest approximation the mod shipped and the only one never priced — not because anyone chose
that, but because a millisecond and a kelvin had never been put in the same sentence for it.
Doubling it does not remove the trade, it moves it onto the top 3 % of the corpus by size: see
[configuration.md](configuration.md#what-a-shortened-step-costs) for what was bought and what it
cost.

---

## The iteration log

One row per pass over this repository, so a change over many sessions reads as a trend rather than
being reconstructed from commit messages. Each row records what the suite cost to run and what the
solver cost to step, taken at the end of that pass.

> The rule argued here is stated canonically in [rules.md](rules.md): `M7`.

**Read the columns for their shape, not their absolute value.** Suite duration and step figures both
belong to the machine that took them; a row taken elsewhere is not comparable to its neighbours. The
calibration and noise columns are what say whether two rows may be read against each other at all —
a run taken at several times the usual noise moves the smallest figures by several per cent.

**A row belongs to its *session*, not only to its machine, and the two rows are further apart than
they look.** Pass 9's sixth iteration measured bit-identical code at a stage median of 4.75 ms in one
held window and 11.40 in another on the same machine, and the calibration and noise columns did not
see it — what moved was the shape of the distribution rather than its floor. Two rows of this log
are a trend, not a measurement; a measurement is two legs alternated inside one window (`M7`).

The benchmark baseline itself is versioned at
[`tests/benchmarks/performance.csv`](../tests/benchmarks/performance.csv), so any figure in the
report can be recovered for any row here by reading that file at the row's commit.

### The suite

| # | Date | Commit | Tests | Duration | Note |
| ---: | --- | --- | ---: | ---: | --- |
| 0 | 2026-08-20 | `b0a8496` | 1,017 | 45 s | the state that session started from |
| 1 | 2026-08-20 | `57d1807` | 1,020 | 45 s | +3, `FixedSourceRowTests` |
| 2 | 2026-08-20 | `78d736f` | 1,020 | 45 s | no test added or removed; five suites moved onto one fixture |
| 3 | 2026-08-20 | `9aaf3d2` | 1,026 | 44 s | +6: `StepTermsTests`, and a flight case for the bit-identity suite |
| — | | `9aaf3d2..55a6935` | | | **36 commits recorded no row.** The wind model, per-planet climate, block derivation from build components, the blueprint corpus and the balance lab all landed between rows 3 and 4. |
| 4 | 2026-08-21 | `55a6935` | 1,285 | 2 m 11 s | +259 across those 36 commits and this one; added `DumpAuditTests` and the field-dump fixture |
| 5 | 2026-08-20 | `b7ccf75` | 1,358 | 5 m 4 s | +73: world settings, the overlay budget, the descent, the planet-definition merge, `RescanGate`, the burial audit. Nine slow suites now carry `speed=slow`. |
| 6 | 2026-08-20 | `f411f7d` | 1,359 | **50 s** | one ungated corpus test was 4 m 57 s of every run since the fixture landed; rows 4 and 5 carry it. The `speed!=slow` lane is 14 s. |
| — | | `f411f7d..c18e3e4` | | | **20 commits recorded no row.** The wind burial audit, the per-planet climates, the block heat index, the balance bench and the first full corpus survey landed between rows 6 and 7. |
| 7 | 2026-08-22 | `c18e3e4` | 1,467 | 51 s | +108 across those 20 commits |
| 8 | 2026-08-22 | *(the 2026-08-22 pass)* | 1,528 | 50 s | +61. A defragmentation pass and then a validation one: shipped code is **343 lines shorter** with nothing left in it that nothing calls, and the ground truth every benchmark rests on moved from two ships in a vanished session to 8,102 workshop hulls measured in the lab. |
| 9 | 2026-08-22 | *(the documentation pass)* | 1,529 | — | +1, `EveryPageHasAChangeLog`. Documentation only: 31 pages merged to 21, every page given a change log, and six published figures corrected. **No duration**, because the run was taken on a different machine from rows 6–8 and a figure that cannot be compared to its neighbours is worse on this table than a dash. No shipped solver code changed, so there is no solver row below. |
| 10 | 2026-08-22 | *(the standardisation pass)* | 1,533 | 2 m 36 s | +4: `EveryCheckCitedByTheRulesPageResolves`, `EveryRuleCitedByAPageExists`, `TheRulesPageIndexesEveryRuleItStates` and `NoDocCommentDescribesSomethingThatIsNotThere`. Documentation and comments only. **The duration is not comparable to rows 6–8** — this run was serialised under `maxParallelThreads: 1` and taken on a different machine — so it is recorded for the count beside it and nothing else. No shipped solver code changed, so there is no solver row below. |

| 11 | 2026-08-22 | *(the profiles pass)* | 1,520 | 53 s | −13: the five settings profiles are gone and with them `ProfileTests`, replaced by `DefaultSettingsTests`. The mod ships one configuration. Shipped solver defaults moved, so there is a solver row below. |
| — | | `131fc12..b146255` | | | **Many commits recorded no row.** The retune, the coolant consumable, the reach tests, the corpus reader fix and the rules pass all landed between rows 11 and 12. |
| 12 | 2026-08-26 | *(the performance pass, tip)* | 2,001 | **1 m 22 s** | +478 across those commits and this pass's own suites. Taken on the optimised build; the fast lane is 4 s over 1,582 after the lane refresh, from 37 s before it. The rows above were taken on unoptimised builds, so this duration is not comparable to theirs ([performance.md](performance.md#iteration-1--the-harness-measured-unoptimised-code)). |
| 13 | 2026-08-27 | *(the second performance pass, tip)* | 2,016 | **1 m 21 s** | +15: `StageLabTests`, `GridModelAdjacencyTests`, `ExposureFastPathTests`, `BlockInstanceOneCellTests` and the radix and scan pins in `RoomMapFreezeTests` and `RoomMapSnapshotTests`. No solver row: the pass moved no step figure, by design, and the stage instrument (`bench stages`) is where its figures live ([performance.md](performance.md#pass-2-iteration-10--what-the-pass-moved)). |
| 15 | 2026-08-27 | *(the fifth performance pass, tip)* | 2,044 | **1 m 14 s** | +10: `StepPhaseLabTests` and `LinkSpanProbe` for the new step instruments, `HeatGainHoistTests` for the hoisted sum, and the occupancy-filtered walk, twice-built room air and lowest-critical bound pins added to `GridModelAdjacencyTests`, `RoomAirCanonicalTests` and `OverheatEventTests`. `BranchlessEnvironmentTests` came and went with the change it checked. **A solver row below**, for the first time since row 11: this pass moved the step. |
| 16 | 2026-08-27 | *(the sixth and seventh performance passes, tip)* | 2,047 | **1 m 23 s** | +3, all of them `CanonicalLinkOrderTests`. **Two passes and one change kept**, which is the row's point: the sixth measured five rewrites of the link build's neighbour lookup and the seventh measured the sixth's designed fix, and every one was reverted ([performance.md](performance.md#pass-7--what-sixteen-iterations-on-one-stage-establish)). The tests each of those needed were reverted with them. No solver row: neither pass touched a step. |
| 17 | 2026-08-27 | *(the eighth performance pass, tip)* | 2,048 | **1 m 26 s** | +1: `EveryStageStopsForAReasonItCanName`, which replaced a check that failed and passed on consecutive runs of unchanged code. `LinkWalkTests` came back with pass 6's halving, to be re-measured on the corrected instrument, and went again with it. **This pass changed the instrument, not the solver**: `bench stages` kept the fastest of a fixed fifteen for six passes and had never converged — three runs of one binary spread 48 %, 28 % and 67 % on the surface, room and link stages, against 3.5 % for the solver, which was quietly taking twenty times the samples. It repeats until its best is reproduced now. What that does to the figures already on this page is audited in [performance.md](performance.md#pass-8-iteration-7--which-published-figures-survive). |
| 18 | 2026-08-28 | *(the ninth performance pass, tip)* | 2,082 | **1 m 14 s** | +34. Six of the ten iterations were about the instrument rather than the model, and the suites say so: `ProjectFileTests` (the mod project had not built for three commits and nothing said so), `UncalledCodeTests` (`D2`'s own stated gap, two dead helpers found), `ExposureSkipTests`, and additions to `SampleStatisticTests`, `StageLabTests`, `PerformanceReportTests`, `FacePackingTests` and `BlockInstanceOneCellTests`. **The two changes to the shipped model are both on the load path**: the exposure stage's six per-face writes became one and one-cell blocks stopped allocating a surface array each. No step figure moved and none was meant to. |
| 14 | 2026-08-27 | *(the fourth performance pass, tip)* | 2,034 | **1 m 14 s** | **+18 since row 13, across the third and fourth performance passes** — the third took no row of its own, which is why this one spans two. The fourth's own suites are `RoomAirCanonicalTests`, `RoomSpanFloodTests` and `GridOccupancyTests`, the rank pins in `CellBitsetTests`, and the store, hint and contiguity pins in `RoomCellStorageTests`; against them, five radix-sort cases went when the sort did. No solver row: this pass moved no step figure, by design, and its figures live in the stage instrument ([performance.md](performance.md#pass-4--what-the-pass-moved)). |

### The solver

Headline figures from `bench report --size 32000 --max 125000`, on a 32,800-block census hull in
flight unless the row says otherwise.

| # | Commit | Calibration | Noise | Ladder 8k | Ladder 32k | Every feature on | Convection isolated | Env pass, ns/node |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | `b0a8496` | 89.3 ms | 0.065 ms | 1.211 ms | 3.834 ms | 3.831 ms | 1.902 ms | 2.29 |
| 1 | `57d1807` | 86.2 ms | 0.014 ms | 1.129 ms | 3.603 ms | 3.595 ms | 1.661 ms | 2.11 |
| 2 | `78d736f` | — | — | — | — | — | — | — |
| 3 | `9aaf3d2` | 86.2 ms | 0.043 ms | 1.119 ms | 3.544 ms | 3.550 ms | 1.617 ms | 2.11 |
| 4 | `55a6935` | 87.4 ms | 0.038 ms | 1.134 ms | 3.621 ms | 3.613 ms | 1.680 ms | 2.17 |
| 5 | `b7ccf75` | 105.6 ms | 0.206 ms | 1.210 ms | 4.198 ms | 4.282 ms | 1.951 ms | 2.41 |
| 7 | `c18e3e4` | 85.8 ms | 0.057 ms | 1.095 ms | 3.490 ms | 3.496 ms | 1.616 ms | 2.02 |
| 8 | *(the 2026-08-22 pass)* | 84.0 ms | 0.075 ms | 1.085 ms | 3.461 ms | 3.510 ms | 1.636 ms | 2.02 |
| 11 | *(the profiles pass)* | 105.9 ms | 0.123 ms | 2.134 ms | 6.719 ms | 6.583 ms | 3.093 ms | 2.09 |
| 12 | *(the performance pass)* | 83.1 ms | 0.131 ms | 1.143 ms | 5.373 ms | 5.301 ms | 3.010 ms | — |
| 15 | *(the fifth performance pass)* | 59.8 ms | 0.071 ms | 1.026 ms | 4.561 ms | — | 2.424 ms | — |
| 18 | *(the ninth performance pass, tip)* | 41.6 ms | 0.179 ms | 1.048 ms | 4.272 ms | 4.211 ms | 2.162 ms | — |

Row 2 changed no shipped code, so its solver figures are row 1's.

**Row 18's calibration is not comparable with any row above it, and that is the ninth pass's own
doing.** Until that pass the calibration was a *single sample*; it is the fastest of three now
(`M4`), so the 41.6 ms against row 15's 59.8 is a change of instrument as much as of machine — and
since every cross-machine comparison divides by this column, no ratio spanning row 17 and row 18 is
a measurement. The ladder and step columns are directly comparable: their sampling did not change.
And by `M7` none of these rows is a measurement against another in any case — they are a trend, and
the ninth pass's own start-against-tip figures are in
[performance.md](performance.md#pass-9-iteration-10--what-the-pass-moved-and-the-control-that-was-not-one).

**Row 12 is the first row on this table taken on an optimised build, and nothing else on it is**
(`M13`). It is not readable against its neighbours at all: the same commit measured both ways is
3.4× apart. What it *is* readable against is the pass's own starting commit, measured in the same
window on the same build — 286.2 ms calibration, 1.194 and 5.381 ms on the ladder, 5.431 ms with
every feature on, 3.137 ms for isolated convection — which is to say **the pass moved no step
figure**, by design: nothing in it touched the substep loop, and the columns above are its control.
What the pass moved is the world load and the build, and those are in
[performance.md](performance.md#what-the-pass-moved). The environment-pass column is left blank
because that figure comes from `bench elements`, which this pass did not re-run.

**Row 15 is the first row on this table where a step actually moved.** Rows 13 and 14 took no solver
row because their passes moved no step figure; the fifth pass moved it, and this is where a step
figure lives. Against row 12 — the only other optimised row — the ladder reads 1.026 against 1.143
at 8,000 blocks and 4.561 against 5.373 at 32,000. **Read those as corroboration, not as the
result**: the two rows are two separate windows, and the pass's own figure is the
interleaved one, **0.86** at 505,566 blocks with three untouched stages as controls
([performance.md](performance.md#pass-5--what-the-pass-moved)). *Every feature on* is blank because
that case is not in `bench report`'s output any more, and the environment column for the same reason
as row 12's.

**Row 11's step columns doubled on purpose and its cost did not move.** `Frequency` went from eight
steps a second to four, so a step covers twice as long and demands twice the substeps — 21.96 to
43.91 on the 8,000 rung — and `MaxElementVisitsPerStep` was doubled with it, from 1,000,000 to
2,000,000, to keep the per-frame charge identical. What a player pays is per simulated second, and
that column is **8.948 ms before against 8.537 ms after** on a machine whose calibration is 23%
slower. The ship is also still granted everything it asks for: 44 substeps against 43.91 demanded,
where before it was 22 against 21.96. The doubling is arithmetic, not cost.

**Row 11's machine is not rows 0-8's.** Calibration is up 26% on row 8 and noise is 1.6x, in two
runs twenty minutes apart, so it is the machine's state rather than one loud sample. Nothing in the
row is readable against its neighbours except through the per-simulated-second comparison above,
which is taken from the committed baselines at both commits.

**Row 3's noise column is three times row 1's**, and row 1's is the quietest run this machine has
recorded. Differences of a few per cent between those two rows are not readable.

**Row 4 is row 3 on a slightly slower machine.** Calibration is up 1.4% and every column with it, by
1.4–3.9%, in the same direction and roughly the same proportion — which is what a machine looks
like, not what a change looks like. The two rows are comparable in the one way that matters:
`substeps demanded` on both ladder rungs is identical to six decimal places, so the census hull's
stiffness has not moved and the columns measure the same work.

**Row 5 was taken on a loud machine and its solver columns are not readable against row 4.**
Calibration is up 21% and noise is five times row 4's; a re-take mid-pass moved every column
together. Nothing in that pass touched the per-node path, so the 14–19% across the board is the
machine.

**Rows 7 and 8 were taken back to back on one machine, which is the only way that pass's figures
mean anything.** The committed baseline is pinned at row 3, two hundred commits back, so a diff
against it spans everything since — including the change that stopped filing an overheat event per
substep, which belongs to a commit before that pass began. Measured against its own start instead —
the same report at `c18e3e4` and at the tip, twenty minutes apart on an idle machine — **the pass
moved nothing.** Six rows are named as regressions and every one is inside or barely above a noise
floor that is itself up 30% between the two runs; the ladder rows did not move enough to be named.

**A baseline is re-recorded only when its keys change**, never to refresh its timings: re-recording
for a pass that moved no solver code bakes that run's machine state into every future comparison.
`BenchmarkBaselineTests` reads the keys and not the numbers (`M6`), which is what makes that safe.

### Where a pass moves something the columns cannot see

| # | Figure | Before | After |
| ---: | --- | ---: | ---: |
| 3 | `optimized`, per simulated second | 2.100 ms | **1.873 ms** |
| 3 | `simlite`, per simulated second | 2.081 ms | **1.868 ms** |
| 3 | `simulation`, per simulated second | 4.201 ms | **3.916 ms** |
| 3 | substep cap 1, step | 0.668 ms | **0.619 ms** |
| 3 | substep cap 4, step | 1.210 ms | **1.165 ms** |
| 3 | step shape, fixed per step | 0.535 ms | **0.494 ms** |
| 8 | `GridModel` indexes, B/block at 126,731 | 120 | **86** |
| 8 | `RoomMap` retained, B/block at 126,731 | 166 | **128** |
| 8 | `RoomMap` retained, B/block at 505,566 | 309 | **229** |
| 8 | retained total, B/block at 126,731 | 1,095 | **1,023** |

Row 3's first three figures are the `optimized`, `simlite` and `simulation` settings profiles, which
no longer exist — the mod ships one configuration. They stay because they are what was measured, and
they cannot be re-taken.

**Row 8's second half changed no code the columns can see and changed what the columns mean.** The
census hull is the instrument every figure above is taken on, and it was held to `Census.Field` —
21.35 and 31.25 substeps, from two ships in two live sessions. `StiffnessLab` asks the same question
of 8,102 real workshop blueprints in four minutes. The field figures survive, landing at the 63rd
and 85th percentile. What they could not show is that the population is **bimodal** — a light sets
the substep count on 45% of hulls at a median of 28.5, and armour on the rest at 4.8, with almost
nothing between — and that the census hull sits in the trough between them, feeling a little less of
the air than a typical hull because its stiffest block is less exposed. It also placed the hull's
*heat*: 12.1 kW a block against a real median of 335 W, the 96th percentile, which reaches every
temperature figure and no stiffness one. See
[stiffness.md](stiffness.md#the-same-question-asked-of-eight-thousand-real-ships).

### Recording a row

**Measure the pass against its own start, not against the committed baseline.** The committed file
is whatever pass last had to re-record it, so a diff against it spans every commit since and
attributes all of them to the pass that ran it. Build the pass's starting commit in a worktree, take a report from it into a scratch
directory, then run the tip's report with `--baseline` pointing at that. Twenty minutes apart on one
idle machine is what makes the two comparable, and it is the only way to say a pass moved nothing.

```bash
cd tests
dotnet test                                                                   # tests, duration
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
dotnet run --project Thermodynamics.Sim -- bench elements                     # ns per node
```

Take it on a quiet machine and check the noise row before committing. A row whose noise column is
several times its neighbours' should be re-taken rather than explained.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-28 | Recorded row 18 in both tables, from the ninth performance pass. **Its calibration column is not comparable with any row above it** — that pass made the calibration a fastest-of-three where it had been a single sample, and every cross-machine comparison divides by it. The ladder and step columns are unaffected, and by `M7` no two rows of this log were a measurement against each other in the first place. |
| 2026-08-27 | **The ladder's `build` column is timed three times like every other case.** It was one stopwatch from the day this report was written, under the sentence above saying otherwise — and it is the column a reader compares between runs to say a load-path change worked. `bench report --repeats N` exposes the dial so the claim can be measured rather than believed, and the repeats are asserted to have built the same graph, because the fastest of two different walks is not a figure. |
| 2026-08-27 | Said what a row of the iteration log is and is not. The log's caution was *a row belongs to the machine that took it*, with the calibration and noise columns as the guard; pass 9's sixth iteration measured bit-identical code 2.2× apart between two sessions of one machine, invisibly to both columns, because what moved was the distribution's shape rather than its floor. Two rows are a trend; a measurement is two legs alternated inside one window (`M7`). |
| 2026-08-26 | Recorded row 12 in both tables, from the 2026-08-26 performance pass, and marked it as the first solver row taken on an optimised build — which makes it unreadable against every row above it and readable only against the pass's own start (`M7`, `M13`). The pass moved no step figure and was not meant to: it is a load-path pass, and [performance.md](performance.md) carries what it did move. |
| 2026-08-26 | The ladder's `build` column and the calibration row time the simulation's build alone. From `C26` both had the census generator's bolt search inside the clock — 1.0 s of a 1.1 s "build" at 32,800 blocks — so the calibration figure every cross-machine comparison divides by was mostly the generator. The key is unchanged because the column always meant the mod's build; the committed baseline's `build` rows predate `C26` and are the right scope. |
| 2026-08-26 | **Every figure on this page before this date was measured on an unoptimised build.** The test tree set no `<Optimize>`, so the Debug assemblies `dotnet run` produces told the JIT to compile without optimising. Measured on one commit in one window: a step is 3.4× dearer unoptimised, and the ratio is not uniform — the row fill is 5× and the cost of being measured 7.7× — so every *share* of a step quoted here was taken on an instrument that exaggerated stores through fields. The tree compiles optimised in every configuration now; the figures on this page are left as they were taken and the committed baseline is not re-recorded, because its keys have not changed (`M6`). Argued on [performance.md](performance.md#iteration-1--the-harness-measured-unoptimised-code). |
| 2026-08-24 | **Closed `A11`, and the suite corrected half of it within the hour.** `bench stagger` timed a fixed eight rounds at every fleet size, which is 32 grid-steps at four grids and 512 at sixty-four — two windows an order of magnitude apart, then divided by each other (`P6`). The window is 512 grid-steps at every rung now; the largest is unchanged, so no published figure moves. The *penalty* claim refuses a reading past a 30 % noise floor rather than failing, which is `M5` applied to a test. **The *lump* claim needed something else**: its failure mode is a steady cache bias against the larger working set, which a floor computed from repeat-to-repeat spread cannot see, and it failed at a 6 % floor an hour after the gate went in. It is held on the solver's work counters now — 38,633 element visits a grid-step at four grids and at sixty-four, identical on any machine — with the milliseconds printed beside it. |
| 2026-08-24 | **Priced the element-visit allowance, in its own unit and in kelvin, and both halves were new.** `bench allowance` sweeps grid size, world and allowance through the host's frame-paced entry point — the one path where the bound is in force — and reports what a grid keeps of real time beside what a frame costs. **Two findings.** No published *ns per element visit* figure in this repository is in the unit the budget counts in: the ladder and the telemetry dump both divide by `nodes + links` where the budget counts `links + 4 × nodes`, about 2× apart, so the allowance had never been convertible into milliseconds at all. And it binds in **air**, not vacuum — a hull keeps 100 % of real time to 32,000 blocks in vacuum, 16,000 in atmosphere and 9,000 in flight — where [backlog.md](backlog.md) `C27` had measured only the vacuum column. What the lost rate costs is measured up a ladder rather than read off `F23`'s one point, because the curve is convex: 1.19 K standing at a 5 % deficit and 36.98 K at 60 %, the 10 % row reproducing the degraded-input sweep exactly. |
| 2026-08-24 | **Re-ran the ladder and the environments at `C24`'s pair on the hull `C26` refreshed.** A hull carries about a tenth fewer links, because each census band now mounts the way the block it stands for does; the vacuum demand halved and the flight demand barely moved, which is the retune's shape — a convection-limited demand falls with the clock and a conduction-limited one rises with the pace. Cost per element visit is unmoved at 1.73–1.86 ns, which is what says the step got smaller rather than slower. Figures elsewhere on this page that are quoted from a particular run and not re-taken carry the date they were measured. |
| 2026-08-23 | **Asked `D14`'s question of a fleet instead of a grid.** Spreading a step costs 12.2 % on 242 grids of 1,004 nodes and nothing at all on four — the penalty is the fleet's working set rather than any grid's size, which is why the single-grid ladder never saw it. The lump staggering would put on a frame is 0.53 ms for such a grid and flat in fleet size, and a frame carries the same total work either way, so what staggering really gives up is the ability to split a grid too big for the budget. `bench stagger`, `StaggerTests`. |
| 2026-08-22 | Recorded row 11 in both tables and re-took the baseline on this machine. The step columns doubled because `Frequency` halved and the element allowance doubled with it; per simulated second is 8.948 ms before against 8.537 ms after, so the pass is cost-neutral and the 8,000-block ship went from 22 substeps granted to 44 with demand doubling to match. The machine is 26% slower than rows 0-8 in two runs twenty minutes apart, which is its state and not a loud sample, so only that one comparison is readable. Corrected two statements that the committed baseline is pinned at row 3; it is re-recorded whenever its keys change, which happened this pass. |
| 2026-08-22 | Dropped the profile rows from [The configurations](#the-configurations) and from the report, with the five settings profiles they measured. The section is the substep cap alone now, and the committed baseline was re-recorded because its keys changed (`M6`). **The re-recording is 23% slower than row 3** — calibration 109.4 ms against 86.2, noise 0.129 ms against 0.043 — which the row 11 re-take reproduced, so it is the machine and not one loud run. The file's *timings* are not comparable to the figures quoted on this page; its keys, which is all `BenchmarkBaselineTests` reads, are. |
| 2026-08-22 | Recorded row 10 of [the iteration log](#the-iteration-log), and marked its duration as not comparable rather than leaving a reader to compare it (`M7`). |
| 2026-08-22 | Absorbed `element-cost.md` as [What a substep costs](#what-a-substep-costs) — the measurement behind the step budget's weighting belongs beside the report that uses it — and `iterations.md` as [The iteration log](#the-iteration-log), which is this report's own trend over time. Converted the three optimisation findings to present tense, with the state they replaced kept in their before/after tables. |
| 2026-08-21 | Filed one overheat event per block per step rather than per substep, which had been multiplying every reported critical-block count by the grid's substep demand and OOM-killing a 4,000-tick driven run at 14 GB. Read a node's six face weights once in the row fill instead of twice (−12.4% on the fill in air). Let the environment pass write the watts row instead of clearing it first (+0.6% at half a million blocks — a bandwidth effect, not an instruction one). Gave every substep count the step length it was counted against. |
| 2026-08-20 | Re-recorded the baseline against the fixed-source row, and stopped the first substep filling rows nothing reads. Corrected what being measured was thought to cost. |
| 2026-08-19 | Measured what a substep spends per node, per link and per exposed face, which is what changed the step budget's unit from links to `links + 4 × nodes`. |
| 2026-08-18 | Opened the report: one command producing a table for a person and a CSV keyed on `section / case / metric` for a diff, with the case key as the contract and `PerformanceReportTests` asserting it. |
