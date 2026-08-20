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

`A_exposed = exposedFaces × gridSize² × ExposedSurfaceMultiplier`, where `exposedFaces` is the count the
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
G     = RoomConvectionCoefficient × faces × gridSize² × ExposedSurfaceMultiplier
watts = G × (T_air − T_block)
```

This is the only path heat has between two walls of a compartment that do not touch. Air has
little capacity and a great deal of contact area, so on a pressurised ship it is usually what sets
the substep count.

A room holds air only when the host reports a pressure for it; at zero pressure it has no mass and
no links, and costs nothing. Air appearing in a room for the first time starts at the average
temperature of the surfaces around it, and carries its temperature across map rebuilds that leave
the room's shape unchanged.

Pressure is the game's answer, not this model's, and
[RoomPressure](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomPressure.cs) is the rule for reading
it. Three things can empty a room and each of them can veto air on its own:

| Source | Question | Answer when it says no |
| --- | --- | --- |
| `SessionSettings.EnableOxygen` and `EnableOxygenPressurization` | does this world pressurise at all | no room anywhere holds air |
| `IMyCubeGrid.IsRoomAtPositionAirtight` | does the game call this room sealed | that room holds nothing |
| `IMyAirVent.GetOxygenLevel`, or `Depressurize` | how full is it | that much, or nothing |

None of them can insist on air, only refuse it, because the two mistakes are not equal: air is heat
capacity, so a room wrongly given it warms and cools like a room with a tonne of gas in it and drags
every bounding surface along, where a room wrongly denied it only loses a little inertia.

The game's sealing test is consulted rather than this model's own room map because the two disagree,
and the game is right: it knows the real shape of a sloped or half block where the room mapper knows
only whether a cell's faces seal. The room map still decides the *geometry* — which cells are one
room, and which surfaces face indoors — since that is what exposure needs, and it is unaffected by
whether the world models oxygen.

How full a room is comes from the game's own gas system, per room:

```csharp
IMyOxygenRoom room = grid.GasSystem.GetOxygenRoomForCubeGridPosition(ref cell);
float level = room.OxygenLevel(grid.GridSize);
```

Not from the air vents, which is what this used to read. A vent can only speak for the room it
stands in, and **the game's rooms are the whole connected volume where this model's are pieces of
it** — so giving air only to the pieces a vent physically touched left the rest of the same
compartment in vacuum. The vents remain as a fallback for a world whose gas system cannot be read,
and only run when something goes unanswered; there the old limit applies, and a sealed compartment
nobody ever piped air into cannot be told apart from one nobody can measure.

## Solar and point sources

Solar gain, when the grid is not occluded:

```
watts = solarEnergy × ε × faceWeight(sun) × A_exposed × litFraction
solarEnergy = SolarEnergy × (1 − occludedShare) × (1 − SolarDecay × atmosphereFactor)
```

Occlusion against the rest of the world is resolved per grid, not per block, every
`SolarOcclusionInterval` steps. Planets are tested analytically by angular size; terrain by walking ground heights along the sun ray
([TerrainHorizon](../Data/Scripts/Thermodynamics/Core/Simulation/TerrainHorizon.cs)), for grids near
a surface and only once the planet's own horizon test says the sun is up; voxels by a physics
raycast; other grids by a ray against their blocks. Each of the three is a separate switch, because
each costs a different amount. Being underground forces full occlusion.

The result is a fraction, not a flag: `SolarOcclusionSamples` points spread through the hull are each
tested, and `solarEnergy` is scaled by the share that reached the sun. One sample — the default — is
a single ray from the grid's centre and gives 0 or 1, which is what the model did before.

`litFraction` is the grid's shadow on itself, and is 1 for every block when `SolarSelfShadowing` is
off. When it is on, [SunShadowMap](../Data/Scripts/Thermodynamics/Core/Simulation/SunShadowMap.cs)
walks toward the sun from the air just outside each block face, one cell at a time, until the ray
leaves the grid's bounding box: cross anything solid and that face is shadowed. It is a standard
voxel traversal, so the ray visits every cell it passes through and cannot slip diagonally between
two blocks that touch.

The question is asked of a face, not of a block, and that distinction is the whole model. A wall two
cells thick has an inner layer that cannot see the sun from its own centre — but the inner layer's
side faces are on the outside of the same wall, looking out of the same flank of the ship, in full
sunlight. Ask per block and a solid hull ends up lit along a single row of blocks with the rest of
it dark, which is wrong in the direction that matters: those flanks are most of the area.

At `SolarGridShadows = full`, other grids cast their shadows through the same walk. Each nearby grid is folded into a single
matrix — this grid's cells to metres, metres to the world, world to the occluder's metres, its metres
to its cells — and the ray is carried into that frame and walked against its blocks. Two lattices
that share no axis, origin or scale are then the same problem as one.

A pass starts only when the sun has moved more than 2°, the grid's blocks have changed, or a
neighbouring grid has moved — seconds
apart on a planet — and is spread over ticks in slices of `SunShadowBudget` cells, with the previous
answer readable until the new one completes, the same way the room mapper spreads its flood fill.

A projected-and-bucketed shadow map was built first and measured against this. It is far cheaper and
it is wrong in both directions at once: buckets are axis-aligned and the sun is not, so at an
oblique angle sunlight leaks onto shadowed cells, and the tolerance that closes the leak invents
shadows on cells standing in the open. On a 76-cell test structure at a real planetary sun angle it
lit 38 cells where 28 are lit — ten of them behind something.

`faceWeight` and `litFraction` answer different questions per face and both are needed: the first is
how square that face is to the sun, the second is whether anything of the ship stands in the way of
it. The [self-shadow scenario](../sim/Thermodynamics.Harness/Scenarios.cs) measures both on a solid
slab: the face turned to the sun is lit whole, the flanks around 80%, a recess cut into the hull
0%.

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

`v_rel` is the wind minus the grid's own velocity, and the wind comes from
[WindField](../Data/Scripts/Thermodynamics/Core/Simulation/WindField.cs) rather than straight from
the game. `MyPlanet.GetWindSpeed` is the planet definition's *maximum* wind scaled by air density —
80 m/s everywhere on an earthlike world at sea level, identical at the pole and the equator, with no
direction at all. Read as a wind it puts every parked ship in a permanent hurricane: over the
friction threshold, heating standing still, at nearly double the still-air convection.

So that figure is treated as the ceiling it is, and the field decides how much of it blows and which
way: Earth's bands — trades blowing west out to 30°, westerlies to 60°, polar easterlies beyond —
as a bearing that turns smoothly through the calms, times a fraction of the ceiling that runs from
about an eighth in fair weather to a half in the worst the game reports, times a steady per-place
variation so one valley is windier than the next. None of it is a simulation of anything; it is a
map that is steady, cheap, and recognisable when you fly across it.

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
  and `MyResourceSinkComponent.CurrentInputChanged`. **Which of the two fractions applies to a block
  is decided by the game, not by the definition.** A reactor delivers through the source component,
  so only its producer fraction can ever heat it and its consumer fraction is dead text; a thruster
  is the reverse. This is the one thing a definition can get wrong with no other symptom — a
  reactor with `ProducerWasteEnergy` 0 simply reports 0 W forever — so
  `EveryPowerProducerConvertsSomeOfItsOutputToHeat` checks the producer types by name.
* `thrust = ForceMagnitude × (CurrentThrust / MaxThrust)` — the thruster's force in newtons used
  as a watt-equivalent. This is a balance proxy, not a conversion, and it is what makes hydrogen
  thrusters heat: they draw no electricity, so thrust is the only term that can represent them.

Both fractions are balance figures rather than efficiencies, and the reactor's is the clearest case:
Space Engineers rates a 3×3×3 block at 300 MW, so a real plant's efficiency applied to it would
destroy every large reactor in the game in a build no player could improve. See
[balance.md](balance.md#reactor-waste-heat) for how the shipped 0.02 was measured.

## Coolant loops

A closed ring of pipe blocks carries one parcel of coolant per pipe
([CoolantLoopBuilder](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoopBuilder.cs)). It links
to the pipe blocks it runs through, and to whatever block sits behind each pipe's sink faces:

```
G_pipe  = Conductivity × ReferenceConductivity × A_pipe  / L
G_plate = Conductivity × ReferenceConductivity × A_plate / L
watts   = G × (T_loop − T_block)
```

Each parcel exchanges only with its own pipe and the blocks on that pipe's sink faces, so heat
reaches the far side of the ring only by being carried there:

```
parcels/s = (flowRate / cellSize) x sqrt(sum of pump speed x power supplied)
flowRate  = LargeGridFlowRate or SmallGridFlowRate, m/s, by the grid the ring is on
pipe i reads parcel (i - round(parcels carried)) mod N
```

Carrying the fluid is a rotation of the ring's origin rather than a shuffle of its contents. Because
a pipe's index is a whole number, the rounded offset collapses to one integer shift shared by every
pipe, which makes the mapping a bijection at any speed: no parcel is read twice and none is skipped.
It is therefore exactly conservative and costs no substeps however fast the pump runs.

It does have a resolution limit, which is not the same thing. A rotation advancing by a constant `k`
parcels per substep means pipe `i` only ever reads parcels in the subgroup `k` generates modulo `N`, so
whenever `gcd(k, N) > 1` the ring silently splits into that many disjoint sets and heat cannot cross
between them. Above one parcel per substep the fluid is therefore also mixed toward the ring's mean by
`1 - 1/parcels`: nothing at one parcel per substep, half at two, and complete as the rate runs away.
That is the physically right limit — a ring lapping far faster than it is observed *is* well mixed on
that timescale — and it removes the aliasing outright, because mixing couples every parcel to every
other. It is also plug flow with no numerical diffusion — a hot parcel arrives at the
radiator still hot, smoothed only by the pipes it passed through, which is the physical mechanism
rather than an artefact of the scheme.

Flow going as the square root of combined pump demand is real parallel-pump behaviour against a fixed
circuit, where turbulent pressure loss rises with the square of flow: four pumps carry twice one
pump's flow, not four times.

Demands are **signed** by which way each pump faces — a pump drives fluid out of its outlet port, so
whether that port opens onto the next pipe round the ring or the previous one sets the sign. They
subtract before the square root is taken. So a pump fitted the wrong way round drives the ring
backwards rather than failing, a backwards ring transports exactly as well, and two opposed pumps
cancel to a standstill while both go on drawing power.

A pump's own draw is **linear** in its speed, which is what stops pump count being a discount. The
affinity law — power with the cube of speed, which is what a real centrifugal pump does — was tried
first and is an exploit here rather than a trade: a given flow from N pumps needs each at speed `K/N`,
and cubed power makes the bill fall as `1/N²`, so ten pumps idling cost a hundredth of one pump
working. Linear closes it exactly, because flow `F` needs `Σ speed = (F/base)²` and the bill is then
`maxPower × (F/base)²` — a function of the flow alone, with the pump count cancelled out. Doubling
flow costs four times the power however it is arranged, and a second pump buys redundancy and headroom
rather than a discount.

With no pump running, `parcels/s` is zero and nothing is carried. The ring still holds its coolant and
still exchanges with what it touches, so the coolant beside a reactor saturates while the coolant at
the radiator stays cold — which is what a stopped pump does. `WellMixedCoolant` reverts to the older
single-mass fluid, where the whole ring is one temperature and heat crosses it instantly whether
anything is circulating or not.

Both exchanges are clamped and energy-conserving like every other. Each loop accumulates what it
drew out of blocks and what it pushed back into them over a step, reported as
`LastWattsAbsorbed` and `LastWattsRejected`. They are kept apart rather than summed because a loop
in balance — drawing off a reactor at one sink and shedding into a radiator at another — has a net
of about zero exactly when it is carrying its full load, so a single net figure would describe a
working loop as an idle one. Rings are traced from the ports
each block declares, so any block size or orientation works without special cases, and each ring is
found once whichever pipe the search starts from. A loop keeps its heat across a rebuild through an
order-independent hash of its members.

Radiators are ordinary blocks with high emissivity and a surface-area multiplier. A loop dumps heat
into space by pressing a sink face against one: the panel takes the loop's heat by conduction and
sheds it by radiation from its exposed faces.

## Heat pumps

Every other mechanism here moves heat down a gradient. A heat pump moves it up one, and pays an
energy price to do so ([HeatPump.cs](../Data/Scripts/Thermodynamics/Core/Devices/HeatPump.cs)).

```
COP   = min(MaxCoefficient, CarnotFraction × T_cold / (T_hot − T_cold))
lift  = min(RatedWatts, COP × PowerWatts, (T_cold − T_min) × mass_cold / h)
work  = lift / COP
watts_cold = −lift
watts_hot  = +lift + work
```

The hot side gains the lift **and** the work, which is why a pump concentrates a ship's heat rather
than reducing it. The work is electricity, so this is the one place energy enters the grid from
outside it; a host that bills for `LastDemandWatts` is charging for exactly the energy that appeared.

Three limits bind in turn, and Carnot is the interesting one. As the cold side falls, `T_cold /
(T_hot − T_cold)` falls with it, so each further kelvin costs more power than the last. The block
runs out of electricity long before its cold side runs out of temperature — which is why nothing
clamps the cold side at a floor and nothing needs to. The remaining limit, `(T_cold − T_min) ×
mass / h`, is not a balance decision but a substep guard: it stops a large enough rating from
taking more heat out of a node in one substep than the node contains.

Reported figures are per step, not per substep: energy is accumulated across the substeps and
divided by the step length at the end. See the note on `LastDeltaTemperature` in
[bugs-and-performance.md](bugs-and-performance.md) for what the other convention cost.

## Critical temperature and thresholds

```
if T > CriticalTemperature:
    damage = (T − CriticalTemperature) × OverheatDamagePerKelvin × h
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
weather   = weatherAtFullStrength faded by weatherIntensity
offset    = groundOffset + weather.temperature
swing     = groundSwing × (0.5 + 0.5 × weather.solar)

