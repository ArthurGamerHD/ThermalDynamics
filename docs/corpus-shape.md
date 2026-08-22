# The shape of the corpus data

What the 2026-08-21 datasets say, read for one purpose: **deciding balance**. Every number here is
measured, and the measurement is named. Where the data cannot answer a question this says so rather
than estimating.

[balance-lab.md](balance-lab.md) is the design of the lab and the six criteria. This is the reading
of what came out of it.

## What was collected

Four datasets, none of which covers the others.

| Dataset | Covers | Does not cover |
| --- | --- | --- |
| `out/corpus-2026-08-21/` | 8,132 ships x 5 scenarios = 40,660 outcomes | any atmosphere, any motion, any cooling fitted |
| `out/census-2026-08-21/` | 8,141 ships screened, 109,312 composition rows | anything the solver does |
| `out/knobs-2026-08-21/` | 49-ship panel x 20 dials x 4-6 levels = 12,700 rows | any two dials at once |
| `out/blockheat/blockheat.csv` | 323 block types from the definitions alone | any hull, any arrangement |

The corpus survey ran five scenarios: `idle`, `vacuum-sunlit`, `full-electrical`, `burn-forward`,
`recovery`. **All five are in vacuum.** The knob sweep is the only dataset with air, wind or motion
in it, and it runs on 49 ships rather than 8,132.

**The corpus dataset carries 50 duplicate rows.** A resumed run re-emits the batch it was
interrupted in — `THERMAL_CORPUS_SKIP` counts in files while the writing is per ship — so ten ships
are written twice. `tools/corpus/verdict.py` drops them and prints the count. It is a tenth of a per
cent and changes no finding, but a population statistic that silently double-weights part of its
population is the failure this lab exists to prevent.

**Peaks past critical are not physics.** The harness never destroys an overheating block, so a ship
past critical keeps generating for the rest of the clock. The 541,648 K reading is 1,800 s of an
undamped source. Crossing times and shares are unaffected; peak temperatures above about 1,500 K
should be read as *"ran away"* and nothing finer. Recorded in [known-issues.md](known-issues.md).

## The shape, in one table

Deduplicated, shipped profile, `HeatTimeScale` 225.

| Scenario | peak p50 | critical | s to critical p50 | settle p50 | substeps p50/p95/p99 |
| --- | ---: | ---: | ---: | ---: | --- |
| `idle` | 178 K | 0.22 % | 104.5 | 1,560 | 2.31 / 3.25 / 3.58 |
| `vacuum-sunlit` | 247 K | 0.22 % | 102.7 | 600 | 2.34 / 3.25 / 3.62 |
| `full-electrical` | 632 K | 43.2 % | 8.9 | 300 | 2.36 / 4.30 / 6.02 |
| `burn-forward` | 1,087 K | 67.4 % | 5.6 | 120 | 2.38 / 3.80 / 4.89 |
| `recovery` | 207 K | 3.0 % | 4.0 | 1,320 | 2.31 / 3.25 / 3.63 |

Seconds are simulated seconds. At `SimulationSpeed` 1 they are the seconds a player waits.

**The distribution is bimodal, not spread.** A ship either sits near ambient indefinitely or runs
away in under ten seconds. There is very little population between those two states, and the
2–5 minute window the balance target asks for lands in the gap.

## The five balance goals, against the data

### Vanilla grids challenging but not impossible

Currently the population fails this in both directions at once, and which direction depends entirely
on **one block type**.

| Population | share | full-load waste p50 | critical under full load |
| --- | ---: | ---: | ---: |
| no jump drive, no hydrogen engine | 61.7 % | 76 kW | 12.8 % |
| hydrogen engine only | 10.6 % | 717 kW | 72.0 % |
| jump drive only | 21.6 % | 11.7 MW | **100.0 %** |
| both | 6.1 % | 18.9 MW | **100.0 %** |

