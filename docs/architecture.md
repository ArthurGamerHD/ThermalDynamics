# Architecture

All code lives in `Data/Scripts/Thermodynamics/` under the `Thermodynamics` namespace, except
two vendored API clients (`Draygo.API`, `Draygo.BlockExtensionsAPI`) and the vendored
`SENetworkAPI`.

## Component map

| File | Type | Role |
| --- | --- | --- |
| [Session.cs](../Data/Scripts/Thermodynamics/Session.cs) | `MySessionComponentBase` (`Simulation` order) | Boots the Definition Extensions client, the network API and the HUD. Pumps the debug overlay each simulation tick and the HUD each draw. |
| [PlanetManager.cs](../Data/Scripts/Thermodynamics/PlanetManager.cs) | `MySessionComponentBase` (`NoUpdate`) | Keeps a static list of planets by hooking entity add/remove, and answers "which planet is closest to this point". |
| [ThermalGrid.cs](../Data/Scripts/Thermodynamics/ThermalGrid.cs) | `MyGameLogicComponent` on `MyObjectBuilder_CubeGrid` | The per-grid root. Owns the cell array, wires block/grid events, holds the frame scheduling state. `partial` — split across five more files. |
| [ThermalGridSimulation.cs](../Data/Scripts/Thermodynamics/ThermalGridSimulation.cs) | `partial ThermalGrid` | `UpdateBeforeSimulation`: the frame quota scheduler that decides how many cells tick this frame. |
| [ThermalGridEnvironment.cs](../Data/Scripts/Thermodynamics/ThermalGridEnvironment.cs) | `partial ThermalGrid` | Per-simulation-frame environment snapshot: ambient temperature, air density, wind, effective solar energy. |
| [ThermalGridSolar.cs](../Data/Scripts/Thermodynamics/ThermalGridSolar.cs) | `partial ThermalGrid` | Sun raycast and occlusion test; also draws the solar/wind debug rays. |
| [ThermalGridMapper.cs](../Data/Scripts/Thermodynamics/ThermalGridMapper.cs) | `partial ThermalGrid` | Surface bit flags and the external/room flood fill. See [surface-mapping.md](surface-mapping.md). |
| [ThermalGridLoop.cs](../Data/Scripts/Thermodynamics/ThermalGridLoop.cs) | `partial ThermalGrid` | Coolant pipe topology tables and the loop-detection crawler. |
| [ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/ThermalGridStorage.cs) | `partial ThermalGrid` | Pack/unpack of temperatures into `MyModStorageComponent`. |
| [ThermalCell.cs](../Data/Scripts/Thermodynamics/ThermalCell.cs) | class | One block. Owns its temperature, precomputed constants, neighbour list, exposed-surface counts, and the per-tick `Update()`. |
| [ThermalLoop.cs](../Data/Scripts/Thermodynamics/ThermalLoop.cs) | class | One closed coolant loop: a single lumped temperature plus its transfer step. |
| [Definitions/*.cs](../Data/Scripts/Thermodynamics/Definitions) | classes | `ThermalCellDefinition`, `ThermalLoopDefintion`, `PlanetDefinition` — typed readers over Definition Extensions. |
| [Settings.cs](../Data/Scripts/Thermodynamics/Settings.cs) | class | Config model, defaults, XML load/save helpers. |
| [ThermalHud.cs](../Data/Scripts/Thermodynamics/ThermalHud.cs) | static | Text HUD API readouts and the heat billboard drawn over blocks. |
| [Debug.cs](../Data/Scripts/Thermodynamics/Debug.cs) | static | The on-screen notification dump for the block under the crosshair. |
| [Tools.cs](../Data/Scripts/Thermodynamics/Tools.cs) | static | Constants, unit conversions, the temperature→HSV ramp, occlusion maths. |
| [ToolHelper.cs](../Data/Scripts/Thermodynamics/ToolHelper.cs) | static | `Vector3I` extension methods: `Flatten`, `Unflatten`, `LargestFace`. |
| [MyFreeList.cs](../Data/Scripts/Thermodynamics/MyFreeList.cs) | classes | `ThermalCellArray` — the dense cell array with a position→index map. Also an unused `MyFreeList<T>`. |
| [ThermalRadiationNode.cs](../Data/Scripts/Thermodynamics/ThermalRadiationNode.cs) | class | Per-face radiation accumulator. Currently unreferenced. |
| [HudAPIv2.cs](../Data/Scripts/Thermodynamics/HudAPIv2.cs), [DefinitionExtensionsAPI.cs](../Data/Scripts/Thermodynamics/DefinitionExtensionsAPI.cs), [NetworkAPI/](../Data/Scripts/Thermodynamics/NetworkAPI) | vendored | Third-party API clients. Do not edit; replace wholesale when upstream updates. |

## Cell addressing

A cell is keyed by its block's `Vector3I` position flattened to an `int`:

```csharp
// ToolHelper.cs
Flatten(v) => (1024*1024 * v.Z) + (1024 * v.Y) + v.X;
```

This is `ThermalCell.Id`, the key of `ThermalCellArray.positionToIndex`, and the key written
into save data. It assumes grid coordinates fit inside a 1024-unit stride; beyond that,
positions alias.

`ThermalCellArray` ([MyFreeList.cs](../Data/Scripts/Thermodynamics/MyFreeList.cs)) stores cells
in a dense array with a free list, so iteration order is stable and cheap but may contain
`null` holes after block removal — every consumer null-checks.

## Update order

```
Session.Simulate()                 → Debug.ShowDebugInfo()      (client only)
Session.Draw()                     → ThermalHud.Draw()          (client only)

per grid, every frame:
ThermalGrid.UpdateBeforeSimulation()
  ├─ GridMapperUpdate()            → advances the room flood fill by a quota
  └─ quota loop
       ├─ (on wrap) ThermalLoop.Update() for each loop
       ├─ (on wrap) SimulationFrame++, PrepareNextSimulationStep()
       │              ├─ PrepareSolarEnvironment()   sun direction + occlusion raycast
       │              └─ PrepareEnvironmentTemprature() ambient, air density, wind, solar energy
       └─ ProcessCellsSequentially(n)
            ├─ cell.UpdateSurfaces()  (only on the frame after a mapper pass completes)
            └─ cell.Update()          radiation, friction, generation, conduction, damage
```

### The quota scheduler

[ThermalGridSimulation.cs](../Data/Scripts/Thermodynamics/ThermalGridSimulation.cs) spreads a
grid's cells across frames so a 10 000-block ship does not tick all at once:

* `SimulationQuota = max(1, cellCount * SimulationSpeed * Frequency)` — cell updates owed per
  real second.
* `FrameQuota += cellCount * SimulationSpeed * Frequency / 60` each frame — the fractional
  budget for this frame, remainder carried over.
* When the quota for the second is exhausted, the grid idles until `FrameCount` reaches 60.

`SimulationIndex` walks the cell array and **reverses direction on every full pass**
(`Direction *= -1`). Because a cell reads `LastTemprature` from any neighbour already updated
this frame and `Temperature` from any not yet updated, alternating the sweep direction cancels
out the bias that a fixed-order Gauss–Seidel sweep would introduce.

One *simulation frame* = one full pass over every cell. `SimulationFrame` counts those, not
game frames.

## Grid lifecycle

`ThermalGrid.Init` ([ThermalGrid.cs](../Data/Scripts/Thermodynamics/ThermalGrid.cs)) attaches:

| Event | Handler | Effect |
| --- | --- | --- |
| `OnBlockAdded` | `BlockAdded` | Recomputes surface flags, restarts the room crawl, builds a `ThermalCell`, links neighbours, runs the coolant-loop check. |
| `OnBlockRemoved` | `BlockRemoved` | Clears surface flags for the block's cells, restarts the room crawl, drops any coolant loop containing the cell, stores the temperature in `RecentlyRemoved`, unlinks neighbours. |
| `OnGridSplit` | `GridSplit` | Copies temperatures out of the parent grid's `RecentlyRemoved` map into the new grid's cells so a cut-off section keeps its heat. |
| `OnGridMerge` | `GridMerge` | Copies temperatures from the absorbed grid onto matching positions in the survivor. |
| `SurfaceCheckComplete` | lambda | Sets `SurfaceUpdateFrame = SimulationFrame + 1`, so the next simulation pass refreshes every cell's exposed-surface counts. |

Blocks whose definition sets `IgnoreThermals` never become cells at all.

Mechanical connections (`IMyPistonBase`, `IMyMotorBase`) subscribe to `AttachedEntityChanged`
and add/remove a cross-grid neighbour link, so heat conducts through a rotor or piston head.
The equivalent landing-gear handling exists but is commented out.

`UpdateOnceBeforeFrame` disables updates when the grid has no physics (projections, blueprints),
loads saved temperatures, and primes the first environment snapshot.

## Persistence

[ThermalGridStorage.cs](../Data/Scripts/Thermodynamics/ThermalGridStorage.cs) writes two
base64 blobs into the grid's `MyModStorageComponent`, under the GUIDs registered in
[EntityComponents.sbc](../Data/EntityComponents.sbc):

| GUID | Payload | Record |
| --- | --- | --- |
| `f7cd64ae-…-3db992619343` | Cell temperatures | 6 bytes: `int32` flattened position + `int16` temperature (Kelvin) |
| `f7cd64ae-…-3db992619344` | Loop temperatures | 3 bytes: `byte` loop index + `int16` temperature (Kelvin) |

Temperatures are truncated to whole Kelvin, and `Pack()` writes the truncated value back onto
the live object, so saving nudges the running simulation. Both source files flag this with a
`TODO`. Loop data is indexed by list position, so a loop's identity across a save/load cycle
depends on the crawler rebuilding loops in the same order.

`IsSerialized()` triggers the save.

## Definition loading

Thermal properties are not read from the SBC files directly. They are declared inside
`<ModExtensions>` groups and read at runtime through Draygo's Definition Extensions API
([DefinitionExtensionsAPI.cs](../Data/Scripts/Thermodynamics/DefinitionExtensionsAPI.cs)),
which hands over a delegate table via a mod-message handshake during `LoadData`.

Lookup falls back in three steps — exact `TypeId/SubtypeId`, then
`TypeId/DefaultThermodynamics`, then `EnvironmentDefinition/DefaultThermodynamics`. See
[definitions.md](definitions.md).

## Networking

`SENetworkAPI` is initialised in `Session.Init` with channel `30323` and traffic logging on,
but no commands or `NetSync` properties are registered yet. The simulation is presently
server-authoritative with no replication: clients run their own `ThermalGrid` components, and
only the server applies damage and debug block colouring (`MyAPIGateway.Session.IsServer`
guards).