drop      = PoleTemperatureDrop × (1 − cos(latitude))
mean      = (NightTemperature + DayTemperature)/2 − drop
half      = (DayTemperature − NightTemperature)/2 × swing

target    = mean − half + 2×half × max(0, sin(sunElevation)) + offset
target    = target − AmbientLapseRate × altitude/1000
target    = VacuumTemperature + (target − VacuumTemperature) × (1 − (1 − density)⁸)
target    = depth > 0 ? underground(target, depth, radius) : target

ambient   = hasHistory
              ? ambient + (target − ambient) × (1 − e^(−dt / AmbientLagSeconds))
              : target
ambient   = max(VacuumTemperature, ambient)
```

**Everything is a target, and the lag is applied to it exactly once, last.** That ordering is
load-bearing rather than stylistic. Scaling the *running* ambient instead — which is what the model
did until a test world with a four-minute day made it visible — compounds the scale against the lag
on every step. A factor of 0.977 applied four times a second against a 45-second lag settles at

```
f·k / (1 − f + f·k)   where k = 1 − e^(−dt/τ)
```

which is 14% of the intended temperature, not 98% of it. A snowfield at 5.6 km, where air density
is 0.61, reported 36 K for an entire session, and every block on the grid froze to match. See
[planet-climate.md](planet-climate.md#what-the-four-minute-day-measured).

`hasHistory` is false on the step a grid arrives at a planet, when the only "previous ambient"
available is the vacuum every state is seeded with. Chasing a 290 K climate up from 2.7 K at 45
seconds a decade takes three minutes of play, during which every block on the ship is dragged
toward absolute zero — measured at 103 K on a grid that loaded at 257 K.

The planet's `DayTemperature` and `NightTemperature` are its **equatorial sea-level** figures.
`PoleTemperatureDrop` is the span from there to its poles, interpolated on the cosine because that
is how squarely the sun strikes a band. `groundOffset` and `groundSwing` come from the voxel
material under the grid via
[GroundTemperature](../Data/Scripts/Thermodynamics/Core/Definitions/GroundTemperature.cs) — snow
about 14 K colder and flatter, sand about 8 K warmer and swinging nearly twice as hard, because dry
ground holds nothing overnight. `ClimateGroundInfluence` scales the whole opinion, 0 for none.

The lag is why the day's peak lands after noon rather than at it. Air chases the sun; without it the
hottest instant of the day is exactly local noon, which is true nowhere.

### Altitude, and why density is not it

Two separate facts used to be one multiply. **`AmbientLapseRate`** is the one that does the work of
altitude: air cools as it rises because it expands, at 6.5 K/km on Earth and a default 4 K/km here,
because the ground table already makes mountains snowy and the two at full strength put a 5.6 km
peak near −50 °C.

**Density** decides something else entirely — when there stops being air to have a temperature at
all. That happens at the edge of space, not gradually all the way up, so ambient runs on
`1 − (1 − d)⁸` rather than the `1 − (1 − d)⁴` `atmosphereFactor` that convection and solar decay
use. At two thirds density it is still 99.9%; half is gone by a twelfth. The top of Earth's
troposphere holds a third of sea level's air and sits at 217 K, not at a third of 288.

### Weather

The game names the weather over a point and reports its intensity, and every effect in
`WeatherEffects.sbc` carries a `TemperatureModifier`, a `SolarOutputModifier` and a
`WindOutputModifier` authored per effect.
[WeatherResponse](../Data/Scripts/Thermodynamics/Core/Definitions/WeatherResponse.cs) is those
numbers, converted: the multipliers pass through, and `TemperatureModifier` — a factor on the
game's 0..1 comfort figure — becomes kelvin as `clamp(m − 1, −3, 3) × 6 K`.

| | heavy snow | heavy rain | sandstorm | fog | heat wave |
| --- | --- | --- | --- | --- | --- |
| ambient | −18 K | −3.6 K | +12 K | −4.2 K | +6 K |
| solar | ×0.10 | ×0.30 | ×0.10 | ×0.15 | ×1.75 |
| wind | ×2.00 | ×1.45 | ×2.25 | ×0.10 | ×0.10 |
| convection | ×2.2 | ×2.5 | ×1.4 | ×1.3 | ×1.0 |

Matched on the kind word rather than the exact subtype — there are 33 weathers in the base game,
most of them the same handful of kinds with a prefix — and a name carrying `light` gets half the
departure from calm, which is about what Keen's own light/heavy pairs differ by. Intensity fades
the whole response in from calm, so a weather at a tenth of strength is a tenth of the way toward
itself rather than all of it a tenth of the time.

Convection is the one column with no source: nothing in the definitions records that rain is wet,
and wet air pulls heat off a hull far faster than dry air of the same speed. Those figures are
opinions in the way the ground table is, and `ClimateWeatherInfluence` scales the lot.

The swing term is not a column at all. Cloud that keeps the sun off by day keeps the heat in at
night — one fact — so the day-night swing follows the solar multiplier: half of it at full
overcast, none of it removed in clear air.

### Underground

```
buried    = min(1, depth / UndergroundDampingDepth)
ambient   = surface + (UndergroundTemperature − surface) × buried

