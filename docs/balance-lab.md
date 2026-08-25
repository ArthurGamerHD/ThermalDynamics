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
| **G6** | **Affordable across the population.** Substep demand and step cost at p95/p99 of the corpus, not at the mean. | p99 substep demand exceeds what the shipped caps grant, **or p99 step work exceeds what the shipped element-visit allowance can do in real time** | The census hull is one point; the tail is what stutters. **Two notes it has earned.** Its marker is a *fidelity* marker, not a cost one — a demand above the cap is the cap doing its job, and the step gets cheaper rather than dearer; what the excess buys is approximation, which the verdict prices beside the failure. And the *step cost* half of its own sentence is now written down, above and below, having never been produced before (`C23`). |
| **G7** | **A ship the game spawns survives arrival.** Every vanilla prefab, idle, in the environment its category spawns into, for five simulated minutes, loses no block. | any prefab loses a block | The stated compatibility floor. A mod that destroys the game's own cargo ships as they arrive is broken however good its physics is, and this is the one criterion measured on ships nobody chose to put in a corpus. |
| **G8** | **The significant event lands in the window, and the ship is usable again inside a session.** Under sustained full electrical load, the median time from load to the first block crossing critical falls in 120–300 simulated seconds — and from a full burn throttled to idle, the median hull finishes cooling within an hour. | the crossing median falls outside 120–300 s, or the recovery median exceeds 3,600 s | The mod's own stated balance target, which had never been a scored criterion. Both halves are one criterion because one clock governs both: the dial that puts the block in the window pushes the hull out of the session, and a route that satisfies either half alone is not a route. |

**`G6`'s cost half, written down before it is scored** (`E11`, `E1`). The criterion has always
said *substep demand **and step cost***, and only the demand has ever been produced, because the
corpus carries no timing column — deliberately, since a per-ship millisecond figure taken across
thousands of hulls on a shared machine measures the machine. So the cost is stated as **work**, in
the solver's own unit, and compared against a bound the mod already ships.

* **The statistic.** A step's work in element visits, in the unit the allowance is denominated in:
  `substeps × (links + 4 × nodes)`, which is `ThermalSimulation.SubstepCost` and is what a step's
  length is divided by when the bound decides whether to shorten it. None of it is a clock. **The
  walk records that cost per run** — the mod's own figure for the assembly's most expensive grid,
  since the allowance is per grid — rather than the scorer rebuilding it from columns.

  > **It said *derivable from `blocks`, `joints` and a substep column* until 2026-08-24, and it is
  > not.** `joints` is the mechanical joints between grids and is nought on most blueprints, so that
  > derivation dropped the link term entirely. See the correction below.

  > **The weight was 2.125 until 2026-08-24, and that is a real figure about the solver that is the
  > wrong one here.** It is `ThermalSolverStep.SubstepWork` — the buffers cleared, the environment
  > pass, the apply pass and one more walk — which is the unit a step is cut into *frame-sized
  > slices* in. The allowance is spent in the other one. Scoring in the pacing unit against a bound
  > stated in the budget unit compares two currencies 1.45× apart on a census hull, and it halved
  > every figure below. Corrected in place (`P3`), with the old numbers kept visible.
* **The bound.** `MaxElementVisitsPerStep`, which ships at **4,000,000** (2,000,000 until `C27`).
  It is not an invented threshold: it is the mod's own statement of what one grid's step may cost,
  and a step that exceeds it is not refused — it is spread over more frames, so the grid's simulated
  time runs slower than real time. A ship that cannot keep up is the definition of unaffordable. The
  bound follows the default rather than being pinned here, so this criterion scores the
  configuration that ships.
* **The marker.** `G6`'s cost half fails when **p99 step work over the corpus exceeds the
  allowance**, which is the same shape as its demand half and the same percentile.
* **What it does not cover.** One grid at a time. A fleet's cost is the sum over grids and the
  allowance is per grid, so this says a *ship* is affordable rather than that a *session* is —
  which is `D19`'s question and is measured on a fleet rather than on a population.

