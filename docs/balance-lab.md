# The balance lab

A rig for deciding what good balance *is*, from a population of real ships rather than from a
hull this repository built for itself.

> The rules argued here are stated canonically in [rules.md](rules.md): `E1` `E2` `E7` `E8`
> `M1` `M2` `M3` `M8` `M10` `D1` `O5` `R3` `J3`.

| Looking for | Go to |
| --- | --- |
| What the lab has found | [balance.md](balance.md) |
| The tooling that reads its output | [tools/corpus/README.md](../tools/corpus/README.md) |
| The harness the lab runs on | [tests/README.md](../tests/README.md) |
| The censoring every peak is subject to | [known-issues.md](known-issues.md#deliberate-limits) |

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

Eight criteria, each a number the matrix can produce and each able to fail. These are proposals to
argue with — the point is that they are written down before the data is, so a run that fails them
is a finding rather than an excuse to move a threshold.

| # | Criterion | Fails when | Why it matters |
| --- | --- | --- | --- |
| **G1** | **Idle is safe.** A ship at rest in the environment it was built for does not overheat. | more than ~1 % of the corpus goes critical at idle | A mod that kills parked ships is uninstalled, whatever else it does right. |
| **G2** | **Load bites.** Under sustained full power, a meaningful share of uncooled ships reach a warning state. | fewer than ~20 % ever get warm | Heat that never arrives is scenery. This is the criterion the current build most likely fails. |
| **G3** | **Cooling works.** Fitting radiators or a loop moves the outcome. | median Δpeak from a standard cooling fit is small | If the answer does not respond to the one lever the player has, the mechanic is decoration. |
| **G4** | **Design decides, not size.** Outcome correlates with things a builder controls — exposed area per watt, radiator count, how buried the reactor is — more than with block count or grid size. | rank correlation with block count exceeds that with exposure | Otherwise the mod taxes big ships rather than rewarding good ones. |
| **G5** | **No death spiral.** A ship past critical that throttles to idle returns below critical in bounded time. | recovery time unbounded, or damage continues after the load stops | A player must be able to react to a warning. |
| **G6** | **Affordable across the population.** Substep demand and step cost at p95/p99 of the corpus, not at the mean. | p99 substep demand exceeds what the shipped caps grant | The census hull is one point; the tail is what stutters. **Two notes it has earned.** Its marker is a *fidelity* marker, not a cost one — a demand above the cap is the cap doing its job, and the step gets cheaper rather than dearer; what the excess buys is approximation, which the verdict prices beside the failure. And the *step cost* half of its own sentence has never been produced: the corpus carries `substeps_demanded` and no timing column, so `G6` is scored on half of what it says (`C23`). |
| **G7** | **A ship the game spawns survives arrival.** Every vanilla prefab, idle, in the environment its category spawns into, for five simulated minutes, loses no block. | any prefab loses a block | The stated compatibility floor. A mod that destroys the game's own cargo ships as they arrive is broken however good its physics is, and this is the one criterion measured on ships nobody chose to put in a corpus. |
| **G8** | **The significant event lands in the window, and the ship is usable again inside a session.** Under sustained full electrical load, the median time from load to the first block crossing critical falls in 120–300 simulated seconds — and from a full burn throttled to idle, the median hull finishes cooling within an hour. | the crossing median falls outside 120–300 s, or the recovery median exceeds 3,600 s | The mod's own stated balance target, which had never been a scored criterion. Both halves are one criterion because one clock governs both: the dial that puts the block in the window pushes the hull out of the session, and a route that satisfies either half alone is not a route. |

**G7 holds.** All 705 prefabs, 461,428 blocks, idle: not one crosses critical, let alone loses a
block. The same 705 flown hard lose 616, which is the control rather than the criterion — see
[balance.md](balance.md#the-compatibility-floor-holds).

**G8 was written here before the sweep that tests it**, on the same terms as `G7`, and its five
decisions could each have gone the other way:

* **Ships, not block types.** The reading that opened [backlog.md](backlog.md) `C12` timed each of
  the 72 block types that cannot cool themselves from 293 K, alone, and found none in the window. A
  player never meets a block alone — they meet it bolted to a hull that conducts heat out of it —
  and the whole projected route to the window works *through* that conduction. A criterion over
  isolated blocks would be insensitive to the dial the answer is expected to be.
* **The crossing, not the loss.** The crossing is when a readout changes and a player can act; the
  loss is a median 37 s later ([balance.md](balance.md#how-long-a-block-has-after-it-crosses)). The
  window is about the event a player is meant to notice and respond to, so it is the crossing.
* **Full electrical load.** The state `G2` is already scored in, and the one a player reaches by
  turning everything on rather than by flying in a particular direction. Thrust would make the
  answer a property of a heading.
* **The median, not a share.** A window is two-sided, so it needs a point statistic rather than a
  count, and the median is what the population tables already carry.
* **An hour for the hull, stated as a number rather than as "too long".** A ship that is still
  cooling long after the load came off is a ship a player cannot use, and the shipped clock brings
  one back in about 1,320 s. An hour is the round figure inside a session and outside the shipped
  value by more than a factor of two, so it can fail without being a restatement of the status quo.
  *(The scenario this is scored in was corrected before the grid was read — see below.)*

**G8's second half was corrected once, before the grid was scored against it**, and the direction
matters: the change made the criterion *stricter*, not looser. It was written as *at idle, the median
time for a hull to reach equilibrium stays under an hour*, and idle turned out to be a scenario that
cannot answer it. A hull at idle in vacuum shadow has no equilibrium — it cools toward the vacuum
floor — and the settling figure is *seconds until the hottest block came within 5 K of where it
ended*, which at low `HeatTimeScale` a hull satisfies at the first sample because it has barely
moved. Measured: the median runs 1,230 s, 2,370 s, 3,450 s as the clock falls 225 → 112 → 56, then
reads **120 s** at 25, 15 and 11 with 26 to 36 of 40 hulls sitting on the floor. Under the old
wording four cells passed on a column that was a blind spot; under this one **none of them does**.
The reading is pinned by `SettleReadingTests`, and recovery — from a full burn, throttled to idle —
is the scenario where the hull is driven somewhere and back, so the same figure is a real duration
there and is the one a player actually waits through.

**G8 is measured, and it holds — at a configuration the mod does not ship.** The paired sweep ran
twenty-five cells of conduction against the clock on the forty-hull retest set, and four of them
satisfy both halves: conductivity ×4 at `HeatTimeScale` 120, 100, 90 and 80, crossing at 124–186 s
and recovering in 2,220–3,270 s, at 1.36–2.04× the shipped substep demand and keeping `G1`, `G2`
and `G5`. The projection that opened the question — ×4 with the clock near 15 — was out by a factor
of five; the composition rule behind it was not, and holds to 1 %. The grid, the two curves that
locate the answer and what excludes conductivity ×8 are in
[balance.md](balance.md#two-dials-at-once-the-window-is-reachable-at-conductivity-4-with-the-clock-near-100).
**Whether to ship it is a separate decision** and is [backlog.md](backlog.md) `C12`: retuning moves
every temperature figure in the repository.

**The first reading of that grid was wrong, and the criterion is what caught it.** The crossing
median had been taken over the hulls that crossed rather than over the hulls that were loaded, and
two cells at conductivity ×8 scored as satisfying `G8` on a population where 27 of 40 hulls never
reach critical at all. A hull that never crossed is censored above, not absent (`E9`) — the same
treatment the settling half already gave a hull that never settled — and read that way ×8 has no
median at any clock. The rule is pinned by `tools/corpus/test_scoring.py`.

**G7 was written here before it was measured**, which is the whole of `E11` — a criterion added after
the numbers arrive is not a criterion. Four choices in it were decisions rather than conveniences,
and each could have gone the other way:

* **Losing a block, not crossing critical.** The floor is that a spawned ship does not *fall apart*.
  The two are different events and a block that has just crossed is losing nothing; measuring the
  crossing would fail ships that never lose anything. See
  [balance.md](balance.md#how-long-a-block-has-after-it-crosses).
* **Idle, not loaded.** Arrival is what is being tested. A ship the player has not touched is not
  charging a jump drive, and what happens once they fly it is `G2`'s question.
* **Five simulated minutes.** Long past where a corpus ship that is going to lose a block has lost
  it — the crossing-to-loss gap is under a minute at p90 — and inside the clocks the lab already
  runs.
* **The environment its category spawns into.** Planetary encounters on a planet surface at noon,
  everything else in sunlit vacuum. Running every prefab in both would be a harder test than the
  truth and would fail ships for a place they are never put.

**G3 is answered, and the answer is that the two fits fail in opposite ways.** `retrofit` parses a
real workshop hull, runs it under full electrical load to find where its heat actually is, and puts
the mod's own blocks in the cells that hull left free — so the constraint a player is under is part
of the measurement rather than assumed away.

```bash
dotnet run --project Thermodynamics.Sim -- retrofit --ships 500 --csv out/
```

Over 427 real ships, 332 of which got warm enough under load for cooling them to mean anything:

| fit | fits on | median | p90 | helped >1 % | hurt >1 % |
| --- | ---: | ---: | ---: | ---: | ---: |
| **bolted** — radiators against the hot block | 281 of 332 | **−0.11 %** | 2.45 % | 60 | **85** |
| **plumbed** — a ring, a pump and a sink face | **49 of 332** | **+1.35 %** | 8.94 % | 26 | 5 |

**Bolting fits nearly everywhere and does not work.** It makes more ships worse than better, by the
mechanism [blocks.md](blocks.md) states and the `coolers` ladder found on a rig: a block against a
face is a face that was radiating to the sky and now radiates into a neighbour. The worst hull here
went from 2,941 K to 4,319 K for having seven radiators bolted to its jump drive.

**Plumbing works and hardly ever fits.** Where a ring lands it helps five ships for every one it
hurts, and its ninetieth percentile is nearly four times the bolted one — but a closed loop of free
cells with one of them face-adjacent to the hot block exists on **fifteen per cent** of warm hulls.
A finished ship does not leave a ring of empty cells around the thing that gets hot.

> **The sink face is the whole difference, and leaving it out inverts the result.** The first run of
> this lab built rings without requesting one, which couples a ring to a hot block through ordinary
> block-to-block conduction — the bolted case with extra pipes. It reported a median of 0.13 % over
> 108 fits. Requiring the sink halved what could be fitted and doubled what it bought. That is the
> 1,000 W/K against 167 in [balance.md](balance.md#the-three-findings), arriving as a retrofit result.

So the mechanic is real and the ships people have built cannot receive it. That is a balance
decision rather than a defect — whether cooling should be fittable to a finished hull, or whether
it is something you design in — and it is the one G3 was asked to surface.

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
* **stiffness demand** — conductance over capacity per block, and the distribution's tail, in two
  worlds: `stiff` is the ship in vacuum and `air` is the same ship at sea level.

Stiffness is the one measurement here that is not a property of the ship. Half of it is what a
block exchanges with the world over its exposed area, and in a vacuum that half is radiation
alone — so a hull that demands 2.5 substeps in orbit demands 14.3 over a planet, set by a light
fitting either way. Both are reported because a ship is flown in both, and which one a specimen is
selected on is a decision rather than a default. **It is currently the vacuum figure**, which is
the conservative choice for a feature the panel is stratified on and is worth revisiting once the
battery has run against both.

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

*Motion* — friction against airflow. The hull feels one scalar, the relative wind, so a session
with both a storm and a moving ship cannot attribute a heat to either. The battery holds each
contributor still while the other moves, then runs the two compositions where the sum does
something neither part does. `FrictionIsolationTests` pins the same matrix at unit scale.

| Scenario | The question |
| --- | --- |
| `flight-50`, `flight-100` | Velocity alone, in still air: friction against airflow, at which speed |
| `flight-300` | The same, at the speed limit the servers this mod is played on actually run |
| `storm-parked` | Wind alone: a parked hull in a 100 m/s gale heats exactly as `flight-100` does |
| `storm-300` | Wind alone at the raised limit, so the airspeed can be attributed at 300 too |
| `flight-headwind` | Composition: 40 of wind against 60 of speed trips a threshold neither reaches alone |
| `flight-downwind` | Composition: 80 of speed in a 60 m/s tailwind is 20 of airflow — no friction at full throttle |
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

Roughly 50 ships spanning the strata, across a settings space. The output is not one answer but a
**region**: every settings vector that satisfies G1–G6, with its cost. The shipped configuration
should be a point inside it, and today that is asserted rather than measured.

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

## The pieces

| Piece | What it does |
| --- | --- |
| [`GameBlocks`](../tests/Thermodynamics.Harness/GameBlocks.cs) | Reads every definition out of the installed game — size, mounts, sealing, build cost. |
| [`Blueprints`](../tests/Thermodynamics.Harness/Blueprints.cs) | Turns a `bp.sbc` into ships the solver can run. Models are derived and shared across the corpus. |
| [`CorpusLab`](../tests/Thermodynamics.Harness/CorpusLab.cs) | Corpus yield and size distribution. `-- corpus [--path <dir>]`. |
| [`BlueprintTests`](../tests/Thermodynamics.Tests/BlueprintTests.cs) | Tests on the yield, all synthetic. A real subscribed ship building a simulation that steps is `CorpusSurvey`, which does it for every ship in the corpus. |
| [`CorpusFetch`](../tests/Thermodynamics.Sim/CorpusFetch.cs) | Lists and fetches the corpus. `-- corpus-fetch`. Unexercised against a real key. |
| [`ShipProfile`](../tests/Thermodynamics.Harness/ShipProfile.cs) | Step 2. Every ship measured without stepping it. `-- screen`. |
| [`Specimens`](../tests/Thermodynamics.Harness/Specimens.cs) | Cuts a corpus to a panel that covers it. See [Specimens](#specimens) below. |
| [`ShipLoad`](../tests/Thermodynamics.Harness/ShipLoad.cs) | What a ship has switched on, thrust per direction. |
| [`Battery`](../tests/Thermodynamics.Harness/Battery.cs), [`ScenarioOutcome`](../tests/Thermodynamics.Harness/ScenarioOutcome.cs) | Step 3. `-- battery`. |
| Steps 0 and 4 | Criteria written above; the settings sweep is designed and unbuilt. |

The corpus these run against is 9,981 blueprints yielding **8,142 hulls**, of which 8,132 are
distinct name-and-id pairs. A subscription list yields far less — mostly mods, and mostly under the
size floor — which is why the acquisition route matters.

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

## A block with no exit is the lab's own failure mode

A block with no exposed face and no conduction has nowhere at all to send its heat. It climbs until
the run stops — no exception, no NaN, no warning, just a number nobody has a prior for. It is the
single most misleading result this lab can produce, and every instance of it so far has been the
harness reading a definition wrongly rather than the mod being wrong. That is `D1`, and it is why
`ScreeningTests` carries the standing form of each:

| What a definition says | What it does not mean |
| --- | --- |
| a `ForceMagnitude` element | thrust in newtons — on a gyro it is torque in newton-metres, and reading it as thrust turned one gyro into 33.6 MW and a hull into 342,510 K |
| no `MountPoints` | a block that mounts nowhere — six per cent of the game's definitions leave them out and let the game derive them from model geometry, and an undeclared set means every face mounts |
| a rated input *and* a rated output | both at once — a store covers a shortfall, and counting it as a co-generator drawing its full rating at the same time doubles a ship's load |
| a block that does not seal | a block with no mounts — sealing and mounting are separate properties, and filling the mount bits from a definition that declared none zeroes them |

`hotspot` is the tool for asking why a block is the hottest thing on a ship: it dumps the top of the
distribution with the three figures that decide where each landed — what it generates, what it can
radiate through its own faces, and what it can conduct into its neighbours. A large generation
against a small conductance and no exposure is a *layout* result; a generation with **no** exit at
all is a defect.

**Sealed blocks are bounded rather than asserted to zero.** Twenty-four remain across the corpus,
all on one ship, `UNSC Panama` — 0.002 % of 1.15 million blocks. They are armour cubes that mount on
every face, have no neighbour in any direction, and still report no exposed face, which a synthetic
grid of two disconnected blocks says should be impossible. That one is unexplained and open;
`CorpusSurvey` holds the bound so a regression to the earlier scale fails while one ship does not
hold the suite hostage.

## Open questions

* **The fetcher has never run against a real key.** Its failure paths are checked — a missing key,
  a rejected key and a missing account name all report and stop cleanly — but whether the query
  parameters return what is wanted can only be settled by a run.
* **Does the panel reproduce the corpus?** The claim in [Specimens](#specimens) is a hypothesis:
  that two ships close together in feature space behave the same way under the battery. Settling it
  needs one full battery run over a whole corpus and a comparison against the panel's verdicts —
  which is the expensive thing the panel exists to avoid, so it is paid once.
* **Whether `Census` should be replaced or kept beside the corpus.** Its tiers are a hypothesis the
  corpus can now test; if they hold, that is worth knowing, and if they do not, every scale figure
  taken on them wants re-reading.
* **One unexplained ship in the sealed-block bound**, above.
* **The 25-block size floor is an unexamined constant.** It has never been varied to see whether it
  changes a population figure.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-24 | Two notes on `G6`, neither of which moves it (`P3`). Its marker is a fidelity marker rather than a cost one — a demand above the cap is the cap bounding cost, and the shipped breach *buys* 1.15× of the step for 0.028 K. And the step-cost half of its own sentence has never been produced, because the corpus carries no timing column; that gap is now `C23` ([backlog.md](backlog.md) `C19`). |
| 2026-08-22 | Removed *subgrids are read as separate ships* from the open questions. It was not true and had not been for as long as `ShipAssembly` existed: a blueprint's grids are built as one machine and bridged at their mechanical joints. 747 of the first 1,002 ships of the 2026-08-22 sweep hold more than one grid and 695 resolved joints, 29,604 of them. What was genuinely missing is that nothing checked a bridge *moves heat* — `CorpusSurvey` counted them — and `SubgridBridgeTests` does. |
| 2026-08-23 | **`G8` is measured and it holds**, at conductivity ×4 with `HeatTimeScale` 80–120 — four of twenty-five cells, `G1`, `G2` and `G5` all kept, 1.36–2.04× the shipped substep demand. **The criterion caught a defect in its own scorer first**: the crossing median was taken over the hulls that crossed rather than over the hulls that were loaded, which reported two conductivity ×8 cells as satisfying `G8` on a population where 27 of 40 hulls never reach critical. Censored above as `E9` requires, ×8 has no median at any clock. Nothing in `G8` moved. |
| 2026-08-23 | **Corrected `G8`'s second half in the open, before scoring anything against it** (`E11`). It asked for a settling time *at idle*, and idle has no equilibrium in vacuum shadow — the hull cools toward the floor — so at low `HeatTimeScale` the figure reads 120 s, its own floor, for 26 to 36 of 40 hulls. The old wording is above; it is replaced by the recovery time, which is a real duration and the one a player waits through. **The correction is stricter**: four cells passed the old half and none passes this one. |
| 2026-08-23 | Added `G8`, the significance window, **before the sweep that tests it** (`E11`): under sustained full electrical load the median crossing falls in 120–300 s, and at idle the median hull settles inside an hour. The mod's own balance target had never been a scored criterion, which is [backlog.md](backlog.md) `C12`. Both halves are one criterion because one clock governs both time constants, and the five decisions inside the wording are written out beside it. |
| 2026-08-22 | Added `G7`, the compatibility floor, **before measuring it** (`E11`): every vanilla prefab, idle, in the environment its category spawns into, for five simulated minutes, loses no block. The stated intent that a ship the game spawns must survive arrival had never been a scored criterion and had never been measured, which is [backlog.md](backlog.md) `C10`. The four decisions inside the wording are written out beside it, because a criterion whose terms are settled after the data is not one. |
| 2026-08-22 | Said that `BlueprintTests` is synthetic throughout. The real-ship case it used to end on was demoted to an uncalled helper when `CorpusSurvey` absorbed it, and has now been deleted ([backlog.md](backlog.md) `H4`); the claim is `CorpusSurvey`'s step probe, over every ship rather than one. |
| 2026-08-22 | Finished the split this page began: the three sections still narrating what an early run found are gone. *The first full cycle* reported 32 subscribed ships as a provisional read of G1, G2 and G5, which the 8,142-hull survey in [balance.md](balance.md#the-population) has since answered over a population — quoting the small run beside the large one is `E4` in slow motion. *Where the numbers stand* was the same 32-ship run on one hull. *Sealed blocks: three harness faults* narrated three defects that are fixed and pinned; what survives is the standing hazard, restated as what a definition does **not** mean, which is the form `D1` and `ScreeningTests` hold it in. Three struck-through entries left *Open questions*, and *What exists now* stopped quoting a 32-ship yield as the corpus. |
| 2026-08-22 | Moved the readings of the corpus datasets to [balance.md](balance.md), so this page is the lab's design and that one is what the lab found. Added the standard header and this change log. |
| 2026-08-22 | Answered G3 by fitting cooling to ships people actually built, and said which half of the cooling criterion the ladder answers and which it does not. |
| 2026-08-21 | Measured the speed limit the mod is actually played at. |
| 2026-08-20 | Isolated wind from velocity and composed them where the game does. Measured a ship's stiffness in the world it flies in. Opened the lab: the criteria written down before any data was collected, the staging from cheap screening to the full battery, and the corpus acquisition. |
