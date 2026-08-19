# Blocks and items

All block definitions live in [Data/CubeBlocks/](../Data/CubeBlocks) with one file per block;
the extinguisher tool is in [Data/Extinguisher/](../Data/Extinguisher). Display names and
descriptions are localisation keys resolved from
[Data/Localization/MyTexts.resx](../Data/Localization/MyTexts.resx).

Both grid sizes are provided for every functional block, prefixed `Gauge_LG_` (large) and
`Gauge_SG_` (small).

## Coolant pipes

| Subtype | Type | Size (LG / SG) | Sink faces |
| --- | --- | --- | --- |
| `Gauge_*_CoolantPipe_Straight` | `CubeBlock` | 1×1×1 | none |
| `Gauge_*_CoolantPipe_Straight_SingleSink` | `CubeBlock` | 1×1×1 | Right |
| `Gauge_*_CoolantPipe_Straight_DoubleSink` | `CubeBlock` | 1×1×1 | Left, Right |
| `Gauge_*_CoolantPipe_Corner` | `CubeBlock` | 1×1×1 | none |
| `Gauge_*_CoolantPipe_Corner_SingleSink` | `CubeBlock` | 1×1×1 | Up |
| `Gauge_*_CoolantPipe_Corner_DoubleSink` | `CubeBlock` | 1×1×1 | Backward, Right |
| `Gauge_LG_CoolantPump` | `UpgradeModule` | 1×1×1 | none |
| `Gauge_SG_CoolantPump` | `UpgradeModule` | 1×1×**3** | none |

*Sink faces* are the sides that transfer heat between the coolant and the block pressed against
them. A pipe with no sinks is plumbing only. Plumbing is declared per subtype in
[ThermalCoolantShapes.cs](../Data/Scripts/Thermodynamics/Game/ThermalCoolantShapes.cs) as a
`CoolantShape`: two link ports, any number of sink ports, and whether the block is a pump. **A new
pipe subtype must be added there.** Ports carry the cell they sit on as well as their direction, so
a block of any size or orientation works without a special case — the small-grid pump is three
cells long and needs no code of its own.

Connection directions (before block orientation is applied):

| Shape | Links |
| --- | --- |
| Straight (all variants) and pump | Forward ↔ Backward |
| Corner (all variants) | Forward ↔ Left |

The pipes and pump are grouped into the `CoolantGroupLarge` / `CoolantGroupSmall` block variant
groups ([BlockVarientGroups.sbc](../Data/CubeBlocks/BlockVarientGroups.sbc)) so they cycle on
one toolbar slot.

## Coolant loop rules

[CoolantLoopBuilder](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoopBuilder.cs) re-finds
every loop on the grid whenever the block layout changes. A ring becomes a loop when **all** of
these hold:

1. Following link ports from block to block returns to the starting block — the run must be a
   **closed ring**. A dead end forms no loop.
2. The ring contains at least one block whose shape is marked as a pump.
3. Every block in the ring declares coolant link ports.

Rings are keyed by their member set, so each is found once whichever block the search starts from,
and the order pipes were built in does not matter. Breaking a ring removes its loop; closing it
again restores one, and the heat comes back with it — a loop is identified by an order-independent
hash of its members, so its temperature survives the rebuild.

