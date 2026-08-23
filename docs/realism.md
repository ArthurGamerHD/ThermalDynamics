# How far the model is from physics

What the simulation gives up against real thermodynamics, measured rather than argued, and what each
departure costs. This page is about the **model**; what a player can change about it is
[configuration.md](configuration.md), and what the blocks are worth is [balance.md](balance.md).

> The rules argued here are stated canonically in [rules.md](rules.md): `D5` `D6` `E7` `M1`.

| Looking for | Go to |
| --- | --- |
| The settings themselves, and what each one does | [configuration.md](configuration.md) |
| The equations these profiles vary | [thermal-model.md](thermal-model.md) |
| Why a handful of light fittings sets the cost | [stiffness.md](stiffness.md) |
| The population the balance targets are tested on | [balance.md](balance.md) |

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- profiles          # one rig, four worlds
dotnet run --project Thermodynamics.Sim -- sweep --csv out/  # every scenario and worst case
dotnet run --project Thermodynamics.Sim -- features          # mechanism switches in combination
```

## The four worlds

The mod has no dial for how physically true it is, because `HeatTimeScale` makes it deliberately 225
times faster than the world and nothing shipped turns that off. So the axis is built in the harness
instead, as [`BalanceProfile`](../tests/Thermodynamics.Harness/BalanceProfiles.cs): **it changes
nothing that ships**, and it carries every knob that moves the balance rather than only the
integration ones — both pace scales, the environment constants, which mechanisms run, how the
coolant is modelled, and what a reactor's waste heat is.

Conduction pace is expressed by scaling the material figures the harness builds with, which reaches
the same number the solver would see. That is why no core setting had to exist for any of this to be
measured.

| World | What it is |
| --- | --- |
| `physical` | Every constant its real value. Real capacities, real conductivities, 1361 W/m² solar, nothing clamped. |
| `candidate` | Real materials and constants at a game clock — the hypothesis, not a conclusion. |
| `shipped` | What the mod does today, read off `ThermalSettings` rather than restated. |
| `arcade` | One clamped substep, mechanisms off, ring lumped. The far end, kept so the comparison has one. |

**`shipped` reads the real defaults.** It used to restate them and had drifted — carrying
`MaxSubsteps 16` against a shipped 64 — which is `D3` on the one row the whole comparison is
anchored to.

## Real time is the cheapest thing to integrate

Stiffness is conductance over capacity, so dividing capacity by 225 multiplies substep demand by 225.
Measured on a 150-block hull with a 200 kW reactor in it:

| `HeatTimeScale` | substeps demanded | hull after 5 simulated seconds |
| ---: | ---: | ---: |
| 1 | 0.00 | 293.3 K |
| 225 | 0.90 | 319.8 K |
| 3,600 | 14.40 | 440.5 K |

**Accuracy costs patience, not frames.** What costs frames is the pace — which is the whole reason
the mod ships a clock that is a lie, and the reason that clock is the single largest departure on
this page.

## What the comparison found

**Realism is the cheapest thing here, not the most expensive.** `physical` asks 1.00 substeps
against `shipped`'s 7.22 and costs 8,152 link visits a second against 97,308 — real heat capacities
are 225 times larger, so the grid is 225 times softer. Every substep this mod spends exists because
of `HeatTimeScale`. What realism costs is *responsiveness*: 0.11 K/s against 22.9 K/s, which is the
one number that makes the clock have to be a lie.

**`HeatTimeScale` is equilibrium-neutral and `ConductionScale` is not.** A 225× clock change moves
a settling point 0.7 K. A 2.4× conduction change moves it 128 K at the same clock, and thousands of
kelvin across the scenario library. One is a pace dial; the other silently redistributes where heat
sits. They have been treated as the same kind of thing and they are not.

**The candidate is not a free win.** On one rig it looked like it beat `shipped` on every axis. Across
the library it runs far hotter — 15,588 K against 7,621 on `x-overloaded` — because real conduction
spreads heat less and the source keeps it. Adopting it means rebalancing critical temperatures and
radiator sizing with it.

## Failure, and what actually causes it

The substep estimate is the metric that explains the rest. Every world above is told exactly how
wrong it is; the question is what it does about it.

| profile | substeps | wanted | diverged cells |
| --- | --- | --- | --- |
| physical | 1.00 | 0 | 1 |
| candidate | 4.15 | 31 | 2 |
| shipped | 7.22 | 47 | 1 |
| arcade | 1.00 | **21,101,200** | **6** |

**Starvation is what breaks `arcade`.** Granting it 64 substeps instead of 1, at the same clock,
dropped its divergences from 14 to 5. It is not that the profile is approximate — it is that the
integrator is refused what it asks for by seven orders of magnitude. Every `arcade` divergence in
the sweep is 100% starved.

**Starvation explains nothing anywhere else.** `candidate` is starved 49% on `x-burning-ship` and
peaks at 11,280 K; `shipped` is starved 0% on the same rig and peaks at 11,279 K. One kelvin apart,
with the substeps granted in one case and refused in the other. Whatever is wrong there does not
care how finely the second is cut.

**And starvation was never the only cause.** The five arcade divergences that survived the extra
substeps were loop-bearing, and `loop-faults` got *worse* with more of them — 2.4×10¹⁵ K against
7×10⁴. Something in the coolant path was genuinely unstable rather than merely starved.

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
sweep's arcade divergences fell to the **6 in the table above — every loop-bearing one went**,
including `loop-stiffness` at 2.4×10¹⁰ K. The six survivors carry no coolant loop at all.

Guarded by `TheLoopPathSurvivesAVeryConductivePipe` at copper and at four times copper, and by
`ArcadeNoLongerDivergesOnTheEverythingRig`.

Two hypotheses were tested and both were wrong, which is recorded here so they are not tried again:

* *The clamps do not cover the loop path.* They do — `AccumulateLoops` honours
  `ClampConductionOvershoot`, and `nodeConductanceTotal` includes loop and room coupling.
* *The well-mixed ring puts every pipe link onto one parcel.* It does, but switching it off makes
  arcade's divergences slightly **worse** (14 → 16), so it is not the mechanism.

### The shipped default was never diverging on the burning ship

```
shipped     x-burning-ship   11,279 K   0% starved   4268 over critical   settled
candidate   x-burning-ship   11,280 K  49% starved   4268 over critical   settled
```

This was published as a divergence for as long as it was measured, on the strength of one number:
the hottest block passes 10,000 K. **It is a converged answer**, and three things say so. Run ten
times longer the rig is flat to the last digit from 600 s to 6,000 s. Its energy balances —
**1,083.360 MW made against 1,083.251 MW vented**, a part in ten thousand. And the pair above,
which was originally the evidence that *starvation* was not the cause, is stronger evidence than
that: two integrators refused wildly different substep counts landing one kelvin apart is what
convergence looks like and is not something a divergence does.

**Where the number comes from is arithmetic.** `Burning` drives the census hull's producers at
twenty times their rating, so 488 of them make 1,083 MW inside a four-thousand-block hull. The
hottest block is one with **no exposed face at all**: its only way out is 1,317 W/K of conduction
into neighbours that are themselves buried and hot, and the temperature that pushes 2.22 MW down
that path is 11,279 K. The peak among blocks that *can* radiate is 2,822 K. The rig exists to raise
a damage event on every step, and it is not a state a ship reaches.

**The defect it leaves behind is the column, not the cell.** `Diverged` was a threshold on a
temperature, and a threshold on a temperature cannot tell a converged extreme from a diverged
integration — so it reported one as the other and a year of work went after a cause that was not
there. It now requires the run to have *failed to settle* as well, measured over the last fifth of
the clock rather than over one interval. `ProfileSuiteTests.TheBurningShipSettlesRatherThanDiverging`
and `TheHottestBlockOnTheBurningShipHasNoFaceToRadiateFrom` hold both halves.

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

* **Scenario run lengths are fixed to the shipped clock.** A world 225× slower is still climbing
  when a run ends: 59 of 136 cells are untrustworthy, mostly `physical`'s. Run lengths would have to
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
| 2026-08-22 | Withdrew the burning-ship divergence and corrected it in place (`E10`). It settles: flat to the last digit over ten times the run, energy balanced to a part in ten thousand, and two integrators refused wildly different substep counts one kelvin apart. The 11,279 K is a conduction-limited interior temperature — the hottest block has no face to radiate from — and the peak among blocks that can is 2,822 K. The real defect was the divergence column: a threshold on a temperature, which cannot tell a converged extreme from a diverged integration. It now requires a failure to settle as well. |
| 2026-08-22 | Re-measured every figure here against one sweep, after `shipped` began reading the real defaults. The correction that matters: the shipped default's divergence on `x-burning-ship` was published as starvation, and it is not — it diverges with 0% of its substeps refused, one kelvin from `candidate`, which is refused 49%. |
| 2026-08-22 | Became this page. The five shipped presets are gone — see [configuration.md](configuration.md#change-log) — and what is left is the half of the old `profiles.md` that was never about them: the harness's realism comparison, what it found, and the two divergences it pins. `shipped` now reads `ThermalSettings` rather than restating it, which is how it came to carry `MaxSubsteps 16` against a shipped 64. |
| 2026-08-22 | Corrected the account of the shipped bundles, which named a `minimal` profile that does not exist and said all five ran `HeatTimeScale` 225 when three ran 1. |
| 2026-08-21 | Moved a block's function out of code and into `Cubes.xml`, which deleted the per-profile definition overlays. |
| 2026-08-19 | Established that real time is cheap to integrate, and pinned the loop path's stiffness ceiling — the defect that had to be fixed before the coarse configurations were safe. |
