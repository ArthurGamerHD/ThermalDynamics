# Block balance

Every block this mod ships, costed and measured against the vanilla blocks a player would
otherwise have built. Run it with:

```bash
cd tests
dotnet run --project Thermodynamics.Sim -- balance
dotnet run --project Thermodynamics.Sim -- balance --csv out/     # also a diffable table
```

The scenarios in [the sim README](../tests/README.md) answer *does this mechanism work*. This
answers *is this block worth building*, which needs a different shape of measurement: not a
settling curve, but a per-block figure that can be divided by what the block costs.

## Where the numbers come from

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
temperature that block ever reaches. Change a definition and the next run measures the change.

`Vanilla` is checked in because most machines running this suite have no game install. On a
machine that does have one, `TheVanillaReferenceStillMatchesTheInstalledGame` re-derives every
figure and fails if it has drifted — the same arrangement `Census` uses for its field
observations, and for the same reason: a transcription nobody checks becomes fiction.

## What is measured, and why each one can bind

1. **Capacity** — J/K. How much heat the block swallows before it warms. No solver involved.
2. **Shedding** — watts radiated at 600 K into a 2.7 K sky. What the block gets rid of.
3. **The joint** — W/K through the block's mount faces. What can *reach* it.
4. **Delivered** — an end-to-end steady state with a real load. The only measurement that accounts
   for all three at once, and the one a player experiences.

Loads are stated as **watts of heat**, not watts of power. The report drives them with a block
whose waste fraction is 1, because `Catalog.Reactor` carries the shipped 0.25 and every row would
otherwise be a quarter of its heading.

## The finding

**A coolant sink face is the stiffest way to move heat into a panel, and nothing else is close.**

On one 200 kW source with one shipped radiator, changed one dial at a time:

| Dial | Change | Source settles | Gain |
| --- | --- | --- | --- |
| *(shipped)* | as built | 740.2 K | — |
| `Emissivity` | 0.35 → 0.80 | 729.5 K | 10.7 K |
| `ExposedSurfaceMultiplier` | 1.25 → 5.0 | 723.0 K | 17.2 K |
| `ExposedSurfaceMultiplier` | 1.25 → 10.0 | 715.6 K | 24.6 K |
| panel depth | 1×5×2 → 1×1×2, same mass | 698.8 K | 41.5 K |
| **coolant sink** | **fed by a loop, not bolted** | **544.9 K** | **195.3 K** |

A bolt joint between the source and the panel carries **167 W/K**. A coolant sink face carries
**1,000 W/K**. That six-fold difference is worth more than every surface property of the block put
together — an eight-fold area multiplier buys 25 K, and plumbing buys 195 K.

The reason the surface dials disappoint is that they work on the wrong end. Multiplying the area
makes the panel *colder* — 338 K down to 213 K across that sweep — and radiation falls away as the
fourth power, so eight times the surface moves only 25 % more heat. Meanwhile the drop across the
joint grows from 402 K to 503 K: the panel is starving, not saturating.

Shortening the panel is the only geometry change that helps, and only a little. Conduction runs
centre-to-interface, so the shipped panel's five-cell height puts 6.25 m of metal between its
middle and the joint; flattening it to one cell triples the joint to 500 W/K — but costs most of
the surface, and the two nearly cancel.

**So: the radiator is not a block you bolt to a hot thing. It is a block you plumb.** That is worth
saying in `blocks.md` more loudly than it currently is.

### The radiator still earns its place

Against a slab of ordinary light armour of the same shape, in the same position, on the same load:

| Fit | Count | Settles | Saved | Mass | K per tonne |
| --- | --- | --- | --- | --- | --- |
| bare source | 0 | 783.2 K | — | — | — |
| radiators | 1 | 740.2 K | 42.9 K | 600 kg | 71.6 |
| radiators | 8 | 736.5 K | 46.6 K | 4,800 kg | 9.7 |
| armour slab | 1 | 775.7 K | 7.5 K | 5,000 kg | 1.5 |
| armour slab | 8 | 772.7 K | 10.5 K | 40,000 kg | 0.26 |

Six times the cooling for an eighth of the mass — about **48× better per tonne**. Pinned by
`TheRadiatorBeatsTheArmourItDisplaces`.

The second radiator is worth 3 K and the eighth is worth nothing, for the same reason as above: one
joint feeds them all.

**Past a certain load the radiator turns negative.** At 2 MW into one cell, bolting panels on makes
the source 33 K *hotter*, because they cover faces that were radiating and cannot carry away what
they blocked. Panels bolted to a saturated block are worse than no panels.

## The heat pump

Large grid, cold side 300 K. Which of the three limits binds changes twice across an ordinary
range, which is the block's whole character:

| Gap | Coefficient | Lifted | Drawn | Binding |
| --- | --- | --- | --- | --- |
| 5 K | 8.00 | 60 kW | 7.5 kW | coefficient cap |
| 20 K | 6.00 | 60 kW | 10 kW | rating |
| 40 K | 3.00 | 60 kW | 20 kW | rating |
| 100 K | 1.20 | 24 kW | 20 kW | carnot |
| 400 K | 0.30 | 6 kW | 20 kW | carnot |

Pinned by `TheHeatPumpPassesThroughAllThreeOfItsLimits`.

## Coolant rings

A 500 kW block with one sink face, rings of rising size:

| Pipes | Coupling | Settles |
| --- | --- | --- |
| 8 | 9,000 W/K | 857.2 K |
| 12 | 13,000 W/K | 811.6 K |
| 20 | 21,000 W/K | 768.5 K |
| 28 | 29,000 W/K | 740.9 K |

Each pipe adds 1,000 W/K of its own and the fluid mass does not grow, so a longer ring is strictly
better. Pinned by `LongerRingsDeliverColderBlocks`, and by
`LongerRingsCoupleHarderAndCarryTheSameFluid` in the coolant suite.

## Reactor waste heat

**Reactors generated no waste heat at all.** The `Reactor` entry in `Cubes.xml` set
`ProducerWasteEnergy` to 0 and `ConsumerWasteEnergy` to 0.25, but a reactor delivers power through
the *source* component, so only the producer fraction can ever apply to it. The largest heat source
on a ship was inert, and the `LOAD` table showed every reactor in the game at 0 W. Thrusters were
never affected — they consume, so their 0.25 applied.

Nothing in the solver was wrong. The number it was handed was, which is why the whole suite
stayed green over it.

### Why the fraction could not be picked by analogy

The thruster's 0.25 was chosen against a 33 MW draw. A reactor's fraction has to hold across three
orders of magnitude of rated output — 0.5 MW on a small-grid small generator against 300 MW on a
large-grid large one — while the block it heats is the same size in both grids. So it was measured
instead, by `reactors`, in two rigs that bracket the answer:

* **bare** — one reactor alone in shadow, every face radiating to a 2.7 K sky. The coolest a reactor
  can possibly run, so a fraction that cooks here cooks in every build and no plumbing reaches it.
  This sets the ceiling.
* **skinned** — the same reactor under one cell of light armour, which is how one is installed. A
  fraction the skinned rig survives at full rating is a fraction nobody ever has to cool, so this
  sets the floor.

Both rigs at four candidate fractions, at full rating, against a 1,200 K critical temperature:

| Fraction | Small gen (15 MW) | Large gen (300 MW) | SG small (0.5 MW) | SG large (14.75 MW) |
| --- | --- | --- | --- | --- |
| 0.01 bare | 728.8 K | 889.9 K | 696.4 K | 937.0 K |
| 0.01 skinned | 603.5 K | **1,245.0 K** | 495.4 K | 971.0 K |
| **0.02 bare** | 866.7 K | 1,058.2 K | 828.1 K | 1,114.2 K |
| **0.02 skinned** | 800.3 K | **1,809.2 K** | 603.1 K | **1,240.5 K** |
| 0.05 bare | 1,089.9 K | **1,330.7 K** | 1,041.3 K | **1,401.1 K** |
| 0.25 bare | **1,629.7 K** | **1,989.8 K** | **1,557.1 K** | **2,095.1 K** |

Bold is past critical. 0.05 and above put a reactor past critical *bare*, which is unbuildable —
there is no arrangement cooler than open space. 0.01 leaves only one reactor of the four wanting
cooling, and only when fully buried at full rating.

**0.01 is the fraction where both bounds hold.** Every reactor survives at full rating with its
faces on open space; the 300 MW one goes past critical once wrapped in hull. That makes where a
reactor is installed a decision rather than a detail, and it is the first thing in the mod that
makes a player want a coolant loop for a reason other than curiosity.

It was 0.02 for one pass, against a critical temperature of 1,200 K that had been typed into
`Cubes.xml` by hand. Deriving a reactor from its own build cost instead puts the four of them
between 938 and 1,090 K — fuel and graphite in a steel assembly, not the round number — and at 0.02
the two smaller reactors then cook themselves bare in vacuum, which is a state no build can improve
on. The table above is the pre-derivation measurement; the retune is what deriving is for. A
balance figure resting on a number nobody had checked is not a balance figure.

An idling ship is deliberately not a cooling problem: at 10 % of rating every reactor stays clear of
critical in both rigs. Heat arrives when power is drawn.

### It is balance, not efficiency

0.02 implies a 98 % efficient reactor, which no fission plant approaches. The figure is not an
efficiency and should not be read as one.

Space Engineers rates a 3×3×3 block at 300 MW — a power density about three orders of magnitude
past any real plant. A real plant's efficiency of about a third would put 600 MW of waste heat into
a 73-tonne box, which settles near 2,000 K bare in vacuum: every large reactor in every world
destroys itself the moment it is switched on, in a build no player can improve. Applying a real
efficiency to a fictional rating compounds the fiction rather than correcting it. The fraction is
chosen so the *consequences* land where they should, which is the honest way round when one of the
two inputs is already invented.

## Open items

* **The harness reactor and the shipped reactor still disagree.** `Catalog.ReactorThermal` carries
  `ProducerWasteEnergy` 0.25 against the shipped 0.01, so any scenario quoting a reactor temperature
  quotes one no player will see. `Vanilla.Reference` now derives from real build costs and is the
  right model for `Catalog` to follow. The same shape of problem as C4 below.
* **`Catalog` masses are not the shipped masses.** The harness's hand-written stand-ins are up to
  4× out (`Battery` 1040 kg against 3,845; `Thruster` 10,000 kg against 43,200; `Radiator` 900 kg
  against 600). They only affect scenarios, not this report, which reads the definitions directly —
  but every scenario temperature is quoted off them.