`JumpDrive` is **71 % of all waste heat in the population**, carried by 27.7 % of ships. Not one of
the 2,249 ships carrying one survives full electrical load. That is not a balance point that needs
adjusting; it is a block whose definition makes it unsurvivable, and
[`BlockHeatIndex`](../tests/Thermodynamics.Harness/BlockHeatIndex.cs) already prices it at a self
index of 7.4 without needing a corpus to say so.

Meanwhile 53.5 % of ships carrying *neither* offender still go critical on `burn-forward`, from
thrusters alone.

**So the lever is not a global scale.** Cutting every waste fraction by half moves the median hull's
peak from 1,382 K to 1,177 K on the panel — a 15 % change that leaves both populations exactly where
they were. The measured per-type dials say the same:

| dial | effect on `burn-forward` peak p50 across its whole sweep |
| --- | --- |
| `thruster-waste` 0.10 → 2.00 | 652 K → 1,448 K — **the strongest dial in the set** |
| `consumer-waste` 0.25 → 4.00 | 776 K → 1,736 K |
| `producer-waste` 0.25 → 4.00 | 1,177 K → 1,240 K |
| `reactor-waste` 0.50 → 8.00 | 1,205 K → 1,242 K — **very nearly inert** |
| `engine-conductivity` 1 → 8 | 1,205 K → 1,204 K — **inert** |

