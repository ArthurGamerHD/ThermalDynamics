# Architecture

The mod is in two halves, and the line between them is the point of the design.

| Half | Where | Knows about |
| --- | --- | --- |
| **The model** | [`Data/Scripts/Thermodynamics/Core/`](../Data/Scripts/Thermodynamics/Core) | Blocks, heat, geometry. One Space Engineers assembly, `VRage.Math`, for vectors. No session, no entity, no `MyAPIGateway`. |
| **The adapter** | [`Data/Scripts/Thermodynamics/Game/`](../Data/Scripts/Thermodynamics/Game) | Definitions, entity events, raycasts, damage, storage, the HUD. |

The model is what [`sim/`](../sim) builds and tests outside the game; the adapter is what the
game compiles around it. Everything the game supplies crosses one of three boundaries — block
layout, an environment sample, and results out — and nothing else.

## Component map

### The adapter

| File | Type | Role |
| --- | --- | --- |
| [Session.cs](../Data/Scripts/Thermodynamics/Session.cs) | `MySessionComponentBase` (`Simulation`) | Loads settings, boots the Definition Extensions client, the network API and the HUD. Runs the chat commands, the cross-grid conduction tick and the debug overlay. |
| [PlanetManager.cs](../Data/Scripts/Thermodynamics/PlanetManager.cs) | `MySessionComponentBase` (`NoUpdate`) | Keeps a list of planets and answers "which planet is closest to this point". |
| [Game/ThermalGrid.cs](../Data/Scripts/Thermodynamics/Game/ThermalGrid.cs) | `MyGameLogicComponent` on `MyObjectBuilder_CubeGrid` | The per-grid root: mirrors the game grid into a `GridModel`, owns the `ThermalSimulation`, wires block and grid events. `partial` — split across four more files. |
| [Game/ThermalGridSimulation.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridSimulation.cs) | `partial ThermalGrid` | `UpdateBeforeSimulation10`: sample, step, apply damage, refresh readouts. |
| [Game/ThermalGridEnvironment.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridEnvironment.cs) | `partial ThermalGrid` | Builds the `EnvironmentSample`: planet, air, wind, sun, occlusion. |
| [Game/ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridStorage.cs) | `partial ThermalGrid` | Save and load through the model's codec. |
| [Game/ThermalGridDebug.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridDebug.cs) | `partial ThermalGrid` | The block-colouring overlays. |
| [Game/ThermalBlock.cs](../Data/Scripts/Thermodynamics/Game/ThermalBlock.cs) | class | One placed block bound to one solver node. Pushes power, thrust, door state and mass into the model by event. |
| [Game/ThermalBlockCatalog.cs](../Data/Scripts/Thermodynamics/Game/ThermalBlockCatalog.cs) | static | Block definition → `BlockModel`, once per definition per session. |
| [Game/ThermalCoolantShapes.cs](../Data/Scripts/Thermodynamics/Game/ThermalCoolantShapes.cs) | static | Subtype → coolant plumbing. |
| [Game/ThermalBridges.cs](../Data/Scripts/Thermodynamics/Game/ThermalBridges.cs) | static | Conduction across a rotor or piston, where the two blocks belong to different grids. |
| [Definitions/*.cs](../Data/Scripts/Thermodynamics/Definitions) | classes | Typed readers over Definition Extensions. |
| [Settings.cs](../Data/Scripts/Thermodynamics/Settings.cs) | class | Config model, defaults, XML load/save, and the conversion to the model's own settings type. |
| [ThermalHud.cs](../Data/Scripts/Thermodynamics/ThermalHud.cs) | static | Text HUD API readouts and the heat billboard. |
| [Debug.cs](../Data/Scripts/Thermodynamics/Debug.cs) | static | The crosshair readout. |
| [Tools.cs](../Data/Scripts/Thermodynamics/Tools.cs), [ToolHelper.cs](../Data/Scripts/Thermodynamics/ToolHelper.cs) | static | Unit conversions, the temperature→HSV ramp, occlusion maths. |
| [Telemetry/](../Data/Scripts/Thermodynamics/Telemetry) | static + records | Data collection. See [telemetry.md](telemetry.md). |
| [HudAPIv2.cs](../Data/Scripts/Thermodynamics/HudAPIv2.cs), [DefinitionExtensionsAPI.cs](../Data/Scripts/Thermodynamics/DefinitionExtensionsAPI.cs), [NetworkAPI/](../Data/Scripts/Thermodynamics/NetworkAPI) | vendored | Third-party API clients. Do not edit; replace wholesale when upstream updates. |

### The model

| Area | Types |
| --- | --- |
| Layout | `GridModel`, `BlockInstance`, `BlockModel`, `BlockOrientation`, `CellSurface`, `BlockSurfaceBuilder`, `CoolantShape` |
| Simulation | `ThermalSimulation`, `ThermalSolver`, `ThermalNode`, `ThermalLink`, `SimulationScheduler`, `EnvironmentSample` / `EnvironmentState` / `EnvironmentSolver`, `ISimulationProfiler` |
| Surfaces | `SurfaceMap`, `RoomMapper`, `RoomMap` |
| Loops | `CoolantLoop`, `CoolantLoopBuilder` |
| Definitions | `BlockThermalProperties`, `LoopThermalProperties`, `PlanetThermalProperties`, `ThermalSettings` |
| Storage | `ThermalStorageCodec` |
| Maths | `BoxGeometry`, `GridMath`, `Face`, `OcclusionMath`, `TemperatureScale`, `ThermalConstants` |

`ThermalSimulation` is the whole surface a host needs. The adapter calls `AddBlock`,
`RemoveBlock`, `RefreshBlock`, `Update`, `Save` and `Load`, and reads back node temperatures and
overheat events.

## Update order

```
Session.Simulate()                      every frame
  ├─ chat command registration
  ├─ ThermalBridges.Update()            every 10th frame: conduction across rotors and pistons
  └─ Debug.ShowDebugInfo()              client only, behind DebugTextOnScreen

Session.Draw()      → ThermalHud.Draw() client only

per grid, every 10th frame:
ThermalGrid.UpdateBeforeSimulation10()
  ├─ scheduler.WouldStep()?             no  → skip sampling entirely
  ├─ Sample()                           planet, air, wind, sun, occlusion (throttled)
  ├─ Simulation.Update(dt, sample)
  │    ├─ topology rebuild              only after a block change
  │    ├─ room mapping                  one budgeted slice
  │    ├─ exposure refresh              only after a mapping pass completes
  │    └─ solver.Step() × steps due     substepped for stability
  └─ AfterSteps()
       ├─ apply overheat damage         server only
       ├─ mass sweep                    every 8 steps
       ├─ hottest block                 every 4 steps, and only if something will read it
       ├─ telemetry                     only while collection is on
       └─ debug colouring               only while a debug toggle is on
```

The grid polls on the ten-frame tick. How often the simulation *steps* is
`SimulationScheduler`'s business — `Frequency × SimulationSpeed` steps per real second, with
fractional credit carried between ticks — so polling faster would only add entity update
callbacks.

The old scheduler spread individual cell updates across frames and alternated its sweep
direction to cancel the bias that introduced. The solver is order-independent and
energy-conserving, so the whole grid steps at once and none of that machinery is needed.

## Grid lifecycle

`ThermalGrid.Init` attaches:

| Event | Effect |
| --- | --- |
| `OnBlockAdded` | Resolves the block's model from the catalogue, builds a `BlockInstance`, adds a node, subscribes to the block's power/thrust/door/attachment events. |
| `OnBlockRemoved` | Stores the temperature in `RecentlyRemoved`, unsubscribes, removes the node. |
| `OnGridSplit` | Copies temperatures out of the parent's `RecentlyRemoved` onto the child's blocks. |
| `OnGridMerge` | Copies temperatures from the absorbed grid onto matching positions. |

`UpdateOnceBeforeFrame` disables the component when the grid has no physics (projections,
blueprints), adds any blocks that already existed, runs one full `RebuildAll`, and loads saved
temperatures. Building everything once is cheaper than replaying the incremental path per block,
and it leaves the room map complete before the first step rather than after it.

Blocks whose definition sets `IgnoreThermals` never become nodes.

A block only tells the simulation something when something changes: power output, thrust, door
state and attachment are all events. Mass is the exception — the game raises nothing a mod can
hook for build progress or damage — so it is swept every eight steps.

## Persistence

[Game/ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/Game/ThermalGridStorage.cs) writes
one base64 blob into the grid's `MyModStorageComponent`, under the GUID registered in
[EntityComponents.sbc](../Data/EntityComponents.sbc):

| GUID | Payload |
| --- | --- |
| `f7cd64ae-…-3db992619343` | Block and loop temperatures, encoded by `ThermalStorageCodec` |

The codec's v2 format carries loop temperatures in the same payload, keyed by the loop's own
signature rather than by its position in a list, so a loop that is rebuilt keeps its heat. It
reads the v1 format, so old saves load. Unlike v1 it does not truncate temperatures to whole
Kelvin, and it does not write the truncated value back onto the running simulation.

`IsSerialized()` triggers the save.

## Definition loading

Thermal properties are declared inside `<ModExtensions>` groups and read at runtime through
Draygo's Definition Extensions API. `ThermalBlockCatalog` reads them **once per block
definition** and builds a `BlockModel` that every placed block of that type shares: size, mass,
thermal properties, per-cell surface bits derived from the definition's airtightness table and
mount points, and coolant plumbing. Placing a block is then a dictionary hit and a rotation.

Lookup falls back in three steps — exact `TypeId/SubtypeId`, then `TypeId/DefaultThermodynamics`,
then `EnvironmentDefinition/DefaultThermodynamics`. See [definitions.md](definitions.md).

## Networking

`SENetworkAPI` is initialised in `Session.Init` with channel `30323`, but no commands or
`NetSync` properties are registered. The simulation is server-authoritative with no replication:
clients run their own `ThermalGrid` components, and only the server applies damage and debug
block colouring.
