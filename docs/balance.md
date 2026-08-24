# Balance

What every block this mod ships is worth against the vanilla blocks it competes with, and what a
population of 8,132 real ships says about whether the balance targets hold.

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `E4` `E5` `E9` `M10`.

| Looking for | Go to |
| --- | --- |
| The design of the lab and the eight criteria | [balance-lab.md](balance-lab.md) |
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

### What a selective surface is worth

A surface is not obliged to absorb what it emits, and a radiator is the one block where that matters
most: its job is to emit in the infrared without collecting in the visible. Real ones are finished
for exactly that — a second-surface mirror runs `α ≈ 0.08` against `ε ≈ 0.8`, white paint about 0.2
against 0.9. The mod's radiator was authored at `ε 0.35` with no absorptivity, so it absorbed a
third of the sunlight that landed on it.

Measured on a source under eight radiators, 75 kW of heat, sun across the stack against the same rig
in shadow — `dotnet run --project tests/Thermodynamics.Sim -- bench surface`:

| Surface | ε | α | Sunlit | Shadow | The sun costs |
| --- | ---: | ---: | ---: | ---: | ---: |
| shipped, before | 0.35 | *follows* | 355.6 K | 319.0 K | 36.5 K |
| **shipped, now** | **0.35** | **0.10** | **344.7 K** | **319.0 K** | **25.7 K** |
| second-surface mirror | 0.80 | 0.10 | 335.3 K | 310.8 K | 24.5 K |
| emissive only | 0.80 | *follows* | 353.7 K | 310.8 K | 42.8 K |

**The selective finish is worth 10.9 K in sunlight and exactly nothing in shadow**, which is what
says the rig is measuring the surface rather than the geometry. It is authored on both radiators:
emissivity is untouched, so the block emits precisely what it emitted and stops absorbing sunlight a
real one would not.

**The last row is the one worth reading twice.** Raising emissivity alone — making the radiator a
better *emitter*, which is the obvious improvement — buys 8.2 K in shadow and **1.9 K in sunlight**,
because a better emitter that is also a better absorber gives almost all of it back: the sun's
penalty rises from 36.5 K to 42.8 K. The two changes are complementary rather than additive, and
doing only the obvious one is nearly worthless where a radiator is most often used.

**What is not authored is the emissivity.** 0.35 is low for a radiator and 0.8 is what a real one
reaches; the table says that is worth 8.2 K in shadow and 9.4 K in sun on top of the finish. That is
a balance change to the mod's own block rather than a fidelity correction, so it stays a decision
with a number on it. `SelectiveSurfaceTests` pins both halves.

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
question [backlog.md](backlog.md) `C2` held open was what that did to the balance a player meets, and
it is answered below: the two figures it turned on are censored and everything else is unmoved.

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

**Read the two bold rows as censored, because they are.** The lab never destroys an overheating
block, so from the moment anything on a hull is past its own rating the peak is 1,800 s of an
undamped source rather than a temperature (`E9`). In `burn-forward` that is **34 of the 40 hulls**
and in `full-electrical` **29 of 40** — the two rows the table sets in bold are the two where the
peak has stopped being a prediction. The censoring report in `retest.py` said 9 % of the run was
censored because it tested the peak against 1,500 K rather than against each block's own rating;
that is corrected, and it is the reason the two figures below were read as balance rather than as
artefacts.

**Where they come from, for what it is worth.** Thrusters and reactors alone account for 44.9 K of
the burn row, and the vanilla blocks gaining conductance claw about ten of that back; the load row is
the vanilla blocks entirely — the jump drive at 3.51× is the block type carrying 71.3 % of the
population's full-load waste, and it now spreads what it makes. Nothing moves at idle, in sun, or in
recovery, because none of those is a hull with a hot block in it — and those three rows are the
uncensored ones.

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

**Both totals are dominated by single hulls and the per-hull split is a wash.** Under thrust the
shipped world is better on 14 hulls, worse on 11 and unchanged on 15, with a median of exactly zero
and one hull carrying −88 of the −257 (`P1`: a sum over a population is not a statistic about its
members).

**Every statistic that survives the censoring is unmoved**, which is the finding the peaks were
standing in front of. Shipped minus pre-conversion, medians over the forty:

| | burn-forward | full-electrical |
| --- | ---: | ---: |
| seconds to the first block over critical | **−0.13 s** | +0.75 s |
| seconds to the first block lost | **+0.50 s** | +1.13 s |
| share of blocks over critical | **0.000** | 0.000 |
| peak temperature *(censored)* | +34.73 K | −16.28 K |

