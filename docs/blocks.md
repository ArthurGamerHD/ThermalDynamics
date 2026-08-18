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
* Loop length does not increase total transfer — the coupling constants divide by segment
  count. Longer loops spread the same cooling over more contact points.
* A loop's temperature is saved and restored by member hash, so reloading cannot swap two loops'
  heat and rebuilding a ring does not reset it.

## Radiator

`Gauge_LG_Radiator` / `Gauge_SG_Radiator` — a plain `CubeBlock`, 1×5×2 (LG), mounting only on
Top and Bottom.

It has **no script behaviour**. It is purely a definition-driven heat shedder: low specific
heat (1), high emissivity (0.35) and a 1.25× surface area scaler, so it radiates faster than
any armour block of comparable mass. To use it, conduct heat into it — mount it on a coolant
pipe sink face or directly against a hot block — and keep its faces exposed to vacuum.

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
block's name — with that block's temperature, rate of change, critical point, exposed faces and
area, waste heat, the room it bounds, what its heat pump is achieving if it has one, and a short
summary of the grid. It refreshes while the panel is open.

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
