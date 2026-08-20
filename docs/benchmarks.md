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
`MaxSubsteps` refuses at 16 on every hull this ladder builds, so *granted* is 16 everywhere and
says nothing, while *demanded* keeps moving. In flight the gap between them is wider still — see
[the environments section](#the-environments--and-why-the-old-numbers-were-the-cheap-case).

### The environments — and why the old numbers were the cheap case

Every shape in every world, because which is worst depends on which world:

| | step ms | substeps demanded |
| --- | ---: | ---: |
| ship, vacuum | 1.48 | 23.2 |
| ship, atmosphere | 1.35 | 31.0 |
| ship, flight at 300 m/s | 1.35 | **43.9** |
| cube, vacuum | 1.37 | 22.7 |
| cube, flight | 1.28 | 37.0 |
| truss, vacuum | 0.65 | **16.0** |
| truss, atmosphere | 0.69 | 33.5 |
| truss, flight | 0.72 | **50.9** |

**A truss is the cheapest hull in vacuum and the stiffest in flight** — 16 substeps to 51, a
threefold inversion. One link per node and nothing to convect with makes it trivial in space;
every node being exposed makes convection a per-node term on all of them once there is air.

Until this section existed, every benchmark in this repository ran a **ship in vacuum**, which is
the cheapest of those nine on the axis that matters. Worse, `AtmosphereFactor` is zero in vacuum,
so convection was switched off *by the world* rather than by its setting — the report dutifully
recorded convection and friction as costing nothing, having never run either. Both are now
measured: 1.30 ms and 0.82 ms isolated, which puts convection above solar.

Everything else in the report is now measured in flight, deliberately the worst of the nine.

> **Cost and accuracy have different worst cases.** `MaxSubsteps` grants 16 whatever the demand,
> so flight does not cost much more per step — it is *further from being resolved*. In vacuum the
> default profile is 23 demanded against 16 granted; in flight it is 44 against 16. The extra
> demand is paid in accuracy, through the overshoot clamps, not in milliseconds.

### The features, two ways

Every switch a world can turn off, measured twice:

* **marginal** — everything on, this one off. What removing it from a working configuration gives
  back.
* **isolated** — everything off, this one on. What the feature does on its own.

They differ whenever features interact, and the gap is usually the interesting part. From the
committed baseline, on a 32,800-block hull in flight where a whole step is 6.22 ms and a hull with
every feature off is 0.49 ms:

| feature | marginal | isolated |
| --- | ---: | ---: |
| conduction | **3.68 ms** | 3.63 ms |
| conduction clamp | **2.84 ms** | 0.00 ms |
| damage | 0.77 ms | 0.66 ms |
| convection | −0.56 ms | **1.30 ms** |
| radiation | 0.11 ms | 1.03 ms |
| friction | −0.19 ms | 0.82 ms |
| solar | 0.17 ms | 0.79 ms |
| environment clamp | 0.27 ms | 0.00 ms |
| waste heat | −0.22 ms | 0.22 ms |
| heat sources, coolant loops, room air, self shadow | within the noise | within the noise |

Three things fall out of that table that no single-number benchmark would have shown.

**`ClampConductionOvershoot` is the second most expensive thing in the simulation.** 2.84
milliseconds of a 6.22 ms step — and nothing at all in isolation, because it only acts
where there is conduction to clamp. It is a per-link branch and two extra reads inside the hottest
loop in the mod.

That is worth putting beside a result from [stiffness.md](stiffness.md): while
`MaxSubstepsPerBlock <= MaxSubsteps` **the clamps never engage**, because every step is short
enough for the grid it is integrating. A configuration that caps the demand is paying three and a
quarter milliseconds for a branch that can no longer fire.

**Solar's marginal cost is now a tenth of its isolated cost.** Everything about solar gain except
the fact of it is precomputed once per step, so removing it saves the one multiply-add it still
costs. The isolated figure — what solar does on a grid with nothing else running — is a
millisecond, and reading that column alone would predict a saving from disabling it that is not
there.

**Damage costs 0.77 ms marginal**, for a per-node comparison against a definition field on the
apply pass — a check that fired zero times across two field sessions.

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

| profile | demanded | granted | clamped |
| --- | ---: | ---: | --- |
| simulation | 18.5 | 19 | no |
| default | 36.9 | 16 | yes |
| responsive | 1,811 | 8 | yes |
| arcade | **80,319,608** | 1 | yes |
| minimal | **52,879,248** | 1 | yes |

`simulation` is the only profile that resolves what it is integrating. `arcade` and `simlite`
are eight orders of magnitude under-resolved in an atmosphere: every exposed block is being driven
to ambient by the overshoot clamp on every substep, and the clamp is the entire integrator rather
than a guard on it. That is bounded — `ClampEnvironmentOvershoot` is exactly what makes it
bounded — but it is not a simulation of anything, and it is a much larger number in flight than
the 1,761 the same profile shows in vacuum.

Worth deciding whether those profiles should scale `HeatTimeScale` down in an atmosphere, or
whether being pinned to ambient is the intended arcade behaviour.

### The scenarios

A plain hull in a plain world does not reach air in the compartments, plumbing on the ship, or
blocks past their rating. Each of those is a section of the solver that had never been in a
measurement, and each row carries **what the scenario actually built** beside what it cost — a
scenario that quietly builds nothing reports zero and is indistinguishable from a feature that is
free, which is precisely what happened to convection.

| scenario | step | what it built |
| --- | ---: | --- |
| plain | 1.33 ms | — |
| pressurised | **1.54 ms** | 13 rooms with air |
| plumbed | 1.38 ms | 8 coolant loops, 8 heat pumps |
| burning | 1.33 ms | **1.4 million overheat events** |

Room air costs about 15 % of a step for thirteen compartments, which is the first time it has been
measured at all. Plumbing costs 3 %.

**The burning row is the finding.** Thirty steps raised 1,444,321 overheat events — about 15,500
per step on a hull with 970 heat producers. That is one event *per producer per substep*, where one
per producer per step would do: the damage check lives on the apply pass, which runs sixteen times
a step. The total damage is right, because `DamageIsPerSecond` divides by the substep length — but
the event count is sixteen times what it needs to be, and in game every one of those events becomes
a `DoDamage` call on the host. A burning ship is making sixteen times the damage API calls it
should.

### The fleets

A world is many grids and every benchmark here has been one. Each grid pays its own fixed per-step
cost, so a fleet of small grids is not obviously the same price as one large one:

| | step, whole fleet | blocks | per thousand blocks |
| --- | ---: | ---: | ---: |
| 1 grid | 1.36 ms | 8,904 | 0.153 ms |
| 10 grids | 1.48 ms | 11,240 | 0.131 ms |
| 100 grids | 12.05 ms | 83,500 | 0.144 ms |

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
