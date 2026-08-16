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

`Gauge_LG_HeatPump` / `Gauge_SG_HeatPump` — a plain `CubeBlock`, 1×1×1.

**Not yet implemented.** The models, icons, localisation, block definitions and thermal
properties all exist, but no C# references it, so it currently behaves as an ordinary
conducting block with default-ish properties. It is not in the coolant direction tables and is
not part of the coolant variant groups.

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
Ambient:          current grid ambient temperature (°C)
Peak T:           hottest block on the grid (°C), text tinted by the heat ramp
Peak dT/s:        that block's temperature change over the last step × StepsPerSecond, K/s
Critical Blocks:  count over critical temperature in the last completed pass
Coolant Loops:    number of valid loops on the grid
```

Both HUD elements require Text HUD API; without it `HudInit` never fires and nothing is drawn. The
terminal readout ([ThermalTerminal.cs](../Data/Scripts/Thermodynamics/ThermalTerminal.cs)) and the
thermal vision overlay ([ThermalVision.cs](../Data/Scripts/Thermodynamics/ThermalVision.cs)) have no
such dependency.

## Thermal vision

`/thermal vision`, or the button on any block's terminal, switches the view to a thermal one.
`/thermal greyscale` switches between the ironbow palette and white-hot.

A mod has no shader, no post-process and no frame buffer, so the ordinary view cannot be
recoloured. It is replaced: a black billboard at the back of the scene removes the rendered world,
and every body with a temperature is drawn in front of it as a billboard of its own. Nothing the
game renders survives, so nothing of it can occlude the thermal image or show through it.

### The projection

Nothing is drawn at its real distance. Each billboard is scaled about the camera onto a shallow
band 2–502 m deep, keeping its direction and shrinking its size by the same factor. A perspective
projection is invariant under scaling about the eye, so the image on screen is unchanged — but the
depth order now belongs to the mod rather than to the scene. Near things land near, far things land
far, the renderer sorts them among themselves, and the blackout sits behind the whole band where it
cannot come out in front of what it is hiding.

The mapping is `depth → near + span × d/(d + 300)`: monotonic, so real depth order is preserved, and
bounded, so nothing can land behind the blackout.

### What is drawn

| Body | As |
| --- | --- |
| Ground | Patches laid flat on the surface, sampled in rings out to about half a kilometre. Slopes facing the sun read warm, shadowed slopes cold, so terrain keeps its relief. |
| Sun | A disc at the back of the band with a wide bloom. |
| Planets | A disc at surface temperature, from outside the atmosphere. Inside it, the ground is the planet. |
| Asteroids | A disc at ambient. A voxel body has no shape a mod can draw, and a hole would read as open space. |
| Grids | The outer skin, one quad per exposed block face. |
| Characters | A body at `ThermalVisionBodyTemperature`. |
| Heat sources | A glowing point sized by output. |

**Only surfaces are drawn.** A block face appears when the simulation says that face is exposed —
the same figure radiation is computed from — and when it is turned toward the camera. A block buried
in the hull has no exposed face and is never drawn, so there is no x-ray: a solid hull is a solid
picture. The skin costs only the faces that can be seen, not a draw per block.

`ThermalVisionMinKelvin` and `ThermalVisionMaxKelvin` are the sensor's span. A real camera has no
absolute colours: it stretches its palette between two temperatures the operator chooses, and
anything outside clips to black or white. Narrow the span to pull detail out of a cold hull; widen
it to keep a reactor from washing the frame out.

Everything is client side and per frame. There is no engine state to restore, which is what
separates it from `DebugTemperatureBlockColors`.

## Radiators and the heat pump

`Gauge_LG_Radiator` / `Gauge_SG_Radiator` are ordinary blocks with aluminium's specific heat, high
emissivity (0.35 against the 0.125 default) and a surface-area multiplier of 1.25. They have no
coolant ports: a loop sheds heat through one by pressing a pipe's sink face against it, and the
panel then radiates from its own exposed faces. Bolting a radiator flat against the hull removes the
faces it would have radiated from, which is what the `radiator` scenario measures.

`Gauge_LG_HeatPump` / `Gauge_SG_HeatPump` currently have no behaviour beyond being blocks with steel
properties — see [known-issues.md](known-issues.md#unfinished).
