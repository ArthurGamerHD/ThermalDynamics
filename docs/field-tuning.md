# Field tuning — TestWorld1, 2026-08-19

A 296 s live run, six 1,300-block large grids, `MaxSubsteps 3` / `MaxSubstepsPerBlock 3`.
Telemetry in the world's `Storage/ThermalDynamics_Thermodynamics`.

## Performance is not the problem

| | |
| --- | --- |
| total measured | **~1.8 %** of real time (see note) |
| solver alone | 1.63 % |
| mean frame | 0.304 ms |
| worst frame | 25.8 ms |
| frames over a 60 fps budget | **2 of 17,352** (0.01 %) |
| block updates / real second | 31,145 |
| ns per element visit | 50.4 |

> **Note on `total measured`.** The report that produced these figures summed the grid update
> into the session frame that already contained it, so the dump read 3.62 % and 4.70 %. The
> session frame is very nearly all grid update, so the true totals are close to half those — hence
> the approximations above. They cannot be recovered exactly without re-running, because the dump
> printed the sum rather than the two terms. `solver alone` was never affected.

Both of the two over-budget frames are in the first eight seconds — build and load, not steady
state. There is a great deal of headroom here, and the tuning question is therefore not "how do we
make it cheaper" but "what should we spend the headroom on".

## Drift is inside tolerance

Peak against mean-hottest, per grid, over the whole run: 2.89 K, 1.18 K, 1.0 K, 0.9 K, 1.1 K,
1.05 K. That is **0.2–0.6 K per minute**, against a 3 K per minute tolerance. Nothing is running
away; no grid recorded a critical event and total heat damage is zero.

## The whole grid's cost is set by decorative blocks

The stability estimate is driven by the least massive block on the ship, and on this ship those are
lights:

| subtype | live | mass | J/K | substeps demanded |
| --- | --- | --- | --- | --- |
| `LargeBlockLight_1corner` | 36 | 24 kg | 48 | **21.4** |
| `SmallLight` | 54 | 16 kg | 32 | 17.5 |
| `LargeCameraBlock` | 12 | 40.6 kg | 81 | 9.3 |
| `LargeNeonTubesStraightEnd2` | 12 | — | 172 | 5.8 |

Everything else on a 1,355-block warship — armour, trusses, reactors — asks for five or fewer. A
grid takes as many substeps as its stiffest element needs and every other element pays for all of
them, so **the light fittings are setting the simulation cost of the entire ship**.

`MaxSubstepsPerBlock 3` is what makes that affordable today. It floors 122 blocks per grid — 9.18 %
of them — and buys a 7.12× saving.

## What the headroom could buy

Element visits per simulated second, from the run's own cap sweep:

| cap | blocks floored | share | saving | solver cost |
| --- | --- | --- | --- | --- |
| off | 0 | — | — | 11.6 % of real time |
| 8 | 60 | 0.75 % | 62.5 % | 4.3 % |
| 6 | 84 | 1.05 % | 71.9 % | 3.3 % |
| **3** (in force) | 732 | 9.18 % | 86.0 % | **1.6 %** |
| 1 | 3,009 | 37.7 % | 95.3 % | 0.5 % |

Going from 3 to 6 doubles the solver's cost — from 1.6 % of real time to 3.3 % — and cuts the
blocks being under-resolved from 9.18 % to 1.05 %. On this machine that is close to free accuracy.

**`MaxSubsteps` must move with it.** At the current settings the per-block cap reduces demand from
21.35 to exactly 3.00 and `MaxSubsteps 3` grants it, so nothing is refused — `clamped_steps` is 0
and that is not luck, it is the two caps agreeing. Raising `MaxSubstepsPerBlock` to 6 without
raising `MaxSubsteps` to at least 6 would start refusing steps, which is the failure mode described
in [profiles.md](profiles.md).

## Three ways to spend it, cheapest first

1. **`MaxSubstepsPerBlock 6`, `MaxSubsteps 6`.** One config change, roughly 3.3 % of real time,
   nine tenths of the currently-floored blocks resolved properly. No code, no rebalance.
2. **Give decorative blocks a thermal mass that is not absurd.** Lights and cameras fall through to
   `DefaultThermodynamics` and inherit steel's 450 J/(kg·K) on a 16 kg body. They are the top three
   stiffness drivers; treating them as thermally uninteresting would drop the uncapped demand from
   21.4 to about 5.8 and make the cap almost irrelevant. This is the fix that removes the cause
   rather than capping the symptom.
3. **`ExcludeFromSimulation` on decorative types.** Cheapest of all, but it removes the cell
   entirely — it conducts nothing *and blocks nothing*, so a light set into a wall would stop
   sealing the room behind it. Not recommended without checking room mapping first.

## Two things worth a second look

