# The performance report

One command that measures what the simulation costs — by size, by feature, and by configuration —
and knows how to compare itself against an earlier run.

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
measures the operating system.

### The ladder

Blocks, links, build time, step time, substeps granted, substeps demanded, cost per simulated
second and cost per element visit, at each rung. The two substep columns matter separately:
*granted* is what `MaxSubsteps` allowed, *demanded* is what the grid asked for, and the gap between
them is accuracy given up. In flight the gap is widest — see
[the environments section](#the-environments--and-why-the-old-numbers-were-the-cheap-case).

| rung | blocks | links | step | demanded | per element visit |
| --- | ---: | ---: | ---: | ---: | ---: |
| 8,000 | 8,904 | 20,779 | 1.12 ms | 22.0 | 1.71 ns |
| 32,000 | 32,800 | 73,787 | 3.54 ms | 18.5 | 1.75 ns |
| 125,000 | 126,731 | 277,967 | 16.4 ms | 21.7 | 1.85 ns |

**Cost per element visit is now flat across the ladder** — 1.82, 1.87, 1.91 ns — where it used to
climb from 2.8 to 6.1. The climb was the two passes that reached through the node objects to the
heap on every visit; both now read flat arrays, and what is left scales with the elements rather
than with how far apart they landed in memory.

### The environments — and why the old numbers were the cheap case

Every shape in every world, because which is worst depends on which world:

| | step ms | substeps demanded |
| --- | ---: | ---: |
| ship, vacuum | 0.62 | 11.6 |
| ship, atmosphere | 0.83 | 15.5 |
| ship, flight at 300 m/s | 1.11 | **22.0** |
| cube, vacuum | 0.58 | 11.4 |
| cube, flight | 0.90 | 18.5 |
| truss, vacuum | 0.26 | **7.9** |
| truss, atmosphere | 0.53 | 16.8 |
| truss, flight | 0.78 | **25.5** |

**A truss is the cheapest hull in vacuum and the stiffest in flight** — 7.9 substeps to 25.5, a
threefold inversion. One link per node and nothing to convect with makes it trivial in space;
every node being exposed makes convection a per-node term on all of them once there is air.

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

Each named profile, and the substep cap across its useful range, reported as cost per simulated
second — the unit that does not move when the step length does.

The profile rows say something the profile table in [configuration.md](configuration.md) does not.
Measured in flight, on a hull with air moving over it:

| profile | demanded | granted | clamped | per simulated second |
| --- | ---: | ---: | --- | ---: |
| simulation | 0.08 | 1 | no | 3.92 ms |
| optimized | 0.17 | 1 | no | 1.87 ms |
| simlite | 0.17 | 1 | no | 1.87 ms |
| responsive | 18.5 | 19 | no | 28.5 ms |
| arcade | **36.9** | 6 | **yes** | 10.4 ms |

`arcade` is the only profile in this measurement that does not resolve what it is integrating: it
asks for 37 substeps, is granted 6, and the overshoot clamps carry the difference. That is bounded —
the clamps are exactly what makes it bounded — but the blocks past their limit are being driven
towards ambient rather than integrated towards it.

`responsive`, the shipped default, is resolved and costs 30.1 ms per simulated second on a
32,800-block hull in flight. `simulation` and the two cut-down profiles run real time, which needs
one substep and costs proportionately little.

> These figures moved by a factor of two against every earlier version of this document, in both
> directions and for two unrelated reasons: `substeps demanded` was rescaled when the element-visit
> budget was corrected, and `per simulated second` halved when the overshoot clamp stopped running
> on steps where it cannot bind. Figures quoted here from before either change are not comparable
> with these. `BenchmarkBaselineTests` now fails when the committed baseline's keys drift from the
> report's, which is the class of rot that made the older numbers hard to place.

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

**At one substep the fixed part is three quarters of a step**, and one substep is what `simulation`,
`optimized` and `simlite` all run. A change that halves the per-substep cost does nothing for those
three, which is worth knowing before optimising for them.

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
| **row fill** | **0.22** | **the first substep's share, and none of any later one's** |
| later substep | 0.17 | one substep once the rows are filled |

The three add to 0.45 against a fitted intercept of 0.49, which is the model reconstructing the fit
to within the noise floor. **Half of what the fit calls fixed is the row fill**, and it is neither
irreducible nor where anyone was looking.

Two things came out of measuring it. The relaxation row was filled on every step and read only by
the clamped conduction loop, which on a grid granted its substeps never runs — a store per node per
step that nothing looked at. And the wind weighting, a six-face sum, was computed twice per node in
the fill: once for the convection factor and once for the friction row, both of them live in air at
speed. Removing the two took the one-substep profiles down about a tenth: `optimized` 2.10 ms to
1.87 ms a simulated second, `simlite` 2.08 to 1.87, `simulation` 4.20 to 3.92.

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

Worth deciding whether `arcade` should scale `HeatTimeScale` down in an atmosphere, or whether
being pinned to ambient is the intended arcade behaviour.

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
changes has been a drift. [iterations.md](iterations.md) keeps one row per pass over this
repository — the suite's size and duration, and the headline step figures — so the trend is
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
[load-and-hitching.md](load-and-hitching.md#the-ladder-was-measured-on-the-wrong-ship). Everything
is now built from [`Census`](../tests/Thermodynamics.Harness/Census.cs), the block population of a
real ship read out of a telemetry dump, and `CensusFidelityTests` fails if it drifts away from the
field observations recorded beside it.

**Refresh both as dumps arrive.** The census tiers describe what a ship is made of; `Census.Field`
records what those ships were observed to do. When a new report lands, update the tiers to the new
population and the field constants to the new observations, and the fidelity tests will say whether
the synthetic ship still resembles the real one.