`reactor-waste` moving 16-fold for a 3 % change in outcome is worth noting on its own: the reactor
retune argued in [balance.md](balance.md#reactor-waste-heat) is not a population-level lever.

### The cooling block desirable but not trivializing

**This is the one goal the data cannot answer directly, and the reason is structural.** No corpus
ship carries a radiator — the filter rejects non-vanilla blocks — so G3 has never been measured. The
retrofit pass does not exist yet.

What *can* be computed is the physics the retrofit would obey, and it is unpromising for a
surface-area answer:

* `exposed-surface` x2 moves the panel's `burn-forward` peak 1,205 K → 1,019 K, a **15.4 %** drop.
  The ideal radiative prediction is 2^-0.25 = 15.9 %. The model is behaving as clean radiative
  equilibrium, so **T scales as the fourth root of area**. Halving a ship's temperature needs
  sixteen times the radiating surface.
* `emissivity` x2 gives 1,020 K — the same curve. The two dials are the same dial.

A fourth-root law cannot produce a moderate retrofit: the first radiator does almost nothing and the
hundredth does less. **A cooling block that matters has to remove watts linearly** — a loop that
carries heat somewhere, or a pump with a rating — not add area.

Priced against the population, one `Gauge_LG_Radiator` (1x5x2 cells, 265.6 m² effective, emissivity
0.35) is worth:

| panel temperature | 400 K | 500 K | 600 K | 700 K | 800 K |
| --- | ---: | ---: | ---: | ---: | ---: |
| watts shed | 96 kW | 291 kW | 644 kW | 1.23 MW | 2.12 MW |

and the population needs, at a 600 K panel:

| ship | full-load waste | radiators to break even |
| --- | ---: | ---: |
| p50 | 337 kW | **0.5** |
| p75 | 5.94 MW | 9.2 |
| p90 | 20.5 MW | 31.8 |
| p99 | 125 MW | 194.6 |

57.7 % of ships need one or fewer. 19.2 % need more than sixteen. **The retrofit is either
unnecessary or impossible, with almost nothing in between**, and the gap between p50 and p75 is a
factor of 17.6 — which is the jump drive again.

### A moderate retrofit encouraged

Follows from the two above and is currently unreachable. A retrofit is moderate when the ship needs
a handful of blocks; the population's requirement is 0.5 blocks at the median and 195 at p99. Fixing
the jump drive is what creates the middle of that distribution — nothing else in the census moves it.

### The most significant point in a 2–5 minute window

**Measured: no block in the game lands in that window, and none can be made to at
`HeatTimeScale` 225.**

72 of 323 block types generate more heat than their own skin can shed. Their time from 293 K to
their own critical temperature, alone, is now a column in `blockheat.csv`:

| band | blocks | share |
| --- | ---: | ---: |
| under 10 s | 15 | 20.8 % |
| 10–60 s | 54 | 75.0 % |
| 60–120 s | 3 | 4.2 % |
| **120–300 s (the target)** | **0** | **0 %** |
| over 300 s | 0 | 0 % |

Median 16.9 s, worst 0.5 s, best 87.1 s.

**Waste fractions cannot fix this.** Solving for the waste heat that would produce a 180 s crossing,
per block, the required self index is **1.000 to three decimals for 90 % of them** — the band between
"never overheats" and "overheats in under a minute" is about a tenth of a per cent wide. That is a
direct consequence of T⁴: radiated power rises so steeply that a block is either comfortably under
its limit or running away, with no wide region where it approaches slowly.

The dial that does reach the window is the one that sets capacity:

| `HeatTimeScale` | corpus `full-electrical` s-to-critical p50 | vacuum substeps p50 |
| ---: | ---: | ---: |
| 225 (shipped) | 8.9 | 2.41 |
| 112 | ~17.8 | 1.21 |
| 56 | ~35.8 | 0.60 |
| 25 | ~80 | 0.27 |
| **~11** | **~180** | **~0.12** |

The scaling is exactly linear — the sweep confirms it at every level, and `specific-heat` produces
the identical curve mirrored, because they are the same quantity.

**But one clock governs two time constants that are ~100x apart.** A hot block's rise is set by its
own small mass; a hull's response to sun or air is set by its whole mass. At 225 the block crosses in
17 s while the hull takes 1,560 s to settle at idle. Dividing the clock by 20 puts the block in the
window and pushes the hull to eight hours.

Closing that gap needs conduction, which couples the block to the hull's mass:

| `conductivity` | `burn-forward` s-to-critical p50 | peak p50 | substeps p50 |
| ---: | ---: | ---: | ---: |
| 0.25 | 4.2 | 1,296 K | 0.77 |
| 1.00 (shipped) | 4.8 | 1,205 K | 2.75 |
| 2.00 | 9.2 | 1,107 K | 5.23 |
| 4.00 | 13.5 | 1,004 K | 9.93 |

Substep demand is conductance over capacity, so it is proportional to `conductivity` x
`HeatTimeScale`. **Raising conduction and lowering the clock together moves the crossing into the
window at constant or lower cost.** Conductivity x4 with `HeatTimeScale` ~15 projects to a ~200 s
crossing at ~0.7 substeps in vacuum, against 4.8 s at 2.75 today.

**That projection is an extrapolation across an interaction the sweep never measured.** Every
configuration in `knobs.csv` moves one dial. The pair has to be run before it is trusted.

### Minimize substeps while keeping responsiveness

In vacuum this is already cheap: corpus p99 is 6.02 against 64 granted. In air it is not, and the
corpus never measured air.

On the 49-ship panel, shipped settings:

| environment | substeps p50 | p95 |
| --- | ---: | ---: |
| `vacuum-shadow` | 2.41 | 3.56 |
| `surface-cold-night` | 12.59 | 14.78 |
| `surface-hot-noon` (still air) | 14.15 | 16.66 |
| `surface-windy` (60 m/s) | 23.28 | 27.61 |
| `storm-parked` (100 m/s) | 25.94 | 30.81 |
| `reentry` (200 m/s) | 30.87 | 36.71 |

**Air costs about six times what vacuum costs, before anything moves.** That is the responsiveness
budget, and it is spent on convection rather than on heat.

## The 300 m/s constraint

Vanilla caps a grid at 100 m/s; the servers this mod is played on commonly run 300. **Nothing in the
battery measured above 200 m/s until now** — `flight-300` and `storm-300` are added in
[`Battery.All()`](../tests/Thermodynamics.Harness/Battery.cs), and
[`FrictionIsolationTests`](../tests/Thermodynamics.Tests/FrictionIsolationTests.cs) pins the two laws
that decide what happens up there.

The two terms scale in opposite directions, and this is the whole finding:

* **Friction goes as the cube of airspeed.** 100 → 300 m/s is **27x** the friction heat.
* **Forced convection saturates.** The coefficient is `h = h0 * (1 + 0.1 * sqrt(v))`, so the same
  change moves cooling from 2.00x still air to 2.732x — **1.37x**.

So tripling the speed limit multiplies the heating term by 27 and the cooling term by 1.37. Whether
that destroys atmospheric flight is set by `FrictionScale`, and the panel says friction is not
currently the binding term at 100 m/s: sweeping `FrictionScale` 0 → 4 moves the `flight-100` peak
only 856 K → 878 K, because convection dominates. At `reentry` it does bite: 300 K → 521 K over the
same sweep.

Substep demand is linear in the convection coefficient, and the fit is exact enough to project on.
Fitting the two measured points at h=1 (`surface-hot-noon`) and h=2 (`storm-parked`) and testing
against the held-out 200 m/s `reentry` measurement:

| | fit | predicted at 200 m/s | measured at 200 m/s | error | **projected at 300 m/s** |
| --- | --- | ---: | ---: | ---: | ---: |
| p50 | 2.35 + 11.79h | 30.83 | 30.87 | −0.13 % | **34.58** |
| p95 | 2.50 + 14.15h | 36.67 | 36.71 | −0.11 % | **41.17** |

**300 m/s demands about 35 substeps at the median and 41 at p95, against `MaxSubsteps` 64.** It fits,
with roughly a third of the budget in reserve, and it costs about 15x what the same ship costs in
vacuum. This is a projection from a validated fit, not a measurement; the scenarios now exist to
measure it.

## What the data settles, and what it does not

**Settled:**

* Design decides more than size. Spearman rank correlation of peak against `w_per_m2` is +0.56 at
  idle and `local_w_per_m2_max` +0.89 under full load, against +0.26 and +0.49 for block count. G4
  holds.
* The failure is per-block, not per-hull. The hot spot is a median of 3–4 blocks on a median
  1,112-block ship. 82 % of ships that go critical in under 5 s under full electrical load do so on
  a `LargeHydrogenEngine`; on `burn-forward` it is 31 % `SmallBlockSmallAtmosphericThrust`.
* Timing is linear in `HeatTimeScale` and unaffected by anything that changes equilibrium.
  Equilibrium is unaffected by `HeatTimeScale`. The two axes are cleanly separable.
* G1, G2, G5 and G6 all hold on shipped defaults.

**Not settled:**

* **G3 has never been measured.** No corpus ship carries a cooling block. The retrofit pass is the
  single highest-value missing run, and two of the five balance goals depend entirely on it.
* **No dial has been measured against another.** Every knob row moves one thing. The conduction/clock
  pair the timing goal depends on is unmeasured.
* **The corpus has never seen air.** Five vacuum scenarios on 8,132 ships; air only on the 49-ship
  panel. Every atmospheric statement here rests on 49 hulls.
* **`full-electrical` charges every jump drive continuously.** A jump drive charges and then stops,
  so the scenario is a bound rather than a steady state — and since it is 71 % of population waste,
  that choice sets most of the shape of the load results.

## Reproducing this

```bash
python3 tools/corpus/verdict.py out/corpus-2026-08-21     # criteria and distributions
tools/corpus/build-report.sh                              # the browsable page
THERMAL_CORPUS_DATA=$PWD/out/blockheat dotnet test \
  tests/Thermodynamics.Tests/Thermodynamics.Tests.csproj \
  --filter BlockHeatIndexTests                            # block index, with pace
```

See [corpus-run-operations](backlog.md) for the five ways a full sweep dies.
