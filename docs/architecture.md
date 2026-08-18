# Architecture

The mod is in two halves, and the line between them is the point of the design.

| Half | Where | Knows about |
| --- | --- | --- |
| **The model** | [`Data/Scripts/Thermodynamics/Core/`](../Data/Scripts/Thermodynamics/Core) | Blocks, heat, geometry. One Space Engineers assembly, `VRage.Math`, for vectors. No session, no entity, no `MyAPIGateway`. |
| **The adapter** | everything else under [`Data/Scripts/Thermodynamics/`](../Data/Scripts/Thermodynamics) | Definitions, entity events, raycasts, damage, storage, HUD, terminal, mod API. |

The model is what [`sim/`](../sim) builds and tests outside the game; the adapter is what the game
compiles around it. Everything the game supplies crosses one of three boundaries — block layout, an
environment sample, and results out — and nothing else.

## The model

| Area | Types |
| --- | --- |
| Layout | `GridModel`, `BlockInstance`, `BlockModel`, `BlockOrientation`, `CellSurface`, `BlockSurfaceBuilder`, `CoolantShape`, `IBlockAdjacency` |
| Simulation | `ThermalSimulation`, `ThermalSolver`, `ThermalNode`, `ThermalLink`, `SimulationScheduler`, `ThermalThresholds`, `EnvironmentSample` / `EnvironmentState` / `EnvironmentSolver`, `ISimulationProfiler` |
| Climate | `ClimateModel` (latitude, ground, lag), `WindField` (the wind map the game lacks), `TerrainHorizon` (ground shadowing the sun), `SolarOcclusionSampler` (where to cast occlusion rays from) |
| Shadow | `SunShadowMap` (a grid's own shadow, and its neighbours'), `VoxelWalk` (ray through a grid's cells, from any point) |
| Surfaces | `SurfaceMap`, `RoomMapper`, `RoomMap`, `RoomPortal`, `RoomAirNode`, `RoomPressure`, `SurfaceAudit` |
| Loops | `CoolantLoop`, `CoolantLoopBuilder` |
| Devices | `HeatPumpShape`, `HeatPumpDevice` |
| Definitions | `BlockThermalProperties`, `LoopThermalProperties`, `PlanetThermalProperties`, `GroundTemperature`, `ThermalSettings` |
| Storage | `ThermalStorageCodec` |
| Maths | `BoxGeometry`, `GridMath`, `Face`, `OcclusionMath`, `TemperatureScale`, `ThermalConstants` |

`ThermalSimulation` is the whole surface a host needs: `AddBlock`, `RemoveBlock`, `RefreshBlock`,
`RefreshBlockSealing`, `Update`, `StepExact`, `Save`, `Load`, `SetRoomPressure`, and read back node
temperatures, overheat events and threshold crossings.

## The adapter

| File | Type | Role |
| --- | --- | --- |
| [Session.cs](../Data/Scripts/Thermodynamics/Session.cs) | `MySessionComponentBase` | Loads settings, boots the Definition Extensions client, the HUD, the terminal controls and the mod API. Runs the chat commands, cross-grid conduction and the debug overlay. |
| [PlanetManager.cs](../Data/Scripts/Thermodynamics/PlanetManager.cs) | `MySessionComponentBase` | Which planet is closest to a point. |
| [Game/ThermalGrid.cs](../Data/Scripts/Thermodynamics/Game/ThermalGrid.cs) | `MyGameLogicComponent` | The per-grid root: mirrors the game grid into a `GridModel`, owns the `ThermalSimulation`, wires block and grid events. `partial`, split across four more files. |
| [Game/ThermalGridSimulation.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridSimulation.cs) | `partial` | The tick: sample, step, apply damage, raise crossings, refresh readouts. |
| [Game/ThermalGridEnvironment.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridEnvironment.cs) | `partial` | Builds the `EnvironmentSample`: planet, air, wind, sun, occlusion, registered heat sources. |
| [Game/ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridStorage.cs) | `partial` | Save and load through the model's codec. |
| [Game/ThermalBlock.cs](../Data/Scripts/Thermodynamics/Game/ThermalBlock.cs) | class | One placed block bound to one solver node. Pushes power, thrust, door state and mass into the model by event. |
| [Game/ThermalBlockCatalog.cs](../Data/Scripts/Thermodynamics/Game/ThermalBlockCatalog.cs) | static | Block definition → `BlockModel`, once per definition per session. |
| [Game/ThermalCoolantShapes.cs](../Data/Scripts/Thermodynamics/Game/ThermalCoolantShapes.cs) | static | Subtype → coolant plumbing. |
| [Game/ThermalHeatPumpShapes.cs](../Data/Scripts/Thermodynamics/Game/ThermalHeatPumpShapes.cs) | static | Subtype → heat-pump faces and ratings. |
| [Game/ThermalHeatPumpBlock.cs](../Data/Scripts/Thermodynamics/Game/ThermalHeatPumpBlock.cs) | `MyGameLogicComponent` | The electrical half of a heat pump: the resource sink it draws through, and the switch that runs it. |
| [Game/ThermalBridges.cs](../Data/Scripts/Thermodynamics/Game/ThermalBridges.cs) | static | Conduction across a rotor or piston, between two grids. |
| [Game/ThermalHeatSources.cs](../Data/Scripts/Thermodynamics/Game/ThermalHeatSources.cs) | static | Registered point heat sources, and their irradiance at a grid. |
| [ThermalApi.cs](../Data/Scripts/Thermodynamics/ThermalApi.cs) | static | The mod-facing delegate table. See [api.md](api.md). |
| [ThermalTerminal.cs](../Data/Scripts/Thermodynamics/ThermalTerminal.cs) | static | Thermal readout in every block's terminal. |
| [ThermalHud.cs](../Data/Scripts/Thermodynamics/ThermalHud.cs) | static | Cockpit summary and extinguisher readout, on Rich HUD, plus the extinguisher billboard. |
| [ThermalDebugView.cs](../Data/Scripts/Thermodynamics/ThermalDebugView.cs) | static | The x-ray overlay: a coloured box per block, or per room cell, cycled with Ctrl+Shift+=. |
| [ThermalSettingsMenu.cs](../Data/Scripts/Thermodynamics/ThermalSettingsMenu.cs) | static | The Rich HUD settings menu, generated from `Settings.Names()`. Opened with Ctrl+Shift+S. Owns the framework registration. |
| [ThermalDebugPanel.cs](../Data/Scripts/Thermodynamics/ThermalDebugPanel.cs) | static | The Rich HUD readout beside the overlay: per-view figures for the grid being drawn. |
| [Debug.cs](../Data/Scripts/Thermodynamics/Debug.cs) | static | The crosshair readout. |
| [Settings.cs](../Data/Scripts/Thermodynamics/Settings.cs) | class | Config file, defaults, access by name, and the write-through to the model's settings. |
| [Definitions/](../Data/Scripts/Thermodynamics/Definitions) | classes | Typed readers over Definition Extensions. |
| [Telemetry/](../Data/Scripts/Thermodynamics/Telemetry) | static + records | Data collection. See [telemetry.md](telemetry.md). |
| [DefinitionExtensionsAPI.cs](../Data/Scripts/Thermodynamics/DefinitionExtensionsAPI.cs), [NetworkAPI/](../Data/Scripts/Thermodynamics/NetworkAPI), [RichHudFramework/](../Data/Scripts/Thermodynamics/RichHudFramework) | vendored | Third-party API clients. Do not edit; replace wholesale when upstream updates. |

## Update order

```
Session.Simulate()                      every frame
  ├─ chat command registration
  ├─ keybind poll                       client only, Ctrl+Shift+= overlay, Ctrl+Shift+S menu
  ├─ ThermalBridges.Update()            every 10th frame: conduction across rotors and pistons
  └─ Debug.ShowDebugInfo()              client only, behind DebugTextOnScreen

Session.Draw()                          client only
  ├─ ThermalHud.Draw()
  ├─ ThermalDebugView.Draw()            client only, off unless a mode is selected
  └─ ThermalDebugPanel.Update()         the readout beside it; sweeps the grid a few times a second

per grid, every 10th frame:
ThermalGridScheduler.Tick()   — every frame, every grid
  ├─ scheduler.WouldStep()?             no  → skip sampling entirely
  ├─ Sample()                           planet, air, wind, sun, occlusion, heat sources
  ├─ push heat pump state                switch and available power, before the step spends it
  ├─ Simulation.Update(dt, sample)
  │    ├─ settings revision check       rescale capacities, rebuild loops and room air
  │    ├─ topology rebuild              only after a block change
  │    ├─ room mapping                  one budgeted slice
  │    ├─ exposure + room air refresh   only after a mapping pass completes
  │    └─ solver.Step() × steps due     substepped for stability
  └─ AfterSteps()
       ├─ apply overheat damage         server only
       ├─ raise threshold crossings     to registered mods
       ├─ publish heat pump demand      what each pump wants to draw, into its resource sink
       ├─ mass sweep                    every 8 steps
       ├─ room pressure sweep           every 8 steps, grids with air vents only
       ├─ hottest block                 every 4 steps, and only if something will read it
       ├─ telemetry                     only while collection is on
       └─ debug colouring               only while a debug toggle is on
```

The grid polls on the ten-frame tick. How often the simulation *steps* is `SimulationScheduler`'s
business — `Frequency × SimulationSpeed` steps per real second, with fractional credit carried
between ticks — so polling faster would only add entity update callbacks.

## Grid lifecycle

`ThermalGrid.Init` attaches:

| Event | Effect |
| --- | --- |
| `OnBlockAdded` | Resolves the model from the catalogue, builds a `BlockInstance`, adds a node, subscribes to power, thrust, door and attachment events, registers air vents and heat pumps. |
| `OnBlockRemoved` | Stores the temperature in `RecentlyRemoved`, unsubscribes, removes the node. |
| `OnGridSplit` | Copies temperatures out of the parent's `RecentlyRemoved` onto the child's blocks. |
| `OnGridMerge` | Copies temperatures from the absorbed grid onto matching positions, mapped through world space. |

`UpdateOnceBeforeFrame` disables the component when the grid has no physics (projections,
blueprints), adds any blocks that already existed, runs one full `RebuildAll`, and loads saved
temperatures. Building everything once is cheaper than replaying the incremental path per block, and
it leaves the room map complete before the first step rather than after it.

Blocks whose definition sets `IgnoreThermals` never become nodes.

A block only tells the simulation something when it changes: power, thrust, door state and
attachment are all events. Mass is the exception — the game raises nothing a mod can hook for build
progress or damage — so it is swept every eight steps, alongside room pressure.

## Persistence

[Game/ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridStorage.cs) writes one
base64 blob into the grid's `MyModStorageComponent`, under the GUID registered in
[EntityComponents.sbc](../Data/EntityComponents.sbc). `IsSerialized()` triggers the save.

The codec's v2 format carries block, loop and room air temperatures, keyed by 64-bit position, by
the loop's own signature rather than by its index, and by the room's anchor cell — so a rebuilt loop
keeps its heat and distant blocks cannot alias. It reads v1, so old saves load, and it grows by
adding a section rather than by changing its marker, so a save written now still loads on a build
that predates the section.

Room air loads onto rooms the map already holds, which is why `UpdateOnceBeforeFrame` runs
`RebuildAll` before `Load`. The restored air is marked initialised: pressurisation arrives later,
from the vent sweep, and filling a room for the first time is what would otherwise take its
temperature from the walls. Air that was never filled is not written at all — its figure is a
placeholder rather than a measurement — and a room whose shape changed while the world was closed
has a different anchor, so it starts from its surfaces exactly as it would have done mid-session.

## Definition loading

Thermal properties are declared inside `<ModExtensions>` groups and read through Draygo's Definition
Extensions API. `ThermalBlockCatalog` reads them **once per block definition** and builds a
`BlockModel` every placed block of that type shares: size, mass, thermal properties, per-cell
surface bits from the definition's airtightness table and mount points, and coolant plumbing.
Placing a block is then a dictionary hit and a rotation. See [definitions.md](definitions.md).

## Networking

`SENetworkAPI` is initialised in `Session.Init` with channel `30323`, but no commands or `NetSync`
properties are registered. The simulation is server-authoritative with no replication: clients run
their own `ThermalGrid` components from the same inputs, and only the server applies damage,
settings changes and debug block colouring.

## Extending it

Other mods bind to the delegate table in [ThermalApi.cs](../Data/Scripts/Thermodynamics/ThermalApi.cs);
see [api.md](api.md). A host that wants to drive the model directly implements `IBlockAdjacency` and
pumps `ThermalSimulation` — which is exactly what the test harness does.
