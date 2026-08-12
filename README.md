# Thermal Dynamics

A Space Engineers mod that simulates heat as a first-class resource. Every block on every
grid is a thermal cell with a mass, a specific heat and a conductivity. Heat flows between
touching blocks, radiates into space, convects into an atmosphere, arrives from the sun,
and — if you let it build up — destroys your ship.

* **Steam Workshop ID:** `2985582372` (see [modinfo.sbmi](modinfo.sbmi))
* **Namespace:** `Thermodynamics`
* **Network channel ID:** `30323`
* **Target:** Space Engineers 1 (`net472`, C# 6)

## What it does

| System | Summary |
| --- | --- |
| Conduction | Heat moves between neighbouring blocks based on shared mount-point surface area, conductivity and thermal mass. |
| Radiation | Every exposed block face radiates to the ambient temperature via the Stefan–Boltzmann law. |
| Convection | Inside an atmosphere, exposed faces exchange heat with the air; wind speed and grid velocity raise the coefficient. |
| Solar | Grids raycast to the sun; unoccluded exposed faces absorb energy weighted by their facing angle. |
| Aerodynamic friction | Fast atmospheric flight heats leading surfaces (currently computed but not applied — see [Known issues](docs/known-issues.md)). |
| Waste heat | Power producers and consumers convert a configurable share of their throughput into heat; thrusters heat with throttle. |
| Coolant loops | Closed loops of coolant pipe blocks with at least one pump form a shared thermal reservoir that pulls heat out of adjacent blocks. |
| Damage | Blocks above their critical temperature take continuous damage. |
| Airtightness mapping | A per-grid flood fill classifies every cell as external or part of a sealed room, which determines what counts as an "exposed" surface. |

## Required dependencies

Thermal Dynamics does not work standalone. Both of these must be loaded in the world:

| Mod | Used for | Handshake ID |
| --- | --- | --- |
| **Definition Extensions** (Draygo) | Reads all thermal properties out of `<ModExtensions>` blocks in the SBC/XML definitions. Without it, block definition lookups throw. | `2756894170` |
| **Text HUD API** (Draygo) | Draws the in-cockpit thermal readout and the extinguisher tool readout. Degrades quietly if absent. | `573804956` |

## Documentation

| Document | Contents |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Component layout, update order, grid lifecycle, save format. |
| [docs/thermal-model.md](docs/thermal-model.md) | Every equation the simulation actually evaluates, with the source lines. |
| [docs/definitions.md](docs/definitions.md) | The `ThermalBlockProperties`, `ThermalPlanetProperties` and `ThermalLoopProperties` reference, and how to add support for another mod's blocks. |
| [docs/blocks.md](docs/blocks.md) | The blocks and items this mod ships, and the coolant-loop build rules. |
| [docs/surface-mapping.md](docs/surface-mapping.md) | The surface bit-flag format and the room flood fill. |
| [docs/configuration.md](docs/configuration.md) | Every setting, its default, and what the debug toggles draw. |
| [docs/development.md](docs/development.md) | Building, deploying, repo layout, and conventions. |
| [docs/known-issues.md](docs/known-issues.md) | Confirmed bugs, dead code and unfinished systems found in the current tree. |
| [docs/bugs-and-performance.md](docs/bugs-and-performance.md) | Defects and performance findings from putting the simulation under test, with a suggested order of work. |
| [docs/engine-api-notes.md](docs/engine-api-notes.md) | Survey of the game assemblies: engine APIs this mod reimplements by hand, and what it could use instead. |
| [docs/se2-research.md](docs/se2-research.md) | What the Space Engineers 2 assemblies contain: platform, the block octree, variable block sizes, and what a future SE2 adapter would bind to. |
| [docs/model-redesign.md](docs/model-redesign.md) | Feature inventory and the data-structure changes needed to serve both games, with per-feature must-haves and available shortcuts. |
| [docs/scale-design.md](docs/scale-design.md) | Design for grids up to a million blocks: chunking, activity tracking, lumping, multirate stepping and the memory budget. |
| [sim/README.md](sim/README.md) | The isolated simulation environment: how to run the tests and scenarios. |

## Rewrite in progress

The simulation is being rebuilt as a pure, testable core under
[Data/Scripts/Thermodynamics/Core/](Data/Scripts/Thermodynamics/Core) with no dependency on the
game session. It ships with the mod — Space Engineers compiles it — and the projects under
[sim/](sim) link the same files so it can be built, tested and profiled outside the game:

```bash
cd sim && dotnet test                                  # 224 tests
dotnet run --project Thermodynamics.Sim -- run all     # scenario suite
```

The live mod still runs on the original code in `Data/Scripts/Thermodynamics/`; nothing in
`Core/` is wired into it yet.

## Quick start (in game)

1. Subscribe to Definition Extensions and Text HUD API, then enable all three mods.
2. Load a world and build anything. Heat starts simulating immediately — the debug block
   colouring is **on by default**, so your grid will be recoloured by temperature. Turn
   `DebugTemperatureBlockColors` off in the config to stop that (see
   [docs/configuration.md](docs/configuration.md)).
3. Sit in a cockpit to see ambient temperature, peak block temperature, peak rate of
   change, critical block count and coolant loop count in the top right.
4. Equip the **Extinguisher** hand tool and look at a block to read its temperature and see
   it and its neighbours highlighted by heat.
5. To cool a hot subsystem, build a **closed** ring of coolant pipes containing at least one
   **Coolant Pump**, with sink faces pressed against the blocks you want to cool.

## Repository layout

```
ThermalDynamics/
├── Data/
│   ├── Cubes.xml                 ModExtensions thermal properties per block subtype
│   ├── Planets.xml               ModExtensions planet climate properties
│   ├── Loops.xml                 ModExtensions coolant loop properties
│   ├── EntityComponents.sbc      Registers the two mod-storage GUIDs used for saving
│   ├── TransparentMaterials.sbc  The billboard material used for heat highlights
│   ├── CubeBlocks/               Block definitions (coolant pipes, pumps, radiator, heat pump)
│   ├── Extinguisher/             Hand tool: weapon, ammo, hand item, audio, decorative block
│   ├── Localization/             DisplayName/Description strings
│   └── Scripts/Thermodynamics/   All C# source
├── Models/                       .mwm models — do not restructure, LOD paths are baked in
├── Textures/                     Block, decal and particle textures
├── Audio/                        FireExtinguisher.wav
└── docs/                         This documentation
```
