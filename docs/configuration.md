# Configuration

All runtime settings live in [Settings.cs](../Data/Scripts/Thermodynamics/Settings.cs), and are
read from `ThermodynamicsConfig.cfg` in world storage on the server at session start. The file
is written with defaults the first time a world loads, and regenerated when `Version` does not
match — so an old config is replaced rather than partially applied.

Settings are converted once into the model's own `ThermalSettings`
([Core/Settings/ThermalSettings.cs](../Data/Scripts/Thermodynamics/Core/Settings/ThermalSettings.cs)),
which every grid's solver holds a reference to.

## Settings reference

| Setting | Default | Effect |
| --- | --- | --- |
| `Version` | 3 | Config schema version. The file is discarded and regenerated when this does not match. |
| `EnableEnvironment` | `true` | Master switch for radiation and convection. Off = blocks only exchange heat with each other and their own generation. |
| `EnableSolarHeat` | `true` | Solar gain and the sun occlusion raycast. |
| `EnablePlanets` | `true` | Planet climate. Off = ambient is always `VacuumTemperature`, even at sea level. |
| `EnableFriction` | `true` | Aerodynamic heating at speed in atmosphere. |
| `EnableDamage` | `true` | Whether exceeding `CriticalTemperature` damages blocks. |
| `EnableCoolantLoops` | `true` | Coolant loop heat transport. |
| `ClampConductionOvershoot` | `true` | Limits each conduction exchange to the energy that equalises the pair, so a node can never overshoot what it is exchanging with. Off reproduces the unbounded behaviour of the original solver. |
| `DamageIsPerSecond` | `true` | Overheat damage is `(T − critical) × CriticalTemperatureScaler` per second. Off applies it per solver step, which makes damage scale with `Frequency` — the original behaviour. |
| `Frequency` | 4 | Solver steps per simulated second. The integration step is `1/Frequency`. Clamped to `≥ 1`. |
| `SimulationSpeed` | 1 | Multiplier on how fast heat evolves relative to real time, applied by running *more* steps rather than by lengthening the step. Costs CPU in proportion. |
| `HeatTimeScale` | 225 | How many times faster than real physics heat moves. `SpecificHeat` in the definitions is real J/(kg·K); this divides every heat capacity, which is exactly running thermal time faster — equilibrium temperatures and every ratio between mechanisms are unchanged. 1 is fully physical (a hull takes hours to cool). Free in CPU terms, but it makes the system stiffer, so very high values cost substeps. |
| `VacuumTemperature` | 2.7 K | Ambient in space; also the floor for planetary ambient. |
| `SolarEnergy` | 1000 W/m² | Solar irradiance before atmospheric decay. |
| `FrictionAtSpeedsAbove` | 50 m/s | Relative airspeed at which aerodynamic heating begins. |
| `FrictionScale` | 0.001 | Coefficient on the v³ aerodynamic heating term. |
| `SolarOcclusionInterval` | 12 | Solver steps between solar occlusion raycasts. The raycast is the most expensive thing a grid does and the sun moves slowly, so the answer is reused in between. |
| `EnableTelemetry` | `false` | Session-long data collection. See [telemetry.md](telemetry.md). |
| `TelemetrySampleStride` | 4 | Fraction of each grid's blocks sampled per step for the wide per-definition statistics — `1/n`. Every block is still seen once per `n` steps. |

### Debug toggles

| Setting | Default | Draws |
| --- | --- | --- |
| `DebugTextOnScreen` | `true` | The crosshair readout: temperature, per-mechanism watts, block constants, environment, grid totals, raw surface bits. Client-side. Switching it on also makes the solver record per-mechanism watts, which is not free. |
| `DebugTemperatureBlockColors` | `true` | **Recolours every block on every grid by temperature.** Server-side, and it writes real block colours via `ColorBlocks`. |
| `DebugSolarRadiationBlockColors` | `false` | Recolours blocks by solar watts. |
| `DebugExposedSurfaceBlockColors` | `false` | Recolours blocks by exposed face count (`0 … 6`). |
| `DebugFrictionColors` | `false` | Recolours blocks by friction watts. |
| `DebugSolarRaycast` | `true` | Draws the sun ray from each grid — white when lit, red when occluded. Client-side, skipped on dedicated servers. |
| `DebugWindRaycast` | `true` | Retained for the wind vector; the ray itself is drawn from the environment sample. |
| `DebugTextureColors` | `true` (compile-time `const`) | Unused. |

