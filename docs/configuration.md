# Configuration

Settings live in `ThermodynamicsConfig.cfg` in world storage, written with defaults the first time
a world loads and regenerated when `Version` does not match.

Every setting can also be changed while the world is running. A change is written into the settings
object every grid already holds and picked up on the next step: heat capacities are rescaled,
coolant loops and room air are rebuilt, and each mechanism reads its own switch. Nothing is written
to disk unless asked.

## Runtime control

| Command | Effect |
| --- | --- |
| `/thermal status` | Collection state, sample stride, live grids, block models, bridges. |
| `/thermal settings` | Every setting and its current value. |
| `/thermal set <name> <value>` | Changes one setting for this session. Switches take `on`/`off` or `1`/`0`. |
| `/thermal save` | Writes the current values to the config file. |
| `/thermal vision` | Toggles the thermal vision overlay. |
| `/thermal greyscale` | Switches the overlay between greyscale and the heat ramp. |
| `/thermal telemetry on` / `off` | Starts and stops data collection. |
| `/thermal stride <n>` | Telemetry sample stride. |
| `/thermal dump` | Writes a telemetry report without closing the world. |

Settings are server side; `set` from a client is refused. The same names are reachable from other
mods — see [api.md](api.md#settings).

## Mechanisms

Each switch removes exactly its own mechanism and its own cost.

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableEnvironment` | `true` | Master switch for radiation and convection. |
| `EnableConduction` | `true` | Heat flow between touching blocks. |
| `EnableRadiation` | `true` | Radiative exchange with the ambient sky. |
| `EnableConvection` | `true` | Convective exchange with the surrounding air. |
| `EnableSolarHeat` | `true` | Solar gain and the sun occlusion raycast. |
| `EnableHeatSources` | `true` | Gain from point sources registered by other mods. |
| `EnableWasteHeat` | `true` | Heat from power production, power draw and thrust. |
| `EnablePlanets` | `true` | Planetary climate. Off means ambient is always `VacuumTemperature`. |
| `EnableFriction` | `true` | Aerodynamic heating at speed in atmosphere. |
| `EnableDamage` | `true` | Damage above a block's critical temperature. |
| `EnableCoolantLoops` | `true` | Coolant loop heat transport. |
| `EnableRoomAir` | `true` | Sealed rooms hold an air mass that couples their surfaces. |
| `EnableHeatPumps` | `true` | Heat pumps move heat against a gradient for an electrical cost. Off makes them ordinary blocks. |

## Solver

| Setting | Default | Effect |
| --- | --- | --- |
| `Frequency` | 4 | Solver steps per simulated second. The integration step is `1/Frequency`. |
| `SimulationSpeed` | 1 | Simulated seconds per real second, applied by running more steps rather than longer ones. Linear in CPU. |
| `HeatTimeScale` | 225 | How much faster than real physics heat moves. Divides every heat capacity. |
| `ClampConductionOvershoot` | `true` | Caps each exchange at the energy that equalises the pair. Off reproduces the original unbounded solver. |
| `DamageIsPerSecond` | `true` | Overheat damage per second of simulated time. Off applies it per step, which makes damage scale with `Frequency`. |

## Environment

| Setting | Default | Effect |
| --- | --- | --- |
| `VacuumTemperature` | 2.7 K | Ambient in space, and the floor for planetary ambient. |
| `SolarEnergy` | 1000 W/m² | Solar irradiance above the atmosphere. |
| `FrictionAtSpeedsAbove` | 50 m/s | Relative airspeed at which aerodynamic heating starts. |
| `FrictionScale` | 0.001 | Coefficient on the v³ friction term. |
| `RoomConvectionCoefficient` | 8 W/(m²·K) | Coupling between a room's air and the surfaces facing it. Lower than the planetary figure because room air is still. |
| `RoomAirDensity` | 1.225 kg/m³ | Air density in a fully pressurised room. |
| `SolarOcclusionInterval` | 12 | Solver steps between solar occlusion raycasts. The raycast is the most expensive thing a grid does and the sun moves slowly. |

## Heat pumps

The two ratings a heat pump has — how much heat it can move and how much electricity it can draw —
belong to the block and live in
[ThermalHeatPumpShapes](../Data/Scripts/Thermodynamics/Game/ThermalHeatPumpShapes.cs). What is
tunable here is the physics between them, which is the same for every pump in the world.

| Setting | Default | Effect |
| --- | --- | --- |
| `HeatPumpCarnotFraction` | 0.4 | How much of the Carnot limit a pump achieves, 0..1. A real domestic heat pump manages about 0.4; 1 would be a thermodynamically perfect machine. This one number is the whole balance of the block. |
| `HeatPumpMaxCoefficient` | 8 | Ceiling on the coefficient of performance. Carnot's figure runs to infinity as the two sides converge, and a real machine is limited by its compressor long before that. |

Raising the fraction does not change what a pump can do — the shape of the cost curve is Carnot's
and is not negotiable — only how far up it the block sits. Lowering the ceiling makes pumps
predictable near equilibrium at the cost of making cheap, small-gap cooling less rewarding.

## Presentation

All client side and all off by default, apart from thermal vision.

| Setting | Default | Draws |
| --- | --- | --- |
| `EnableThermalVision` | `true` | Allows thermal vision. Toggle it in play with `/thermal vision` or the terminal button. |
| `ThermalVisionGreyscale` | `false` | `false` draws the ironbow palette, `true` white-hot greyscale. |
| `ThermalVisionRange` | 400 m | How far anything is drawn at all. |
| `ThermalVisionDetailRange` | 80 m | How far grids are drawn block by block. Past it a grid is one body at its hottest block's temperature. |
| `ThermalVisionDimming` | 1.0 | How much of the visible-light image is removed, 0..1. At 1 the ordinary view is gone entirely and only what the mod draws is visible. Lower it to make a tinted visor instead of a camera. |
| `ThermalVisionMinKelvin` | 240 K | Bottom of the sensor's span. Colder clips to black. |
| `ThermalVisionMaxKelvin` | 500 K | Top of the span. Hotter clips to white. |
| `ThermalVisionIntensity` | 0.9 | Brightness of drawn bodies. |
| `ThermalVisionBodyTemperature` | 310 K | Temperature characters are drawn at. Bodies are not simulated. |
| `DebugTextOnScreen` | `false` | Crosshair readout: temperature, per-mechanism watts, block constants, environment, grid totals, room classification, raw surface bits. Switching it on also makes the solver record per-mechanism watts, which is not free. |
| `DebugSolarRaycast` | `false` | Draws the sun ray from each grid, white when lit and red when occluded. |
| `DebugWindRaycast` | `false` | Draws the relative wind vector. |
| `DebugTemperatureBlockColors` | `false` | **Destructive.** Repaints every block by temperature using real block paint. |
| `DebugSolarRadiationBlockColors` | `false` | The same, by solar watts. |
| `DebugExposedSurfaceBlockColors` | `false` | The same, by exposed face count. |
| `DebugFrictionColors` | `false` | The same, by friction watts. |

> The four block-colouring modes call `MyCubeGrid.ColorBlocks`. That is a real, replicated,
> permanent change to every ship in the world, and switching it off does not undo it. Thermal
> vision answers the same question by drawing an overlay: client side, per frame, nothing written
> to the grid. Use the colouring modes only on a world you are willing to repaint.
>
> The modes are mutually exclusive; the colouring pass takes the first one switched on, in the
> order above.

## Telemetry

| Setting | Default | Effect |
| --- | --- | --- |
| `EnableTelemetry` | `false` | Session-long data collection. See [telemetry.md](telemetry.md). |
| `TelemetrySampleStride` | 4 | Fraction of each grid's blocks sampled per step for the per-definition statistics — `1/n`. Every block is still seen once per `n` steps. |

## Time and pace

Three settings affect pace and they do different things:

```
StepSeconds    = 1 / Frequency              simulated seconds in one solver step
StepsPerSecond = Frequency × SimulationSpeed
Capacity       = SpecificHeat × Mass / HeatTimeScale
```

* **`Frequency` is accuracy** — how finely a simulated second is integrated.
* **`SimulationSpeed` is how fast simulated time runs.** Honest, and linear in CPU.
* **`HeatTimeScale` is how fast heat moves within a simulated second.** Free in CPU terms, and the
  reason the definitions can carry real material values without a hull taking hours to cool.
  Dividing every capacity by *k* is exactly running thermal time at *k*×: every rate scales
  together, so equilibrium temperatures, the balance between mechanisms and the ratios between
  block types are all unchanged. Only the clock moves.

Total acceleration over real physics is `SimulationSpeed × HeatTimeScale`. The shipped 225 is what
makes steel's real 450 J/(kg·K) behave the way the old flat value of 2 did.

`HeatTimeScale` costs stability margin rather than time: higher values make the system stiffer, so
the solver takes more substeps. When a grid is stiff enough to hit the substep cap the telemetry
report says so.

Block tuning — conductivity, specific heat, critical temperatures — is not in this file. It lives in
the definition XML; see [definitions.md](definitions.md).