The crossing and the loss are decided before anything diverges, and both are inside a second and a
half against a damage event that runs a median 37 s
([how long a block has](#how-long-a-block-has-after-it-crosses)). The share of blocks past their
rating does not move at all. **So the conversion moved the one column that is not a prediction and
left every column that is** — which is what `C2` was deciding about, and is why it is closed.

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

### The load reaches the window too, and costs the bite instead of the levers

Conduction and the clock are both *transport*. The load is not: it changes how much heat a ship
makes without changing how the heat moves. Eighteen cells of a waste-heat multiplier against the
clock, same forty hulls, same three scenarios, same stop rule — `PairSweep` runs it and
`tools/corpus/load.py` scores it.

**`G8` is satisfied by four cells, at conductivity ×1.** Waste ×0.5 with `HeatTimeScale` 110, 100,
90 or 80:

| `waste` | `HeatTimeScale` | crossing p50 | crossed | recovery p50 | ratio | G1 | G2 | G5 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0.5 | 110 | 122.4 s | 22/40 | 2,670 s | 21.8 | 0 % | 100 % | 97.5 % |
| **0.5** | **100** | **134.5 s** | **22/40** | **2,880 s** | **21.4** | **0 %** | **100 %** | **97.5 %** |
| 0.5 | 90 | 149.5 s | 22/40 | 3,180 s | 21.3 | 0 % | 100 % | 97.5 % |
| 0.5 | 80 | 168.1 s | 22/40 | 3,570 s | 21.2 | 0 % | 100 % | 97.5 % |
| *(shipped)* | *225* | *10.4 s* | *29/40* | *1,320 s* | *127.2* | *0 %* | *100 %* | *97.5 %* |

**The ratio column is the finding.** `G8` wants a crossing over 120 s and a recovery under 3,600 s,
and both go as one over the clock — so the clock slides the pair along and cannot change
`recovery / crossing`. At the shipped load that ratio is 127, and the window needs about 30 or less.
**The load is the dial that moves it**: 315 at waste ×2, 127 at ×1, 21 at ×0.5, and within each load
it barely moves across the clock at all. Conduction moves it the same way — that is why ×4 worked —
and the load moves it without touching a transport term.

**The mechanism is a change of population, not of rate.** Cutting the load makes every hull heat
more slowly *and* pushes the marginal hulls below their own critical temperature entirely, so the
set that crosses shrinks from 29 of 40 to 22 and the survivors are the slow ones near their
equilibrium. The median moves from 10.4 s to 59.9 s at the same clock because it is a median of a
different set. That is also why the deeper cuts fail: at ×0.25 only 8 of 40 hulls cross and at
×0.125 only 1, so the 50th percentile sits in the censored tail and there is no median at any clock
— the same exclusion conductivity ×8 met (`E9`).

**Both composition rules hold exactly**, which is what makes the band a band rather than four
lucky cells: `crossing × clock` is 13,455 across clocks 225, 110, 100, 90, 80 and 45 to **0.2 %**,
and `recovery × clock` is about 286,000 to 3 %. Those put the window at clock 45–112 and the
recovery bound at clock 80 or above; the four cells above are the overlap, and the interior was
measured rather than read off the fit.

**The cell would be ×0.5 / 100**, by the widest margin on its worst criterion — the same rule the
conduction grid was read with. Clock 110 sits 3 % from the window edge and clock 80 sits 1 % from
the recovery bound; 100 is 16 % clear of both. **Would be, because every row of this table is
conditional on the load case it was taken in**, and measured against the other one it does not hold
— see [neither route survives the other bound](#the-event-has-a-length-and-measured-across-it-g8-selects-the-same-cells).

#### What it costs, and it is not free either

| | Shipped | ×0.5 / 100 |
| --- | ---: | ---: |
| median peak under full load | 999.0 K | **734.5 K** |
| median blocks over critical under full load | 4 | **1** |
| hulls that ever cross critical | 29/40 | **22/40** |
| median substep demand | 5.99 | **2.53**, 0.43× |

**It costs the bite.** A median hull under sustained full load peaks 264 K cooler and loses one
block instead of four, and a quarter of the hulls that used to overheat no longer do. `G2` still
passes at 100 % — every hull still reaches 400 K — so the mod still bites; it bites less hard.

**What it does not cost is a lever.** Conductivity does not move, so a coolant sink still
out-performs every surface dial, bolting still loses, a buried reactor still cooks, and the
atmosphere still sets the stiffest block's pace. And it is **cheaper**: substep demand goes as
`conductivity × clock`, so 0.43× — which also takes the atmospheric p99 of `C19` from 115 % of the
substep cap to about 50 %.

**So the window costs something whichever dial reaches it.** Conduction buys the timing with three
of the mod's levers; the load buys it with a quarter of the damage. That is the trade, and it is now
measured on both sides rather than argued.

#### And the load route halves a number that is sourced

A waste multiplier looked like the cheaper dial partly because the fractions it scales read as
guesses. Weighted by the heat they actually carry, they are not. Every fraction in `Cubes.xml` now
states its provenance ([definitions.md](definitions.md#every-waste-fraction-says-where-it-came-from-and-most-of-them-say-invented)),
and over the 8,142-ship census at full electrical load:

| Where a loaded fleet's waste heat comes from | Share |
| --- | ---: |
| a fraction derived from the efficiency the game itself states | **76.3 %** |
| a fraction sourced to a real conversion | 8.4 % |
| a fraction somebody invented | 15.3 % |

*Population: the census of 2026-08-21, 109,312 block rows, drives restated at their derived
fractions. Basis: full electrical load with every drive charging, no thrust (`E3`).
`tools/corpus/provenance.py`.*

**So ×0.5 is an admitted balance knob and not a correction.** Three quarters of what it would halve
is `1 − PowerEfficiency` on the jump drive, which the game publishes; the mod would be declaring that
a charging drive is twice as efficient as its own definition says. That is a legitimate thing for a
`ConductionScale`-shaped dial to do, and it has to be argued as one rather than as fixing a guess.
The 15.3 % that *is* invented sits on four block types — artificial mass, the reactor, the refinery
and the assembler — and retuning those four reaches a sixth of the load, not the window.

#### The event has a length, and measured across it `G8` selects the same cells

A jump drive holds `PowerNeededForJump` 3 MWh, draws `RequiredPowerInput` 32 MW while filling and
keeps `PowerEfficiency` 0.8 of it, so it fills in **421.9 s**. Every one of those figures is read off
the definition the game ships; none is chosen here. That is the length of the largest thermal event
most ships have, and `jump-charge` is the scenario that runs it: the drives charge for 421.9 s,
finish, and the ship holds.

| | crossing p50 across the charge | crossed | recovery p50 | `G8` |
| --- | ---: | ---: | ---: | :---: |
| *(shipped)* ×1 / 225 | **10.4 s** | 29/40 | 1,320 s | — |
| the load route, ×0.5 / 110 | 122.4 s | 22/40 | 2,670 s | **pass** |
| the load route, ×0.5 / 100 | **134.5 s** | 22/40 | 2,880 s | **pass** |
| the load route, ×0.5 / 90 | 149.5 s | 22/40 | 3,180 s | **pass** |
| the load route, ×0.5 / 80 | 168.1 s | 22/40 | 3,570 s | **pass** |
| the conduction route, ×4 / 120 | 124.0 s | 25/40 | 2,220 s | **pass** |
| the conduction route, ×4 / 100 | 148.9 s | 25/40 | 2,640 s | **pass** |
| the conduction route, ×4 / 90 | 165.4 s | 25/40 | 2,940 s | **pass** |

**So `G8` can be scored, and it selects the cells the two grids already found.** The transient
reproduces the charging bound to the last figure, because every hull that crosses does so in the
first minute of a seven-minute charge — which is itself the clearest statement of the defect:
**a player who starts charging a jump drive has a block past its rating about ten seconds in, and
the charge runs for seven minutes.**

**This corrects the section below.** `full-electrical-charged` is not a bound on the event; it is a
ship not having one, and reading *fewer than half the hulls cross* there as evidence against the two
routes was reading the absence of an event as a short one. The two real bounds on the event are its
length and its absence, and the length is what `G8` is about.

**Two things this does not settle.** The ×0.5 cells cross on 22 of 40 hulls, which is barely over
the half the median needs, so the pass is not robust to a different population. And ×4 / 80 lost
seven hulls to run failures in this dataset and is reported on 33; its `full-electrical` figure of
186.0 s over 40 stands and its `jump-charge` figure does not.

#### Measured: with the drives full there is no event to time

The confound below was measured rather than left as a caveat. `full-electrical-charged` is the same
load with the jump drives full instead of charging, and every cell of both routes was run through
both cases on the same forty hulls:

| | crossing p50, drives **charging** | crossed | crossing p50, drives **full** | crossed |
| --- | ---: | ---: | ---: | ---: |
| *(shipped)* ×1 / 225 | 10.4 s | 29/40 | **censored** | 12/40 |
| the load route, ×0.5 / 100 | **134.5 s** | 22/40 | **censored** | 3/40 |
| the conduction route, ×4 / 100 | **148.9 s** | 25/40 | **censored** | 6/40 |

**With the drives full, fewer than half the hulls cross critical at all in any of the three**, so
the 50th percentile sits in the censored tail and there is no median. The shipped configuration is
in the same position: 12 of 40. **Read correctly that is not a failure of the routes** — it is a
ship that is not doing the thing, and a criterion about an event cannot be scored where there is no
event. It is kept because it is the other half of the bracket and because it says how much of a
loaded ship's heat one block type is.

**So the mod is both too fast and too slow, and which one depends on one block.** With drives
charging the median hull crosses in 10 s, far under the window; with them full it never crosses.
Drives are 71.3 % of population waste, and a ship carries either state at different minutes of the
same session.

**`G8` asks for *the most significant thermal event* to land in 2–5 minutes, and on a real ship that
event is the drive charging** — a transient with a beginning and an end, which neither of these two
cases is. That is what `jump-charge` above measures, and it is where the criterion is scored.

**This dataset reproduced both grids** : the conduction route's four cells came out at 124.0, 148.9, 165.4 and
186.0 s in this dataset against 124.0, 148.9, 165.4 and 186.0 s in the pair grid, which is two
independent runs agreeing to the last figure.

**One defect found underneath it.** The jump drive was filed as a *tool* in `ShipLoad`, so
`State.Tools` governed it and `State.Consumers` — the dial that reads as the ship's electrical load
— never reached the block carrying 71.3 % of the load's heat. No published figure moves: the two
shares agree in all four states that existed, checked against 1,794 rows of the previous dataset,
of which none moved. What it cost was a dial nobody could use, and it is why the first run of this
comparison produced two identical tables.

#### The confound this rests on, and it is a known gap

`full-electrical` charges every jump drive continuously, and drives are 71.3 % of population waste
([backlog.md](backlog.md) `F13`). So the load scenario is a **bound rather than a steady state**, and
a waste multiplier of 0.5 is within the distance between that bound and a realistic load. There are
therefore two readings of the table above, and they are not distinguishable from this dataset:

* the shipped mod makes twice as much heat as it should, and ×0.5 is a retune; or
* the shipped mod is right and the *scenario* over-states the load, in which case `G8` may already
  be satisfied against a realistic one and nothing needs retuning at all.

The second would be much the better outcome — no balance change, a corrected measurement — and it
is one run away: the same grid at waste ×1 under a load case that does not charge every drive. That
makes `F13` the next thing to measure rather than a footnote, and it is why this page recommends a
cell without recommending that it ship.

#### What the retune was measured to cost, and why it is not shipped

The pair was built and the suite was run against it: `ConductionScale` 2.4 → 9.6 and
`HeatTimeScale` 225 → 100, which is exactly the ×4 / 100 cell. **Forty tests failed, and they are
not stale figures — most of them are the mod's own levers going away.**

| What was measured | Shipped | At ×4 / 100 |
| --- | ---: | ---: |
| a coolant sink, against the best surface dial | **195.3 K vs 41.5 K** | **73.3 K vs 135.3 K** |
| …with the loop's own pace moved ×4 as well | — | **108.2 K vs 135.3 K** |
| thirty-two radiators *bolted* to a reactor | under 1 % of it | **25.6 K off 890 K** |
| plain armour bolted to a reactor | loses — it buries a radiating face | **saves 2.42 K** |
| a 300 MW reactor under one cell of armour | overheats, asks to be cooled | **settles at 940.6 K, inside its 1,090.1 K** |
| the census hull's stiffest block, in air against vacuum | 1.04× or more, as real ships are | **1.00× — it stops responding to air** |

**The first row is the finding.** *The radiator is a block you plumb* is this page's own headline,
and it inverts: at four times the solid conduction pace a bolt joint is no longer the bottleneck, so
shortening a panel buys more than plumbing it does. Moving the loop's fluid coupling with the solid
pace recovers a third of that and does not restore the ordering, because the surface dial gained too.

**The last row is the one that decides it.** Conduction at ×4 dominates every other transport the
model has. A hull shares heat well enough that burying a reactor solves it, bolting works, and the
atmosphere stops mattering to the stiffest block on the ship — which is the conductivity ×8 failure
mode, *the hull absorbs the whole event*, arriving three rungs early. The mod's purpose is that heat
is a resource with a lever; ×4 removes three of the levers to buy the timing of one.

**So `G8` and `G3` cannot both be satisfied by moving conduction**, and the sweep that found ×4
never scored `G3` — it scored `G1`, `G2`, `G5`, `G8` and cost. That is the gap, not the cell.

**What the search has not tried.** Both dials moved so far act on *transport*. The crossing is when
a block passes critical, and it depends on what the block is *made to absorb* as much as on how fast
the heat leaves: `crossing × clock` is a constant per conductivity, and waste heat and the critical
temperatures move that constant without touching the conduction pace at all — so without touching
plumbing, radiation geometry or air. Recovery is set by the clock alone, so that half is unaffected.
That is where `C12` goes next, and it is a sweep the same machinery runs.

**One trap this pass found on the way.** The game has two conduction paces —
`ThermalConstants.ConductionScale` for solids and `ThermalConstants.ReferenceConductivity` for the
loop's fluid coupling — and nothing made them agree. Raising the first alone makes a coolant loop
four times weaker *relative to the structure it competes with*, silently; it is the whole difference
between the first and second rows above. `ConductionPaceTests` now fails if one moves without the
other.

### Air is where the substep budget goes, and the shipped pair does not fit it

**Every atmospheric figure this section carried was exactly half what the shipped configuration
demands.** The table below was taken when `Frequency` was 8; the shipped step became a quarter of a
second when it went to 4, which doubles the demand — substep demand is a conductance times the step
over a capacity — and the table was never re-derived. Re-measured on the same 49-ship panel, all
eight of its figures come out at **2.000×** what it published, in four environments at once, and
`AirCostTests` pins the proportionality the correction rests on.

| Environment | substeps p50 | p95 | as published | |
| --- | ---: | ---: | ---: | --- |
| `vacuum-shadow` | 4.83 | 7.12 | 2.41 / 3.56 | ×2.00 |
| `surface-hot-noon` (still air) | 28.28 | 33.31 | 14.15 / 16.66 | ×2.00 |
| `storm-parked` (100 m/s) | 51.86 | 61.61 | 25.94 / 30.81 | ×2.00 |
| `reentry` (200 m/s) | 61.67 | 73.37 | 30.87 / 36.71 | ×2.00 |

**Read against the cap, the shipped configuration fails `G6` in air.** `MaxSubsteps` grants 64. At
200 m/s in thick air the panel's p99 demand is **73.4**, and **14 of 49 hulls are over the cap**;
the median hull is at 61.7, which is 96 % of it. The same run on the forty-hull retest set gives
73.6 and the same verdict, so this is a property of the configuration rather than of a population.
Exceeding the cap is not a slow step: the solver integrates the step at the ceiling instead of
dividing it as finely as the estimate asked. **What that costs is now measured and it is 0.028 K**
— on the hottest block of a driven census hull over 600 simulated seconds, in the same thick air at
200 m/s, against a run granted everything it asked for. Every hull over the cap in either
population is at 1.14–1.15× over-subscribed, which is the first rung of a ladder that stays free to
about 2× and breaks between 2× and 3×; the worst block on that rung is 0.041 K out. See
[stiffness.md](stiffness.md#what-refusing-the-demand-costs).

**Two claims this section carried are corrected.** The mechanism named — the solver flooring a
block's heat capacity — is `MaxSubstepsPerBlock`, which ships at zero and does not run; what bounds
the refused step is the two overshoot clamps. And *simulated wrong rather than slowly* is not what
the measurement says: refusing 1.15× the demand buys 1.15× of the step for three hundredths
of a kelvin. **`G6` still fails as written** — the criterion's marker is demand exceeding what the
caps grant, and it does — but the failure now has a price on it, and it is smaller than the
instrument that reads it.

The criterion had been scored in vacuum, where the same panel demands 7.1 at p95 and nothing is
close to anything. **`G6` was read where the money is not being spent.**

#### The retune `C12` is about costs less here, not more

`G8`'s four candidate cells were priced at 1.36–2.04× the shipped demand, and that column is a
vacuum column. In air the same cells are **cheaper**, and the mechanism is exact:

> Substep demand is a conductance over a heat capacity. `HeatTimeScale` divides every capacity and
> touches nothing else, so lowering it divides *every* stiffness term at once; multiplying
> conductivity restores the conduction term and only that one.

A hull in vacuum is conduction-limited, so it gets dearer by `conductivity × clock / 225`. A hull in
air is convection-limited, that term is not restored, and it gets cheaper by nearly `clock / 225`.
Both halves are pinned on rigs that isolate one term each.

Measured over the 49-ship panel, worst environment per cell, against the 64 the caps grant:

| cell | reentry p99 | of cap | `G6` | projected at 300 m/s, p50 / p95 | of cap | `G6` |
| --- | ---: | ---: | :---: | ---: | ---: | :---: |
| shipped, ×1 / 225 | 73.4 | 115 % | **fail** | 69.2 / 82.4 | 129 % | **fail** |
| ×4 / 120 | 46.4 | 72 % | pass | 43.6 / 51.2 | 80 % | pass |
| ×4 / 100 | 38.6 | 60 % | pass | 36.3 / 42.6 | 67 % | pass |
| ×4 / 90 | 34.8 | 54 % | pass | 32.7 / 38.4 | 60 % | pass |
| ×4 / 80 | 30.9 | 48 % | pass | 29.1 / 34.1 | 53 % | pass |

**So the retune is cheaper here than the cost column made it look**, and its vacuum price is paid
in the environment where demand is 12 % of the cap rather than 115 % of it. It was read as *the fix
for a defect that already exists*; that reading is withdrawn — the defect is worth 0.028 K on the
hottest block ([stiffness.md](stiffness.md#what-refusing-the-demand-costs)), so the retune has to be
justified by `G8`'s timing after all, not by this. `PairSweep.EveryCandidateCellIsPricedInAir` is the run and `tools/corpus/air.py` scores it.

**What this does not measure.** The 300 m/s columns are a projection — demand is linear in the
convection coefficient, fitted per cell on its own three atmospheric points, and validated against a
held-out point below. `storm-parked` at clocks 80–100 hits the sweep's 7,200 s ceiling rather than
its stretched length, so its column is read at a slightly earlier state than the rule asks for;
`reentry` is not affected and is the binding case. And the demand recorded is the one the *last*
step needed, which is the same statistic `verdict.py` scores `G6` on.

### The route is chosen, and it is the one the cost column argued against

Both routes reach `G8`'s window on the forty-hull retest set, so the choice was never about which
one works. **Resampled from the same population, they are not equally likely to keep working**:

| cell | crossing p50 | crossed | recovery p50 | `G8` holds on a resampled fleet |
| --- | ---: | ---: | ---: | ---: |
| shipped, ×1 / 225 | 10.4 s | 29/40 | 1,320 s | 1 % |
| waste ×0.5 / 110 | 122.4 s | 22/40 | 2,670 s | 24 % |
| waste ×0.5 / 100 | 134.5 s | 22/40 | 2,880 s | 37 % |
| waste ×0.5 / 90 | 149.5 s | 22/40 | 3,180 s | 33 % |
| waste ×0.5 / 80 | 168.1 s | 22/40 | 3,570 s | 27 % |
| conduction ×4 / 120 | 124.0 s | 25/40 | 2,220 s | 50 % |
| **conduction ×4 / 100** | 148.9 s | 25/40 | 2,640 s | **76 %** |
| **conduction ×4 / 90** | 165.4 s | 25/40 | 2,940 s | **76 %** |
| conduction ×4 / 80 | 186.0 s | 25/40 | 3,270 s | 72 % |

**The cliff is what separates them, and the median cannot show it.** `G8`'s crossing median is
censored above, so it exists only while more than half the hulls cross. The waste route puts 22 of
40 past critical and the conduction route 25 — two hulls either way is the difference between a
criterion satisfied and a criterion with *no median at all*, and this repository has already been
caught by exactly that once, at conductivity ×8 where 13 of 40 crossed and the cell read as the
grid's best. The share is printed beside every median for that reason; what it does not say is how
close a cell sits to the boundary, and the bootstrap does. Both halves of `G8` are drawn from one
resample of hulls rather than tested separately, because two medians over the same forty hulls are
not independent. `tools/corpus/scoring.py` holds it and `test_scoring.py` pins it.

**The cost column that argued for the waste route decides nothing, and that is now measured rather
than projected.** The air grid priced only the conduction cells, so the two routes were being
compared on a cost measured for one and projected for the other, which is the comparison `P6`
forbids. Measured on the same 49-ship panel, worst environment per cell, against the 64 the caps
grant:

| cell | worst p99 | of cap | `G6` | projected at 300 m/s, p95 | of cap |
| --- | ---: | ---: | :---: | ---: | ---: |
| shipped, ×1 / 225 | 73.58 | 115 % | **fail** | 82.36 | 129 % |
| waste ×0.5 / 100 | 32.70 | 51 % | pass | 36.60 | 57 % |
| waste ×0.5 / 90 | 29.43 | 46 % | pass | 32.94 | 51 % |
| conduction ×4 / 100 | 39.02 | 61 % | pass | 42.63 | 67 % |
| conduction ×4 / 90 | 35.12 | 55 % | pass | 38.37 | 60 % |

The waste route is about 16 % cheaper in air at the same clock. Both take a configuration that is
at 115 % of the cap to 41–73 % of it, so the difference between them is spent where nothing binds —
which is the same shape as the vacuum cost column that started this, and the reason that column
could not decide anything either.

**So the route is conduction ×4 with `HeatTimeScale` 90, and it is a trade rather than a free
win** — what it costs is below. It centres the window it exists to
reach — 165.4 s in 120–300 — holds it on 76 % of resampled fleets against the waste route's best of
37 %, recovers in 2,940 s against a 3,600 s bound, keeps `G1` at 0 %, `G2` at 100 % and `G5` at
100 %, and is the cheapest of its band in the environment `G6` is decided in. Between it and ×4/100
the two are indistinguishable on stability, and 90 has the more central crossing and the lower air
demand.

**And the provenance argument that blocked this row is moot.** It was an argument against the waste
route — that halving a fraction derived from the game's own stated efficiency is a balance knob
rather than a correction — and the route it was blocking is the worse one on the criterion itself.
Conduction ×4 departs from a pace that is already an admitted 2.4× the world's, which is what
`ConductionScale` is for.

**What shipping it costs, sized by attempting it, and the attempt is what the three levers turn out
to mean.** Setting `ConductionScale` to 9.6 and `HeatTimeScale` to 90 fails **52 of 1,826 tests**
across 28 classes. Reading them rather than counting them, most are figures that legitimately move
with the pair — and four are the mod's own guidance failing, each in the words its own test uses:

* *"a coolant sink bought 73.5 K and the best surface dial bought 135.3 K; the surface dials have
  caught up and the guidance needs revisiting"* — a coolant sink stops out-performing every surface
  dial, which is what [blocks.md](blocks.md) tells a player to build for.
* *"thirty-two bolted radiators took 25.6 K off 890 K, which is more than a bolt joint should buy"* —
  bolting starts working, and *plumb it, do not bolt it* is guidance this contradicts.
* *"the census hull's stiffest block is 1.00 times stiffer in air than out of it … the hull has
  stopped responding to the air"* — the stiffest fitting on a hull stops feeling the atmosphere.
* *"a plain armour block bolted to the reactor saved 2.42 K; the control is meant to lose"* — the
  control in the cooling ladder stops being a control.

**So the cost column that called this route *three of the mod's levers* was not rhetorical**, and
this page's own earlier sentence — that the provenance argument made the choice easy — understated
it. The route is still the one the criterion selects, by 76 % against 37 %; what is now on the table
is that it buys that by making conduction strong enough to blunt three of the design signals the mod
is built around. **That is a trade to take deliberately or not at all**, and it is why `C24` is a
separate row rather than a follow-up commit.

Two more things it needs. Every temperature figure in this repository is measured at the shipped
pair and moves with it. And `G7` — the stated compatibility floor, the one criterion measured on
ships nobody chose to put in a corpus — has to be re-scored on the 705 prefabs; both changes point
the right way for it, and *points the right way* is not a measurement.

**The attempt also found a defect that has nothing to do with the retune.** The coolant path has no
overshoot clamp, so where a refused substep demand *approximates* on a block it *diverges* on a
loop, which on a hull carrying nothing stiffer is what sets the demand. It is
[backlog.md](backlog.md) `A10`,
nothing shipped reaches it, and it is recorded because
[stiffness.md](stiffness.md#what-refusing-the-demand-costs)'s refusal ladder reads as though it
described a whole grid.

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
measurement — re-derived at the shipped quarter-second step, which is what the section above
corrects:

| | Fit | Predicted at 200 m/s | Measured at 200 m/s | Error | **Projected at 300 m/s** |
| --- | --- | ---: | ---: | ---: | ---: |
| p50 | 4.70 + 23.58h | 61.63 | 61.67 | −0.06% | **69.2** |
| p95 | 5.01 + 28.30h | 73.33 | 73.37 | −0.05% | **82.4** |

**300 m/s demands about 69 substeps at the median and 82 at p95, against `MaxSubsteps` 64.** It does
not fit: at the speed these servers actually run, the *median* ship in atmosphere is over the cap.
That is the same defect the section above measures at 200 m/s, one speed further on. This is a
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
* **G1, G2 and G5 hold on shipped defaults. `G6` does not, in air.** It holds comfortably in
  vacuum — panel p95 7.1 against the 64 the caps grant — and fails at 200 m/s in thick air, where
  p99 is 73.4 and fourteen of forty-nine hulls are over the cap. It had only ever been scored in
  vacuum. See [Air is where the substep budget goes](#air-is-where-the-substep-budget-goes-and-the-shipped-pair-does-not-fit-it).

**Not settled.**

* **G3 has never been measured.** No corpus ship carries a cooling block — the filter rejects
  non-vanilla blocks — so the single highest-value missing run is the retrofit pass, and two of the
  five balance goals depend entirely on it.
* **Whether to ship the retune `G6` and `G8` now agree on.** Conductivity ×4 with the clock at
  100 satisfies `G8`, keeps `G1`, `G2` and `G5`, and takes the atmospheric demand from 115 % of the
  cap to 60 %. What is unbuilt is the pass itself, which re-quotes every temperature figure on this
  page ([backlog.md](backlog.md) `C12`).
* **The corpus has never seen air.** Five vacuum scenarios on 8,132 ships; air only on the 49-ship
  panel, so every atmospheric statement here rests on 49 hulls.
* **`full-electrical` charges every jump drive continuously**, so the scenario is a bound rather
  than a steady state — and since drives are 71.3% of population waste, that choice sets most of the
  shape of the load results.

### Open items in the block report

* ~~**The harness catalogue is not the blocks it stands in for.**~~ **Fixed 2026-08-23.** The six
  stand-ins derive from `Vanilla`'s build costs and the shipped derivation now, so a scenario
  reactor weighs 4,793 kg and wastes a hundredth of what it makes exactly as a player's does. Four
  of the six had been out by more than five per cent — the large thruster by **4.32×** at 10,000 kg
  against a real 43,200, the battery by 3.70× because it carried the *small-grid* battery's mass
  under a large-grid name — and `ReactorThermal` had said a quarter of a reactor's output becomes
  heat against the shipped hundredth. `CatalogDriftTests` now fails if a hand-typed mass reappears.
  **What it moved**: rigs whose source was incidental state their heat in watts through
  `GridBuilder.Wasting` and carry the same load they always did, so their figures are unchanged;
  the three scenarios that are *about* a reactor — `reactor`, `atmosphere`, `meltdown` — run a real
  reactor at a real rating and their temperatures moved with it.

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
| 2026-08-24 | Corrected this page's own account of `C12`'s choice, from the attempt at shipping it. The conduction route's *three levers* are four named behaviours — a coolant sink stops out-performing surface dials, bolting starts working, the stiffest fitting stops responding to air, and the cooling ladder's control stops losing — so the route is a trade taken deliberately rather than the easy choice the previous entry implied. |
| 2026-08-24 | **Chose `C12`'s route, and the choice inverted on evidence the cost column could not carry.** Both routes reach `G8`'s window; resampled from the same population, conduction ×4 holds it on 76 % of fleets and waste ×0.5 on 33–37 %, because the censored median has a cliff at half the hulls crossing and the waste route sits two hulls from it. Priced the waste route in air for the first time — the two routes had been compared on a cost measured for one and projected for the other — and both take `G6` from 115 % of the cap to 41–73 %, so the cost decides nothing. The route is conduction ×4 with `HeatTimeScale` 90; shipping it is `C24`. |
| 2026-08-23 | **The loop's fluid coupling is stated as a heat transfer coefficient** ([backlog.md](backlog.md) `C20`): 160 W/(m²·K), which is exactly what the old quality-against-a-reference came to on a large grid, so every cooling figure on this page still describes the loop it was measured on. What moves is small grids, where the old formula implied 800 W/(m²·K) for the same fluid because it divided by half a cell — a small-grid ring now couples a fifth as hard, worth 45.7 K on a four-by-four rig. |
| 2026-08-23 | **Authored the mod's first selective surface and measured what it is worth** ([backlog.md](backlog.md) `C15`). The radiator absorbed sunlight at its emissivity because nothing declared otherwise; `SolarAbsorptivity 0.1` is what a second-surface mirror or white paint gives, and it is worth **10.9 K** to a sunlit stack and nothing in shadow. The finding beside it: raising emissivity alone would buy 1.9 K in sunlight, because a better emitter that is also a better absorber gives it back — the two changes are complementary, and the obvious one alone is nearly worthless in the sun. |
| 2026-08-23 | **Closed `C4`: the scenario catalogue is the blocks it stands in for.** The six stand-ins derive from `Vanilla`'s transcribed build costs and the shipped derivation rather than being hand-typed, so a scenario reactor wastes the hundredth a player's does instead of the quarter the catalogue asserted, and the large thruster weighs 43,200 kg instead of 10,000. Rigs that only wanted a heat source now state it in watts of heat (`GridBuilder.Wasting`) and are unchanged; the scenarios that are about a reactor moved. |
| 2026-08-23 | **Closed `C2`: its two headline figures are censored and every uncensored one is unmoved.** A hull with anything past its rating keeps generating undamped for the rest of the clock, so its peak is a harness artefact (`E9`) — and that is 34 of 40 hulls in `burn-forward` and 29 of 40 under load, which is both of the rows the finding rested on. Crossing moves −0.13 s, first loss +0.50 s and the share over critical 0.000. The block-count totals are a wash per hull as well: 14 better, 11 worse, 15 unchanged, one hull carrying a third of the −257. `retest.py` had been testing the peak against 1,500 K rather than against each block's own rating, which reported 9 % of the run censored where 31 % was, and 85 % in the scenario the finding came from. |
| 2026-08-23 | **Priced the load route's dial against the provenance of what it scales, and it argues against the route.** Every waste fraction in `Cubes.xml` now states where it came from, and weighted by the heat each carries over the census, **76.3 %** of a loaded fleet's waste comes through a fraction derived from the game's own `PowerEfficiency`, 8.4 % through a sourced conversion and 15.3 % through an invention. So waste ×0.5 halves a sourced number — an admitted balance knob rather than the correction of a guess — and the invented sixth sits on four block types that between them cannot reach the window. |
| 2026-08-23 | **Gave `G8` a load case that is an event, and it selects the cells the grids already found.** A jump drive holds 3 MWh, draws 32 MW and keeps 80 % of it, so it fills in **421.9 s** — every figure off the game's own definition — and `jump-charge` runs exactly that: charge, finish, hold. Measured across it, both routes satisfy `G8` and the shipped pair does not, at 10.4 s. **This corrects the entry below**: `full-electrical-charged` is a ship not having an event, not a bound on one, and reading its censored medians as evidence against the routes was reading an absence as a short crossing. The transient reproduces the charging bound to the last figure, because every hull that crosses does so in the first minute of a seven-minute charge — which is the defect in one sentence. |
| 2026-08-23 | **Measured `F13`, and it withdraws both of `C12`'s routes.** `full-electrical` charges every jump drive for the whole run; run against the other bound — the same load with the drives full — the conduction route crosses on 6 of 40 hulls, the load route on 3, and the shipped configuration on 12. All three are censored, so none has a median and none satisfies `G8`. **The mod is both too fast and too slow depending on one block**: 10 s to cross with drives charging, never with them full. That is a fact about the criterion rather than the dials — `G8`'s *most significant thermal event* is the drive charging, which is a transient, and both scenarios model it as permanent or absent. What it needs is a duty-cycled load. The comparison also reproduced the pair grid's four cells to the last figure across two independent runs, and found that the jump drive was filed as a *tool*, so `State.Consumers` never reached 71.3 % of the load's heat — neutral on every published figure, checked against 1,794 rows of which none moved. |
| 2026-08-23 | **The significance window is reachable without touching transport.** Eighteen cells of the load against the clock: `G8` is satisfied by waste ×0.5 at `HeatTimeScale` 110, 100, 90 and 80, at conductivity ×1 — so plumbing, radiation geometry and air are all untouched — while keeping `G1`, `G2` and `G5` and costing 0.43× the substep demand, which also takes `C19`'s atmospheric breach from 115 % of the cap to about 50 %. **The ratio is the quantity**: `recovery / crossing` is 127 at the shipped load and needs 30 or less, the clock cannot change it because both halves go as one over the clock, and the load can — 315 at ×2, 127 at ×1, 21 at ×0.5. It costs the bite rather than the levers: the median hull peaks 264 K cooler and loses one block instead of four. **And it rests on a confound**: `full-electrical` charges every jump drive continuously (`F13`), so a ×0.5 multiplier is within the distance between that bound and a realistic load, and the alternative reading is that nothing needs retuning at all. |
| 2026-08-23 | **Built `C12`'s retune, measured it, and did not ship it.** `ConductionScale` 2.4 → 9.6 with `HeatTimeScale` 225 → 100 satisfies `G8` and fixes `C19`, and costs three of the mod's four levers: a coolant sink stops out-performing the best surface dial (195.3 K vs 41.5 K becomes 73.3 K vs 135.3 K), bolting starts working, a 300 MW reactor buried in armour settles inside its rating, and the stiffest block on the census hull stops responding to air at all. The sweep that chose ×4 scored `G1`, `G2`, `G5`, `G8` and cost, and never scored `G3`. Also found the trap underneath it: the game has two conduction paces and nothing made them agree, so raising the solid one alone weakens every coolant loop by four relative to the structure it competes with — `ConductionPaceTests` now fails if one moves without the other. |
| 2026-08-23 | **Priced `C12`'s candidate cells in air, and the answer inverted the question.** The cost column that made the retune a decision was a *vacuum* column, where demand is 12 % of what the caps grant; in air it is 115 % of it. Two findings came out of the same run. **Every atmospheric figure this page carried was exactly half**, taken when `Frequency` was 8 and never re-derived when the shipped step became a quarter second — measured on the same 49-ship panel, all eight are 2.000× what was published. **And the shipped configuration fails `G6` in air**: p99 substep demand at 200 m/s is 73.4 against 64, fourteen of forty-nine hulls are over the cap, and at the 300 m/s servers run the *median* ship is. Read there, all four candidate cells pass and the shipped pair is the only one that does not, because lowering the clock divides every stiffness term while raising conductivity restores only conduction. The retune is the fix for a defect rather than a cost to be justified. |
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