The block-colouring modes are mutually exclusive: the colouring pass picks the first one that is
switched on, in the order above.

> The toggles defaulting to `true` mean a fresh install is in debug presentation: coloured grids,
> on-screen text and drawn rays. For a play session set `DebugTextOnScreen`,
> `DebugTemperatureBlockColors` and `DebugSolarRaycast` to `false`.
>
> Be aware that `DebugTemperatureBlockColors` permanently overwrites players' paint jobs — it
> calls `MyCubeGrid.ColorBlocks`, it is not an overlay.

## Time scaling

Three settings affect pace, and they do different things:

```
StepSeconds    = 1 / Frequency              // simulated seconds advanced by one solver step
StepsPerSecond = Frequency × SimulationSpeed
ThermalMass    = SpecificHeat × Mass / HeatTimeScale
```

* `Frequency` is accuracy: how finely a simulated second is integrated.
* `SimulationSpeed` is how many simulated seconds pass per real second. Honest, and linear in CPU.
* `HeatTimeScale` is how fast heat moves *within* a simulated second. Free, and the reason the
  definitions can carry real material values without a ship taking hours to cool. It costs
  stability margin rather than time: the solver answers a stiffer system with more substeps.

Total acceleration over real physics is `SimulationSpeed × HeatTimeScale`.

`SimulationSpeed` is deliberately not baked into the step length: it increases how many steps are
scheduled per real second, which keeps each individual step as accurate as it was while making
heat evolve faster.

Raising `Frequency` gives a finer integration at higher CPU cost. Lowering it is cheaper, and is
safe in a way it was not before: the solver picks its own substep count from the stiffest node on
the grid, and `ClampConductionOvershoot` bounds any exchange that substepping alone cannot make
accurate. When a grid is stiff enough to hit the substep cap, the telemetry report says so —
"steps clamped by substep cap".

## The config file

```xml
<Settings>
  <Version>3</Version>
  <DebugTextOnScreen>false</DebugTextOnScreen>
  <DebugTemperatureBlockColors>false</DebugTemperatureBlockColors>
  <DebugSolarRadiationBlockColors>false</DebugSolarRadiationBlockColors>
  <DebugSolarRaycast>false</DebugSolarRaycast>
  <DebugExposedSurfaceBlockColors>false</DebugExposedSurfaceBlockColors>
  <DebugWindRaycast>false</DebugWindRaycast>
  <DebugFrictionColors>false</DebugFrictionColors>
  <EnableEnvironment>true</EnableEnvironment>
  <EnableSolarHeat>true</EnableSolarHeat>
  <EnablePlanets>true</EnablePlanets>
  <EnableFriction>true</EnableFriction>
  <EnableDamage>true</EnableDamage>
  <EnableCoolantLoops>true</EnableCoolantLoops>
  <ClampConductionOvershoot>true</ClampConductionOvershoot>
  <DamageIsPerSecond>true</DamageIsPerSecond>
  <Frequency>4</Frequency>
  <SimulationSpeed>1</SimulationSpeed>
  <HeatTimeScale>225</HeatTimeScale>
  <VacuumTemperature>2.7</VacuumTemperature>
  <SolarEnergy>1000</SolarEnergy>
  <FrictionAtSpeedsAbove>50</FrictionAtSpeedsAbove>
  <FrictionScale>0.001</FrictionScale>
  <SolarOcclusionInterval>12</SolarOcclusionInterval>
  <EnableTelemetry>false</EnableTelemetry>
  <TelemetrySampleStride>4</TelemetrySampleStride>
</Settings>
```

`TimeScaleRatio` and `PerSecond` are `[XmlIgnore]` — they are derived after load.

Block tuning (conductivity, specific heat, critical temperatures) is **not** in this file. It
lives in the definition XML — see [definitions.md](definitions.md).
