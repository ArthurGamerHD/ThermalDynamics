# Balance profiles

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

### The loop path has a stiffness ceiling

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

**This is a millisecond-long reproduction of the arcade loop divergence**, in a test that was
already in the suite. Pinned by `TheLoopPathStillHasAStiffnessCeiling`, written to fail when the
defect is fixed — at which point the pipes can go back to copper and the arcade profile can be
re-measured.

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
