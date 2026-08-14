# Thermal Dynamics

A Space Engineers mod that simulates heat as a first-class resource, built as a framework other
mods can stand on. Every block is a thermal node with mass, specific heat, conductivity, emissivity
and a critical temperature. Heat conducts between touching blocks, radiates into space, convects
into atmosphere and into the air of sealed rooms, arrives from the sun, and — if you let it build up
— destroys your ship.

* **Steam Workshop ID:** `2985582372` (see [modinfo.sbmi](modinfo.sbmi))
* **Namespace:** `Thermodynamics`
* **Target:** Space Engineers 1 (`net48`, C# 6)

## Design goals

* **Light.** A grid's cost is one pass over its links per substep, and nothing else. Every readout,
  diagnostic and overlay is off unless something is reading it.
* **Isolated.** Every mechanism has its own switch, and switching one off removes exactly its own
  cost. Switches take effect on the next step, with no reload.
* **Tested.** The simulation is a pure library with no dependency on the game session, built and
  tested outside it — 437 tests and 20 deterministic scenarios.
* **Open.** Everything the simulation knows is readable and everything it does is drivable from
  another mod, through a delegate table passed by mod message. See [docs/api.md](docs/api.md).

## What it does

| System | Summary |
| --- | --- |
| Conduction | Heat flows between blocks over the area where both carry a mount surface, as two conductors in series. Crosses rotors and pistons between grids. |
| Radiation | Every exposed face radiates to ambient by Stefan–Boltzmann. |
| Convection | Exposed faces exchange with the surrounding air; wind and grid velocity raise the coefficient. |
| Room air | Sealed rooms hold an air mass that couples every surface bounding them — the only path between two walls that do not touch. Pressure comes from air vents. |
| Solar | Grids raycast to the sun; unoccluded faces absorb energy weighted by facing angle. |
| Point sources | Other mods register heat sources bound to an entity or a position; they radiate like small suns. |
| Aerodynamic friction | Fast atmospheric flight heats leading surfaces with the cube of relative airspeed. |
| Waste heat | Power producers and consumers convert a configurable share of throughput into heat; thrusters heat with throttle, which is what makes hydrogen thrusters run hot. |
| Coolant loops | Closed rings of coolant pipe with a pump form a shared fluid mass that pulls heat out of adjacent blocks; radiators shed it to space. |
| Damage and thresholds | Blocks above their critical temperature take continuous damage. Any other temperature can be watched by another mod. |
| Airtightness mapping | A per-grid flood fill classifies every cell as external, structure or room, which decides what counts as an exposed surface. Doors are portals, so cycling one costs a walk over the doors rather than a remap. |
| Readouts | Terminal panel per block, cockpit summary, crosshair readout, and a non-destructive thermal vision overlay in heat or greyscale. |

## Required dependencies

| Mod | Used for | Workshop ID |
| --- | --- | --- |
| **Definition Extensions** (Draygo) | Reads all thermal properties out of `<ModExtensions>` blocks in the definitions. Required — without it block lookups throw. | `2756894170` |
| **Text HUD API** (Draygo) | The cockpit readout and the extinguisher readout. Degrades quietly if absent. | `573804956` |

## Quick start

1. Subscribe to Definition Extensions and Text HUD API, and enable all three mods.
2. Load a world and build. Heat simulates immediately; nothing is repainted and nothing is drawn
   until you ask for it.
3. Sit in a cockpit for ambient, peak temperature, peak rate of change, critical block count and
   coolant loop count.
4. Open any block's terminal for its own temperature, rate of change, exposed area, waste heat and
   the room it bounds.
5. Type `/thermal vision` for the heat overlay, `/thermal greyscale` to switch palette.
6. To cool a hot subsystem, build a **closed** ring of coolant pipes containing at least one
   **Coolant Pump**, with sink faces pressed against the blocks to cool — and, if you want the heat
   gone rather than moved, against a **Radiator** with a clear view of space.
7. `/thermal settings` lists every switch; `/thermal set <name> <value>` changes one live.

## Documentation

| Document | Contents |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Component layout, update order, grid lifecycle, persistence. |
| [docs/thermal-model.md](docs/thermal-model.md) | Every equation the simulation evaluates, with its source. |
| [docs/surface-mapping.md](docs/surface-mapping.md) | Surface bit format, the room flood fill, portals, and room air. |
| [docs/api.md](docs/api.md) | The mod API: reading, writing, heat sources, thresholds, settings. |
| [docs/definitions.md](docs/definitions.md) | Block, planet and loop properties, and how to add support for another mod's blocks. |
| [docs/blocks.md](docs/blocks.md) | The blocks and items this mod ships, and the coolant loop build rules. |
| [docs/configuration.md](docs/configuration.md) | Every setting, its default, and the runtime commands. |
| [docs/telemetry.md](docs/telemetry.md) | Session data collection and what the report contains. |
| [docs/development.md](docs/development.md) | Building, deploying, repo layout, conventions. |
| [docs/known-issues.md](docs/known-issues.md) | Confirmed defects, unfinished systems and deliberate limits. |
| [docs/bugs-and-performance.md](docs/bugs-and-performance.md) | Findings from putting the simulation under test. |
| [docs/engine-api-notes.md](docs/engine-api-notes.md) | Engine APIs this mod reimplements by hand, and what it could use instead. |
| [docs/se2-research.md](docs/se2-research.md) | What the Space Engineers 2 assemblies contain, and what an SE2 adapter would bind to. |
| [docs/model-redesign.md](docs/model-redesign.md) | Feature inventory and the data-structure changes behind the current model. |
| [docs/scale-design.md](docs/scale-design.md) | Design for grids up to a million blocks. |
| [sim/README.md](sim/README.md) | The isolated simulation environment: running the tests and scenarios. |

## Building and testing

The simulation core lives under
[Data/Scripts/Thermodynamics/Core/](Data/Scripts/Thermodynamics/Core) and ships with the mod — the
game compiles it. The projects under [sim/](sim) link the same files so it can be built, tested and
profiled outside the game:

```bash
cd sim && dotnet test                                  # 437 tests
dotnet run --project Thermodynamics.Sim -- run all     # scenario suite
```

The mod as a whole builds against the installed game assemblies:

```bash
dotnet build Generic.csproj
```

## Repository layout

```
ThermalDynamics/
├── Data/
│   ├── Cubes.xml                 ModExtensions thermal properties per block subtype
│   ├── Planets.xml               ModExtensions planet climate properties
│   ├── Loops.xml                 ModExtensions coolant loop properties
│   ├── EntityComponents.sbc      Registers the mod-storage GUID used for saving
│   ├── TransparentMaterials.sbc  The billboard material used for heat overlays
│   ├── CubeBlocks/               Block definitions (coolant pipes, pumps, radiator, heat pump)
│   ├── Extinguisher/             Hand tool: weapon, ammo, hand item, audio, decorative block
│   ├── Localization/             DisplayName/Description strings
│   └── Scripts/Thermodynamics/   All C# source
├── Models/                       .mwm models — do not restructure, LOD paths are baked in
├── Textures/                     Block, decal and particle textures
├── Audio/                        FireExtinguisher.wav
├── sim/                          Isolated build, tests and scenarios
└── docs/                         This documentation
```
