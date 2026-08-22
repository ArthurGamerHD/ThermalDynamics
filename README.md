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
  tested outside it — 1,532 tests, 33 deterministic scenarios and a load benchmark that goes to a
  million blocks in one grid.
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
| Climate | Ambient follows latitude, the ground underfoot, the hour, and how far above sea level it is, chasing its target with a lag so the day peaks after noon. |
| Weather | Rain, snow, storms and fog cool or warm the air, dim the sun, drive the wind and strip heat off a hull faster — read from the game's own authored figures per weather type. |
| Underground | Depth blunts the day out over tens of metres, then the rock warms toward the planet's core below the sea-level deadzone. |
| Point sources | Other mods register heat sources bound to an entity or a position; they radiate like small suns. |
| Aerodynamic friction | Fast atmospheric flight heats leading surfaces with the cube of relative airspeed. |
| Waste heat | Power producers and consumers convert a configurable share of throughput into heat; thrusters heat with throttle, which is what makes hydrogen thrusters run hot. |
| Coolant loops | Closed rings of coolant pipe with a pump form a shared fluid mass that pulls heat out of adjacent blocks; radiators shed it to space. |
| Heat pumps | The one block that moves heat *up* a gradient, for an electrical cost set by Carnot: cheap across a small difference, ruinous across a large one. |
| Damage and thresholds | Blocks above their critical temperature take continuous damage. Any other temperature can be watched by another mod. |
| Airtightness mapping | A per-grid flood fill classifies every cell as external, structure or room, which decides what counts as an exposed surface. Doors are portals, so cycling one costs a walk over the doors rather than a remap. |
| Readouts | Terminal panel per block, cockpit summary, crosshair readout, and an x-ray block overlay — every block of the ship in front of you drawn as a box coloured by temperature, exposed faces or friction watts, a solar view that shades the ship's skin face by face — plus a room view that draws the mapped air itself, cycled with Ctrl+Shift+=, with a readout panel of the figures behind whichever view is up. |

## Required dependencies

| Mod | Used for | Workshop ID |
| --- | --- | --- |
| **Definition Extensions** (Draygo) | Reads all thermal properties out of `<ModExtensions>` blocks in the definitions. Required — without it block lookups throw. | `2756894170` |
| **Rich HUD Master** (Zach Hembree) | Every piece of text the mod draws on screen: cockpit summary, extinguisher readout, settings menu, debug readout. Degrades quietly if absent — the simulation, the terminal readouts, the chat commands and the overlay all still work. | `1965654081` |

## Quick start

1. Subscribe to Definition Extensions and Rich HUD Master, and enable all three mods.
2. Load a world and build. Heat simulates immediately; nothing is repainted and nothing is drawn
   until you ask for it.
3. Sit in a cockpit for ambient, peak temperature, peak rate of change, critical block count and
   coolant loop count.
4. Open any block's terminal for its own temperature, rate of change, exposed area, waste heat and
   the room it bounds.
5. To cool a hot subsystem, build a **closed** ring of coolant pipes containing at least one
   **Coolant Pump**, with sink faces pressed against the blocks to cool — and, if you want the heat
   gone rather than moved, against a **Radiator** with a clear view of space.
6. To cool something below what surrounds it, sandwich a **Heat Pump** between it and a radiator:
   the pump's front face draws heat out, its back face rejects that heat plus the power it took.
7. Press **Ctrl+Shift+=** to cycle the block overlay through its views, and again to switch it off.
8. Press **Ctrl+Shift+S** for the settings menu: every value in the config file, with a slider or a
   switch and a description of what it does.
9. `/thermal settings` lists every switch; `/thermal set <name> <value>` changes one live.
10. `/thermal profile` lists the five presets; `/thermal profile arcade` applies one live. They
    are a ladder on two axes — how faithfully the simulation is integrated, and how fast heat is
    made to move. **A fresh world runs `responsive`**: simulation's integration with the clock run
    at the tuned pace. `simulation` itself is real time, which is physically honest and far too
    slow to play on. See [docs/profiles.md](docs/profiles.md).

## Documentation

Every page opens with what it covers and closes with a change log; history lives there rather than
in the prose.

**Start here**

| Document | Contents |
| --- | --- |
| [docs/document-of-intent.md](docs/document-of-intent.md) | What the mod is for and the goals it is measured against, where those goals conflict with the code, and where no intent has been stated at all. |
| [docs/rules.md](docs/rules.md) | The standing rules, in one place: fourteen principles, the rules that follow from them, and whether each is load-bearing, conditional or not worth keeping. |
| [docs/architecture.md](docs/architecture.md) | Component layout, update order, grid lifecycle, persistence. |
| [docs/backlog.md](docs/backlog.md) | Every open item across these documents, categorised, one line each. |