deadzone  = meanRadius − SealevelDeadzone
descended = max(0, 1 − radius / deadzone)
ambient   = ambient + (CoreTemperature − ambient) × descended
```

Two things happen going down, at very different scales. The first is that **the day stops**: rock
is slow, so the further down a tunnel goes the less of the surface's swing reaches it, until a few
tens of metres in there is no day left. A cold night and a hot noon converge on the same rock,
which is the point of it — weather and sun both damp out with the same term, for free.

The second is that **the planet is hot inside**. Below `SealevelDeadzone` the rock warms linearly
toward `CoreTemperature`, reaching it at the centre. On an earthlike's 60 km radius with the
shipped 3000 K core that is about 47 K/km, roughly twice Earth's crustal gradient.

The deadzone is measured from **sea level**, not from the surface. That is what makes a tunnel
bored a kilometre into a mountainside stay cold however far in it goes — it is deep in the rock and
still a long way above the hot part — while a shaft sunk from a beach reaches the same depth and
starts warming.

Depth comes free. The surface height under a grid is already looked up for the ground material and
cached on the same 40 m movement rule, so between refreshes how deep a grid is buried is the
difference of two radii: a subtraction, exact for a shaft sunk straight down, and anything that
moves far enough sideways for it not to be has already tripped the resample.

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
