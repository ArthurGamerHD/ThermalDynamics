# Balance profiles

Five presets, in the shape a graphics menu uses: a ladder from *everything on, cost ignored* down
to *cheap and quick*, with the pace of heat as a second axis crossing it. This page is what the
profiles are, what each costs and how to design another; the settings they set are defined one by
one in [configuration.md](configuration.md).

> The rules argued here are stated canonically in [rules.md](rules.md): `M1` `M6` `C7`.

| Looking for | Go to |
| --- | --- |
| What each setting a profile sets actually does | [configuration.md](configuration.md) |
| Why the substep caps are where they are | [stiffness.md](stiffness.md) |
| What the profiles cost in a full report | [benchmarks.md](benchmarks.md#the-configurations) |

`/thermal profile` lists them, `/thermal profile arcade` applies one live, and the settings menu
carries them as buttons. A fresh world runs `responsive`.

| Profile | Integration | Pace | For |
| --- | --- | --- | --- |
| `simulation` | 64 substeps, no cap, no budget | **real time** (1) | The reference. Real physics, and nothing you can watch. |
| `optimized` | 6 / 6, budgeted | **real time** (1) | The same temperatures at the same moments, for less work. |
| `simlite` | 3 / 3, tighter budget, no self-shadowing | **real time** (1) | Real physics, approximately drawn, for a crowded server. |
| `responsive` | as simulation | tuned (225) | Simulation with the clock run fast. How the mod is meant to be played. |
| `arcade` | as optimized | tuned (225) | Responsive's pace at optimized's price. |

**A profile is the whole world, not a patch on it.** Applying one returns every setting it does not
speak for to the shipped value first, so applying the same profile twice with tinkering in between
lands in the same place both times. That is also why the settings menu has no reset button: a
profile *is* the reset.

## What each one costs, and moves

Measured by `bench profiles --seconds 8`: blocks crossed along a held-hot 200-block run, work as
element visits per real second, and the solver's own milliseconds per simulated second.

| Profile | Freq | HeatTimeScale | MaxSubsteps | Blocks crossed | Work/s | ms/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `simulation` | 8 | **1** | 64 | 0.0 | 3,192 | 0.58 |
| `optimized` | 4 | **1** | 6 | 0.0 | 1,596 | 0.13 |
| `simlite` | 4 | **1** | 3 | 0.0 | 1,596 | 0.04 |
| `responsive` | 8 | 225 | 64 | 0.1 | 3,192 | 0.07 |
| `arcade` | 4 | 225 | 6 | 0.1 | 1,596 | 0.04 |

**Read the first three rows' zero honestly.** It is not a rounding artefact: at `HeatTimeScale` 1 a
ship changes temperature at the rate a ship does, and eight seconds of play moves heat across no
blocks at all. The same run with the environment on leaves a hot spot 768 K above its hull on those
profiles and 10 K above it on the two that run the clock fast. That is the whole difference between
the reference and a way to play.

> **The `Work/s` column tracks `Frequency` exactly, and that is a property of what it was measured
> on rather than of `Frequency`.** Both figures come from a 200-block conduction run where every
> node has at most two neighbours and the substep estimate sits at or below one — the regime where a
> step costs one pass whatever its length. On a stiff grid the estimate is far above one and the
> column would be flat in `Frequency` instead; see
> [configuration.md](configuration.md#frequency-is-not-the-cost-dial-it-looks-like).

**The shipped default is the worst point on this table**, and that is worth saying plainly: it is
outrun by every other profile including the cheapest one. It spends its budget on accuracy — a low
`HeatTimeScale` with substeps to spare — and accuracy is not what most of the settings are for. It
stays the default because a simulation mod that quietly stops being a simulation is a worse surprise
than a slow one, but a world that wants heat to *do* something should not be on it.

## The two axes

**`HeatTimeScale` is the clock, and 1 is real.** It divides every heat capacity, so anything above 1
is thermal time running fast: a real ship at real heat capacity takes hours to change temperature,
which is why the mod ships at 225 and why `simulation` — the profile that claims pure realism —
is the one nobody plays on.

**Real time is also the cheapest thing to integrate**, which is the opposite of what a maximum
quality preset usually means. Stiffness is conductance over capacity, so dividing capacity by 225
multiplies substep demand by 225. Measured on a 150-block hull with a 200 kW reactor in it:

| `HeatTimeScale` | substeps demanded | hull after 5 simulated seconds |
| ---: | ---: | ---: |
| 1 | 0.00 | 293.3 K |
| 225 | 0.90 | 319.8 K |
| 3,600 | 14.40 | 440.5 K |

Accuracy on this ladder costs patience, not frames. What costs frames is the pace.

Every profile is a point on both axes. `simulation`, `optimized` and `simlite` run at real time and
descend in how finely that is integrated; `responsive` and `arcade` are `simulation` and `optimized`
with the clock run fast.

## Designing your own

Two relationships bound everything, and neither can be tuned around.

**Heat spreads as the square root of the arithmetic you spend on it.** Diffusion is a square-root
process: a front crosses blocks at a rate proportional to `sqrt(substeps per second)`, while cost
is proportional to substeps per second outright. Measured, holding everything else: 1 substep/s
gave 0.5 blocks/s, 4 gave 1.0, 8 gave 1.4, 16 gave 2.0 — square root to two figures. **Doubling
how responsive a world feels costs four times as much.**

**But where you spend the substeps changes the exchange rate by about three times.** There are two
ways to move more heat per second. The accurate one raises `HeatTimeScale` and grants the extra
substeps its stiffness demands. The approximate one raises `HeatTimeScale` *and refuses* the
substeps with `MaxSubsteps`, letting the overshoot clamps decide how much crosses — which is the
most a substep can carry, by definition. Measured: the clamped route delivered 0.125 blocks/s per
substep/s against 0.045 for the accurate one. If the shape of the curve between two temperatures
does not matter to your world, do not pay for it.

**The far end is a wall, not a slope.** Past roughly `HeatTimeScale / Frequency = 4000` the clamps
are carrying the entire step and blocks start being driven to the ambient floor. `arcade` sits at
3,333 deliberately. `Validate()` warns above 4,000, and the settings menu shows it.

So, knob by knob:

* **`Frequency` sets responsiveness and cost together — but only while `MaxSubsteps` is 1**, or the
  grid is soft enough that the estimate never rises above one substep. That is the regime every
  profile above was tuned in. On a grid stiff enough to ask for real substeps it cancels out of the
  cost entirely; reach for `MaxSubstepsPerBlock` there instead. See
  [configuration.md](configuration.md#frequency-is-not-the-cost-dial-it-looks-like).
* **`HeatTimeScale` sets how much a substep carries.** Raise it until the clamps engage; past that
  it buys nothing, because the clamp is already moving all it can. `arcade` at 20,000 and the same
  profile at 1,000,000 reach identically far.
* **`MaxSubsteps` chooses accuracy or speed.** High means the estimate is always granted and
  nothing clamps. `1` means every step is deliberately too long and the clamps carry it.
* **Keep `HeatTimeScale / Frequency` under 4000.** This is the safety rail. Everything else is
  taste.
* **`EnableRoomAir` and `SolarSelfShadowing` are the two mechanisms that cost most** for what a
  player notices.

Both clamps must stay on for any of this. `ClampConductionOvershoot` and
`ClampEnvironmentOvershoot` are what make a deliberately-too-long step bounded instead of
divergent — with them off, `arcade` reaches 10^22 K in twenty seconds.

## Each profile brings its own definitions

Settings alone cannot make a coarse profile stable. A block's demand on the integrator is its
conductance over its heat capacity, so a 16 kg light fitting with a metal's conductivity asks for
twenty substeps while the armour around it asks for one — and a profile granting three is
integrating that block outside the range its own physics is stable in. `MaxSubstepsPerBlock` floors
exactly those blocks, which is a tolerance rather than a balance.

There is **one balance for every profile.** A profile carries settings — how often the solver
runs, how many substeps it may take, which mechanisms are on — and nothing else. It cannot change a
block's properties.

Profiles used to ship a definition overlay in `Profiles/<name>.xml`, applied over the loaded
definitions at runtime. That was a mistake: it meant a block's numbers depended on which profile a
world happened to run, so `Data/Cubes.xml` was not the answer to "what is this block", and a modder
reading or overriding it could be silently overruled. The four block families those overlays
softened — lights, neon, cameras — are now folded into `Cubes.xml` as type entries and apply
everywhere.

`Data/Cubes.xml`, `Data/Loops.xml` and `Data/Planets.xml` are the sources of truth at all times.
A rebalance edits those files once; nothing rewrites them while the game runs.

## Two different things are called a profile

[`ThermalProfiles`](../Data/Scripts/Thermodynamics/Core/Settings/ThermalProfiles.cs) ships the five
world presets above. They are settings only: `Frequency`, the two substep caps, the visit budget,
`HeatTimeScale` and which mechanisms run.

[`BalanceProfile`](../tests/Thermodynamics.Harness/BalanceProfiles.cs) is the other axis: **how
physically true the model is**. It lives in the harness, changes nothing that ships, and carries
every knob that moves the balance — both pace scales, the environment constants, which mechanisms
run, how the coolant is modelled, and what a reactor's waste heat is.

A profile expresses its conduction pace by scaling the material figures the harness builds with,
which reaches the same number the solver would see. That is why no core setting had to exist for
any of this to be measured.

| Profile | What it is |
| --- | --- |
| `physical` | Every constant its real value. Real capacities, real conductivities, 1361 W/m² solar, nothing clamped. |
| `candidate` | Real materials and constants at a game clock — the hypothesis, not a conclusion. |
| `shipped` | What the mod does today. |
| `arcade` | One clamped substep, mechanisms off, ring lumped. |

## What the comparison found

**Realism is the cheapest thing here, not the most expensive.** `physical` asks 1.00 substeps
against `shipped`'s 5.40 and costs 8,152 link visits a second against 60,374 — real heat capacities
are 225 times larger, so the grid is 225 times softer. Every substep this mod spends exists because
of `HeatTimeScale`. What realism costs is *responsiveness*: 0.11 K/s against 23 K/s, which is the
one number that makes the clock have to be a lie.

**`HeatTimeScale` is equilibrium-neutral and `ConductionScale` is not.** A 225× clock change moves
a settling point 0.7 K. A 2.4× conduction change moves it 128 K at the same clock, and thousands of
kelvin across the scenario library. One is a pace dial; the other silently redistributes where heat
sits. They have been treated as the same kind of thing and they are not.

**The candidate is not a free win.** On one rig it looked like it beat `shipped` on every axis. Across
the library it runs far hotter — 7,502 K against 3,661 on `reactor` — because real conduction spreads
heat less and the source keeps it. Adopting it means rebalancing critical temperatures and radiator
sizing with it.

## Failure, and what actually causes it

The substep estimate is the metric that explains the rest. Every profile is told exactly how wrong
it is; the question is what it does about it.

| profile | substeps | wanted | diverged cells |
| --- | --- | --- | --- |
| physical | 1.00 | 0 | 1 |
| candidate | 4.15 | 31 | 2 |
| shipped | 5.40 | 47 | 1 |
| arcade | 1.00 | **30,900,640** | **14** |

**Starvation is the dominant cause.** Granting `arcade` 64 substeps instead of 1, at the same clock,
drops its divergences from 14 to 5. It is not that the profile is approximate — it is that the
integrator is refused what it asks for by six orders of magnitude.

**But not the only cause.** The five that survive are loop-bearing, and `loop-faults` gets *worse*
with more substeps — 2.4×10¹⁵ K against 7×10⁴. Something in the coolant path is genuinely unstable
rather than merely starved.

### The loop path has a stiffness ceiling, and the clamp was what set it

The question that found it was whether the coolant pipes could go back to copper. They cannot, and
the reason is the same defect. `CoolantFlowTests.SpreadAcrossAHeatedRing` deliberately runs **one substep across a
whole second** and relies on the overshoot clamps to bound it. They do, up to a point — and then
they do not:

| pipe conductivity, effective W/(m·K) | 264 (brass) | 360 | 480 | 600 | 960 (copper) |
| --- | --- | --- | --- | --- | --- |
| flow tests failing | 0 | 0 | 3 | 5 | 5 |

The edge is between 360 and 480. Copper sits at 960 — 2.7× past it — and the ring reaches
291,360 K. Brass at 264 is comfortably clear, which is why the shipped pipes are brass and why
`Cubes.xml` calls that a compromise rather than a materials decision.

**That is a millisecond-long reproduction of the arcade loop divergence**, in a test that had been
in the suite all along.

**The cause is the clamp, not the material.** `AccumulateLoops` bounded each link with
`ClampExchange`, which limits one exchange to the energy that would equalise *that pair*. Correct
for a pair, and wrong for a parcel carrying more than one link: two links each allowed to equalise
deliver twice the energy equalising takes, so the parcel overshoots past its neighbours and the
overshoot grows every substep. A pipe with a sink face has exactly that shape — its own link plus
the sink's — and a well-mixed ring puts *every* link in the ring on one parcel.

Below the clamp threshold it changed nothing, which is why it went unnoticed for so long. It only
bites once exchanges are large enough to saturate, and conductance is what decides that: brass
stayed under it and copper did not.

The fix is one aggregate limit per parcel, the same shape as `RelaxationFactor` for conduction —
`mass / (h × total conductance on that parcel)`, with the worst parcel setting the ring's factor.
The same case now settles at **0.8 K instead of 291,360**, the pipes are copper again, and the
sweep's arcade divergences fell from **14 to 7 — every loop-bearing one**, including
`loop-stiffness` at 2.4×10¹⁰ K. The seven survivors are all non-loop.

Guarded by `TheLoopPathSurvivesAVeryConductivePipe` at copper and at four times copper, and by
`ArcadeNoLongerDivergesOnTheEverythingRig`.

Two hypotheses were tested and both were wrong, which is recorded here so they are not tried again:

* *The clamps do not cover the loop path.* They do — `AccumulateLoops` honours
  `ClampConductionOvershoot`, and `nodeConductanceTotal` includes loop and room coupling.
* *The well-mixed ring puts every pipe link onto one parcel.* It does, but switching it off makes
  arcade's divergences slightly **worse** (14 → 16), so it is not the mechanism.

### The shipped default diverges too, on a ship a player can build

```
shipped   x-burning-ship   11,280 K   49% starved   68,288 over critical   DIVERGED
```

On a burning 4,000-block ship the current default is refused half the substeps it asks for, passes
10,000 K, and never settles. This is not an arcade problem that arcade made visible; it is a
divergence problem that arcade made loud. Pinned by
`ProfileSuiteTests.TheShippedProfileStillDivergesOnABurningShip`, which is written to **fail when
the defect is fixed**.

## The feature matrix

Every switch is tested alone by `FeatureToggleTests`. The matrix tests them in combination, because
the paths interact through shared node temperatures and a shared substep budget.

Three families: all on, one off at a time, one on at a time — enough to find an interaction without
2^12 runs.

**10 of 100 combinations broke, and all ten are arcade.** Arcade diverges under *every* combination
on a rig with a 2 MW source in a pressurised box, with a signature worth recognising: tens of
thousands of kelvin at the hot end while other blocks sit at **0 K**. It runs away and collapses at
the same time. Since removing any single mechanism does not fix it, it is arcade itself and not an
interaction.

A source with every sink switched off is *supposed* to run away, so those combinations are counted
separately rather than reported as defects — otherwise four correct results bury ten real ones.

## Known limits of this suite

* **Scenario run lengths are fixed to the shipped clock.** A profile 225× slower is still climbing
  when a run ends: 67 of 136 cells are untrustworthy, mostly `physical`'s. Run lengths would have to
  scale with `HeatTimeScale` for that column to mean anything. The report marks them rather than
  letting a transient read as an equilibrium.
* **The divergence flag is a 10,000 K threshold**, so it catches both genuine runaway and
  absurd-but-stable steady states. A buried 40 MW reactor really does reach tens of thousands of
  kelvin in this model, because conduction can only carry about 1.8 kW/K away from one cell.
  Convergence and starvation are the honest discriminators.
* **`RealismGaps`** in `BalanceProfiles.cs` lists the eight departures from physics that no profile
  can close — grey-body emissivity, no inter-block radiation, solid-billet conduction, no contact
  resistance, conductive fluid coupling, well-mixed room air, capped waste fractions, and thruster
  heat with no exhaust. `physical` is as real as the *equations* allow, which is not the same as
  real.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Corrected [Two different things are called a profile](#two-different-things-are-called-a-profile), which named the shipped bundles as "`simulation` through `minimal`" and said all five run `HeatTimeScale = 225`. There is no `minimal` profile in `ThermalProfiles`, and three of the five run `HeatTimeScale` **1** — which is the whole point of the ladder this page opens with, contradicted two sections below it. Took the profile ladder's cost table and both halves of [Designing your own](#designing-your-own) out of [configuration.md](configuration.md), which was carrying a second account of this page's subject — including the same `HeatTimeScale` substep-demand table, twice over. That page now defines the settings and this one is what the presets are. |
| 2026-08-22 | Added the standard header and this change log. |
| 2026-08-21 | Moved a block's function out of code and into `Cubes.xml`, which deleted the per-profile definition overlays. |
| 2026-08-19 | Made the profiles a ladder on two axes rather than a list, established that `simulation` means real time and that real time is cheap, and gave each profile the definitions its settings are safe with. Pinned the loop path's stiffness ceiling — the defect that had to be fixed before the profiles were safe. |