> **Every step-work figure below is withdrawn: the link count was the joint count.**
>
> **Corrected 2026-08-24.** The unit is `links + 4 × nodes` and the corpus has never carried a link
> column, so `verdict.py` was handed `joints` — which is the count of *mechanical joints between
> grids*, a rotor or a piston, and is nought on almost every blueprint. `links + 4 × nodes` was
> therefore evaluating to `4 × nodes`, and **every step-work figure this repository has published is
> the node half of the unit alone.** On a 2,000-block census hull that is **1.51× low** — 2.06 links
> a block — and on any other hull it is low by that hull's own link-to-node ratio, which is why no
> factor is applied here and the walk records the cost instead (`StepWorkUnitTests`).
>
> **What it does to the verdicts: nothing, and it makes the failure worse.** A hull's ratio is
> between nought and about three links a block, so the true figure is between 1.0× and 1.75× what is
> written below. The air walk's p99 of 5,812,731 is really between 5.8 M and 10.2 M against a
> 4,000,000 allowance — already failing, and failing harder. The vacuum survey's 881,279 is between
> 0.88 M and 1.54 M against the same allowance — still holding. **No verdict on this page moves; every
> number on it does**, and the numbers cannot be recovered from the datasets that produced them
> because the count they need was never recorded (`P2`). They are replaced by a walk carrying the
> `substep_cost` column, and until it lands `verdict.py` reports `G6`'s cost half as *unmeasured*
> rather than reprinting the old arithmetic.
>
> This is the same class of error as the 2.125-against-4 correction below, one level down: that one
> had the right count in the wrong currency, this one has the wrong count. The figures are left as
> written rather than deleted (`E11`, `E10`).

**Scored in air, and the cost half fails.** Over the 32,575 runs of the 2026-08-24 air walk — the
whole corpus, four scenarios, at the pair that ships: **demand p50 7.9, p95 30.2, p99 34.8** against
64 granted, so the demand half passes with a wide margin; and **step work p50 45,323, p95 1,652,491,
p99 5,812,731** against the 4,000,000 the allowance grants, which is **1.45× over and a failure**.
**222 ships of 8,144 — 2.73 % — pass the allowance in at least one air scenario**, the smallest at
28,781 blocks and the median at 55,631. Per scenario the work p99 is 8,451,031 in re-entry,
7,321,903 parked in a storm, 4,663,327 at hot noon and 2,551,719 in the dark, so **the criterion
fails in three of the four worlds and holds only in vacuum**. `C27`'s doubling of the allowance was
necessary and not sufficient: at the 2,000,000 that shipped this morning the same p99 is 2.9× over.
What to do about it is a decision and is deliberately not taken in the commit that carries the data
(`E11`).

**And `G1` holds in air**, which the corpus had never been asked: 0.02 % of runs have a block over
critical against 0.22 % in vacuum, because air cools.

**Previously, scored in vacuum, where it holds.** Over the 40,656 runs of the 2026-08-21 survey that carry all three
columns: **p50 10,909 element visits, p95 269,153, p99 881,279** against the 4,000,000 the allowance
grants — the ninety-ninth percentile is a little over a fifth of what a step may cost. **29 runs are
past it, and they are 8 ships**, the smallest at 159,449 blocks and the largest at 628,524. So the
allowance covers a ship of about a hundred and sixty thousand blocks in vacuum, and the ships that
exceed it run their simulated time slower than real time rather than being refused anything.

> At the 2,000,000 that shipped until `C27`, the same runs read **102 past it across 28 ships from
> 74,160 blocks up**. The work figures are unchanged; what moved is the bound, and it moved because
> the simulated time a shortened step gives up was priced for the first time (`E11`).

> Corrected twice on 2026-08-24. The figures moved a third of a per cent when the percentile did:
> `verdict.py` took `values[int(q × n)]` where `air.py` interpolated, and the two are printed side
> by side on these pages — a corpus p99 against a panel p99. On forty thousand samples they agree
> to 0.3 %; on **forty** they do not agree at all, because `int(0.99 × 40)` is 39 and the fortieth
> of forty is the *maximum*. One definition now, the interpolating one, which is what every panel
> figure this repository publishes was computed with (`P5`). Before that, and corrected when the
> unit was: **p50 7,293, p95 174,996, p99 446,707, fifteen runs past it and three ships**. What moved is the weight per node, from 2.125 to the 4 the
> allowance is actually divided by, and the substep column, from granted to demanded — granted is
> already clamped by `MaxSubsteps` and by the allowance itself, so it is the answer rather than the
> question. The verdict is unchanged.