* ~~The settings file reports `Version 6`; this branch is 4, so the run is not against this
  branch.~~ **Wrong, and worth recording as wrong.** There are two settings types with independent
  version numbers: `Core/Settings/ThermalSettings.cs` is the solver's, at 4, and
  `Data/Scripts/Thermodynamics/Settings.cs` is the game-side one the `.cfg` is written from, at
  **6**. Every field I thought was missing — `SolarOcclusionSamples`, `ClimateGroundInfluence`,
  `SolarGridShadows`, `SolarTerrainRange` — is in this repository. The run *is* against this
  branch, and the tuning conclusions stand without qualification.
* **Convection is reported at 50 W/(m²·K) with air density 0.0000 at 44 km altitude.** A vacuum
  should not carry a convection coefficient at all. Harmless while the density term zeroes the
  transfer, but it suggests the coefficient is being reported before the density blend rather than
  after.


---

# Second run — `MaxSubstepsPerBlock 6` / `MaxSubsteps 6`

Same world, same ship, same build; only the two caps moved. 118.5 s.

| | cap 3 | cap 6 |
| --- | --- | --- |
| blocks floored per grid | 121.97 (9.0 %) | **14.0 (1.03 %)** |
| `demand_configured` | 3.000 | 6.000 |
| `clamped_steps` | 0 | **0** |
| substeps per step | 3.00 | 6.00 |
| solver share of real time | 1.63 % | 2.17 % |
| total measured | ~1.8 % | ~2.4 % |
| ns per element visit | 50.4 | **34.3** |
| worst peak-to-mean drift | 2.89 K | **1.07 K** |
| frames over 60 fps | 2 of 17,352 | 2 of 6,801 |

**It cost a third more, not double.** Twice the substep passes came out at 1.33× the solver time,
because the cost of a single pass fell 32 % — 0.2314 ms to 0.1576 ms, and 50.4 ns per element visit
to 34.3. More substeps over the same node set is a tighter loop with less per-step setup and better
locality; the arithmetic per visit is unchanged, so this is cache behaviour rather than a saving in
work.

**The answer did not move, only its resolution.** Peak temperatures are within 2 K of the first run
on every grid (883–908 K against 885–910 K), so the extra substeps are not buying a different
physics result — they are buying the same result with nine tenths of the previously under-resolved
blocks resolved properly. Drift more than halved on the way.

Both over-budget frames are again frame 1 and frame 15, at 4.1 s and 5.1 s: load, not steady state.

## Stop here

`demand_uncapped` is still 21.35 and still set by `LargeBlockLight_1corner`, so 14 blocks per grid
are still floored. Going further is possible and not obviously worth it:

* Cap 8 floors ten blocks a grid instead of fourteen.
* Cap 16 floors two.
* Uncapped floors none, at roughly 3.5× the substep passes of cap 6 — extrapolating the observed
  sublinear scaling, somewhere near 5 % solver and 7–8 % total.

That last one is still affordable, but it buys perhaps a tenth of a kelvin of drift on a figure
already three times inside tolerance. **Cap 6 is the right place to stop**, and the remaining
demand should be attacked at its source rather than paid for: the fourteen floored blocks are
decorative lights, and giving them a sane thermal mass would drop uncapped demand from 21.4 to
about 5.8 and make the cap irrelevant.

## Done: the lights now have definitions

`Cubes.xml` had no entry for any decorative or electronic type, so every light, neon tube and
camera fell through to `DefaultThermodynamics` and inherited **mild steel's 50 W/(m·K)** on a 16 kg
body. That is what made the least massive block on the ship the stiffest thing on it.

Four per-type entries now say what these blocks are actually made of. It is not a fudge — a light
is a plastic housing around a glass lens, and plastic is about 0.2 W/(m·K) against steel's 50:

| type | conductivity | specific heat | demand before | after |
| --- | --- | --- | --- | --- |
| `InteriorLight` | 2 | 900 | 21.4 | **0.43** |
| `ReflectorLight` | 2 | 900 | — | — |
| `EmissiveBlock` | 1 | 840 | 5.8 | **0.06** |
| `CameraBlock` | 5 | 800 | 9.3 | **0.52** |

Uncapped demand for the ship falls from **21.4 to about 5.6**, where the next driver is the armour
panelling — the structure, which is what should have been setting the pace all along. The substep
cap becomes very nearly irrelevant: at cap 6 nothing should be floored at all.

Worth a run to confirm. The figures above are the stiffness relation, `conductivity / (mass ×
specific heat)`, applied to the measured demands rather than a fresh measurement.

---

# Frequency: there is no sweet spot above 1

`dotnet run --project Thermodynamics.Sim -- frequency`. A 289-block grid with a real stiffness
spread, `MaxSubsteps` high enough that the estimate is always granted, warmed up and best-of-three.