Once formed, the loop is one lumped coolant mass shared by every segment: it draws heat from
the pipe blocks themselves and, through each sink face, from whatever block is mounted against
that face. See [thermal-model.md](thermal-model.md#coolant-loops) for the transfer equations.

Practical build advice:

* Run the loop *through* your heat sources with sink faces against reactors, thrusters and
  batteries, then out to radiators or a cold hull section.
* **Loop length does increase total transfer, and the fluid mass does not grow with it.** Each pipe
  in the ring gets its own full-strength link to the fluid, so a 32-pipe ring couples at 32,000 W/K
  against an 8-pipe ring's 8,000 W/K, while both carry the same 500 kg of coolant. A longer ring
  therefore cools strictly better: measured with a single sink face on the same hot block, an
  8-pipe ring took it to 549.8 K and a 32-pipe ring to 532.5 K, because the fixed fluid mass is
  buffered by more pipe metal and so stays colder at the sink. Nothing divides by segment count.
  Pinned by `LongerRingsCoupleHarderAndCarryTheSameFluid`.
* A loop's temperature is saved and restored by member hash, so reloading cannot swap two loops'
  heat and rebuilding a ring does not reset it.
* **Spread your sources around the ring; do not bother splitting it.** Four reactors bunched into one
  stretch of a 32-pipe ring settle at 134 C; the same four spread evenly around the same ring settle
  at 93 C. Dividing that ring into four separate rings with a pump each lands at 95 C — no better than
  spreading, for four times the pumps. What saturates a loop is several sources dumping into one short
  run of pipe, not the length of the ring. Measured by the `loop-layout` scenario.
* **A pump fitted the wrong way round is not broken — it drives the loop backwards**, and a loop
  driven backwards cools exactly as well. A pump pushes fluid out of its outlet port; whether that
  faces one way round the ring or the other is all that changes.
* **Two pumps facing each other cancel.** Their demands subtract before the square root, so a ring
  with three pumps one way and one the other circulates at the rate of two, and a ring with one each
  way does not circulate at all — while both pumps go on drawing their full power. If a loop has
  pumps and no flow, check that they agree.
* One large ring is also the more robust arrangement. Flow rises with the square root of combined
  pumping, so a ring with four pumps that loses one still circulates at 87 %; a ring with one pump that
  loses it stops circulating altogether and becomes a local heat buffer.

### When a ring does not become a loop

A broken ring's only symptom is that no loop appears, which is the one thing the loop list cannot
report. `ThermalSimulation.DiagnoseLoops` re-runs the search and names the reason for every coolant
block that ended up in no loop:

| Reason | What to look for |
| --- | --- |
| open end | A port faces empty space. The run has a free end. |
| a port faces a block with no plumbing | The run walks into armour or a conveyor. |
| pipes touch but their ports do not line up | The hardest one to see: the run looks continuous and carries nothing. Rotate one of the two blocks. |
| closed ring with no pump | Reported against every block in the ring, because the fix is to the ring. |
| a branch or crossing, not a ring | Three pipes meeting, or a figure of eight. A loop is a simple cycle. |
| the run doubles back on itself | The walk returned to its start through the port it left by. |

It is opt-in and costs one extra walk per unclaimed run, so an ordinary rebuild does not pay for a
readout nobody opened. Each reason carries up to four example cells, capped so a grid of broken
plumbing cannot flood a readout while the counts stay exact.

## Radiator

`Gauge_LG_Radiator` / `Gauge_SG_Radiator` — a plain `CubeBlock`, 1×5×2 (LG), mounting only on
Top and Bottom.

It has **no script behaviour**. It is purely a definition-driven heat shedder: aluminium's specific
heat (900 J/(kg K)), conductivity 1, high emissivity (0.35 against the 0.125 default) and a 1.25×
surface area scaler, so it radiates faster than any armour block of comparable mass. Against a slab
of light armour of the same shape on the same load it is about 48× better per tonne, which is what
earns it its place.

**Plumb it; do not bolt it.** A coolant sink face couples to the panel at about 1,000 W/K and a bolt
joint at about 167 W/K, and on the same load that difference is worth 195 K — more than every
surface property of the block put together. A panel bolted straight onto a hot block is limited by
its joint, not its surface, which is why the second one you bolt on is worth 3 K and the eighth is
worth nothing. Past a certain load it goes further than useless: bolted to a block already saturated,
a panel makes it *hotter*, because it covers faces that were radiating and cannot carry off what it
blocked. Keep the panel's own faces exposed to vacuum either way. Measured in
[balance.md](balance.md).

## Heat pump

`Gauge_LG_HeatPump` (1×1×1) / `Gauge_SG_HeatPump` (3×3×1) — an `UpgradeModule`, so it has a
terminal and an on/off switch. It carries no upgrades; the type is there for the switch.

The only block that moves heat **against** a gradient. It draws heat out of whatever is bolted to
its front face and rejects it into whatever is behind it, and pays for the privilege in
electricity. Placement is the whole of its configuration: put its cold face on what you want cooled
and its hot face on a radiator, or on a coolant pipe's sink face.

| | Lifts up to | Draws up to |
| --- | --- | --- |
| Large grid | 60 kW | 20 kW |
| Small grid | 12 kW | 4 kW |

Three limits decide what it actually achieves each step, and which one binds is the block's whole
character:

* **Carnot.** Efficiency is `0.4 × Tcold / (Thot − Tcold)`, capped at 8. Lifting heat across a
  small gap is nearly free; across a large one it is ruinous.
* **Its rating.** Against a small gap it runs out of machine before it runs out of efficiency, and
  draws less than its maximum because it cannot use power it has no capacity to move.
* **What is there.** It cannot take more heat out of a block than the block has.

Two consequences worth knowing before building around it. The hot side gains **more** than the cold
side loses — the lift plus the work that lifted it — so a pump does not reduce a ship's heat, it
concentrates it somewhere you can radiate it away from. And nothing clamps its cold side at a floor:
the cost of a kelvin simply rises without limit as that side approaches absolute zero, so the
block's own electrical rating stops it long before the temperature does.

### As air conditioning

Yes, with one indirection. A heat pump binds to two **blocks**, and a room's air is not a block, so it
cannot draw from a compartment directly. Put its cold face on a block that *bounds* the compartment
and the wall goes cold; the air touching that wall gives up its heat to it, and the room follows. The
hot face goes outward, into a radiator.

Two consequences. It only works on a **pressurised** room — with no air there is nothing coupling the
compartment to its walls, and the pump is just chilling a piece of hull. And the rate is limited by
how much wall the room has in contact with the cooled block, not by the pump's rating: one wall of a
large cabin is a small window to pull heat through.

Measured by the `air-conditioning` scenario: a sealed cabin with a 60 kW reactor inside settles at
−50 C with the pump off and −102 C with it on.

The terminal shows what it is moving, what it is drawing, and the coefficient between them. A pump
with nothing bolted to one of its faces says so rather than silently doing nothing.

The efficiency fraction and the cap are tuned for the whole mod in
[configuration.md](configuration.md#heat-pumps); the two ratings are per block and live in
[ThermalHeatPumpShapes](../Data/Scripts/Thermodynamics/Game/ThermalHeatPumpShapes.cs).

Its waste-energy fractions in `Cubes.xml` are deliberately zero. The simulation already puts every
watt the block draws into the hot side; a waste fraction on top would charge the same energy twice.

## Extinguisher (hand tool)

A rifle-class hand item that acts as a **thermal scanner**, not a cooling device.

| Definition | File |
| --- | --- |
| `PhysicalGunObject/ExtinguisherGunItem` | [PhysicalItems.sbc](../Data/Extinguisher/PhysicalItems.sbc) |
| `WeaponDefinition/ExtinguisherGun` | [Weapons.sbc](../Data/Extinguisher/Weapons.sbc) |
| `AutomaticRifle/ExtinguisherGun` (hand item) | [HandItems.sbc](../Data/Extinguisher/HandItems.sbc) |
| `AmmoMagazine/BottleMag`, `AmmoDefinition/Bottle` | [AmmoMagazines.sbc](../Data/Extinguisher/AmmoMagazines.sbc), [Ammos.sbc](../Data/Extinguisher/Ammos.sbc) |
| `AudioDefinition/FireExtinguisher` | [Audio.sbc](../Data/Extinguisher/Audio.sbc) |
| `CubeBlock/Extinguisher` (decorative wall block) | [Extinguisher.sbc](../Data/Extinguisher/Extinguisher.sbc) |
| Character animation overrides | [AC_Astronaut.sbc](../Data/Extinguisher/AC_Astronaut.sbc) |

The `Bottle` ammo does zero damage, has zero trajectory and zero impulse — firing only produces
the snow particle effect and the extinguisher sound.

The functional part is in `ThermalHud.DrawToolHud`
([ThermalHud.cs:57](../Data/Scripts/Thermodynamics/ThermalHud.cs#L57)): while the tool is
equipped, a 15 m camera raycast finds the block you are looking at, prints its temperature in
°C at the lower left, and draws heat-coloured billboards over that block and each of its
thermal neighbours using the `GaugeThermalTexture` transparent material
([TransparentMaterials.sbc](../Data/TransparentMaterials.sbc)).

## Cockpit HUD

`ThermalHud.DrawGridHud` ([ThermalHud.cs:95](../Data/Scripts/Thermodynamics/ThermalHud.cs#L95))
shows, whenever the player controls a cube block:

```
ambient    current grid ambient temperature (°C)
peak       hottest block on the grid (°C), tinted by the heat ramp
rate       that block's change over the last step × StepsPerSecond, K/s
critical   count over critical temperature in the last completed pass, red when above zero
loops      number of valid loops on the grid
```

It is a Rich HUD panel at the top right: a background behind the text, because five lines of bare
text over a planet is unreadable whatever drew them, and the label column dimmed so the numbers are
what the eye lands on. Only the two lines that can mean trouble carry colour — the peak temperature
and a non-zero critical count.

Both HUD elements are drawn by the Rich HUD Framework; without Rich HUD Master the framework never
registers and no text appears. The extinguisher's temperature billboard is drawn through the mod API
directly and does not care.

## The terminal readout

Selecting any simulated block in the terminal fills its **detail info** panel — the pane under the
block's name — and it says only what applies to *that* block. The pane is a few lines tall and shares
them with whatever else the block reports, so a line reading `Waste heat: 0.0 kW` on a block that
generates none is a line spent saying nothing. Every section is conditional.

A reactor:

```
Temp     584°C  +0.00 K/s
Critical 927°C
Waste    500.0 kW
```

A coolant pump, which adds its loop's flow rate and coolant temperatures:

```
Temp     169°C  +0.00 K/s
Critical 727°C

Coolant  133°C  (109 - 157)
Flow     -10.0m/s
Transfer 142.9 kW in  142.9 kW out
```

A heat pump reports how far its conditions are from letting it reach full output:

```
Pump     23.1 kW for 20.0 kW   x1.15
Optimal  -48°C
```

**`Optimal` is the gap this pump has left before it stops reaching its rating**, and it goes negative
once the gap is past that. `-48°C` means the two sides are forty-eight degrees further apart than they
need to be — bring either one that far toward the other and the pump reaches its rating. A positive
figure is headroom: how much further apart they could drift before output starts falling.

The pump saturates while `coefficient × power ≥ rating`, and the coefficient is
`fraction × Tcold / gap`, so the widest gap that still saturates it is
`fraction × Tcold × power / rating` — 40 K at a 300 K cold side on the shipped figures. Throttling the
pump narrows that allowance in proportion, and the line follows.

It replaced a sentence naming which limit was binding. That only named a state; this one can be acted
on, which is the difference between a readout and a diagnosis.

An idle pump carries one word for which of three reasons applies — `no block on one face`, `off` or
`unpowered` — because those want three different fixes and no figure separates them.

Coolant is given as a mean with the **range across the ring** in brackets, because with the fluid
carried round in parcels a loop is not one temperature. A wide spread with the pump running means the
flow cannot keep up with the load; a wide spread with no flow means nothing is circulating.

Flow is in **metres per second** — parcels per second is the solver's unit and nobody has any
intuition for it — and the **sign is the direction**, because a ring driven the other way is a working
ring. **A stopped ring reports `0m/s` and nothing else.** It has several causes, pumps switched off,
pumps unpowered, pumps fighting each other, and naming them would cost a line each to say what is
already legible from the figure and the pumps you built.

`Waste`, `Critical` and `Room` appear only when they apply.

The one thing still spelled out is a run of pipe that formed **no loop at all**, which reports the
reason from [the fault table above](#when-a-ring-does-not-become-a-loop). There is no figure to read
in that case: the absence is the whole symptom.

It goes there rather than into a terminal control because the terminal's controls are single-line
fields: a text box handed fifteen lines shows one and a half and clips the rest.
[ThermalTerminal.cs](../Data/Scripts/Thermodynamics/ThermalTerminal.cs) needs no HUD framework at
all, so it is the readout that works in any world.

## Radiators and the heat pump

`Gauge_LG_Radiator` / `Gauge_SG_Radiator` are ordinary blocks with aluminium's specific heat, high
emissivity (0.35 against the 0.125 default) and a surface-area multiplier of 1.25. They have no
coolant ports: a loop sheds heat through one by pressing a pipe's sink face against it, and the
panel then radiates from its own exposed faces. Bolting a radiator flat against the hull removes the
faces it would have radiated from, which is what the `radiator` scenario measures.

`Gauge_LG_HeatPump` / `Gauge_SG_HeatPump` move heat from the block on their front face into the
block behind them, for an electrical cost set by Carnot. They pair naturally with a radiator on the
hot side: the pump concentrates a ship's heat somewhere it can be shed, which is the one thing
radiators alone cannot do when the thing you need cooled is already cooler than its surroundings.
