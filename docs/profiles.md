# Balance profiles

Five presets, in the shape a graphics menu uses: a ladder from *everything on, cost ignored* down
to *cheap and quick*, with the pace of heat as a second axis crossing it.

| Profile | Integration | Pace | For |
| --- | --- | --- | --- |
| `simulation` | 64 substeps, no cap, no budget | **real time** (1) | The reference. Real physics, and nothing you can watch. |
| `optimized` | 6 / 6, budgeted | **real time** (1) | The same temperatures at the same moments, for less work. |
| `simlite` | 3 / 3, tighter budget, no self-shadowing | **real time** (1) | Real physics, approximately drawn, for a crowded server. |
| `responsive` | as simulation | tuned (225) | Simulation with the clock run fast. How the mod is meant to be played. |
| `arcade` | as optimized | tuned (225) | Responsive's pace at optimized's price. |

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

Two axes, and every profile is a point on both. `simulation`, `optimized` and `simlite` run at real
time and descend in how finely that is integrated; `responsive` and `arcade` are `simulation` and
`optimized` with the clock run fast.

**A profile is the whole world, not a patch on it.** Applying one returns every setting it does not
speak for to the shipped value first, so applying the same profile twice with tinkering in between
lands in the same place both times. That is also why the settings menu has no reset button: a
profile *is* the reset.

## Each profile brings its own definitions

Settings alone cannot make a coarse profile stable. A block's demand on the integrator is its
conductance over its heat capacity, so a 16 kg light fitting with a metal's conductivity asks for
twenty substeps while the armour around it asks for one — and a profile granting three is
integrating that block outside the range its own physics is stable in. `MaxSubstepsPerBlock` floors
exactly those blocks, which is a tolerance; an overlay is a balance.

Each profile may ship a **definition overlay** in `Profiles/<name>.xml`, applied over what
`Cubes.xml`, `Planets.xml` and `Loops.xml` loaded:

| Profile | Overlay |
| --- | --- |
| `simulation` | **none, deliberately** |
| `optimized` | decorative and electronic blocks given the materials they are actually made of |
| `arcade` | the same as optimized |
| `simlite` | that, plus a raised fallback specific heat and more coolant per pipe |
| `responsive` | none — it grants the substeps to resolve its own pace |

**The files live outside `Data/`**, and that is not tidiness: everything under `Data/` is loaded by
the game and scanned by Definition Extensions, so a second `Cubes.xml` there would collide with the
first. The mod reads these itself and applies them over the loaded definitions.

**`simulation` has no overlay and must not get one.** It runs the shipped definitions exactly, which
is what makes it the reference every other configuration is measured against — and what stops a
benchmark quietly becoming a comparison between two sets of definitions rather than between two
settings. `ThermalProfileOverlays.Benchmarking` forces that state, and the performance report never
reads an overlay.

An overlay states only what it changes. A `Block` entry with no subtype reaches every block,
including the ones no definition file mentions — which on an ordinary world is most of them, and is
where a stiffness problem usually lives. A named subtype refines that, and later entries win. A
misspelled property is reported in the log rather than silently doing nothing.

`ProfileTests` holds the ladder to account: each fast profile must carry heat further than the one
it is built from, and `arcade` must reach within a tenth of `responsive`, since it is meant to be
the same physics tuned rather than different physics. That check is made at the fast pace on
purpose — at real time both numbers are nearly zero and would prove nothing.

Where [balance.md](balance.md) asks whether a block is worth building, this asks whether a *world
configuration* is worth running. Three commands:

```bash
cd sim
dotnet run --project Thermodynamics.Sim -- profiles              # one rig, four worlds
dotnet run --project Thermodynamics.Sim -- sweep --csv out/      # every scenario and worst case
dotnet run --project Thermodynamics.Sim -- features              # mechanism switches in combination
```

## Two different things are called a profile

[`ThermalProfiles`](../Data/Scripts/Thermodynamics/Core/Settings/ThermalProfiles.cs) ships five
bundles — `simulation` through `minimal` — and every one of them tunes the same axis: **how
accurately a step is integrated**. All five run `HeatTimeScale = 225`, so even `simulation` is a
faithful integration of a model that is deliberately 225 times faster than the world.

[`BalanceProfile`](../sim/Thermodynamics.Harness/BalanceProfiles.cs) is the other axis: **how
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

### The loop path had a stiffness ceiling — fixed

Found by asking whether the coolant pipes could go back to copper. They cannot, and the reason is
the same defect. `CoolantFlowTests.SpreadAcrossAHeatedRing` deliberately runs **one substep across a
whole second** and relies on the overshoot clamps to bound it. They do, up to a point — and then
they do not:

| pipe conductivity, effective W/(m·K) | 264 (brass) | 360 | 480 | 600 | 960 (copper) |
| --- | --- | --- | --- | --- | --- |
| flow tests failing | 0 | 0 | 3 | 5 | 5 |

The edge is between 360 and 480. Copper sits at 960 — 2.7× past it — and the ring reaches
291,360 K. Brass at 264 is comfortably clear, which is why the shipped pipes are brass and why
`Cubes.xml` calls that a compromise rather than a materials decision.

**This was a millisecond-long reproduction of the arcade loop divergence**, in a test that had been
in the suite all along.

**The cause was the clamp, not the material.** `AccumulateLoops` bounded each link with
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

### The shipped default diverges too

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
