# Field tuning — TestWorld1, 2026-08-19

A 296 s live run, six 1,300-block large grids, `MaxSubsteps 3` / `MaxSubstepsPerBlock 3`.
Telemetry in the world's `Storage/ThermalDynamics_Thermodynamics`.

## Performance is not the problem

| | |
| --- | --- |
| total measured | **3.62 %** of real time |
| solver alone | 1.63 % |
| mean frame | 0.304 ms |
| worst frame | 25.8 ms |
| frames over a 60 fps budget | **2 of 17,352** (0.01 %) |
| block updates / real second | 31,145 |
| ns per element visit | 50.4 |

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
| total measured | 3.62 % | 4.70 % |
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
