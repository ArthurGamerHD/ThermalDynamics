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
them. A pipe with no sinks is plumbing only. The direction tables are
`CoolantPlateDirections` and `CoolantPipeLinkDirections` in
[ThermalGridLoop.cs](../Data/Scripts/Thermodynamics/ThermalGridLoop.cs) — **these are the
authority on loop behaviour, and any new pipe subtype must be added to both.**

Connection directions (before block orientation is applied):

| Shape | Links |
| --- | --- |
| Straight (all variants) and pump | Forward ↔ Backward |
| Corner (all variants) | Forward ↔ Left |

The small-grid pump is three cells long, so the crawler multiplies its positive link offset by
3 to step past its own body — this is special-cased on the subtype name in both
`StartCoolantCrawl` and `CoolantCrawl`.

The pipes and pump are grouped into the `CoolantGroupLarge` / `CoolantGroupSmall` block variant
groups ([BlockVarientGroups.sbc](../Data/CubeBlocks/BlockVarientGroups.sbc)) so they cycle on
one toolbar slot.

## Coolant loop rules

`ThermalGrid.StartCoolantCrawl`
([ThermalGridLoop.cs:90](../Data/Scripts/Thermodynamics/ThermalGridLoop.cs#L90)) runs whenever a
pipe block is added. A loop is only created when **all** of these hold:

1. Starting from the new pipe and following link directions, the crawl returns to the starting
   block — the run must be a **closed ring**. A dead end returns nothing and no loop forms.
2. The ring contains at least one **Coolant Pump** (`Gauge_LG_CoolantPump` or
   `Gauge_SG_CoolantPump`).
3. Every block in the ring is a known coolant pipe subtype.

Removing any pipe in a loop destroys that whole loop
(`OnRemoveDoCoolantCheck`). The loop is not rebuilt until a pipe is placed again, so repairing
a broken ring means placing the final block last.

Once formed, the loop is one lumped coolant mass shared by every segment: it draws heat from
the pipe blocks themselves and, through each sink face, from whatever block is mounted against
that face. See [thermal-model.md](thermal-model.md#coolant-loops) for the transfer equations.

Practical build advice:

* Run the loop *through* your heat sources with sink faces against reactors, thrusters and
  batteries, then out to radiators or a cold hull section.
* Loop length does not increase total transfer — the coupling constants divide by segment
  count. Longer loops spread the same cooling over more contact points.
* A loop's temperature is saved and restored, but by **list index**, so loops can swap
  temperatures across a reload if the crawl order changes.

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
Peak dT:          that block's (conduction + generation) delta × PerSecond, i.e. K/s
Critical Blocks:  count over critical temperature in the last completed pass
Coolant Loops:    number of valid loops on the grid
```

Both HUD elements require Text HUD API; without it `HudInit` never fires and nothing is drawn.