> **The dataset is the survey as it was walked**, at `ConductionScale` 2.4 and `HeatTimeScale` 225
> and in the five **vacuum** scenarios, which is the same restriction that let this criterion's
> *demand* half read as passing for months (`C19`). `C24` multiplies a vacuum demand by about 1.6,
> which would put p99 near 1,410,000 — still inside the allowance. **In air it is level with it**:
> the 40-hull panel's p99 demand is 2.81× its vacuum one at the pair that ships, which projects a
> corpus p99 near **4.0 million**, against the 4,000,000 the allowance grants since `C27` and twice
> the 2,000,000 it granted before. Both are projections of a projection and neither is a
> measurement; what would settle it is walking the corpus in air, which is `F11`, and eight hours
> (`P6`). What the scorer needs is any walk's `blocks`, `joints` and a substep column, so it prices
> whichever run it is handed. *(Corrected 2026-08-24: what it needs is `substep_cost` and a substep
> column. `joints` is not the link count and no walk before that date carries one — see the
> withdrawal above.)*

### What the corpus in air is expected to say, written before it says it

`CorpusAirWalk` is running as this is written. The prediction goes here first, with the numbers
that would falsify it, because a projection that is only compared to the data after the fact is a
projection that will be found to have been right (`E1`, `P3`).

**The two comparisons the walk makes, and why the anchor is in it.** The 2026-08-21 survey was
walked at `ConductionScale` 2.4 and `HeatTimeScale` 225; the air walk runs at the pair that ships.
So the air walk against the old survey moves *two* things at once and settles nothing on its own
(`P6`). What it carries instead is `vacuum-shadow` alongside its three atmospheric scenarios, on the
same ships in the same run:

* **air against vacuum, at the shipped pair** — the walk's own `reentry` against its own
  `vacuum-shadow`. Everything but the world held equal. This is the comparison `F11` exists for.
* **the pair, in vacuum** — the walk's `vacuum-shadow` against the survey's `idle`, which is the
  same simulation under another name (`M8`). Everything but the configuration held equal.

**The prediction.** From the 40-hull panel at the pair that ships, air multiplies a p99 substep
demand by **2.81** — `reentry` 35.12 against `vacuum-shadow` 12.51 — and `C24` multiplies a vacuum
demand by about 1.6. Carried onto the survey's own figures:

| | vacuum, old pair | expected vacuum, shipped pair | expected re-entry, shipped pair |
| --- | ---: | ---: | ---: |
| demand p99 | 3.58 | ~5.7 | ~16 |
| step work p99 | 881,279 | ~1.41 M | **~3.96 M** |

> **Measured 2026-08-24, and the projection is falsified.** Re-entry work p99 is **8,451,031** and
> the whole-dataset p99 **5,812,731**, against a stated falsifier of "below 3 M or above 5 M". **The
> assumption named below as most likely to be wrong was not the one that broke**: the panel's
> air-to-vacuum ratio transferred well, 3.05 measured on the corpus against 2.81 on the panel. What
> did not transfer is the *retune* factor. The corpus's own vacuum demand p99 went 3.58 to **11.4**
> between the two walks, a factor of **3.19**, where these pages projected 1.6 — and the 1.6 was the
> panel's own figure, 12.51 over 8.20. So the panel's **air** ratio describes the population and its
> **retune** ratio does not, by about two: `C24` hits the corpus's tail roughly twice as hard as it
> hits forty typical hulls, which is what a tail made of conduction-limited hulls does when
> conduction is multiplied by four. Every projection below is left as written (`P3`).

**So the cost half is predicted to land within about 30 % of the 4,000,000 allowance, either side.**
That is the whole point of writing it down: the projection cannot distinguish pass from fail, and
whichever it is, it is a finding rather than a confirmation.

* **The demand half is predicted to pass comfortably** — about 16 against 64 granted, a quarter of
  the cap. A p99 anywhere near 64 would mean the panel's air multiplier does not describe the
  population at all.
* **What would falsify the projection**: a re-entry work p99 below 3 M or above 5 M. The panel is
  forty hulls chosen to be typical, not a random sample, so its air multiplier transferring to eight
  thousand is the assumption under every number above and the one most likely to be wrong.
* **If the cost half fails**, `C27`'s doubling was necessary and not sufficient, and the next
  question is the size of the tail — 29 ships or two thousand — which decides whether the allowance
  is a setting a few worlds raise or a default that is still wrong.
