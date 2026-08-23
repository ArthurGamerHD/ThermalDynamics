# Balance

What every block this mod ships is worth against the vanilla blocks it competes with, and what a
population of 8,132 real ships says about whether the balance targets hold.

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `E4` `E5` `E9` `M10`.

| Looking for | Go to |
| --- | --- |
| The design of the lab and the six criteria | [balance-lab.md](balance-lab.md) |
| The equations behind every figure here | [thermal-model.md](thermal-model.md) |
| What the blocks are and how to build with them | [blocks.md](blocks.md) |
| The censoring limit every peak temperature is subject to | [known-issues.md](known-issues.md#deliberate-limits) |

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- balance             # the per-block report
dotnet run --project Thermodynamics.Sim -- balance --csv out/  # also a diffable table
dotnet run --project Thermodynamics.Sim -- coolers             # the whole-game cooling ladder
python3 tools/corpus/verdict.py out/corpus-2026-08-21          # the population criteria
```

---

## The three findings

1. **A coolant sink face is the stiffest way to move heat into a panel, and nothing else is
   close.** A bolt joint carries 167 W/K; a sink face carries 1,000 W/K. Every surface property of
   the radiator put together is worth a quarter of what plumbing it is worth. **The radiator is not
   a block you bolt to a hot thing — it is a block you plumb.**
2. **Nothing in the game solves a reactor by being bolted to it**, and two common blocks make it
   worse. Cooling has to *carry heat away*, because radiative equilibrium puts temperature on the
   fourth root of area: halving a ship's temperature needs sixteen times the surface.
3. **The population is bimodal, not spread.** A ship either sits near ambient indefinitely or runs
   away in under ten seconds, and the 2–5 minute window the balance target asks for lands in the
   gap between them. One block type decides which side most ships fall on.

---

## Block balance

### Where the numbers come from

Nothing here is transcribed except the vanilla figures, and those are checked.

| Source | What it supplies |
| --- | --- |
| [`ShippedBlocks`](../tests/Thermodynamics.Harness/ShippedBlocks.cs) | The mod's own blocks, read from `Data/CubeBlocks/*.sbc` and `Data/Cubes.xml` at run time — size, mount faces, components, thermal properties |
| [`Vanilla`](../tests/Thermodynamics.Harness/Vanilla.cs) | Component masses and the comparison blocks, transcribed from Space Engineers' own `Content/Data` |
| [`BalanceLab`](../tests/Thermodynamics.Harness/BalanceLab.cs) | The measurements |
| [`ReactorLab`](../tests/Thermodynamics.Harness/ReactorLab.cs) | The reactor waste fraction sweep |
| [`BalanceTests`](../tests/Thermodynamics.Tests/BalanceTests.cs) | The conclusions, pinned |
| [`ReactorWasteHeatTests`](../tests/Thermodynamics.Tests/ReactorWasteHeatTests.cs) | The reactor conclusions, pinned |

**A block's mass is the sum of its components**, priced through `Vanilla.ComponentMasses`. That
matters more than it looks: heat capacity is `mass × specific heat`, so a wrong mass scales every
temperature that block ever reaches.

`Vanilla` is checked in because most machines running this suite have no game install. On a machine
that does have one, `TheVanillaReferenceStillMatchesTheInstalledGame` re-derives every figure and
fails if it has drifted — a transcription nobody checks becomes fiction.

### What is measured, and why each one can bind

| | Measure | Unit | What it answers |
| --- | --- | --- | --- |
| 1 | **Capacity** | J/K | how much heat the block swallows before it warms. No solver involved. |
| 2 | **Shedding** | W at 600 K into a 2.7 K sky | what the block gets rid of |
| 3 | **The joint** | W/K through the block's mount faces | what can *reach* it |
| 4 | **Delivered** | steady state under a real load | all three at once, and the only one a player experiences |

Loads are stated as **watts of heat**, not watts of power, and are driven with a block whose waste
fraction is 1 — otherwise every row would be a fraction of its heading.

### The radiator is a block you plumb

One 200 kW source, one shipped radiator, one dial changed at a time:

| Dial | Change | Source settles | Gain |
| --- | --- | --- | --- |
| *(shipped)* | as built | 740.2 K | — |
| `Emissivity` | 0.35 → 0.80 | 729.5 K | 10.7 K |
| `ExposedSurfaceMultiplier` | 1.25 → 5.0 | 723.0 K | 17.2 K |
| `ExposedSurfaceMultiplier` | 1.25 → 10.0 | 715.6 K | 24.6 K |
| panel depth | 1×5×2 → 1×1×2, same mass | 698.8 K | 41.5 K |
| **coolant sink** | **fed by a loop, not bolted** | **544.9 K** | **195.3 K** |

**The surface dials disappoint because they work on the wrong end.** Multiplying the area makes the
panel *colder* — 338 K down to 213 K across that sweep — and radiation falls away as the fourth
power, so eight times the surface moves only 25% more heat. Meanwhile the drop across the joint
grows from 402 K to 503 K: the panel is starving, not saturating.

Shortening the panel is the only geometry change that helps, and only a little. Conduction runs
centre-to-interface, so the shipped panel's five-cell height puts 6.25 m of metal between its middle
and the joint; flattening it to one cell triples the joint to 500 W/K — but costs most of the
surface, and the two nearly cancel.

### The same question asked of the whole game

The `coolers` ladder takes the largest reactor the game ships, at its plate rating, in shadow, with
**every block that has a plausible claim to being the best cooling in the game** stacked against it
in a column, one to thirty-two. Candidates are chosen by the derivation rather than by taste —
anything whose thermal properties give it more surface than an ordinary cube of the same size — plus
the mod's radiator and a plain armour block as the control.

The bare reactor settles at **889.9 K**. What bolting things to it buys, at the count where each
stops improving:

| Bolted on | Best saving | Of 889.9 K |
| --- | ---: | ---: |
| Wind turbine | 27.2 K | 3.1% |
| Exhaust pipe | 3.7 K | 0.4% |
| **`Gauge_LG_Radiator`** | **2.1 K** | **0.24%** |
| Heat vent | 0.4 K | 0.05% |
| Ladder shaft | 0.3 K | 0.03% |
| Armour cube *(control)* | **−0.6 K** | worse |
| Large thruster | **−16.6 K** | worse |

**Two things make it worse**, because a block against a face is a face that was radiating to the sky
and now radiates into a neighbour. The mod's own radiator is beaten by an exhaust pipe and a wind
turbine, both of which win on having more exposed surface *and* a shorter path to the joint — the
same handicap the panel-depth row measures. `CoolingLadderTests` pins the conclusions, not the
figures.

**A stack saturates by about eight.** Thirty-two radiators save no more than eight do: the top of a
thirty-two stack sits at 61 K, having shed to the sky everything the joints below could deliver. It
is a fin, and it has a length.

> **Two candidates cannot be stacked at all**, and the far-end column is what says so rather than
> leaving it to look like saturation. An exhaust pipe and a wind turbine above the first one come
> back at 34 K and 17 K — *below* the 293 K they were built at — so no heat ever reached them. Their
> n > 1 rows are a fact about the block rather than a rung on a ladder.

### The radiator still earns its place

Against a slab of ordinary light armour of the same shape, in the same position, on the same load:

| Fit | Count | Settles | Saved | Mass | K per tonne |
| --- | --- | --- | --- | --- | --- |
| bare source | 0 | 783.2 K | — | — | — |
| radiators | 1 | 740.2 K | 42.9 K | 600 kg | 71.6 |
| radiators | 8 | 736.5 K | 46.6 K | 4,800 kg | 9.7 |
| armour slab | 1 | 775.7 K | 7.5 K | 5,000 kg | 1.5 |
| armour slab | 8 | 772.7 K | 10.5 K | 40,000 kg | 0.26 |

Six times the cooling for an eighth of the mass — about **48× better per tonne**, pinned by
`TheRadiatorBeatsTheArmourItDisplaces`. The second radiator is worth 3 K and the eighth is worth
nothing: one joint feeds them all.

**Past a certain load the radiator turns negative.** At 2 MW into one cell, bolting panels on makes
the source 33 K *hotter*, because they cover faces that were radiating and cannot carry away what
they blocked.

### The heat pump

Large grid, cold side 300 K. Which of the three limits binds changes twice across an ordinary range,
which is the block's whole character:

| Gap | Coefficient | Lifted | Drawn | Binding |
| --- | --- | --- | --- | --- |
| 5 K | 8.00 | 60 kW | 7.5 kW | coefficient cap |
| 20 K | 6.00 | 60 kW | 10 kW | rating |
| 40 K | 3.00 | 60 kW | 20 kW | rating |
| 100 K | 1.20 | 24 kW | 20 kW | carnot |
| 400 K | 0.30 | 6 kW | 20 kW | carnot |

Pinned by `TheHeatPumpPassesThroughAllThreeOfItsLimits`.

### Coolant rings

A 500 kW block with one sink face, rings of rising size:

| Pipes | Coupling | Settles |
| --- | --- | --- |
| 8 | 9,000 W/K | 857.2 K |
| 12 | 13,000 W/K | 811.6 K |
| 20 | 21,000 W/K | 768.5 K |
| 28 | 29,000 W/K | 740.9 K |

Each pipe adds 1,000 W/K of its own and the fluid mass does not grow, so **a longer ring is strictly
better**. Pinned by `LongerRingsDeliverColderBlocks` and
`LongerRingsCoupleHarderAndCarryTheSameFluid`.

### What the real-unit conversion moved

`Conductivity` used to be a 0…1 quality against a 200 W/(m·K) reference and is now the figure a
materials table gives, times `ThermalConstants.ConductionScale` = 2.4 — calibrated so mild steel
lands exactly where it was ([definitions.md](definitions.md#conductivity-is-in-real-wmk)). The
question [backlog.md](backlog.md) `C2` held open is what that did to the balance a player meets.

**The old world is recoverable exactly, which is what makes this a measurement.** `Data/Cubes.xml`
at `4f6b44a^` held twenty-two definitions and derivation from build components arrived after the
conversion, so before it every block in the game took one of two conductances: **120 W/(m·K)** from
the 0.6 fall-through, and **200** for `Thrust`, `Reactor` and the mod's own nineteen blocks at
quality 1.

**Where it landed, block by block.** Measured off the shipped definitions; the full table is in
[definitions.md](definitions.md#conductivity-is-in-real-wmk) and these are its ends:

| Block | Before | Now | Change |
| --- | ---: | ---: | ---: |
| light and heavy armour | 120 | 120.0 | **1.00×** — the calibration |
| large ion thruster | 200 | 45.3 | **0.23×** |
| large reactor | 200 | 103.2 | **0.52×** |
| battery | 120 | 61.2 | **0.51×** |
| jump drive | 120 | 421.1 | **3.51×** |

The published table had four rows and described only the families `Cubes.xml` authors, so both of
those extremes were unrecorded — and one of the four rows was wrong, because *thrusters went 0.6×*
is true of the hydrogen ones and of nothing else.

#### The two moves the corpus cannot see

Coolant pipes went 4.8× and radiators 2.84×, and **not one of the forty ships in the retest set
carries either**: the corpus filters admit vanilla hulls, so the walk's own reach column reports that
arm as reaching **0 of 177,822 blocks**. Both are priced on a rig instead —
`dotnet run --project Thermodynamics.Sim -- conductance`, one family moved per rig with everything
else left shipped, a large reactor at 300 kW in shadow:

| Rig | Before | Now | Source settles | Saved |
| --- | ---: | ---: | ---: | ---: |
| 1 radiator | 200 | 568.8 | 333.8 → 328.4 K | **5.34 K** |
| 8 radiators | 200 | 568.8 | 328.7 → 316.6 K | **12.07 K** |
| 32 radiators | 200 | 568.8 | 328.6 → 316.2 K | **12.44 K** |
| 12-pipe ring | 200 | 960 | 333.2 → 333.1 K | **0.13 K** |
| 20-pipe ring | 200 | 960 | 319.9 → 319.8 K | **0.06 K** |
| 28-pipe ring | 200 | 960 | 310.7 → 310.7 K | **0.02 K** |

**The 4.8× on coolant pipes is the largest number the conversion produced and the smallest effect it
had.** The fluid couples to its pipe through `LoopThermalProperties.Conductivity`, a 0…1 quality
against `ThermalConstants.ReferenceConductivity` that the conversion never touched; the pipe's own
material decides only what crosses between the pipe block and whatever is bolted to it. Copper made
the pipes better conductors and left the loop carrying exactly what it carried.

**The radiator's 2.84× is real and it saturates.** It is worth 5.3 K on one radiator and 12.4 K by
sixteen, and it stops there because a stack's limit is the area it radiates from rather than the
rate heat reaches it — the visible sign is that the far end of the stack is now *hotter* (43.0 K
against 59.0 K at thirty-two), which is the heat getting further up before it leaves.

Pinned by `ModHardwareRetestTests`, which first holds `Catalog`'s pipe and radiator conductances
against the shipped ones, so a rig cannot quietly become a measurement of a block the mod does not
ship (`C4`).

#### What it did to the ships people fly

Forty hulls from [the retest set](../tools/corpus/README.md#the-retest-set) — each carrying thrust,
power, an airtight room and 100 kW of load, nearest the population median on the four quantities the
census found decide an outcome — run through seven scenarios in the shipped world and in four
counterfactual ones. 1,400 runs, about thirty-five minutes, `ConductanceRetestWalk` and
`tools/corpus/retest.py`. **17.8 % of the 177,822 blocks on the set changed conductance at all**;
the rest derive as steel and sit where the calibration put them.

Shipped minus pre-conversion, median over the forty. Positive means the shipped world is the hotter
one:

| Scenario | composite | vanilla blocks only | thrust and reactors only | mod blocks |
| --- | ---: | ---: | ---: | ---: |
| idle | +0.00 K | −0.11 K | +0.08 K | 0.00 K |
| vacuum-sunlit | +0.03 K | −0.02 K | +0.01 K | 0.00 K |
| recovery | +0.11 K | −0.06 K | +0.14 K | 0.00 K |
| **full-electrical** | **−16.28 K** | **−16.27 K** | +5.12 K | 0.00 K |
| **burn-forward** | **+34.73 K** | −3.04 K | **+44.90 K** | 0.00 K |
| flight-100 | +26.40 K | −2.40 K | +28.35 K | 0.00 K |
| flight-300 | +17.94 K | −2.08 K | +19.46 K | 0.00 K |

**The two effects live in different scenarios and do not cancel.** A burning hull is 34.7 K hotter
than it was and the arms say why: thrusters and reactors alone account for 44.9 K of it, and the
vanilla blocks gaining conductance claw about ten of that back. A hull under electrical load is
16.3 K *cooler*, and every kelvin of that is the vanilla blocks — the jump drive at 3.51× is the
block type carrying 71.3 % of the population's full-load waste, and it now spreads what it makes.
Nothing moves at idle, in sun, or in recovery, because none of those is a hull with a hot block in
it.

**What actually changed for a player is not the peak, it is the spread.** Blocks over critical,
summed across the set:

| Scenario | shipped | pre-conversion | change |
| --- | ---: | ---: | ---: |
| full-electrical | 1,461 | 1,269 | **+192**, on 21 of 40 hulls |
| burn-forward | 1,354 | 1,611 | **−257**, on 25 of 40 hulls |

Both rows run against their own peak column. Under load the peak fell 16 K and 192 *more* blocks
went over critical, because a jump drive that conducts 3.5× better pushes its heat into blocks that
used to stay cold. Under thrust the peak rose 35 K and 257 *fewer* did, because an ion thruster at
0.23× keeps its heat to itself. **The conversion traded peak temperature for spread, in both
directions at once**, and a headline temperature alone would have reported each of those backwards.

Time to the first block lost is unmoved: every median is within 1.5 s, against a damage event that
runs a median 37 s ([how long a block has](#how-long-a-block-has-after-it-crosses)).

**And no criterion moved.** G1, G2 and G5 read identically in all five worlds — 0 of 40 critical at
idle, 40 of 40 warm under load, 39 of 40 recovering. The set is not the population and its G2 is
100 % against the corpus's 65.5 % because membership requires 100 kW of waste; what the identical
columns say is that a change worth tens of kelvin did not reach a threshold the balance is judged
on.

**The null arm is a control and it earned its fifth of the run.** The arm for the mod's own blocks
retunes nothing on a vanilla corpus, and all 280 of its rows came back bit-identical to the shipped
world — so the walk's zero is a measured zero rather than an arm that failed to install (`E8`), and
the harness is deterministic across independently built worlds.

**Read the peaks against the censoring.** One run in eleven on this set — 8.9 % of the shipped
world's 280 — ends past 1,500 K, which says *ran away* and nothing finer, so the findings above rest
on medians and counts and not on any hull's extreme.

### Reactor waste heat

A reactor's fraction has to hold across three orders of magnitude of rated output — 0.5 MW on a
small-grid small generator against 300 MW on a large-grid large one — while the block it heats is
the same size in both grids. It cannot be picked by analogy with the thruster's 0.25, which was
chosen against a 33 MW draw, so it is measured in two rigs that bracket the answer:

* **bare** — one reactor alone in shadow, every face radiating to a 2.7 K sky. The coolest a reactor
  can possibly run, so a fraction that cooks here cooks in every build and no plumbing reaches it.
  **This sets the ceiling.**
* **skinned** — the same reactor under one cell of light armour, which is how one is installed. A
  fraction the skinned rig survives at full rating is a fraction nobody ever has to cool. **This
  sets the floor.**

Both rigs at four candidate fractions, at full rating, against a 1,200 K critical temperature. Bold
is past critical:

| Fraction | Small gen (15 MW) | Large gen (300 MW) | SG small (0.5 MW) | SG large (14.75 MW) |
| --- | --- | --- | --- | --- |
| 0.01 bare | 728.8 K | 889.9 K | 696.4 K | 937.0 K |
| 0.01 skinned | 603.5 K | **1,245.0 K** | 495.4 K | 971.0 K |
| 0.02 bare | 866.7 K | 1,058.2 K | 828.1 K | 1,114.2 K |
| 0.02 skinned | 800.3 K | **1,809.2 K** | 603.1 K | **1,240.5 K** |
| 0.05 bare | 1,089.9 K | **1,330.7 K** | 1,041.3 K | **1,401.1 K** |
| 0.25 bare | **1,629.7 K** | **1,989.8 K** | **1,557.1 K** | **2,095.1 K** |

0.05 and above put a reactor past critical *bare*, which is unbuildable — there is no arrangement
cooler than open space. **0.01 is the fraction where both bounds hold**: every reactor survives at
full rating with its faces on open space, and the 300 MW one goes past critical once wrapped in
hull. That makes where a reactor is installed a decision rather than a detail, and it is the first
thing in the mod that makes a player want a coolant loop for a reason other than curiosity.

An idling ship is deliberately not a cooling problem: at 10% of rating every reactor stays clear of
critical in both rigs. **Heat arrives when power is drawn.**

**It is balance, not efficiency.** 0.01 implies a 99% efficient reactor, which no fission plant
approaches, and the figure should not be read as one. Space Engineers rates a 3×3×3 block at
300 MW — a power density about three orders of magnitude past any real plant — so a real plant's
efficiency of about a third would put 600 MW of waste heat into a 73-tonne box, settling near
2,000 K bare in vacuum: every large reactor in every world destroys itself the moment it is switched
on, in a build no player can improve. **Applying a real efficiency to a fictional rating compounds
the fiction rather than correcting it.** The fraction is chosen so the *consequences* land where
they should, which is the honest way round when one of the two inputs is already invented.

> The table above is the pre-derivation measurement, taken against a 1,200 K critical temperature
> typed into `Cubes.xml` by hand. Deriving a reactor's critical temperature from its own build cost
> instead puts the four of them between 938 and 1,090 K — fuel and graphite in a steel assembly, not
> a round number — and at 0.02 the two smaller reactors then cook themselves bare in vacuum, which
> is a state no build can improve on. A balance figure resting on a number nobody had checked is not
> a balance figure.

---

## The population

What the corpus datasets say when read for balance. Every number is measured and the measurement is
named; where the data cannot answer a question this says so rather than estimating.

> **Quote the figures from `verdict.py`, not from here or from memory.** The corpus is 8,142 hulls,
> 8,132 distinct name-and-id pairs and 8,054 distinct names, and every statistic is computed over
> one of the three. Naming which one is not pedantry — three pages of this documentation once
> carried three different figures for the jump drive's share of population waste, and all three were
> arithmetically correct over three different populations.

### The datasets

| Dataset | Covers | Does not cover |
| --- | --- | --- |
| `out/corpus-2026-08-21/` | 8,132 ships × 5 scenarios = 40,660 outcomes | any atmosphere, any motion, any cooling fitted |
| `out/census-2026-08-21/` | 8,141 ships screened, 109,312 composition rows | anything the solver does |
| `out/knobs-2026-08-21/` | 49-ship panel × 20 dials × 4–6 levels = 12,700 rows | any two dials at once |
| `out/blockheat/blockheat.csv` | 323 block types from the definitions alone | any hull, any arrangement |

All five corpus scenarios — `idle`, `vacuum-sunlit`, `full-electrical`, `burn-forward`, `recovery` —
are **in vacuum**. The knob sweep is the only dataset with air, wind or motion in it, and it runs on
49 ships rather than 8,132.

Two caveats apply to every figure below:

* **The 2026-08-21 dataset carries 50 duplicate rows.** A resumed run re-emitted the batch it was
  interrupted in, because the skip was counted in files while the writing was per ship, so ten ships
  are written twice. `verdict.py` drops them and prints the count. It is a tenth of a per cent and
  changes no finding, but a population statistic that silently double-weights part of its population
  is the failure this lab exists to prevent. The resume is now a record of finished blueprints
  rather than a count, so nothing collected since can carry them.
* **Peaks past critical are not physics.** The harness never destroys an overheating block, so a
  ship past critical keeps generating for the rest of the clock; the 541,648 K reading is 1,800 s of
  an undamped source. Crossing times and shares are unaffected. See
  [known-issues.md](known-issues.md#deliberate-limits).

### The shape, in one table

Deduplicated, shipped settings, `HeatTimeScale` 225. Seconds are simulated seconds; at
`SimulationSpeed` 1 they are the seconds a player waits.

| Scenario | peak p50 | critical | s to critical p50 | settle p50 | substeps p50/p95/p99 |
| --- | ---: | ---: | ---: | ---: | --- |
| `idle` | 178 K | 0.22% | 104.5 | 1,560 | 2.31 / 3.25 / 3.58 |
| `vacuum-sunlit` | 247 K | 0.22% | 102.7 | 600 | 2.34 / 3.25 / 3.62 |
| `full-electrical` | 632 K | 43.2% | 8.9 | 300 | 2.36 / 4.30 / 6.02 |
| `burn-forward` | 1,087 K | 67.4% | 5.6 | 120 | 2.38 / 3.80 / 4.89 |
| `recovery` | 207 K | 3.0% | 4.0 | 1,320 | 2.31 / 3.25 / 3.63 |

**The distribution is bimodal, not spread.** A ship either sits near ambient indefinitely or runs
away in under ten seconds, and the 2–5 minute window the balance target asks for lands in the gap.

### The compatibility floor holds

`G7` asks that a ship the game spawns survives arriving. Every prefab in the install — 705 files,
461,428 blocks — run idle in the environment its category spawns into, for five simulated minutes:

| | Prefabs | Cross critical | Lose a block |
| --- | ---: | ---: | ---: |
| **arriving** (idle) — this is `G7` | 705 | **0** | **0** |
| flown hard (everything on) — the control | 705 | 630 | **616** |

**The second row is what makes the first one a measurement.** A floor that can only ever pass has
not been tested, and running the same ships under load is how this one is shown to be able to fail.
The distance between the two rows is the whole of what the mod is for: a ship that arrives is safe,
and a ship that is worked is not.

**Where the failures land under load** is by category, and it is not uniform: 22 of 23 cargo ships,
45 of 47 drones and 2 of 2 respawn ships lose a block, against 28 of 41 unknown signals and 32 of 46
planetary encounters. The planetary ones are the mildest, which is convection doing its work.

**Two things the walk cannot see, and they are part of the result.** 1,503 blocks are subtypes the
installed game no longer defines — coloured legacy armour, every one in `LegacyContent` — so they
are not simulated at all. And one prefab is stored gzipped; the parser un-gzips it now, and before
that it was one ship the floor had never been measured on.

Run it with `dotnet run --project tests/Thermodynamics.Sim -- prefabs`, and `--load` for the
control. `PrefabWalk` holds both in the suite over a stride of 140.

### How fast a ship crosses critical

Of the ships that cross critical at all, seconds from the start of the run to the crossing —
computed over the deduplicated outcomes, counting only ships that cross:

| Scenario | Ships crossing | p10 | Median |
| --- | ---: | ---: | ---: |
| `idle` | 18 | 17.6 s | 104.5 s |
| `full-electrical` | 3,516 | 3.5 s | **8.9 s** |
| `burn-forward` | 5,481 | 2.9 s | **5.6 s** |

**These are seconds of play, not of physical time.** `HeatTimeScale` is 225, so nine seconds at the
controls is about thirty-four minutes of real heating. That compression is the point of the dial —
heat is meant to happen on a human scale — and this is the first measurement of where it put the
hottest designs.

**Lowering the dial is not the answer**, because it moves the inert ships too and the spread between
the two groups is only about twelvefold. What separates them is per-block: a producer whose waste
watts are large against its own heat capacity crosses critical almost immediately whatever the clock
says.

**The crossing is not the damage, and for a long time this page said it was.** It is the moment the
damage rate leaves zero — the solver's rule is `(T − critical) × OverheatDamagePerKelvin`, which at
the crossing is nothing at all. What a player loses is a block, and that is the section below.

### How long a block has after it crosses

Measured on `out/corpus-2026-08-23-loss`, 798 ships, the first dataset to carry
`seconds_to_first_loss` — the figure the harness has computed since 2026-08-22 and no run had
recorded. Paired per ship before the difference is taken (`E6`), because the gap between two
percentiles of two populations is not a percentile of the gap:

| Scenario | Crossing p50 | First loss p50 | Own gap p10 | Own gap p50 | Own gap p90 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `full-electrical` | 9.0 s | **37.0 s** | 7.7 s | **24.2 s** | 54.8 s |
| `burn-forward` | 5.5 s | **23.0 s** | 4.2 s | **13.0 s** | 66.6 s |

**Add the warning's own lead and the window is what the goal asked for.** `HeatWarning` sounds three
seconds before the crossing, so the median ship under full electrical load gives its pilot **40
seconds** between the first cue and the first block gone, and 26 seconds in a hard burn. At p10 it
is 18 and 12 seconds. `G5`'s rationale — *a player must be able to react to a warning* — holds when
it is measured against the event that costs the player something.

**The crossing still predicts the loss**, which is what makes it a fair thing to warn on: of the
ships that cross, 98.7 % under full electrical load and 97.0 % in a burn go on to lose a block
before the clock runs out. It arrives about four times too early, not wrongly.

**Why the sample is a population figure.** 798 ships against the 8,054 of the full survey, taken as
a stride through the size-sorted corpus, and it reproduces the run it is a sample of on the quantity
both measure: `burn-forward` crossings at p10/p50/p90 of 2.8/5.5/30.3 s against 2.9/5.6/31.0 s, and
`full-electrical` at a median of 9.0 s against 8.9 s. The one place it does not is `idle`, where two
ships cross rather than eighteen and nothing can be read from either.

**The damage dial is the reason the window exists, and it was not chosen for the rule it now runs
under.** The solver used to apply the whole overshoot as damage on *every update*, so at the shipped
`Frequency` of 8 it bit eight times harder than it does now — and `OverheatDamagePerKelvin` was
authored under that rule. Re-running the 72 shipped types that cross at eight times the dial says
what restoring their authored intent would cost:

| Damage per kelvin | p10 | Median | p90 |
| --- | ---: | ---: | ---: |
| as shipped, per simulated second | 8.7 s | **25.1 s** | 238.4 s |
| as authored, per step at `Frequency` 8 | 2.8 s | **6.5 s** | 35.3 s |

**So the authored values stay.** Under the rule they were written for the median block's entire life
past its rating is six and a half seconds, which is the failure `C11` was opened for and `G5`
forbids. This is recomputed rather than scaled, because the dial is not a square root: a block that
settles just over its limit grinds down at a constant rate, so the slow end loses 6.8× where the
fast end loses 3.1×. Pinned by `TheAuthoredDamageRuleWouldPutTheWholeEventInsideTenSeconds`.

The per-block-type counterpart is [`BlockHeatIndex.SecondsFromCriticalToLoss`](../tests/Thermodynamics.Harness/BlockHeatIndex.cs),
which integrates the same damage rule against a block's own hit points with the block alone in the
dark: of the 72 shipped types that cross at all, the median survives **24.7 s** past its rating and
the tenth percentile 8.7 s. A ship lasts longer than its worst block because its neighbours are
taking heat off it, and shorter than its median block because the first loss is the fastest of
thousands.

### One block type decides the load criterion

The jump drive's effect on the outcome is binary, and the figures depend on which population is
meant:

| Population | Blocks | Ships | Share of full-load waste | Per block |
| --- | ---: | ---: | ---: | ---: |
| `LargeJumpDrive` alone | 10,567 | 2,183 | **67.1%** | 4.80 MW |
| all `JumpDrive` subtypes | 10,954 | 2,253 | **71.3%** | — |

Read over the 8,132 deduplicated `full-electrical` outcomes, with membership taken from
`composition.csv` rather than inferred from whichever block ended up hottest:

| | Ships | Reach 400 K | Lose a block |
| --- | ---: | ---: | ---: |
| Carrying any jump drive | 2,249 | **100.0%** | **100.0%** |
| Carrying none | 5,883 | 65.5% | 21.5% |

**The mechanism is the load state rather than the block.** `ShipLoad.State.Full` sets `Tools = 1`,
so every drive on the hull charges at its full draw for the whole 1,800 s clock. A real drive
charges and then stops. At idle the two groups are indistinguishable — 0.2% critical either way,
medians of 179 and 177 K — because a jump drive is classed as a tool and tools are off when parked.

G2 is not invalidated: it holds at **65.5% on ships with no drive at all**, well past its 20% gate.
But "44% of the corpus loses a block under load" is really "every jump-drive ship, plus 22% of
everyone else", and **any tuning read off the aggregate will be tuning for one block.** Whether
charging a drive *should* be a thermal event is a design question; that it should be an unsurvivable
one for every hull that mounts one is probably not.

### A global scale is not the lever

Cutting every waste fraction by half moves the median hull's peak from 1,382 K to 1,177 K on the
panel — a 15% change that leaves both populations exactly where they were. The measured per-type
dials say the same:

| Dial | Effect on `burn-forward` peak p50 across its whole sweep |
| --- | --- |
| `thruster-waste` 0.10 → 2.00 | 652 K → 1,448 K — **the strongest dial in the set** |
| `consumer-waste` 0.25 → 4.00 | 776 K → 1,736 K |
| `producer-waste` 0.25 → 4.00 | 1,177 K → 1,240 K |
| `reactor-waste` 0.50 → 8.00 | 1,205 K → 1,242 K — **very nearly inert** |
| `engine-conductivity` 1 → 8 | 1,205 K → 1,204 K — **inert** |

`reactor-waste` moving sixteen-fold for a 3% change in outcome is worth noting on its own: the
reactor retune argued above is not a population-level lever.

### What actually causes a hot spot is local watts per local square metre

The census measures arrangement, not just totals: burial depth in conduction hops out from the
nearest radiating node, the Clark-Evans nearest-neighbour index of the heat sources, watt-weighted
spread about their centroid, and the worst watts-per-exposed-area found in any 5×5×5 neighbourhood.
Correlated against the hot spot (peak minus mean) **with total waste heat partialled out** — which
has to be done, because every raw correlation here is mostly "this ship makes more heat":

| Term | Partial ρ vs hot spot | vs peak |
| --- | ---: | ---: |
| worst local W/m² | **+0.369** | **+0.552** |
| spread of sources about their centroid | −0.216 | −0.406 |
| conductance away from the hottest source | −0.137 | −0.116 |
| mean burial depth of the sources | +0.111 | +0.018 |
| **Clark-Evans clumping index** | **−0.033** | **+0.076** |

**The clumping index does not work, and that is worth recording.** It measures how evenly spaced the
sources are, and evenness is not the mechanism: two 5 MW generators ten metres apart score as
dispersed and still cook the bay they share. Local power against local radiating area is what a
block actually experiences, and it is an order of magnitude more predictive.

Banded, the local figure is a design rule rather than a correlation — share of the 8,132
`full-electrical` ships losing a block:

| Worst local W/m² | Ships | Lose a block | Median peak |
| --- | ---: | ---: | ---: |
| under 500 | 2,033 | 0.5% | 184 K |
| 500 – 2,000 | 1,202 | 6.9% | 545 K |
| 2,000 – 10,000 | 2,356 | 42.7% | 656 K |
| 10,000 – 50,000 | 1,447 | 93.2% | 1,287 K |
| 50,000 – 200,000 | 465 | 95.5% | 1,700 K |
| over 200,000 | 629 | 98.9% | 2,541 K |

**Spreading the generators helps, up to a point.** Within matched bands of total waste heat, the
most-spread third against the tightest third: at 100 kW–1 MW, 21.7% lose a block against 45.2%; at
1–10 MW, 61.6% against 91.5%. Past about 10 MW installed it stops mattering — 98% to 100% either way
— so arrangement buys a builder a great deal in the middle of the range and nothing at the top. The
raw banding of spread says the opposite, because spread correlates with hull size and so with total
heat; **it has to be read within a waste band or not at all.**

The failure is per-block, not per-hull: the hot spot is a median of **4 blocks on a median
1,110-block ship**, and of the 436 ships that go critical in under five seconds under full
electrical load, **82.3% do so on a `LargeHydrogenEngine`**.

### One number says whether a block can survive itself

`BlockHeatIndex` computes, from the definition alone and with no simulation, the heat a block makes
at full rating over the most it could possibly shed while staying at its own critical temperature.
The denominator is deliberately the best case that exists — every face radiating to deep space *and*
every face bolted to armour held at ambient, both at once.

* **index** — against radiation and conduction together. Above 1 the block is impossible: no hull,
  no arrangement and no cooling saves it, and only the definition can be changed.
* **self index** — against its own skin alone. Above 1 the block cannot cool itself and depends
  entirely on exporting into the hull, which is a much weaker position than it sounds.

**Four of the game's 323 heat-making blocks are impossible, and 72 cannot cool themselves.** Banded
against the corpus, the index predicts the outcome well enough to use as a gate:

| Worst index on the ship | Ships | Lose a block |
| --- | ---: | ---: |
| under 0.25 | 3,458 | 8.8% |
| 0.25 – 0.50 | 1,639 | 47.0% |
| 0.50 – 0.75 | 2,059 | 84.6% |
| 0.75 – 1.00 | 39 | 100.0% |
| over 1 | 799 | 82.6% |

**0.5 is the practical threshold and 1.0 the theoretical one.** `BlockHeatIndexTests` gates on the
set of blocks above 1, so a new block or a changed waste fraction that crosses the line fails the
suite.

**`LargePrototechReactor` is a definition accident and the worst block in the game.** The game gives
it the TypeId `HydrogenEngine`, so the derivation charges a 400 MW plant a combustion engine's 0.60
waste fraction: **240 MW of heat out of a 3×2×2 block, an index of 17.9 and a self index of 46.** At
the reactor family's own 0.01 it would make 4 MW and be unremarkable. This is the same shape as the
reactor-waste defect above — the derivation keys on the type, and the type is decided by the game's
wiring rather than by what the block is called. It needs a per-subtype override.

**The jump drive is not impossible, and that correction matters.** Its index is 0.75: bolted to
armour that stays at ambient it survives, because superconductors conduct extremely well. Its *self*
index is 7.4 — its own skin sheds an eighth of what it makes — so it lives or dies on the hull
taking the rest. **The hull never does.** Of the corpus ships carrying one, 909 have more than the
19,068 m² of 400 K armour the index says is needed to absorb its export, and **all 909 still lose a
block**, because total hull area is irrelevant when the heat cannot travel: block-to-block
conductance is about 112 W/K, so moving megawatts even one block needs thousands of kelvin. **Area
has to be *near* the source to count.**

### Cooling has to remove watts, not add area

The model behaves as clean radiative equilibrium, so **T scales as the fourth root of area**:

* `exposed-surface` ×2 moves the panel's `burn-forward` peak 1,205 K → 1,019 K, a 15.4% drop against
  an ideal radiative prediction of 2^−0.25 = 15.9%.
* `emissivity` ×2 gives 1,020 K — the same curve. The two dials are the same dial.

A fourth-root law cannot produce a moderate retrofit: the first radiator does almost nothing and the
hundredth does less. **A cooling block that matters has to remove watts linearly** — a loop that
carries heat somewhere, or a pump with a rating.

Priced against the population, one `Gauge_LG_Radiator` (1×5×2 cells, 265.6 m² effective, emissivity
0.35) sheds:

| Panel temperature | 400 K | 500 K | 600 K | 700 K | 800 K |
| --- | ---: | ---: | ---: | ---: | ---: |
| Watts shed | 96 kW | 291 kW | 644 kW | 1.23 MW | 2.12 MW |

and the population needs, at a 600 K panel:

| Ship | Full-load waste | Radiators to break even |
| --- | ---: | ---: |
| p50 | 337 kW | **0.5** |
| p75 | 5.94 MW | 9.2 |
| p90 | 20.5 MW | 31.8 |
| p99 | 125 MW | 194.6 |

57.7% of ships need one or fewer; 19.2% need more than sixteen. **The retrofit is either unnecessary
or impossible, with almost nothing in between**, and the factor of 17.6 between p50 and p75 is the
jump drive again. Fixing the jump drive is what creates the middle of that distribution — nothing
else in the census moves it.

### No block lands in the 2–5 minute window, and none can be made to at `HeatTimeScale` 225

Of the 72 block types that generate more heat than their own skin can shed, time from 293 K to their
own critical temperature, alone:

| Band | Blocks | Share |
| --- | ---: | ---: |
| under 10 s | 15 | 20.8% |
| 10–60 s | 54 | 75.0% |
| 60–120 s | 3 | 4.2% |
| **120–300 s (the target)** | **0** | **0%** |
| over 300 s | 0 | 0% |

Median 16.9 s, worst 0.5 s, best 87.1 s.

**Waste fractions cannot fix this.** Solving for the waste heat that would produce a 180 s crossing,
per block, the required self index is **1.000 to three decimals for 90% of them** — the band between
"never overheats" and "overheats in under a minute" is about a tenth of a per cent wide. That is a
direct consequence of T⁴: radiated power rises so steeply that a block is either comfortably under
its limit or running away.

The dial that does reach the window is the one that sets capacity, and the scaling is exactly
linear:

| `HeatTimeScale` | corpus `full-electrical` s-to-critical p50 | vacuum substeps p50 |
| ---: | ---: | ---: |
| 225 (shipped) | 8.9 | 2.41 |
| 112 | ~17.8 | 1.21 |
| 56 | ~35.8 | 0.60 |
| 25 | ~80 | 0.27 |
| **~11** | **~180** | **~0.12** |

**But one clock governs two time constants about a hundredfold apart.** A hot block's rise is set by
its own small mass; a hull's response to sun or air is set by its whole mass. At 225 the block
crosses in 17 s while the hull takes 1,560 s to settle at idle. Dividing the clock by 20 puts the
block in the window and pushes the hull to eight hours.

Closing that gap needs conduction, which couples the block to the hull's mass:

| `conductivity` | `burn-forward` s-to-critical p50 | peak p50 | substeps p50 |
| ---: | ---: | ---: | ---: |
| 0.25 | 4.2 | 1,296 K | 0.77 |
| 1.00 (shipped) | 4.8 | 1,205 K | 2.75 |
| 2.00 | 9.2 | 1,107 K | 5.23 |
| 4.00 | 13.5 | 1,004 K | 9.93 |

Substep demand is conductance over capacity, so it is proportional to `conductivity ×
HeatTimeScale`. **Raising conduction and lowering the clock together moves the crossing into the
window at constant or lower cost:** conductivity ×4 with `HeatTimeScale` ~15 projects to a ~200 s
crossing at ~0.7 substeps in vacuum, against 4.8 s at 2.75 today.

**That projection was an extrapolation across an interaction the sweep never measured**, it has now
been measured, and **it was wrong by a factor of five**: conductivity ×4 with `HeatTimeScale` 15
gives a 991 s crossing, not ~200 s. What was wrong was not the composition rule — the two dials do
multiply, to within 1 % — but the two curves fed into it, which came from different scenarios and
from a statistic taken over the ships that crossed rather than over the ships that were loaded. The
grid that replaces it is below.

### Two dials at once: the window is reachable at conductivity ×4 with the clock near 100

Twenty-five cells of conduction against the clock, on the forty-hull retest set, each scenario's run
length stretched by the clock ratio so every cell is stopped the same way (`M1`). `PairSweep` runs
it and `tools/corpus/pairs.py` scores it.

**`G8` is satisfied, and by four cells.** Under sustained full electrical load the median hull
crosses critical inside the 120–300 s window while a hull driven to full burn and throttled back
recovers inside the hour:

| `conductivity` | `HeatTimeScale` | crossing p50 | recovery p50 | substep demand | G1 | G2 | G5 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 120 | 124.0 s | 2,220 s | 2.04× shipped | 0 % | 100 % | 100 % |
| 4 | 100 | 148.9 s | 2,640 s | 1.70× | 0 % | 100 % | 100 % |
| 4 | 90 | 165.4 s | 2,940 s | 1.53× | 0 % | 100 % | 100 % |
| 4 | 80 | 186.0 s | 3,270 s | 1.36× | 0 % | 100 % | 100 % |

None of the four breaks a criterion it has to keep: no hull goes critical at idle, every hull gets
warm under load, and every hull comes back. **The price is 1.36–2.04× the shipped substep demand**,
which is what `G6` is about and is a price rather than a bound — whether it is affordable is a
question about the shipped caps and the tail, not about the median in that column.

**Both dials behave exactly, which is why four cells were enough to find.** Two curves came out of
the twenty-one cells that ran first, and each holds across every cell that has a median:

* **The crossing goes as one over the clock.** `crossing × clock` is a constant per conductivity —
  2,302 at ×1 over six cells, 5,418 at ×2 over three, 14,880 at ×4 over nine — held to within
  1.5 %, 0.4 % and 0.3 %. So a conductivity *is* that constant, and the clocks that put it in the
  window are the constant divided by 300 and by 120.
* **Recovery is set by the clock and barely moves with conduction.** At the shipped clock it is
  1,320 s at ×1 and 1,230 s at ×8; the bound of an hour falls between clock 70 (3,660 s) and clock
  80 (3,210 s) whatever the conduction.

The two bands overlap at exactly one conductivity. ×1 wants a clock of 8–19 and ×2 wants 18–45,
both far under the clock recovery needs; ×4 wants 50–124, which reaches it. The four cells above are
that overlap, and their crossings were predicted from the constant to within 0.2 % before they were
run.

**Conductivity ×8 is out, and not because it is slow.** At ×8 only 13 of 40 hulls ever reach
critical, so there is no median crossing time at any clock — the 50th percentile of the population
sits in the censored tail. Conduction spreads a block's heat into the hull it is bolted to, and past
some point the hull absorbs the whole event. That is a defensible mod and it is not the one `G8`
describes, so the rung is excluded by the criterion rather than by cost. The share is a property of
conduction alone: 72 % at ×1, 68 % at ×2, 62 % at ×4, 32 % at ×8, flat across every clock.

**This corrected a scoring defect before it corrected the answer.** The first reading of the grid
took the crossing median over the hulls that crossed, which reported two ×8 cells as satisfying
`G8` — a cell where two thirds of the population never overheats read as the grid's best. Read as
`E9` requires, with a hull that never crossed censored above rather than dropped, none of them does.
The rule is pinned by `tools/corpus/test_scoring.py`.

**What this is not.** It is a measured configuration, not a shipped one. It is taken on the forty
hulls of the retest set rather than on the standing panel, so it is not the published single-dial
rows re-derived (`M1`); what is compared is the interior of this grid against its own edges.
Retuning to it would move every temperature figure in this repository, and the cost column has been
read at the median only. See [backlog.md](backlog.md) `C12`.

### Air costs about six times what vacuum costs

In vacuum, substeps are cheap: corpus p99 is 6.02 against 64 granted. In air they are not, and the
corpus never measured air. On the 49-ship panel at shipped settings:

| Environment | substeps p50 | p95 |
| --- | ---: | ---: |
| `vacuum-shadow` | 2.41 | 3.56 |
| `surface-cold-night` | 12.59 | 14.78 |
| `surface-hot-noon` (still air) | 14.15 | 16.66 |
| `surface-windy` (60 m/s) | 23.28 | 27.61 |
| `storm-parked` (100 m/s) | 25.94 | 30.81 |
| `reentry` (200 m/s) | 30.87 | 36.71 |

That is the responsiveness budget, and it is spent on convection rather than on heat.

### The 300 m/s constraint

Vanilla caps a grid at 100 m/s; the servers this mod is played on commonly run 300. The two terms
scale in opposite directions, and that is the whole finding:

* **Friction goes as the cube of airspeed.** 100 → 300 m/s is **27×** the friction heat.
* **Forced convection saturates.** `h = h₀ × (1 + 0.1 √v)`, so the same change moves cooling from
  2.00× still air to 2.732× — **1.37×**.

Tripling the speed limit therefore multiplies the heating term by 27 and the cooling term by 1.37.
Whether that destroys atmospheric flight is set by `FrictionScale`, and friction is not the binding
term at 100 m/s: sweeping `FrictionScale` 0 → 4 moves the `flight-100` peak only 856 K → 878 K,
because convection dominates. At `reentry` it does bite: 300 K → 521 K over the same sweep.

Substep demand is linear in the convection coefficient. Fitting the two measured points at h=1
(`surface-hot-noon`) and h=2 (`storm-parked`) and testing against the held-out 200 m/s `reentry`
measurement:

| | Fit | Predicted at 200 m/s | Measured at 200 m/s | Error | **Projected at 300 m/s** |
| --- | --- | ---: | ---: | ---: | ---: |
| p50 | 2.35 + 11.79h | 30.83 | 30.87 | −0.13% | **34.58** |
| p95 | 2.50 + 14.15h | 36.67 | 36.71 | −0.11% | **41.17** |

**300 m/s demands about 35 substeps at the median and 41 at p95, against `MaxSubsteps` 64.** It
fits, with roughly a third of the budget in reserve, at about 15× the vacuum cost. This is a
projection from a validated fit, not a measurement; `flight-300` and `storm-300` exist in
[`Battery.All()`](../tests/Thermodynamics.Harness/Battery.cs) to measure it.

---

## What the data settles, and what it does not

**Settled.**

* **Design decides more than size.** Spearman rank correlation of peak against `w_per_m2` is +0.56
  at idle and `local_w_per_m2_max` +0.89 under full load, against +0.26 and +0.49 for block count.
  G4 holds.
* **The failure is per-block, not per-hull**, and it strands in one or two block types.
* **Timing is linear in `HeatTimeScale` and unaffected by anything that changes equilibrium**;
  equilibrium is unaffected by `HeatTimeScale`. The two axes are cleanly separable.
* **G1, G2, G5 and G6 hold on shipped defaults.**

**Not settled.**

* **G3 has never been measured.** No corpus ship carries a cooling block — the filter rejects
  non-vanilla blocks — so the single highest-value missing run is the retrofit pass, and two of the
  five balance goals depend entirely on it.
* **No dial has been measured against another.** Every knob row moves one thing, and the
  conduction/clock pair the timing goal depends on is unmeasured.
* **The corpus has never seen air.** Five vacuum scenarios on 8,132 ships; air only on the 49-ship
  panel, so every atmospheric statement here rests on 49 hulls.
* **`full-electrical` charges every jump drive continuously**, so the scenario is a bound rather
  than a steady state — and since drives are 71.3% of population waste, that choice sets most of the
  shape of the load results.

### Open items in the block report

* **The harness reactor and the shipped reactor disagree.** `Catalog.ReactorThermal` carries
  `ProducerWasteEnergy` 0.25 against the shipped 0.01, so any scenario quoting a reactor temperature
  quotes one no player will see. `Vanilla.Reference` derives from real build costs and is the right
  model for `Catalog` to follow.
* **`Catalog` masses are not the shipped masses.** Measured and pinned by `CatalogDriftTests`: four
  of the six blocks the catalogue stands in for are out by more than five per cent, and the worst is
  **4.32×** — `Thruster` at 10,000 kg against a real 43,200 — with `Battery` at 3.70× because it
  carries the *small-grid* battery's 1,040 kg under a large-grid name, and the two reactors at 1.60×
  and 1.42×. Armour matches exactly, which is what makes the rest a measurement rather than an
  artefact of the comparison. They affect scenarios rather than this report, which reads the
  definitions directly, but every scenario temperature is quoted off them — and **the fix moves all
  of those at once**, so it is a pass of its own rather than a line in another one.

---

## Reproducing this

```bash
python3 tools/corpus/verdict.py out/corpus-2026-08-21     # criteria and distributions
tools/corpus/build-report.sh                              # the browsable page
THERMAL_CORPUS_DATA=$PWD/out/blockheat dotnet test \
  tests/Thermodynamics.Tests/Thermodynamics.Tests.csproj \
  --filter BlockHeatIndexTests                            # block index, with pace
```

See [tools/corpus/README.md](../tools/corpus/README.md#the-five-ways-a-full-sweep-dies) for the
five ways a full sweep dies, and [backlog.md](backlog.md) for what is still open.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | **Measured the two dials together and found the significance window**, which is `C12` and the one thing every sweep before it could not answer: `G8` is satisfied by conductivity ×4 at `HeatTimeScale` 80–120, crossing at 124–186 s and recovering in 2,220–3,270 s, at 1.36–2.04× the shipped substep demand and with `G1`, `G2` and `G5` all kept. **Corrected the projection this page carried**, which put the window at ×4 with the clock near 15 — measured, that cell crosses at 991 s, five times its prediction, because the two curves multiplied together came from different scenarios and from a median over the ships that crossed. The composition rule itself holds to 1 %. **Conductivity ×8 is excluded by the criterion rather than by cost**: only 13 of 40 hulls ever cross there, so the population has no median crossing at any clock. |
| 2026-08-23 | **Measured what the real-unit conversion did to the ships people fly**, which is what was left of `C2`. Forty retest hulls through seven scenarios in the shipped world and four counterfactual ones recovered exactly from `Cubes.xml` at `4f6b44a^`: a burning hull is **+34.7 K** hotter than before the conversion and a loaded one **−16.3 K** cooler, the arms attribute each to one family, and **no criterion moves** — G1, G2 and G5 are identical in all five worlds. What changed for a player is the spread rather than the peak: under load the peak fell 16 K and **192 more** blocks went over critical, under thrust the peak rose 35 K and **257 fewer** did. Also corrected the published per-block table, which described only the four families `Cubes.xml` authors and said *thrusters went 0.6×* — true of the hydrogen ones alone, against **0.23×** for ion and **1.27×** for atmospheric. |
| 2026-08-23 | Settled one of `C2`'s three claims with a measurement rather than a retune: `OverheatDamagePerKelvin` was authored for the per-step damage rule, which at `Frequency` 8 bit eight times harder, and restoring that intent would put the median block's whole life past its rating at **6.5 s**. The authored values stay. |
| 2026-08-23 | Answered the damage-timing question with the event that costs a player something. The section had been called *"damage arrives too fast to be played around"* and quoted a **median 8.9 s** that is the *crossing* — the moment the damage rate leaves zero. The first block is lost at a median **37.0 s** under full electrical load, the median ship's own crossing-to-loss gap is **24.2 s**, and with the cue's three-second lead the window is 40 s. `G5`'s rationale holds. Split into [the crossing](#how-fast-a-ship-crosses-critical) and [the loss](#how-long-a-block-has-after-it-crosses), which is the section `BlockHeatIndex` has cited since it was written and which did not exist. |
| 2026-08-22 | Measured the catalogue drift this page had recorded as *up to 4×*: it is **4.32×** at worst, on the large thruster, and four of six blocks are out rather than three. `CatalogDriftTests` pins all of it, armour included — matching exactly is what makes the rest a measurement ([backlog.md](backlog.md) `C4`). |
| 2026-08-22 | Added [The compatibility floor holds](#the-compatibility-floor-holds). Every one of the 705 prefabs the game ships has now been simulated — the criterion `G7` was written down first ([balance-lab.md](balance-lab.md)) — and none loses a block arriving, against 616 of 705 that do when flown hard. |
| 2026-08-22 | Restored the damage-timing finding, which the merge of `corpus-shape.md` and the register had dropped, and corrected its idle figure: the median time to critical at idle is **104.5 s**, matching this page's own shape table, where the prose had carried 112 s — one of the eighteen values rather than their median. |
| 2026-08-22 | Merged `corpus-shape.md` and the corpus findings from `known-issues.md` into this page, so the block-level argument and the population that tests it sit together. **Corrected the jump-drive figures**, which stood at three different values across three pages: recomputed from `composition.csv` and `outcomes.csv`, `LargeJumpDrive` alone is 10,567 blocks on 2,183 ships and 67.1% of full-load waste, while all `JumpDrive` subtypes together are 10,954 on 2,253 and 71.3% — both previous figures were right over populations neither page named. The outcome split is 2,249 ships carrying one against 5,883 without, at 100.0% and 65.5% reaching 400 K; the register had 2,255/5,887 and quoted G2 as both 67.1% and 65.6% in adjacent paragraphs. **Corrected the local-W/m² banding table**, which dropped 894 of 8,132 ships and omitted the 50,000–200,000 band entirely. The block-index tables, the cooling ladder and the reactor sweep were re-verified against the datasets and are unchanged. |
| 2026-08-22 | Fitted cooling to ships people actually built, and said which half of the cooling criterion the ladder answers and which it does not. |
| 2026-08-21 | Read the first complete corpus survey — 8,142 ships — for balance: the bimodal distribution, the per-block failure, the local-W/m² rule, and the block heat index as a gate. Recorded that the clumping index does not predict hot spots and the local figure does. |
| 2026-08-20 | Measured the reactor waste fraction in two bracketing rigs rather than picking it by analogy, and settled on 0.01. Added the whole-game cooling ladder, which found nothing in the game solves a reactor by being bolted to it. |
| 2026-08-19 | Opened the block report: capacity, shedding, the joint and delivered steady state, each costed against the vanilla block it displaces. Established that a coolant sink face carries six times what a bolt joint does. |
