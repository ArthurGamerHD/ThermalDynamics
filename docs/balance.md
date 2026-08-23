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

### Damage arrives too fast to be played around

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

This is a balance decision rather than a defect, and it is the one the survey most clearly asks for.
It is also the sharpest tension with the stated goals: `G5` holds as written — recovery is bounded —
while the reason given for it, *"a player must be able to react to a warning"*, does not survive a
median of 8.9 seconds. See
[document-of-intent.md](document-of-intent.md#where-the-goals-and-the-code-disagree).

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

**That projection is an extrapolation across an interaction the sweep never measured.** Every
configuration in `knobs.csv` moves one dial. The pair has to be run before it is trusted.

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
* **`Catalog` masses are not the shipped masses.** The harness's hand-written stand-ins are up to 4×
  out — `Battery` 1,040 kg against 3,845; `Thruster` 10,000 kg against 43,200; `Radiator` 900 kg
  against 600. They affect scenarios rather than this report, which reads the definitions directly,
  but every scenario temperature is quoted off them.

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
| 2026-08-22 | Restored the damage-timing finding, which the merge of `corpus-shape.md` and the register had dropped, and corrected its idle figure: the median time to critical at idle is **104.5 s**, matching this page's own shape table, where the prose had carried 112 s — one of the eighteen values rather than their median. |
| 2026-08-22 | Merged `corpus-shape.md` and the corpus findings from `known-issues.md` into this page, so the block-level argument and the population that tests it sit together. **Corrected the jump-drive figures**, which stood at three different values across three pages: recomputed from `composition.csv` and `outcomes.csv`, `LargeJumpDrive` alone is 10,567 blocks on 2,183 ships and 67.1% of full-load waste, while all `JumpDrive` subtypes together are 10,954 on 2,253 and 71.3% — both previous figures were right over populations neither page named. The outcome split is 2,249 ships carrying one against 5,883 without, at 100.0% and 65.5% reaching 400 K; the register had 2,255/5,887 and quoted G2 as both 67.1% and 65.6% in adjacent paragraphs. **Corrected the local-W/m² banding table**, which dropped 894 of 8,132 ships and omitted the 50,000–200,000 band entirely. The block-index tables, the cooling ladder and the reactor sweep were re-verified against the datasets and are unchanged. |
| 2026-08-22 | Fitted cooling to ships people actually built, and said which half of the cooling criterion the ladder answers and which it does not. |
| 2026-08-21 | Read the first complete corpus survey — 8,142 ships — for balance: the bimodal distribution, the per-block failure, the local-W/m² rule, and the block heat index as a gate. Recorded that the clumping index does not predict hot spots and the local figure does. |
| 2026-08-20 | Measured the reactor waste fraction in two bracketing rigs rather than picking it by analogy, and settled on 0.01. Added the whole-game cooling ladder, which found nothing in the game solves a reactor by being bolted to it. |
| 2026-08-19 | Opened the block report: capacity, shedding, the joint and delivered steady state, each costed against the vanilla block it displaces. Established that a coolant sink face carries six times what a bolt joint does. |
