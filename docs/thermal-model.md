# The thermal model

Every equation the simulation evaluates, with the file that evaluates it. All temperatures are
Kelvin, all rates are Watts, all areas are m², all masses are kg.

The solver holds one state variable per node — temperature — and derives everything else. A step
accumulates watts per node from every mechanism, then applies them all at once.

## Nodes

Three kinds of thermal mass exist. They integrate identically; only their capacity differs.

| Node | Capacity, J/K | Built by |
| --- | --- | --- |
| Block | `SpecificHeat × Mass / HeatTimeScale` | [ThermalNode](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalNode.cs) |
| Coolant loop | `SpecificHeat × Mass / HeatTimeScale` | [CoolantLoop](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoop.cs) |
| Room air | `Volume × RoomAirDensity × Pressure × 1005 / HeatTimeScale` | [RoomAirNode](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomAir.cs) |

Capacity is floored at `ThermalConstants.MinimumThermalMass` so a zero-mass block cannot divide
by zero. `SpecificHeat` is in real J/(kg·K); `HeatTimeScale` is the single global divisor that
turns real thermal time into playable thermal time — see
[configuration.md](configuration.md#time-and-pace).

## Integration

[ThermalSolver.Step](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalSolver.cs) advances
`deltaSeconds` of simulated time:

```
substeps = ceil(deltaSeconds × maxRate / 0.5)        clamped to [1, MaxSubsteps]
h        = deltaSeconds / substeps

repeat substeps times:
    watts[] = 0
    accumulate environment, conduction, coolant loops, room air
    T[i] += watts[i] × h / capacity[i]
    T[i]  = max(T[i], 0)
```

`maxRate` is the stiffest node on the grid: its total conductance plus its linearised radiative
and convective coupling, divided by its capacity.

```
rate = Σ G_links + 4 ε σ A T³ + h_conv A_exposed
```

Three properties follow from this shape and are covered by tests:

* **Order independence.** Every exchange reads the temperatures at the start of the substep and
  writes into an accumulator, so no node sees another's new value. Iteration order cannot change
  the result, and the pass could be parallelised without changing it either.
* **Energy conservation.** Every internal exchange is applied equally and oppositely.
  `Solver.TotalEnergy` is constant on a closed grid.
* **Boundedness.** With `ClampConductionOvershoot` on, every pairwise exchange is capped at the
  energy that brings the pair to their shared equilibrium:

  ```
  E_max = ΔT × (m_a m_b) / (m_a + m_b)
  ```

  Substepping keeps the answer accurate; the clamp keeps it sane when substepping alone cannot.
  Reaching `MaxSubsteps` (16) is reported as `LastStepWasClamped` and appears in the telemetry.

## Conduction

Between two touching blocks, over the area where both carry a mount surface
([ThermalLink.cs](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalLink.cs)):

```
A_contact = cells_shared × coverage_a × coverage_b × gridSize²
G         = A_contact / (L_a / k_a + L_b / k_b)          W/K
watts     = G × (T_b − T_a)
```

Two conductors in series: centre of A to the interface, interface to centre of B. `L` is each
block's half-depth along the contact axis, so a long block conducts more slowly end to end than a
cube does. `k = Conductivity × ReferenceConductivity`, where `Conductivity` is the definition's
0..1 quality value and the reference is 200 W/(m·K).

Blocks that touch without mount surfaces on both sides conduct **nothing**. This is what makes
armour skins, offset blocks and open frames behave differently from a solid slab.

Links are rebuilt only when the block layout changes, never per step. Adjacency comes from
`IBlockAdjacency`, which defaults to the grid's own cell map and can be replaced by a host with a
better index.

### Across a mechanical joint

Two blocks on either side of a rotor or piston belong to different grids and therefore different
solvers, so their link lives outside both, in
[ThermalBridges](../Data/Scripts/Thermodynamics/Game/ThermalBridges.cs). Bridges exchange once per
ten-frame tick, using the same clamped, energy-conserving rule.

## Radiation

Every exposed face radiates to the ambient sky:

```
watts = −ε σ A_exposed × (T⁴ − T_ambient⁴)
```

`A_exposed = exposedFaces × gridSize² × SurfaceAreaScaler`, where `exposedFaces` is the count the
surface mapper produced — see [surface-mapping.md](surface-mapping.md). A block with no exposed
face neither radiates nor absorbs. σ = 5.670374419e-8.

Emissivity doubles as absorptivity for incoming radiation. That is the grey-body assumption, and
it is deliberate.

## Convection

Into the surrounding atmosphere:

```
watts = −h_eff × A_exposed × windFactor × (T − T_ambient)
h_eff = ConvectionCoefficient × (1 + 0.1 √v_rel)
windFactor = 0.5 + 0.5 × faceWeight(wind)        (1.0 in still air)
```

`faceWeight(d)` is the exposure-weighted average of `max(0, faceNormal · d)` over the block's six
faces, so a face turned into the airflow sheds more than one in the lee.

Radiation and convection are blended by how fluid the atmosphere is:

```
total = (1 − atmosphereFactor) × radiation + atmosphereFactor × convection
atmosphereFactor = 1 − (1 − airDensity)⁴
```

A block in vacuum is pure radiation; a block at sea level is pure convection. The curve saturates
quickly: at a quarter density the air already behaves 68% like sea level.

## Room air

A sealed room holds one well-mixed air mass that exchanges with every surface bounding it
([ThermalSolver.AccumulateRoomAir](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalSolver.cs)):

```
G     = RoomConvectionCoefficient × faces × gridSize² × SurfaceAreaScaler
watts = G × (T_air − T_block)
```

This is the only path heat has between two walls of a compartment that do not touch. Air has
little capacity and a great deal of contact area, so on a pressurised ship it is usually what sets
the substep count.

A room holds air only when the host reports a pressure for it; at zero pressure it has no mass and
no links, and costs nothing. In Space Engineers, pressure comes from air vents — the only place the
game exposes it. Air appearing in a room for the first time starts at the average temperature of
the surfaces around it, and carries its temperature across map rebuilds that leave the room's shape
unchanged.

## Solar and point sources

Solar gain, when the grid is not occluded:

```
watts = solarEnergy × ε × faceWeight(sun) × A_exposed
solarEnergy = SolarEnergy × (1 − SolarDecay × atmosphereFactor)
```

Occlusion is resolved per grid, not per block, by a raycast toward the sun repeated every
`SolarOcclusionInterval` steps. Planets are tested analytically by angular size; voxels and grids
by a ray against their bounding segment. Being underground forces occlusion.

Point sources registered by other mods use the same equation with their own direction and
irradiance:

```
watts = irradiance × ε × faceWeight(source) × A_exposed
```

The host reduces a source to a direction and an irradiance before the solver sees it. For a source
of `P` watts at distance `r`, [ThermalHeatSources](../Data/Scripts/Thermodynamics/Game/ThermalHeatSources.cs)
uses `irradiance = P / (4π r²)`, with `r` floored at 1 m. See [api.md](api.md#heat-sources).

## Aerodynamic friction

```
if airDensity > 0.01 and v_rel > FrictionAtSpeedsAbove:
    watts = FrictionScale × airDensity × v_rel³ × A_exposed × faceWeight(wind)
```

The v³ law matches the scaling of convective heating in hypersonic flow; `FrictionScale` is a
game-feel coefficient. `v_rel` is weather wind minus grid velocity, so a stationary ship in a storm
heats like a fast ship in still air.

## Waste heat

Recomputed only when the game reports a change, never per step
([ThermalNode.RefreshHeatGeneration](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalNode.cs)):

```
watts = produced × ProducerWasteEnergy + (consumed + thrust) × ConsumerWasteEnergy
```

* `produced` and `consumed` are electrical watts, from `MyResourceSourceComponent.OutputChanged`
  and `MyResourceSinkComponent.CurrentInputChanged`.
* `thrust = ForceMagnitude × (CurrentThrust / MaxThrust)` — the thruster's force in newtons used
  as a watt-equivalent. This is a balance proxy, not a conversion, and it is what makes hydrogen
  thrusters heat: they draw no electricity, so thrust is the only term that can represent them.

## Coolant loops

A closed ring of pipe blocks containing at least one pump is one lumped fluid mass
([CoolantLoopBuilder](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoopBuilder.cs)). It links
to the pipe blocks it runs through, and to whatever block sits behind each pipe's sink faces:

```
G_pipe  = Conductivity × ReferenceConductivity × A_pipe  / L
G_plate = Conductivity × ReferenceConductivity × A_plate / L
watts   = G × (T_loop − T_block)
```

Both exchanges are clamped and energy-conserving like every other. Rings are traced from the ports
each block declares, so any block size or orientation works without special cases, and each ring is
found once whichever pipe the search starts from. A loop keeps its heat across a rebuild through an
order-independent hash of its members.

Radiators are ordinary blocks with high emissivity and a surface-area multiplier. A loop dumps heat
into space by pressing a sink face against one: the panel takes the loop's heat by conduction and
sheds it by radiation from its exposed faces.

## Critical temperature and thresholds

```
if T > CriticalTemperature:
    damage = (T − CriticalTemperature) × CriticalTemperatureScaler × h
```

With `DamageIsPerSecond` on, damage is per second of simulated time and independent of `Frequency`.
Damage is applied by the server only, and is not replicated: every machine derives it from its own
simulation.

Any other temperature can be watched through
[ThermalThresholds](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalThresholds.cs). A
crossing is measured against the temperature at the top of the step, so a block that crosses and
recrosses within one step reports once, in the direction it ended up going. Thresholds are half
open — a block resting exactly on one cannot report twice. With none registered, the check is a
single integer comparison per step. See [api.md](api.md#thresholds).

## Environment

[EnvironmentSolver](../Data/Scripts/Thermodynamics/Core/Simulation/EnvironmentSolver.cs) is a pure
function from a host sample to the state a step consumes:

```
ambient   = underground ? UndergroundTemperature
                        : NightTemperature + (dot(up, sun) + 1)/2 × (DayTemperature − NightTemperature)
ambient  *= atmosphereFactor
ambient   = max(VacuumTemperature, ambient)
```

With no planet nearby, or with planets switched off, ambient is `VacuumTemperature` (2.7 K) and
there is no convection.

## Where the old model differed

The previous per-cell implementation is preserved verbatim in
[sim/Thermodynamics.Tests/LegacyFormulas.cs](../sim/Thermodynamics.Tests/LegacyFormulas.cs), and
several tests compare against it so the differences stay pinned rather than remembered.

| Then | Now |
| --- | --- |
| Two conductances per joint, disagreeing by block size | One symmetric series conductance |
| Contact counted in whole grid faces | Contact area from block bounds and mount coverage |
| Gauss–Seidel sweep with alternating direction to hide order dependence | Order-independent accumulation |
| Fixed step, unbounded exchange | Substepping from the stiffest node, plus an equilibrium clamp |
| Damage scaled with `Frequency` | Damage per second of simulated time |
| `SpecificHeat` a flat game number | Real J/(kg·K) with one global clock |
