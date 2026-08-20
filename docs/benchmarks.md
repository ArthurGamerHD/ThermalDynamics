# The performance report

One command that measures what the simulation costs — by size, by feature, and by configuration —
and knows how to compare itself against an earlier run.

```bash
cd sim
dotnet run --project Thermodynamics.Sim -- bench report --size 32000 --max 125000 --csv benchmarks
dotnet run --project Thermodynamics.Sim -- bench report --baseline benchmarks/performance.csv
```

The first writes `benchmarks/performance.csv`. The second measures again and prints what moved.
[`benchmarks/performance.csv`](../sim/benchmarks/performance.csv) is committed, so a change can be
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
| 8,000 | 8,904 | 20,779 | 1.19 ms | 22.0 | 1.82 ns |
| 32,000 | 32,800 | 73,787 | 3.79 ms | 18.5 | 1.87 ns |
| 125,000 | 126,731 | 277,967 | 17.0 ms | 21.7 | 1.91 ns |

**Cost per element visit is now flat across the ladder** — 1.82, 1.87, 1.91 ns — where it used to
climb from 2.8 to 6.1. The climb was the two passes that reached through the node objects to the
heap on every visit; both now read flat arrays, and what is left scales with the elements rather
than with how far apart they landed in memory.

### The environments — and why the old numbers were the cheap case

Every shape in every world, because which is worst depends on which world:

| | step ms | substeps demanded |
| --- | ---: | ---: |
| ship, vacuum | 0.75 | 11.6 |
| ship, atmosphere | 1.01 | 15.5 |
| ship, flight at 300 m/s | 1.43 | **22.0** |
| cube, vacuum | 0.69 | 11.4 |
| cube, flight | 1.07 | 18.5 |
| truss, vacuum | 0.33 | **7.9** |
| truss, atmosphere | 0.66 | 16.8 |
| truss, flight | 1.03 | **25.5** |

**A truss is the cheapest hull in vacuum and the stiffest in flight** — 7.9 substeps to 25.5, a
threefold inversion. One link per node and nothing to convect with makes it trivial in space;
every node being exposed makes convection a per-node term on all of them once there is air.

Until this section existed, every benchmark in this repository ran a **ship in vacuum**, which is
the cheapest of those nine on the axis that matters. Worse, `AtmosphereFactor` is zero in vacuum,
so convection was switched off *by the world* rather than by its setting — the report dutifully
recorded convection and friction as costing nothing, having never run either. Both are now
measured: 1.90 ms and 0.88 ms isolated, which puts convection above solar.

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
committed baseline, on a 32,800-block hull in flight where a whole step is 3.78 ms and a hull with
every feature off is 0.37 ms:

| feature | marginal | isolated |
| --- | ---: | ---: |
| convection | **1.37 ms** | **1.90 ms** |
| conduction | 1.22 ms | 0.81 ms |
| radiation | 0.18 ms | 0.90 ms |
| damage | 0.19 ms | 0.13 ms |
| environment clamp | 0.25 ms | 0.00 ms |
| solar | −0.10 ms | 0.92 ms |
| friction | −0.16 ms | 0.88 ms |
| waste heat | −0.14 ms | 0.15 ms |
| conduction clamp | 0.02 ms | 0.00 ms |
| heat sources, coolant loops, room air, heat pumps, self shadow | within the noise | within the noise |

Three things fall out of that table that no single-number benchmark would have shown.

