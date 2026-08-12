# Configuration

All runtime settings live in [Settings.cs](../Data/Scripts/Thermodynamics/Settings.cs).

> **Important:** `Settings.Load()` and `Settings.Save()` are implemented but **never called**.
> The only place `Settings.Instance` is assigned is `ThermalGrid.Init`, which assigns
> `Settings.GetDefaults()`. The world-storage file `ThermodynamicsConfig.cfg` is therefore
> never read or written, and editing it has no effect. To change a setting today you edit the
> defaults in `GetDefaults()` and rebuild. Wiring the loader into `Session.Init` is tracked in
> [known-issues.md](known-issues.md).

## Settings reference

| Setting | Default | Effect |
| --- | --- | --- |
| `Version` | 1 | Config schema version. `Load()` discards and regenerates the file when this does not match. |
| `EnableEnvironment` | `true` | Master switch for radiation and convection. Off = blocks only exchange heat with each other and their own generation. |
| `EnableSolarHeat` | `true` | Solar gain and the sun occlusion raycast. |
| `EnablePlanets` | `true` | Planet climate. Off = ambient is always `VacuumTemperature`, even at sea level. |
| `EnableDamage` | `true` | Whether exceeding `CriticalTemperature` damages blocks. |
| `Frequency` | 4 | Cell updates per simulated second. Also sets `TimeScaleRatio = 1/Frequency`. Clamped to `≥ 1`. |
| `SimulationSpeed` | 1 | Multiplier on how fast heat evolves relative to real time, applied by running *more* updates rather than by changing the step size. |
| `VacuumTemperature` | 2.7 K | Ambient in space; also the floor for planetary ambient. |
| `SolarEnergy` | 1000 W/m² | Solar irradiance before atmospheric decay. |
| `FrictionAtSpeedsAbove` | 50 m/s | Relative airspeed at which aerodynamic heating begins. |

### Debug toggles

| Setting | Default | Draws |
| --- | --- | --- |
| `DebugTextOnScreen` | **`true`** | Notification spam for the block under the crosshair: temperature, deltas, thermal constants, solar intensity, room counts, raw surface bits. Client-side. |
| `DebugTemperatureBlockColors` | **`true`** | **Recolours every block on every grid by temperature.** Server-side, and it writes real block colours via `ColorBlocks`. |
| `DebugSolarRadiationBlockColors` | `false` | Recolours blocks by solar intensity (`0 … 3`, blue at 0.5, red at 1.5). |
| `DebugExposedSurfaceBlockColors` | `false` | Recolours blocks by exposed face count (`0 … 6`). |
| `DebugFrictionColors` | `false` | Recolours blocks by friction heating in Watts (`0 … 20000`). |
| `DebugSolarRaycast` | **`true`** | Draws the sun ray from each grid — white when lit, red when occluded — plus green/blue segments over occluding voxels and grids. Client-side, skipped on dedicated servers. |
| `DebugWindRaycast` | **`true`** | Draws the relative wind vector from each grid. |
| `DebugTextureColors` | `true` (compile-time `const`) | Unused. |

The block-colouring modes are mutually exclusive in practice — they all write to the same
`ColorMaskHSV`, and the last one evaluated per update wins.

> The four toggles defaulting to `true` mean a fresh install is in full debug presentation:
> coloured grids, on-screen text and drawn rays. For a play session, set
> `DebugTextOnScreen`, `DebugTemperatureBlockColors`, `DebugSolarRaycast` and
> `DebugWindRaycast` to `false` in `GetDefaults()`.
>
> Be aware that `DebugTemperatureBlockColors` permanently overwrites players' paint jobs — it
> calls `MyCubeGrid.ColorBlocks`, it is not an overlay.

## Time scaling

Two values control the relationship between real time and simulated time:

```
TimeScaleRatio = 1 / Frequency          // simulated seconds per cell update
PerSecond      = Frequency × SimulationSpeed
```

`TimeScaleRatio` is baked into `C`, `ThermalMassInv` and the loop constants, so every Watt→Kelvin
conversion already accounts for the step size. `SimulationSpeed` is deliberately *not* baked
into the step: it increases the number of updates scheduled per second instead
(`GetSimulationQuota`), which keeps each individual step stable while making heat evolve faster.

`PerSecond` is used by the HUD to convert a per-update delta into K/s for the "Peak dT" readout.

Raising `Frequency` gives a finer, more accurate integration at higher CPU cost; lowering it is
cheaper but can overshoot on blocks with very small thermal mass, since the explicit integrator
has no stability clamp beyond `Temperature = max(0, Temperature)`.

## The (currently inactive) config file

`Load()` expects `ThermodynamicsConfig.cfg` in world storage, serialised as XML from the
`Settings` class. Should the loader be wired up, the file would look like:

```xml
<Settings>
  <Version>1</Version>
  <DebugTextOnScreen>false</DebugTextOnScreen>
  <DebugTemperatureBlockColors>false</DebugTemperatureBlockColors>
  <DebugSolarRadiationBlockColors>false</DebugSolarRadiationBlockColors>
  <DebugSolarRaycast>false</DebugSolarRaycast>
  <DebugExposedSurfaceBlockColors>false</DebugExposedSurfaceBlockColors>
  <DebugWindRaycast>false</DebugWindRaycast>
  <EnableEnvironment>true</EnableEnvironment>
  <EnableSolarHeat>true</EnableSolarHeat>
  <EnablePlanets>true</EnablePlanets>
  <EnableDamage>true</EnableDamage>
  <Frequency>4</Frequency>
  <SimulationSpeed>1</SimulationSpeed>
  <VacuumTemperature>2.7</VacuumTemperature>
  <SolarEnergy>1000</SolarEnergy>
  <FrictionAtSpeedsAbove>50</FrictionAtSpeedsAbove>
  <DebugFrictionColors>false</DebugFrictionColors>
</Settings>
```

`TimeScaleRatio` and `PerSecond` are `[XmlIgnore]` — they are derived in `Init()` after load.

Block tuning (conductivity, specific heat, critical temperatures) is **not** in this file. It
lives in the definition XML — see [definitions.md](definitions.md).