* **If it passes**, the projection was pessimistic, and the reason will be worth having: the corpus
  median hull is 1,110 blocks against the panel's selection, and a small hull's demand is set by its
  stiffest fitting rather than by its size.

### What a per-block cap does to the population, written before it is measured

`CorpusCapWalk` is running as of 2026-08-25 and **has produced nothing yet**: it was launched on
2026-08-24, ran 69 of 8,144 blueprints, was stopped on an estimate that turned out not to be one,
and is walking the population again from the start. The question, the statistic, the decision rule
and the numbers that would falsify each prediction go here first (`E1`, `E11`), because a saving
measured after the decision to ship it has been taken is a saving that will be found to be cheap.
Nothing below is scored against anything.

> **What stopping it measured was the estimator, and the estimator was wrong.** The walk was
> abandoned at 45 minutes on a projection of *past ten hours*, taken from the share of the
> population's blocks it had covered divided by the rate it was covering them at — 0.14 % a minute
> over the last interval against 0.24 % averaged from the start, a rate that was *falling*.
>
> **The corpus is walked largest first, so that rate falls throughout every healthy run.** The fall
> is a property of the ordering rather than of the walk, and a progress mark is ten files, which on
> a largest-first corpus can be one capital hull or ten fighters. Run the same estimator over
> `CorpusAirWalk`, which finished in **104 minutes**, and over that walk's own first 35 minutes it
> projects **104 to 428 minutes, median 154** — a five-fold spread around an answer already known
> ([pace.py](../tools/corpus/pace.py), `P4`). It is not an instrument, and nothing should have been
> decided on it.
>
> **What the cost actually is: three and a half hours.** The cap walk is the air walk's four
> scenarios with a second arm on each, and the two arms share one blueprint parse, so the second arm
> can only add what it simulates and **2x is a ceiling by construction**. Over the fifty files the
> two walks' progress records share, the measured ratio is **1.95x**, and 1.95 x 104 minutes is
> 3.4 hours. The design note's original 3.5 hours was right; it was taken from a 40-ship stride
> sample, which is the part of the corpus where the per-ship overhead is hidden, so it was right for
> a reason that does not support it — and the figure that overturned it was worse.
>
> **So no cheaper design was built, and that is a decision rather than an omission.** A stratified
> sample would have to defend its selection rule (`M10`) and would answer the reach prediction — a
> share of *all* blocks in the population — only under that rule's own assumptions; dropping the
> vacuum anchor would save a quarter of a walk and cost the control that says air cannot make a hull
> softer. Neither is worth an hour and a half against a population walk that answers all four
> predictions as written.
>
> **The 69 blueprints of the abandoned run were not resumed onto**, because three commits touched
> the solver and the harness between that run and this one, and none of the three was checked for
> behaviour — two were documentation passes over comments and one closed a definition-id collision,
> which is exactly the shape of change that looks inert and is not. Restarting costs 35 minutes of the 3.4 hours and buys a dataset collected under one
> build (`M1`) — and the new run's first 69 ships are then a reproduction check on the old partial
> for nothing (`E7`).

**The question, and why it is one question rather than two.** [backlog.md](backlog.md) `C3` asks
whether `MaxSubstepsPerBlock 6` should be a default; `G6`'s cost half fails in air. They are the
same question. The cap is the one lever that lowers a step's *work* — it raises the mirrored heat
capacity of any element demanding more substeps than the cap grants, so the grid's demand becomes
`min(demand, cap)` and the work falls with it — and `C3` is undecided only because what it costs has
been measured on **one hull**.

**Why not simply raise the allowance.** It is the other lever and it is the wrong one, and saying so
before the data is part of the pre-registration. `G6` asks whether a *ship* is affordable. Raising
`MaxElementVisitsPerStep` until the p99 fits does not make a ship cheaper; it moves the same work
into the frame, and the criterion would then pass by having been re-pointed at a bound chosen to let
it pass — which is `E11` exactly. The allowance moves when what a shortened step gives up is priced
against what the frame costs, which is what `C27` did this morning and is not what this walk is
about.

