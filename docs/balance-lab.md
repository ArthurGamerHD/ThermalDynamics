# The balance lab

A rig for deciding what good balance *is*, from a population of real ships rather than from a
hull this repository built for itself.

## Why

Every number this mod ships was chosen against a synthetic rig. [`Census`](../tests/Thermodynamics.Harness/Census.cs)
is honest about the cost of that in its own summary — its tiers come from the telemetry dump of one
1,381-block ship, and *"one ship is one ship"*. The reactor retune in
[balance.md](balance.md#reactor-waste-heat) is the same shape of argument one level up: a fraction
measured against two rigs, both of which this repository invented.

That is enough to say *a ship like this overheats*. It is not enough to say *eleven per cent of the
ships people actually build overheat*, and only the second kind of statement can decide a default.

The lab exists to make the second kind of statement.

## The process

The original sketch was: pull ten thousand grids, run them through scenarios, derive guidelines
from the matrix. That is right in outline and has a gap in the middle — step three has no verdict
function. A matrix of temperatures does not say which of them are *good*, and criteria invented
after seeing the data are criteria fitted to the data.

So the refined process puts the criteria first, and stages the runs so the expensive ones are only
paid where they buy something.

### 0. Define good balance, before collecting anything

Six criteria, each a number the matrix can produce and each able to fail. These are proposals to
argue with — the point is that they are written down before the data is, so a run that fails them
is a finding rather than an excuse to move a threshold.

| # | Criterion | Fails when | Why it matters |
| --- | --- | --- | --- |
| **G1** | **Idle is safe.** A ship at rest in the environment it was built for does not overheat. | more than ~1 % of the corpus goes critical at idle | A mod that kills parked ships is uninstalled, whatever else it does right. |
| **G2** | **Load bites.** Under sustained full power, a meaningful share of uncooled ships reach a warning state. | fewer than ~20 % ever get warm | Heat that never arrives is scenery. This is the criterion the current build most likely fails. |
| **G3** | **Cooling works.** Fitting radiators or a loop moves the outcome. | median Δpeak from a standard cooling fit is small | If the answer does not respond to the one lever the player has, the mechanic is decoration. |
| **G4** | **Design decides, not size.** Outcome correlates with things a builder controls — exposed area per watt, radiator count, how buried the reactor is — more than with block count or grid size. | rank correlation with block count exceeds that with exposure | Otherwise the mod taxes big ships rather than rewarding good ones. |
| **G5** | **No death spiral.** A ship past critical that throttles to idle returns below critical in bounded time. | recovery time unbounded, or damage continues after the load stops | A player must be able to react to a warning. |
| **G6** | **Affordable across the population.** Substep demand and step cost at p95/p99 of the corpus, not at the mean. | p99 substep demand exceeds what the shipped caps grant | The census hull is one point; the tail is what stutters. |

**G6 is not a balance criterion and is here on purpose.** The corpus answers the open questions in
[stiffness.md](stiffness.md) and the D-series of the [backlog](backlog.md) — lumping, multirate,
the substep cap — better than any synthetic ladder can, because those decisions turn entirely on
the *shape of the tail* of the stiffness distribution, and nobody has ever seen it.

### 1. Acquire a corpus

The target is ten thousand popular unmodded designs. See [Acquisition](#acquisition) below for what
that actually costs — it is the one step with constraints outside this repository.

The corpus is a directory of `bp.sbc` files. Nothing downstream cares how they got there, so the
acquisition route can change without touching the lab.

### 2. Screen everything (cheap)

Every ship in the corpus, no solver:

* block census by type, mass and derived thermal properties — this is what replaces `Census`;
* installed power, thrust and their ratio to hull mass;
* exposed area, buried fraction, room count;
* cooling fitted: radiators, loops, heat pumps, vents;
* **stiffness demand** — conductance over capacity per block, and the distribution's tail.

This pass alone answers G6 and re-founds `Census` on a population. It also produces the strata for
what follows.

### 3. Run the scenario battery

The battery is built along **four independent axes**, and a scenario is a point on all four at
once. Nothing is included because it seems interesting: a scenario earns its place by answering a
question no other scenario answers, and where two would answer the same one the cheaper is kept.

| Axis | What varies | Why it is its own axis |
| --- | --- | --- |
| **Environment** | vacuum, sun, air, weather, ground | External heating and external cooling are the *same* axis — a planet's surface does both depending on the hour |
| **Motion** | speed, and direction of travel | Friction heats the leading face and airflow cools every face; which wins is a question of speed, and *where* it lands is a question of heading |
| **Load** | idle, full electrical, thrust per direction, weapons, everything | Where heat is made decides where it concentrates |
| **Configuration** | as built, against the same ship with cooling fitted | The only axis the player controls directly, and the one G3 is about |

**Directional cases are enumerated, not sampled.** A ship is not symmetric: most designs have their
thrusters and their thin armour on different faces, so running only the forward case would miss the
thing the case exists to find. Both thrust and travel expand to six directions.

#### The scenarios

*Environment, with the ship idle* — anything reached here came from outside it, which is what makes
these the controls for everything below.

| Scenario | The question |
| --- | --- |
| `vacuum-shadow` | The cold reference: nothing in, nothing out but radiation |
| `vacuum-sunlit` | External heating at its worst — one face held to the sun |
| `orbit-cycling` | Thermal inertia: does a hull average the day or chase it |
| `surface-hot-noon` | External heating in air: hot ground, high sun, still |
| `surface-cold-night` | External cooling: does anything freeze below a usable floor |
| `surface-windy` | Forced convection at rest — what wind alone is worth |
| `underground` | Buried: no sun, no wind, rock ambient |

*Load, run in shadow on purpose* — with no sun or air to argue about, anything reached here is the
ship heating itself, which is the only way to attribute it.

| Scenario | The question |
| --- | --- |
| `idle` | **G1**: does a parked ship survive doing nothing |
| `full-electrical` | **G2**: every consumer at rating, reactors supplying what they ask |
| `all-peak` | Where heat concentrates when everything runs at once |
| `burn-{forward,backward,left,right,up,down}` | **Hot spots.** A ship burning forward heats the thrusters at its stern and nothing at its bow, and which blocks those are is a property of the design |

*Motion* — friction against airflow.

| Scenario | The question |
| --- | --- |
| `flight-50`, `flight-100` | Friction against airflow: which wins, at which speed |
| `reentry` | The leading face at terminal speed in thick air |

*Transient*

| Scenario | The question |
| --- | --- |
| `recovery` | **G5**: from a full burn, throttled to idle, does it come back |

#### What every run records

A single final temperature cannot judge balance, and the reason is spatial. Two ships settling at
the same peak are different ships if one is uniformly warm and the other is cold everywhere except
a 900 K knot around its thrusters — the first has a cooling problem, the second a **layout**
problem, and only one of them is fixed by adding radiators.

So [`ScenarioOutcome`](../tests/Thermodynamics.Harness/ScenarioOutcome.cs) records the distribution
and where its top end is:

| Group | Fields |
| --- | --- |
| Where it ended up | peak, mean, median, p95, min kelvin |
| How unevenly | gradient (peak − min), **hot spot** (peak − mean) |
| Where the hot spot is | hottest block subtype and cell, and how many blocks are within 50 K of it — one block is a definition problem, fifty is a layout problem |
| Whether it survived | blocks over critical, share, margin at the peak block, seconds to first critical |
| How it got there | seconds to settle within 5 K of final, fastest rate K/s |
| What put the heat there | watts by mechanism — radiation, convection, solar, friction, generation |
| Balance | watts made against watts vented |
| Cost | substeps demanded and granted, energy drift |

**The per-mechanism shares are what make an outcome interpretable.** A hull hot from solar gain
wants shading or a lower absorptivity; one hot from friction wants to slow down; one hot from its
own reactors wants radiators. The temperature alone says none of that.

### 4. Sweep the settings space (expensive, small sample)

Roughly 50 ships spanning the strata, across a settings space rather than the five named profiles.
The output is not one answer but a **region**: every settings vector that satisfies G1–G6, with
its cost. The named profiles are then points chosen inside that region for different tastes, which
is what the user asked for and what [profiles.md](profiles.md) currently asserts without evidence.

Axes worth sweeping: `HeatTimeScale`, `MaxSubsteps`, `MaxSubstepsPerBlock`,
`MaxElementVisitsPerStep`, and the definition-side dials the derivation exposes — the waste
fractions and the critical-temperature blend.

Coordinate sweeps rather than a full grid: the space is large and the axes are close to separable
in their effect on cost, if not on balance.

## Acquisition

**This is the step with constraints this repository cannot remove.** Three routes, and they trade
off effort against scale:

| Route | Needs | Scale | Notes |
| --- | --- | --- | --- |
| **Subscribe in game** | nothing | tens to hundreds | Steam unpacks subscribed items to `steamapps/workshop/content/244850`, already in the layout the lab reads. Free, no credentials, no rate limit. This is what the lab runs on today. |
| **Steam Web API + direct UGC** | a free Web API key | thousands, if it works | `IPublishedFileService/QueryFiles` lists popular blueprints by tag and sort order. Whether the items expose a downloadable `file_url` is **unverified** — newer workshop items often do not, and it has to be probed rather than assumed. |
| **SteamCMD** | a Steam account owning the game | ten thousand | `workshop_download_item 244850 <id>` in batches. Anonymous login generally fails for a paid title. Hours of downloading, and worth rate-limiting out of courtesy. |

**The route taken is the second for listing and the third for fetching**, built as
[`CorpusFetch`](../tests/Thermodynamics.Sim/CorpusFetch.cs):

```bash
# list only: writes out/corpus/manifest.csv, downloads nothing
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --top 10000 --list-only

# list and fetch
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --user <steam-account> --top 10000
```

Ranked by **total unique subscriptions**, not by votes or recency: the corpus is meant to be the
designs people build and fly, and a subscription is the closest signal the workshop has to that.
Ranking by vote would over-weight the spectacular and by date the untested. Items tagged `Mod`
alongside `Blueprint` are dropped at listing rather than downloaded and rejected.

**No credential passes through this tool.** SteamCMD is invoked with a user name and no password,
which works once the account has been logged in by hand:

```bash
steamcmd +login <user> +quit      # once, interactively; SteamCMD caches it
```

Anything else would mean this program handling a password, and it has no business doing that.

The run is resumable and rate-limited: pages pause, fetches go in batches of fifty, the manifest is
reused if it is already long enough, and anything already on disk is skipped. SteamCMD unpacks into
the layout `Blueprints` already reads, so the corpus directory is pointed at rather than assembled.
A nonzero SteamCMD exit is not treated as failure — on a corpus this size an item being deleted or
made private between listing and fetching is routine.

Two filters apply whatever the route:

* **Unmodded only**, and strictly: one unresolved subtype disqualifies a ship, because there is no
  way to tell whether the block that failed to resolve was a decorative panel or the reactor.
* **Designs, not fragments.** Ships under 25 blocks are cockpits, doors and test rigs.

## What exists now

| Piece | State |
| --- | --- |
| [`GameBlocks`](../tests/Thermodynamics.Harness/GameBlocks.cs) | Reads every definition out of the installed game — size, mounts, sealing, build cost. |
| [`Blueprints`](../tests/Thermodynamics.Harness/Blueprints.cs) | Turns a `bp.sbc` into ships the solver can run. Models are derived and shared across the corpus. |
| [`CorpusLab`](../tests/Thermodynamics.Harness/CorpusLab.cs) | Corpus yield and size distribution. `-- corpus [--path <dir>]`. |
| [`BlueprintTests`](../tests/Thermodynamics.Tests/BlueprintTests.cs) | Seven tests on the yield, ending with a real subscribed ship building a simulation that steps. |
| [`CorpusFetch`](../tests/Thermodynamics.Sim/CorpusFetch.cs) | Lists and fetches the corpus. `-- corpus-fetch`. Unexercised against a real key. |
| [`ShipProfile`](../tests/Thermodynamics.Harness/ShipProfile.cs) | Step 2. Every ship measured without stepping it. `-- screen`. |
| [`Specimens`](../tests/Thermodynamics.Harness/Specimens.cs) | Cuts a corpus to a panel that covers it. See [Specimens](#specimens) below. |
| [`ShipLoad`](../tests/Thermodynamics.Harness/ShipLoad.cs) | What a ship has switched on, thrust per direction. |
| [`Battery`](../tests/Thermodynamics.Harness/Battery.cs), [`ScenarioOutcome`](../tests/Thermodynamics.Harness/ScenarioOutcome.cs) | Step 3. `-- battery`. |
| Steps 0 and 4 | Criteria written above; the settings sweep is designed and unbuilt. |

Measured on the 16 blueprints already subscribed on the development machine: 140 ships, 1 modded,
107 under the size floor, **32 usable** totalling 25,893 blocks, from 26 up to 9,378 blocks each.
The yield is low because a subscription list is mostly mods; a corpus acquired as blueprints
deliberately will do far better.

## Specimens

Ten thousand ships is the right number to *start* with and the wrong number to keep running. A
corpus of popular workshop designs is enormously redundant — five hundred small-grid fighters differ
in silhouette and not in anything this simulation can see — and every one of them costs the same to
run as the one ship that taught you something.

**The useful measure of a ship is not how typical it is but how much it says that nothing else
says.** So the panel is chosen by *coverage*, not by frequency:

* the extremes of every feature axis first, because an axis with no example at its end is an axis
  the panel cannot speak about at all;
* then greedy maximin — repeatedly take the ship furthest from everything already taken, which
  spreads a small sample through a space without reproducing its density.

Picking the most popular ships instead would produce a panel of near-duplicates from the densest
part of the design space and no examples of anything unusual, which is exactly backwards: the
interior of a cluster is predictable from its edges, and the edges are where a balance figure fails
first.

Two numbers come out of it. **Fidelity** is the distance from the worst-served ship in the corpus to
its nearest panel member — how well the panel covers what it came from. **Redundancy** is how many
ships sit on top of another, which is what says when a corpus has stopped being worth growing.

**This is a hypothesis and it has to be checked.** The claim is that two ships close together in
feature space behave the same way under the battery. Until the battery has been run over a full
corpus once and the panel's verdicts compared against it, a reduced panel is a guess about what
matters. Fidelity is a statement about the feature space rather than about behaviour, so it is
necessary and not sufficient.

## Running the lab: parallel and linear

Two modes, and the distinction is not a preference.

| Mode | For | Why |
| --- | --- | --- |
| **Parallel** (default) | balance and data collection | Every result is a settling problem that is a pure function of a ship and a scenario. Nothing about a temperature changes because another core was busy, so going wide is free accuracy. |
| **Linear** (`--linear`) | anything whose figure is a *duration* | The moment a result is a time, every other core is noise — cache pressure, memory bandwidth, turbo headroom, the scheduler. A benchmark run alongside thirty others measures contention, not the solver. |

A balance figure taken linearly is the same figure. A cost figure taken in parallel is a different
one, and there is no way to tell from the number itself.

**The run is the unit of parallelism, not the ship.** Every simulation built from one ship shares
that ship's `BlockInstance` objects and the load is written onto them, so two scenarios on one ship
at once would overwrite each other — which does not throw and does not look wrong in a report. Each
job reads the blueprint again for grid state of its own; block *models* stay cached and shared, so
only the per-block instances are rebuilt, at a fraction of the settling run it frees. Without that,
a panel of six ships would use six cores of however many the machine has.

Measured on a 32-core machine, 3 specimens through 20 scenarios:

| | Wall clock | Per run |
| --- | --- | --- |
| linear | 545.3 s | 9.09 s |
| parallel | **118.7 s** | 1.98 s |

**The two matrices were identical to the last digit.** `ParallelAndLinearProduceTheSameMatrix` is
the standing cheap version of that comparison, so a change reintroducing shared state fails in the
suite rather than in a report nobody re-runs linearly. Results keep the order of their inputs in
both modes, so two runs can be diffed.

Two other things made it faster without touching accuracy: runs stop on equilibrium rather than on
the clock — most are flat long before their ceiling — and the definition caches are built once
before the workers start rather than by whichever arrives first. Screening 32 ships is now 0.1 s.

## The first full cycle

Thirty-two ships through twenty scenarios — 640 runs, 432 s wall clock, 0.68 s a run on 31 workers.

| Scenario | Peak K | Mean K | Worst hot spot | Ships over critical |
| --- | --- | --- | --- | --- |
| `vacuum-shadow` / `idle` | 470 | 154 | 152 | **0 of 32** |
| `vacuum-sunlit` | 476 | 210 | 148 | 0 of 32 |
| `surface-hot-noon` | 377 | 295 | 78 | 0 of 32 |
| `surface-cold-night` | 365 | 279 | 82 | 0 of 32 |
| `underground` | 362 | 280 | 78 | 0 of 32 |
| `full-electrical` | 2,507 | 277 | 2,076 | 9 of 32 |
| `all-peak` | 4,033 | 491 | 3,187 | 17 of 32 |
| `burn-down` | 3,141 | 339 | 2,661 | 16 of 32 |
| `burn-left` | 1,687 | 320 | 1,304 | 14 of 32 |
| `flight-100` | 2,582 | 307 | 2,274 | 4 of 32 |
| `reentry` | 381 | 312 | 67 | 0 of 32 |
| `recovery` | 470 | 162 | 177 | **0 of 32** |

Read against the criteria, and **provisionally**, because the counts are contaminated by the sealed
gyro below:

* **G1 holds.** Not one ship in thirty-two goes critical at rest, in any environment — vacuum,
  sunlit, hot surface, cold night, underground. The target was under 1 %.
* **G2 appears to hold.** Between a quarter and a half of ships reach critical under sustained load,
  against a target of at least 20 %. This is the criterion I expected to fail.
* **G5 holds.** Every ship recovers: `recovery` is 0 of 32 over critical, back to a 162 K mean.
* **G3, G4, G6** need the cooled comparison, the correlation analysis and the settings sweep, none
  of which are built.

The corpus is thirty-two subscribed ships, so none of this is a population claim. It says the
pipeline works end to end and the criteria are computable, which is what a first cycle is for.

## A real defect, found by running real ships

**A block whose only mount face is unconnected has no thermal exit at all.**

This model conducts across the area where *both* blocks carry a mount surface. `LargeBlockGyro`
declares exactly one mount point, on its bottom, and is not airtight. A gyro buried in a hull with
nothing mounted below it therefore has no conduction path and no exposed face: whatever it makes
stays, and its temperature rises without bound. Nineteen are in that state across thirty-two ships,
and the gyro is one of the most common functional blocks in the game.

It is not a large heat source — 1.5 kW — but the exit is *zero*, and anything divided by zero goes
the same place. It is also invisible: no exception, no NaN, no warning, just a number nobody has a
prior for. The first battery investigation mistook exactly this signature for a balance problem.

This is a defect in the shipped mod rather than in the lab, and it is the first thing the corpus has
found that a synthetic hull could not — the rig this repository builds for itself never places a
gyro with an unconnected mount face.

**What to do about it is a design decision**, in three shapes:

* conduct across any touching face rather than only mount-to-mount, which is a broad change to the
  conduction model and touches every joint;
* guarantee a floor conductance between touching blocks, keeping the mount rule for the *rate* and
  removing the zero;
* detect and report it — a block generating heat with no exit is exactly what the grid health check
  added for telemetry should raise as a fault.

The third is cheap and immediate; the first two are balance changes that want measuring.

**Every over-critical count above is contaminated by this**, since a sealed gyro crosses any
threshold eventually. The load figures want re-reading once it is settled.

## Where the numbers stand

After three harness faults were found and fixed — the gyro torque, the sealed blocks, the
double-counted stores — the battery reads coherently for the first time. On the 9,378-block Atlas:

| Scenario | Peak K | Mean K | made kW | vented kW | Verdict |
| --- | --- | --- | --- | --- | --- |
| `idle` | 298 | 156 | 71 | 406 | **G1 holds** — a parked ship sits at room temperature and sheds |
| `full-electrical` | 2,507 | 431 | 44,070 | 44,018 | 268 blocks over critical |
| `all-peak` | 2,574 | 815 | 175,313 | 175,256 | the impossible ceiling |
| `burn-forward` | 1,461 | 352 | 23,207 | 23,202 | hottest block is a hydrogen thruster |
| `reentry` | 348 | 305 | 89,992 | 89,956 | 89 MW of friction, shed as fast as it arrives |
| `recovery` | 368 | 213 | 71 | 1,356 | **G5 holds** — throttled to idle, it comes back |

**`made` and `vented` now agree to within a tenth of a per cent in every loaded case.** That is the
strongest evidence the model is behaving: the grids are reaching real equilibrium rather than
climbing until the clock runs out. Substep demand is met throughout.

Corpus-wide thermal stress is now p50 532 W/m², p95 1,476, max 3,451 — down from p50 1,335 / max
9,367 before the fixes, and from p50 17,893 at the very first run.

**What is left is a balance question rather than a defect.** The remaining hot spot is seven
`LargeJumpDrive` charging at 32 MW each; at a 0.15 loss fraction that is 33.6 MW inside one
compartment, and the gyros beside them reach 2,507 K by conduction rather than by anything they do
themselves. Whether that fraction is right is now the kind of question the lab was built to answer.

## Open questions

* **The fetcher has never run against a real key.** Its failure paths are checked — a missing key,
  a rejected key and a missing account name all report and stop cleanly — but whether the query
  parameters return what is wanted can only be settled by a run.
* ~~Is one unresolved block too strict?~~ **Decided: it stays strict.** A softer rule keyed on
  whether the unresolved blocks carry power would raise yield, at the cost of the corpus measuring
  *nearly* vanilla balance. Revisit only if the real yield turns out to be unusably low.
* **Whether `Census` should be replaced or kept beside the corpus.** Its tiers are a hypothesis the
  corpus can now test; if they hold, that is worth knowing, and if they do not, every scale figure
  taken on them wants re-reading.
* ~~Ships reach hundreds of thousands of kelvin under load.~~ **Diagnosed, and it was the
  harness.** Gyros carry a `ForceMagnitude` element and it means *torque in newton-metres*, not
  thrust in newtons — a large gyro reads 3.36e7 and a prototech one 2.016e8, against a real draw of
  ten kilowatts. Reading it off every block that has the element turned one gyro into 33.6 MW of
  waste heat and drove a 9,378-block hull to 342,510 K. It looked exactly like solver instability
  and would have been written up as one. **The substep demand being met throughout is what gave it
  away** — 2.7 against 3 granted, 18.3 against 19 — so the integrator was never short of what it
  asked for. `all-peak` on the same hull now reads 15,751 K and the hottest block is a hydrogen
  thruster, which is what the model says should run hot. `OnlyAThrusterCarriesThrust` pins it.
* ~~Batteries may be the next one.~~ **Two more harness faults, both found by the same method.**
  The 7,634 K battery had `faces 0`, `area 0` and **`W/K out 0`** — no exit of any kind, radiative
  or conductive. A thermally sealed box heats without bound and without any symptom but the
  temperature.
  * **A definition that lists no mount points is not a block that mounts nowhere.** Six per cent of
    the game's definitions leave `MountPoints` out and let the game derive them from model
    geometry; `LargeBlockBatteryBlock` is one. Reading that silence as "no mounts" gave the block
    no conduction links and no exposed faces. Now an undeclared set means every face mounts, which
    is what `BlockModel.Solid` already assumed.
  * **A store is never charging and discharging at once.** A battery rates 12 MW in *and* 12 MW
    out, and both were being counted: every battery on a ship stood beside the reactors as a
    co-generator while also drawing its full rating. Generators now carry the load and stores cover
    only a shortfall.

  Together these moved the Atlas from 7,634 K to **2,507 K** at full electrical load, and idle from
  412 K to **298 K**. `ABlockThatDeclaresNoMountPointsStillConducts` and
  `AStoreIsNotBothChargingAndDischarging` pin them.

* **Does the panel reproduce the corpus?** The claim in [Specimens](#specimens), unchecked. It needs
  one full battery run over a whole corpus to settle, and that run is the expensive thing the panel
  exists to avoid — so it is paid once.
* **Subgrids.** A blueprint's rotor and piston subgrids are read as separate ships today. They are
  thermally connected in game, through the bridges `ThermalBridges` builds, and the lab does not
  reassemble them.