**The simulation**

| Document | Contents |
| --- | --- |
| [docs/thermal-model.md](docs/thermal-model.md) | Every equation the simulation evaluates, and the surface geometry every area term reads. |
| [docs/environment.md](docs/environment.md) | The air, ground, sun and wind outside a grid: how each is computed and what evidence stands behind it. |
| [docs/scale-design.md](docs/scale-design.md) | Where the model is going: variable block sizes, and grids to a million blocks. |

**Using it**

| Document | Contents |
| --- | --- |
| [docs/blocks.md](docs/blocks.md) | The blocks and items this mod ships, and the coolant loop build rules. |
| [docs/configuration.md](docs/configuration.md) | Every setting, its default, the runtime commands, and where the settings surface is going. |
| [docs/profiles.md](docs/profiles.md) | The five presets, as a ladder on two axes. |
| [docs/definitions.md](docs/definitions.md) | Block, planet and loop properties, and how to add support for another mod's blocks. |
| [docs/api.md](docs/api.md) | The mod API: reading, writing, heat sources, thresholds, settings. |

**Measurement and evidence**

| Document | Contents |
| --- | --- |
| [docs/telemetry.md](docs/telemetry.md) | Session data collection and what the report contains. |
| [docs/benchmarks.md](docs/benchmarks.md) | The performance report: cost by size, feature and configuration; what a substep costs; and the trend across passes. |
| [docs/load-and-hitching.md](docs/load-and-hitching.md) | What a grid costs as it grows, what makes it stutter, and what live worlds measure. |
| [docs/stiffness.md](docs/stiffness.md) | Why a handful of light fittings sets the cost of a capital ship, and what to do about it. |
| [docs/memory.md](docs/memory.md) | Where a grid's memory goes, and what can be given back. |
| [docs/balance-lab.md](docs/balance-lab.md) | Deciding good balance from a population of real ships: criteria, staging, and the corpus. |
| [docs/balance.md](docs/balance.md) | Every block costed against the vanilla blocks it competes with, and what 8,132 real ships say about the targets. |

**Working on it**

| Document | Contents |
| --- | --- |
| [docs/development.md](docs/development.md) | Building, deploying, repo layout, conventions. |
| [docs/known-issues.md](docs/known-issues.md) | Deliberate limits, open defects, and the failure patterns worth carrying forward. |
| [docs/engine-notes.md](docs/engine-notes.md) | What both engines actually provide, and what an SE2 adapter would bind to. |
| [tests/README.md](tests/README.md) | The isolated simulation environment: running the tests and scenarios. |
| [tools/corpus/README.md](tools/corpus/README.md) | The corpus tooling: the verdict script and the report builder. |

## Building and testing

The simulation core lives under
[Data/Scripts/Thermodynamics/Core/](Data/Scripts/Thermodynamics/Core) and ships with the mod — the
game compiles it. The projects under [tests/](tests) link the same files so it can be built, tested and
profiled outside the game:

```bash
cd tests && dotnet test                                  # 1,532 tests
dotnet run --project Thermodynamics.Sim -- run all     # scenario suite
dotnet run --project Thermodynamics.Sim -- bench scale # cost from 8k to 1M blocks
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
│   ├── TransparentMaterials.sbc  The billboard material used by the extinguisher overlay
│   ├── CubeBlocks/               Block definitions (coolant pipes, pumps, radiator, heat pump)
│   ├── Extinguisher/             Hand tool: weapon, ammo, hand item, audio, decorative block
│   ├── Localization/             DisplayName/Description strings
│   └── Scripts/Thermodynamics/   All C# source
├── Models/                       .mwm models — do not restructure, LOD paths are baked in
├── Textures/                     Block, decal and particle textures
├── Audio/                        FireExtinguisher.wav
├── tests/                          Isolated build, tests and scenarios
└── docs/                         This documentation
```

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-22 | Rebuilt the documentation index around what a reader is trying to do rather than the order pages were written, after a defragmentation pass took the documentation from 31 pages to 21. Every page now opens with its scope and closes with a change log; the conventions are in [docs/development.md](docs/development.md#documentation-conventions) and checked by `EveryPageHasAChangeLog`. |
| 2026-08-21 | Made the quoted suite size a claim the suite checks, and fixed the sixteen dead documentation links a first check found. |
| 2026-08-20 | Pointed the READMEs at the corpus and the lab. |
| 2026-08-12 | Opened the repository. |