**The design, and the one thing it has to get right.** Every ship, the four `PairLab.AirScenarios`,
two arms: the cap off as it ships, and `MaxSubstepsPerBlock 6`. **The two arms run to the same
simulated clock**, which is the whole reason this is a new walk rather than a second dataset joined
to `F11`'s. `Battery.Run` stops when the hottest block moves less than 0.25 K in a 60-second chunk,
so two arms of the same ship stop at different instants — and the difference the cap is expected to
make is a hundredth of a kelvin, two orders of magnitude *under* that stopping tolerance. So the
uncapped arm runs first and its elapsed clock is handed to the capped one (`M1`, `P6`). Comparing
two settle-stopped runs would have measured the stopping rule.

**Four predictions, each with what would falsify it.**

| | prediction | falsified by |
| --- | --- | --- |
| the identity | capped demand is `min(uncapped demand, 6)` on every ship and scenario | any run differing by more than 1 % |
| the benefit | work p99 **≈ 2.5 M** against the 4,000,000 allowance — `G6`'s cost half passes | a capped p99 over 4 M, or under 1.5 M |
| the cost | Δpeak p99 **under 1 K**, max under 10 K | a p99 over 1 K |
| the reach | the cap holds back **3–10 %** of all blocks in air | outside that band |

The benefit figure is arithmetic rather than a guess, and it is stated with its own weakness. Taking
the 2026-08-24 air walk's 32,575 rows and replacing each demand with `min(demand, 6)` gives a work
p99 of 1,679,952 in the unit those rows were scored in — which is the node half alone, so the
corrected figure is that times a hull's link-to-node ratio, about 1.5. Hence 2.5 M, and hence a
range of 1.7–2.9 M rather than a number. **A projection of the same dataset is not a measurement of
it**: the substitution assumes the identity in the row above, which is the first thing the walk
checks.

The cost prediction is the weak one and is the reason for the walk. Its only evidence is **one
hull**: 0.028 K on the worst-placed block of a driven census hull in thick air, and 0.017 K on the
hottest ([backlog.md](backlog.md) `C3`). A population is not one hull, and the ships this cap
reaches hardest are the ones with the stiffest fittings, which is not what a census hull is built
from. A p99 in the tenths of a kelvin would be unsurprising; a p99 in whole kelvin would mean the
cap re-masses hulls a player watches heat move through, which is what `stiffness.md` warns of below
its own cliff.

**The decision rule, fixed now.** The mod ships fidelity by default and a saving as a switch, and
this repository has two calibration points for what *imperceptible* means: **0.028 K** is accepted
as the price of the substep ceiling's breach, and **0.607 K** is what keeps `MaxSubstepsPerBlock`
out of the defaults today. So:

* **p99 Δpeak at or under 0.03 K** — ship the cap as the default. It costs what the mod already
  accepts elsewhere and it closes `G6`'s cost half.
* **p99 Δpeak at or over 0.6 K** — it stays a switch. That is the number that made it one, measured
  on a population instead of a hull, and `G6`'s cost half stays open with the allowance as the only
  remaining lever.
* **between them** — a judgement, argued when the number is in, and argued in the open (`E11`).
  Nothing about that band is decided here except that it will not be decided by whether the cap
  happens to rescue a criterion.

**And the walk rescores `G6` itself.** Its uncapped arm is the first corpus dataset to carry
`substep_cost`, so it replaces the withdrawn figures above rather than merely being compared with
them — and because it repeats `F11`'s four scenarios on the same ships, its demand column is a
reproduction check on that walk (`E7`).

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

**Sealed blocks are bounded rather than asserted to zero, and the bound is a share for a reason.**
Over the 2026-08-21 corpus: **1,184 blocks across 331 ships of 45.2 million**, which is 0.0026 %.
`CorpusSurvey` holds the share so a regression to the earlier scale fails while a handful of ships
do not hold the suite hostage.

> **This page said twenty-four, all on one ship, of 1.15 million blocks until 2026-08-24.** That
> was a figure from a sample carried forward as a figure about the population — the share was right
> to within a rounding, which is why nobody caught it, and every other number in the sentence was
> wrong by one to two orders of magnitude (`E2`).

**What they are, measured with `bench sealed`, and there are two kinds.**

The larger kind is **not armour**: it is `LargeBlockGyro`, 307 of the 330 on the seven worst ships.
The game's own definition declares exactly one mount point, `Bottom`, so a gyro whose bottom face
looks at empty space and whose other five are buried has no joint to conduct through and no face
onto the outside to radiate from. That is the model's own rule — *blocks that touch without mount
surfaces on both sides conduct nothing*, [thermal-model.md](thermal-model.md) — working exactly as
written, on a block that **makes heat**. It is a consequence of the design rather than a fault in
it, and it is what the rule costs.