**`ClampConductionOvershoot` used to be the second most expensive thing in the simulation**, at
2.84 ms of a 6.22 ms step, and later 4.17 ms of an 8.52 ms one. It is now 0.06 ms. Both halves of
the clamp are the same stability test — `h * G > C` for a node, `h * conductance >` the reduced
mass for a link — and neither reads a temperature, so both can be settled for the whole grid once
per step. On a grid granted the substeps it demands, which is what the substep count is chosen to
guarantee, neither holds anywhere and every clamped branch was computing a value it then discarded.
See [the clamp A/B](#the-overshoot-clamp-ab) for what that is worth in each regime.

**Solar's marginal cost is now a tenth of its isolated cost.** Everything about solar gain except
the fact of it is precomputed once per step, so removing it saves the one multiply-add it still
costs. The isolated figure — what solar does on a grid with nothing else running — is a
millisecond, and reading that column alone would predict a saving from disabling it that is not
there.

**Damage used to cost 0.83 ms marginal** for a check that fired zero times across two field
sessions. It is now 0.19 ms. The check reads a critical temperature once per node per substep, and
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
| simulation | 0.08 | 1 | no | 4.18 ms |
| optimized | 0.17 | 1 | no | 2.11 ms |
| simlite | 0.17 | 1 | no | 2.07 ms |
| responsive | 18.5 | 19 | no | 30.1 ms |
| arcade | **36.9** | 6 | **yes** | 10.7 ms |

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

### The overshoot clamp A/B

The clamp is skipped on any step where it cannot bind. That is a saving in one regime and a cost in
the other, so the report measures both, each twice — with the test and without it:

| regime | always clamped | gated | change | clamp live |
| --- | ---: | ---: | ---: | --- |
| resolved — granted the substeps it demands | 8.13 ms | **3.77 ms** | **−54 %** | no |
| refused — 4 substeps against a demand of 20 | 2.01 ms | 2.01 ms | −0.2 % | yes |

The `clamp live` column is what says the two rows are in different regimes rather than being the
same measurement printed twice; `PerformanceReportTests` asserts it reads 0 and 1 respectively.

**The worst case sits inside the noise floor.** Across repeated runs the `refused` row moved
between −0.3 % and +7.4 % while the noise row moved between 0.08 ms and 0.39 ms on a 2.2 ms step;
the two track each other, and the low-noise runs read as zero. The test returns on the first
element that can bind, so a grid where the answer is yes stops almost immediately — it is one
comparison, not a pass.

Worth deciding whether `arcade` should scale `HeatTimeScale` down in an atmosphere, or whether
being pinned to ambient is the intended arcade behaviour.

### The scenarios

A plain hull in a plain world does not reach air in the compartments, plumbing on the ship, or
blocks past their rating. Each of those is a section of the solver that had never been in a
measurement, and each row carries **what the scenario actually built** beside what it cost — a
scenario that quietly builds nothing reports zero and is indistinguishable from a feature that is
free, which is precisely what happened to convection.

| scenario | step | what it built |
| --- | ---: | --- |
| plain | 1.25 ms | — |
| pressurised | **1.65 ms** | 13 rooms with air |
| plumbed | 1.28 ms | 8 coolant loops, 8 heat pumps |
| burning | 1.22 ms | **411,651 overheat events** |
| scorched | 0.60 ms | **2,136,960 overheat events** — one per node per substep |

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
| 1 grid | 0.97 ms | 8,904 | 0.109 ms |
| 10 grids | 1.35 ms | 11,240 | 0.120 ms |
| 100 grids | 10.39 ms | 83,500 | 0.124 ms |

**Flat, and that is the answer.** Splitting the same work across a hundred grids costs what one
grid costs per block: the solver's per-grid fixed work — state sync, the stability estimate, the
mass floor — does not dominate even at a hundred to one.

That is a narrower result than it looks. The harness has no adapter: no environment sample, no
entity callbacks, no telemetry, no scheduler. The per-grid cost that a real world pays lives almost
entirely in those, which is why the largest finding of this project — every grid in a world
stepping on the same frame — came from a telemetry dump and could not have come from here. What
this section rules out is the *solver* scaling badly with grid count. It says nothing about the
adapter.

---

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
cd sim
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
is now built from [`Census`](../sim/Thermodynamics.Harness/Census.cs), the block population of a
real ship read out of a telemetry dump, and `CensusFidelityTests` fails if it drifts away from the
field observations recorded beside it.

**Refresh both as dumps arrive.** The census tiers describe what a ship is made of; `Census.Field`
records what those ships were observed to do. When a new report lands, update the tiers to the new
population and the field constants to the new observations, and the fidelity tests will say whether
the synthetic ship still resembles the real one.