| freq | substeps/step | substeps/s | demanded | ms/step | **ms/sim second** | settled K |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 14.00 | 14.0 | 13.4 | 0.0375 | **0.038** | 945.3 |
| 2 | 7.00 | 14.0 | 6.7 | 0.0193 | 0.039 | 945.3 |
| 3 | 5.00 | 15.0 | 4.5 | 0.0140 | 0.042 | 945.3 |
| 4 | 4.00 | 16.0 | 3.3 | 0.0111 | 0.044 | 945.3 |
| 6 | 3.00 | 18.0 | 2.2 | 0.0091 | 0.055 | 945.3 |
| 8 | 2.00 | 16.0 | 1.7 | 0.0065 | 0.052 | 945.3 |
| 12 | 2.00 | 24.0 | 1.1 | 0.0065 | 0.078 | 945.3 |
| 16 | 1.00 | 16.0 | 0.8 | 0.0040 | 0.064 | 945.3 |
| 24 | 1.00 | 24.0 | 0.6 | 0.0041 | 0.099 | 945.3 |
| 32 | 1.00 | 32.0 | 0.4 | 0.0039 | 0.126 | 945.3 |
| 60 | 1.00 | 60.0 | 0.2 | 0.0039 | 0.234 | 945.3 |

**Raising `Frequency` cannot reduce substep cost, and the sweep shows why.** A step is `1/Frequency`
seconds long and needs proportionally fewer substeps, so the product — `substeps/s`, the third
column — is the grid's stiffness rather than a setting. It sits at 14–16 from Frequency 1 to 8 and
does not improve anywhere.

Past that it gets *worse*, for a reason worth knowing: **substeps are an integer**. At Frequency 12
the estimate asks for 1.1 and pays 2, which is 24 substeps a second against a true demand of 13. At
Frequency 16 it asks for 0.8, pays 1, and drops back to 16. The curve is not monotonic — Frequency
16 is cheaper than Frequency 12 — and every frequency whose demand lands just above an integer is
paying for a whole substep it does not need.

By the clock, **Frequency 1 is cheapest** and cost rises about six-fold to Frequency 60, entirely in
per-step overhead. The settled temperature is 945.3 K at every single row, so this is a pure
cost-and-latency dial with no effect on the answer.

## It is not a propagation dial either

The obvious follow-up: if it does not buy performance, is `Frequency` at least how fast heat
spreads? No — it moves no heat at all.

Time for the source to reach 90 % of its total rise, at 3-second resolution: **18 s at every
frequency from 1 to 60**. The settled temperature is 945.3 K on all eleven rows. A 60-fold change in
step rate does not move the transient or the equilibrium by a measurable amount.

That is what substepping is for. It is an accuracy device, not a rate one: the integrated transfer
over a second is the same however the second is chopped up, so a shorter step simply needs fewer
substeps to resolve the same physics.

**The exception is when substeps are refused.** With `MaxSubsteps` at 1 the step is deliberately too
long and an overshoot clamp decides how much crosses — the most a substep can carry, by definition.
There, each step moves a fixed maximum and more steps a second really does move more heat, which is
why [configuration.md](configuration.md) describes `Frequency` as a responsiveness dial. That is
propagation bought by being wrong, and it is the arcade profile's whole method.

## What it is actually for

Two things, neither of them heat movement.

**Observation latency.** How often a temperature reaches the HUD, the terminal, an overheat event
and the damage pass. At `Frequency 1` a player's readout updates once a second.

**Headroom under a substep cap** — the practical one. Demand *per step* is proportional to step
length, so it falls straight down the table: 13.4 substeps at `Frequency 1`, 3.3 at 4, 0.8 at 16.
Raising `Frequency` is therefore a legitimate way out of starvation: it buys room under a fixed
`MaxSubsteps` at the price of per-step overhead, without changing the total work or the answer.

That is worth knowing for the field configuration. At `Frequency 4` the test ship demands 3.3
substeps a step, which is why `MaxSubsteps 6` is comfortably sufficient. **At `Frequency 2` demand
would double to about 6.7 and start clipping that cap.** The two settings are not independent, and
lowering `Frequency` for performance would quietly cost accuracy at the cap rather than saving
anything.

## So what should it be?

Not 1, despite the table. `Frequency` is also how often damage lands, how often the HUD moves and
how quickly a change is felt, and none of that is measured here. What the sweep rules out is the
idea that raising it buys performance — it does not, and above the point where demand falls under
one substep it costs several times over.

The shipped 4 is a reasonable middle: 16 substeps a second against a floor of 14, so about 12 %
above the cheapest possible, in exchange for four times the responsiveness of Frequency 1.

This refines what [configuration.md](configuration.md) says. That documents `Frequency` as setting
responsiveness and cost together *while `MaxSubsteps` is 1* — true, and the case measured there. With
the estimate granted, the relationship inverts: cost rises with `Frequency` and the substep count
falls to meet it.