The smaller kind is the one this page called impossible, and it is not. An armour cube standing
alone inside an **interior void** mounts on all six faces and has no neighbour on any of them —
and none of those six neighbouring cells is *external*, so `SurfaceMap.GetExposedFaces` counts no
exposed face, by the definition it is written to. The synthetic grid of two disconnected blocks
cannot reproduce it because two blocks in open space have external neighbours; what the case needs
is an enclosing hull, and the corpus has hulls.

> **And the count is a property of the scenario as well as of the ship.** Air is a block's third
> exit — `BuildRoomLinks` gives every node with a face onto a room a link to that room's air — but
> a lab hull is built unpressurised, so `bench sealed` reports *0 of 623 rooms hold air* and the
> third exit is not there to be had. The same blocks in a pressurised compartment are not sealed.
> The test counts air where there is any; on the corpus battery there is none.

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
* ~~One unexplained ship in the sealed-block bound~~ — **settled 2026-08-24**, and it was neither
  one ship nor unexplained: 1,184 blocks over 331 ships, mostly gyros bolting to nothing because
  the game gives a gyro one mount face. See the sealed-block paragraph above.
* **The 25-block size floor is an unexamined constant.** It has never been varied to see whether it
  changes a population figure.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-25 | **The estimate that abandoned `CorpusCapWalk` was checked against a walk whose answer is known, and it fails the check.** The walk was stopped at 45 minutes on *past ten hours*, projected from the share of the population's blocks covered over the rate they were being covered at. The corpus is walked largest first, so that rate falls throughout every healthy run, and a progress mark is ten files — one capital hull or ten fighters. Run over `CorpusAirWalk`, which finished in **104 minutes**, the same estimator projects **104 to 428 minutes, median 154** over that walk's own first 35 (`P4`). The estimate that survives is the ratio: a second arm sharing one parse is **2x by construction** and **1.95x** measured over the fifty files the two walks share, so the walk costs **3.4 hours**. [pace.py](../tools/corpus/pace.py) is that arithmetic, with `test_pace.py` pinning it; no cheaper design was built, and the paragraph above says why. The walk was restarted rather than resumed, because three commits touched the solver and the harness in between (`M1`). |
