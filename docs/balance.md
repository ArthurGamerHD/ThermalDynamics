# Block balance

Every block this mod ships, costed and measured against the vanilla blocks a player would
otherwise have built. Run it with:

```bash
cd sim
dotnet run --project Thermodynamics.Sim -- balance
dotnet run --project Thermodynamics.Sim -- balance --csv out/     # also a diffable table
```

The scenarios in [the sim README](../sim/README.md) answer *does this mechanism work*. This
answers *is this block worth building*, which needs a different shape of measurement: not a
settling curve, but a per-block figure that can be divided by what the block costs.

## Where the numbers come from

Nothing here is transcribed except the vanilla figures, and those are checked.

| Source | What it supplies |
| --- | --- |
| [`ShippedBlocks`](../sim/Thermodynamics.Harness/ShippedBlocks.cs) | The mod's own blocks, read from `Data/CubeBlocks/*.sbc` and `Data/Cubes.xml` at run time — size, mount faces, components, thermal properties |
| [`Vanilla`](../sim/Thermodynamics.Harness/Vanilla.cs) | Component masses and the comparison blocks, transcribed from Space Engineers' own `Content/Data` |
| [`BalanceLab`](../sim/Thermodynamics.Harness/BalanceLab.cs) | The measurements |
| [`BalanceTests`](../sim/Thermodynamics.Tests/BalanceTests.cs) | The conclusions, pinned |

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

## Open items

Two things this pass found that it did not change, both recorded here so they are not rediscovered:

* **Reactors generate no waste heat.** The `Reactor` entry in `Cubes.xml` sets
  `ProducerWasteEnergy` to 0 and `ConsumerWasteEnergy` to 0.25, but a reactor delivers power
  through the *source* component, so its watts run through the producer fraction. The `LOAD`
  section of the report shows every reactor in the game at 0 W. Thrusters are unaffected — they
  consume, so their 0.25 applies.
* **`Catalog` masses are not the shipped masses.** The harness's hand-written stand-ins are up to
  4× out (`Battery` 1040 kg against 3,845; `Thruster` 10,000 kg against 43,200; `Radiator` 900 kg
  against 600). They only affect scenarios, not this report, which reads the definitions directly —
  but every scenario temperature is quoted off them.
