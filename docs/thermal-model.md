# The thermal model

This is a description of what the code actually computes, not of ideal thermodynamics. Where
the implementation deviates from textbook physics — deliberately or otherwise — that is
called out.

All temperatures are **Kelvin**. All energy rates are **Watts**. Power values coming out of
the game are megawatts and are converted with `Tools.MWtoWatt`.

## Per-cell constants

Computed once in `ThermalCell.PrecalculateVariables()`
([ThermalCell.cs:82](../Data/Scripts/Thermodynamics/ThermalCell.cs#L82)) and re-derived
whenever the definition or mass would change:

| Symbol | Code | Definition |
| --- | --- | --- |
| `Mass` | `Block.Mass` | Block mass in kg, from the game. |
| `Area` | `gridSize² × SurfaceAreaScaler` | Area of one grid face in m². 2.5 m cubes → 6.25 m², 0.5 m cubes → 0.25 m². |
| `C` | `1 / (SpecificHeat × Mass × gridSize) × TimeScaleRatio` | Conduction coupling constant. |
| `ThermalMassInv` | `1 / (SpecificHeat × Mass) × TimeScaleRatio` | Converts Watts to a temperature step. |
| `k` | `Conductivity × (SpecificHeat × Mass × gridSize) / (5 × Area × largestFace)` | Effective conductivity, normalised so that block size and shape do not distort transfer rate. `largestFace` is the product of the block's two largest dimensions in grid units. |
| `Boltzmann` | `−Emissivity × σ × ExposedSurfaceArea` | Pre-multiplied radiation constant. Negative, so a hot block gets a negative (outgoing) result. σ = `5.670374419e-8`. |

`TimeScaleRatio = 1 / Frequency` — the simulated seconds represented by one cell update. See
[configuration.md](configuration.md#time-scaling).

`ExposedSurfaceArea = ExposedSurfaces × Area`, where `ExposedSurfaces` is the count of block
faces the mapper decided are open to vacuum/atmosphere. See
[surface-mapping.md](surface-mapping.md).

## The per-cell step

`ThermalCell.Update()` ([ThermalCell.cs:443](../Data/Scripts/Thermodynamics/ThermalCell.cs#L443)),
in order:

```
LastTemprature  = Temperature
DeltaRadiation  = CalculateRadiation() × ThermalMassInv
DeltaFriction   = CalculateFriction()  × ThermalMassInv

Temperature    += DeltaRadiation + DeltaFriction
Temperature    += HeatGeneration

deltaTemperature = Σᵢ kAᵢ × (Tᵢ − Temperature)          over neighbours
DeltaTemperature = C × deltaTemperature
Temperature     += DeltaTemperature
Temperature      = max(0, Temperature)

HandleCriticalTemperature()
```

`Tᵢ` is the neighbour's `LastTemprature` if that neighbour has already been updated this
simulation frame, otherwise its current `Temperature` — a Gauss–Seidel sweep with the
direction reversal described in [architecture.md](architecture.md#the-quota-scheduler).

> `DeltaTemperature` holds the conduction term only — that is what the HUD's "Peak dT" and the
> debug overlay read. Friction used to be accumulated into it and then overwritten by the
> conduction result, which is why aerodynamic heating had no effect on temperature until it was
> moved onto the `Temperature` line above.

## Conduction

```
kAᵢ = k × min(Area, Areaᵢ) × TouchingSurfacesᵢ
ΔT  = C × Σᵢ kAᵢ × (Tᵢ − T)
```

`TouchingSurfacesᵢ` is an integer count of shared grid faces, not m². It comes from
`FindSurfaceArea()` ([ThermalCell.cs:337](../Data/Scripts/Thermodynamics/ThermalCell.cs#L337)),
which transforms both blocks' mount points into grid space and sums the areas of the
intersections between enabled mount points. Two blocks that touch on a face with no enabled
mount points conduct **zero** heat — this is what makes armour skins, doors and offset blocks
behave differently from a solid slab.

`kA` is cached per neighbour and recomputed by `CalculatekA()` whenever the neighbour list
changes.

Neighbours come from `IMySlimBlock.GetNeighbours` plus explicit cross-grid links added for
attached pistons and rotors.

## Radiation and convection

`CalculateRadiation()` ([ThermalCell.cs:484](../Data/Scripts/Thermodynamics/ThermalCell.cs#L484))
returns Watts and covers three effects.

**Blackbody radiation** (only when `EnableEnvironment`):

```
radiation = −ε σ A_exposed × (T⁴ − T_ambient⁴)
```

**Convection** into the surrounding air:

```
convection = −h_eff × A_exposed × directionalFactor × (T − T_ambient)
```

`directionalFactor` is `DirectionalIntensity(windDirection)` — the exposed faces' average dot
product with the relative wind, so a face pointing into the airflow sheds more heat than one
in the lee.

The two are blended by how thick the atmosphere is:

```
total = (1 − airDensityCurve) × radiation + airDensityCurve × convection
```

so a block in vacuum is pure radiation and a block at sea level is pure convection.

**Solar gain** (only when `EnableSolarHeat` and the grid is not occluded):

```
total += solarEnergy_eff × ε × intensity × A_exposed
```

`intensity = DirectionalIntensity(sunDirection)`, i.e.

```
Σ over the 6 faces:  max(0, dot(worldFaceNormal, sunDirection)) × exposedCount(face)
────────────────────────────────────────────────────────────────────────────────────
                          max(1, totalExposedFaces)
```

Face normals are rotated into world space with the grid matrix captured at the start of the
simulation frame (`Grid.FrameMatrix`), so rotating a ship changes which faces heat up.

> Emissivity is used both as absorptivity for solar gain and as emissivity for radiation, which
> is physically the grey-body assumption — deliberate.

## Aerodynamic friction

`CalculateFriction()` ([ThermalCell.cs:514](../Data/Scripts/Thermodynamics/ThermalCell.cs#L514)):

```
if airDensity > 0.01 and windSpeed > FrictionAtSpeedsAbove:
    friction = 0.001 × airDensity × windSpeed³ × A_exposed × directionalFactor
```

A `v³` law, matching the standard convective-heating scaling for hypersonic flow, with an
arbitrary `0.001` coefficient for game feel. `windSpeed` here is the *relative* speed —
weather wind minus grid velocity — so a stationary ship in a storm heats the same way a fast
ship in still air does.

## Internal heat generation

`UpdateHeat()` ([ThermalCell.cs:428](../Data/Scripts/Thermodynamics/ThermalCell.cs#L428)) is
event-driven, not recomputed per tick. It runs when a block's power draw, power output or
thrust changes:

```
produced  = EnergyProduction    × ProducerWasteEnergy
consumed  = (EnergyConsumption + ThrustEnergyConsumption) × ConsumerWasteEnergy
HeatGeneration = (produced + consumed) × ThermalMassInv
```

* `EnergyProduction` / `EnergyConsumption` come from `MyResourceSourceComponent.OutputChanged`
  and `MyResourceSinkComponent.CurrentInputChanged`, in Watts.
* `ThrustEnergyConsumption = ForceMagnitude × (CurrentThrust / MaxThrust)` — the thruster's
  force in newtons used directly as a Watt-equivalent. This is a game-balance proxy, not a
  physical conversion, and it makes large thrusters the dominant heat source on most ships.
* Because `HeatGeneration` is already scaled by `ThermalMassInv`, it is a **temperature step
  per cell update**, added directly to `Temperature`.

Components added or removed at runtime are handled by `OnComponentAdded`/`OnComponentRemoved`,
which re-subscribe the power listeners.

## Environment snapshot

`PrepareEnvironmentTemprature()`
([ThermalGridEnvironment.cs:48](../Data/Scripts/Thermodynamics/ThermalGridEnvironment.cs#L48))
runs **once per simulation frame per grid**, at the grid's world AABB centre. Every cell in
that pass shares the result.

```
airDensity      = planet.GetAirDensity(position)
airDensityCurve = 1 − (1 − airDensity)⁴            // saturates quickly with altitude

ambient = underground ? UndergroundTemperature
                      : NightTemperature + (dot(up, sunDirection) + 1)/2 × (DayTemperature − NightTemperature)

FrameAmbientTemprature = max(VacuumTemperature, ambient × airDensityCurve)
FrameAmbientTempratureP4 = FrameAmbientTemprature⁴

h_eff = ConvectionCoefficient × (1 + 0.1 × √windSpeed_relative)
solarEnergy_eff = SolarEnergy × (1 − SolarDecay × airDensityCurve)
```

* Being underground also forces `FrameSolarOccluded = true`.
* With no planet nearby, or with `EnablePlanets` off, ambient is `VacuumTemperature` (2.7 K).
* Wind direction is the cross product of local gravity and the planet's forward vector, scaled
  by `MyVisualScriptLogicProvider.GetWeatherIntensity` if a weather event is active, otherwise
  `planet.GetWindSpeed`. Grid velocity is subtracted to get relative wind.

## Solar occlusion

`PrepareSolarEnvironment()`
([ThermalGridEnvironment.cs:129](../Data/Scripts/Thermodynamics/ThermalGridEnvironment.cs#L129))
casts a 15 000 km line from the grid toward the sun and walks everything the pruning structure
reports overlapping it:

| Hit type | Test |
| --- | --- |
| `MyPlanet` | Analytic: the planet's angular size versus the sun-direction dot product (`Tools.GetVisualSize` / `Tools.GetLargestOcclusionDotProduct`). No raycast. |
| `MyVoxelBase` (asteroids) | Physics raycast limited to the voxel's AABB segment. |
| `MyCubeGrid` (other grids) | `RayCastBlocks` limited to that grid's AABB segment. |

Any hit sets `FrameSolarOccluded` for the whole grid — occlusion is all-or-nothing per grid,
not per block. Self-shadowing is not modelled; a partially written per-block shadowing pass
exists but is fully commented out in
[ThermalGridSolar.cs](../Data/Scripts/Thermodynamics/ThermalGridSolar.cs).

## Coolant loops

A `ThermalLoop` ([ThermalLoop.cs](../Data/Scripts/Thermodynamics/ThermalLoop.cs)) is one lumped
mass at a single `Temperature`, updated once per full simulation pass, with two coupling
constants derived from the loop definition:

```
cpart   = SpecificHeat × Mass × gridSize
C       = 1 / cpart × TimeScaleRatio
kAPipe  = Conductivity × cpart / (area × PipeSurfaceAreaScaler  × loopLength) × area × PipeSurfaceAreaScaler
kAPlate = Conductivity × cpart / (area × PlateSurfaceAreaScaler × loopLength) × area × PlateSurfaceAreaScaler
```

The `loopLength` divisor means a longer loop couples more weakly per segment, so total transfer
stays roughly constant as you extend the pipe run — the loop's `Mass` is the fluid mass of the
whole loop, not per segment.

Each pass, for every pipe segment in order:

1. Exchange between the loop fluid and the pipe block itself, using `kAPipe`.
2. For each **sink face** the segment has ([blocks.md](blocks.md#coolant-loop-rules)), find the
   neighbouring block behind that face and exchange between the loop fluid and that block using
   `kAPlate`.

Both exchanges are explicit and applied immediately in both directions, so heat propagates
along the loop within a single pass.

## Critical temperature damage

`HandleCriticalTemperature()`
([ThermalCell.cs:531](../Data/Scripts/Thermodynamics/ThermalCell.cs#L531)):

```
if EnableDamage and T > CriticalTemperature:
    damage = (T − CriticalTemperature) × CriticalTemperatureScaler × TimeScaleRatio
    Block.DoDamage(damage, "thermal", sync: false)
    Grid.CurrentCriticalBlocks++
```

Damage is applied every cell update, but `TimeScaleRatio` (`1 / Frequency`) scales it by the
length of that update, so a block 10 K over critical with a scaler of 1 takes 10 damage per
second at any `Frequency`. Before that scaling, `Frequency = 4` destroyed blocks four times
faster than `Frequency = 1` — a performance setting silently changing difficulty. `sync: false`
means the damage is not network-replicated — each machine applies it independently from its own
simulation.

`CriticalBlocks` is the count from the previous completed pass; `CurrentCriticalBlocks`
accumulates the pass in progress. The two-value swap keeps the HUD readout stable.

## Temperature colour ramp

`Tools.GetTemperatureColor(temp, max = 1000, low = 267, high = 500)` returns HSV:

| Range | Behaviour |
| --- | --- |
| `0 … low` | Hue fixed at blue, value ramps `−1 → 0.5`: black at 0 K, blue at `low`. |
| `low … high` | Hue sweeps blue → red at full saturation. |
| `high … max` | Hue fixed red, saturation falls to 0: red → white. |

The same function drives the debug block colouring, the extinguisher billboards and the HUD
text colour, with different `max/low/high` arguments for the solar, friction and
exposed-surface debug modes.