| 2026-08-24 | **One definition of a percentile, where there were two.** `air.py` interpolated between the two ranks a quantile falls between and `verdict.py` took `values[int(q × n)]`, and these pages print the two side by side — a corpus p99 against a panel p99. On forty thousand samples they agree to a third of a per cent, which is why nobody noticed; on **forty** they do not agree at all, because `int(0.99 × 40)` is 39 and the fortieth of forty is the maximum. A p99 that is the largest reading in the set is not a percentile, and a forty-hull panel is a set this repository scores. The interpolating one survives, because every published panel figure was computed with it; the corpus figures move by up to 0.3 % and are re-quoted (`P5`, `P3`). |
| 2026-08-24 | **Settled the sealed-block anomaly, and corrected a sample figure that had been standing as a population one.** This page said twenty-four sealed blocks, all on `UNSC Panama`, of 1.15 million — the 2026-08-21 corpus says **1,184 across 331 ships of 45.2 million**, and the *share* was right to a rounding, which is why nobody caught the rest (`E2`). Measured with `bench sealed`, there are two kinds and neither is impossible: `LargeBlockGyro`, 307 of the 330 on the seven worst ships, whose game definition declares one mount point so a gyro with its bottom face against empty space bolts to nothing while being buried — the model's *touch without mounts conducts nothing* rule working, on a block that makes heat; and an armour cube alone in an interior void, whose six free faces all look into cells that are not external and so count as unexposed by definition. The synthetic two-block grid could never reproduce the second because two blocks in open space have external neighbours. Also: air is a third exit and the sealed test never looked at it, so `HotSpotLab.IsSealed` is now one definition shared with `CorpusSurvey` and the report prints how many rooms hold air — on an unpressurised lab hull, none. |
| 2026-08-24 | **`CorpusCapWalk` was launched, ran 69 of 8,144 blueprints and was stopped, and the finding is the walk's own cost.** At 45 minutes it had covered 10 % of the corpus's blocks with the rate *falling* — 0.14 % a minute over the last interval against 0.24 % averaged from the start — which extrapolates past **ten hours** rather than the 3.5 the design note projected from `F11`'s wall-clock. The projection was wrong because it was taken on a 40-ship stride sample, and a stride sample is exactly where the per-*ship* overhead hides: pairing costs about twice an unpaired walk only while ships are large, and once the giants are cleared this walk pays a fixed per-ship cost 8,144 times over eight runs each where `F11` paid it over four. The resume record is kept (`O3`) and nothing is scored: **a partial sweep is not a result** (`E4`), and resuming is now a decision rather than a default. |
| 2026-08-24 | **Wrote down what a per-block cap is expected to do to the population, before building the walk that measures it** (`E1`, `E11`). `C3` — whether `MaxSubstepsPerBlock 6` ships — and `G6`'s failing cost half are one question, because the cap is the only lever that lowers a step's work and `C3` is undecided only because its cost has been measured on one hull. Four predictions with their falsifiers, and a decision rule fixed against the two calibration points this repository already has for *imperceptible*: 0.028 K accepted, 0.607 K refused. Also recorded, before the data: raising the allowance is the other lever and is the wrong one, because it moves the work into the frame rather than making a ship affordable, and a criterion that passes by being re-pointed at a bound chosen to let it pass has not passed. The design's one hard requirement is that both arms run to the **same simulated clock** — the effect is a hundredth of a kelvin and `Battery.Run`'s settle tolerance is a quarter of one, so two settle-stopped arms would measure the stopping rule (`M1`, `P6`). |
| 2026-08-24 | **The link count in `G6`'s cost half was the *joint* count, so every step-work figure this repository has published is the node half of the unit alone.** The unit is `links + 4 × nodes`; the corpus carried no link column and `verdict.py` was handed `joints`, which counts rotors and pistons *between grids* and is nought on almost every blueprint — so the expression evaluated to `4 × nodes`. On a 2,000-block census hull that is **1.51× low**, at 2.06 links a block; on any other hull it is low by that hull's own ratio, so no factor is applied and the walk records `ThermalSimulation.SubstepCost` per run instead. **No verdict moves**: the air walk's p99 is really 5.8–10.2 M against a 4,000,000 allowance and was already failing, and the vacuum survey's is 0.88–1.54 M and still holds. The figures cannot be recovered from the datasets that produced them, so `verdict.py` reports the cost half as *unmeasured* on any walk without the column rather than reprinting the old arithmetic (`E8`, `P2`), and `G6` reads as one half unscored rather than as a failure. `StepWorkUnitTests` pins the harness side and `number()` now reads a missing column as absent rather than crashing (`C8`). |
| 2026-08-24 | **Re-scored `G6`'s cost half in the currency the allowance is spent in, and against the bound that ships after `C27`.** The work was measured with `2.125 × nodes + links`, which is `SubstepWork` — the unit a step is cut into frame-sized *slices* in — and compared against a bound denominated in `links + 4 × nodes`, which is what a step's length is divided by when the allowance decides whether to shorten it. Two currencies, 1.45× apart on a census hull, either side of one comparison; and the substep column was granted where the allowance reads demanded. Re-scored: p50 10,909, p95 269,153, **p99 881,279**, 29 runs past the 4,000,000 across 8 ships from 159,449 blocks up, where it read p99 446,707 and fifteen runs across three ships against 2,000,000. **The verdict is unchanged and the restriction under it is now stated where the figure is**: five vacuum scenarios, which is exactly what let this criterion's *demand* half read as passing for months, and projecting into air puts the corpus p99 level with the bound. |
| 2026-08-24 | **Wrote down `G6`'s cost half, before scoring anything against it** (`E11`). The criterion has always said *substep demand and step cost* and only the demand had ever been produced. The cost is stated as **work** rather than as time — `substeps × (2.125 × nodes + links)`, the solver's own charge, derivable from every corpus walk already taken — and the bound is `MaxElementVisitsPerStep` at 2,000,000, which is the mod's own statement of what a grid's step may cost and the point past which a grid's simulated time runs slower than real time. *(Both figures moved later the same day: the unit to `links + 4 × nodes`, which is the one the allowance is spent in, and the bound to 4,000,000 — see the rows below.)* It fails at p99, the same percentile and shape as the demand half. [backlog.md](backlog.md) `C23`. |
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
