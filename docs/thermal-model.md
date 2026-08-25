# The thermal model

Every equation the simulation evaluates, with the file that evaluates it, and the surface geometry
every area term in them reads.

All temperatures are Kelvin, all rates are Watts, all areas are m², all masses are kg. The solver
holds one state variable per node — temperature — and derives everything else. A step accumulates
watts per node from every mechanism, then applies them all at once.

> The rules argued here are stated canonically in [rules.md](rules.md): `C6` `E7`.

| Looking for | Go to |
| --- | --- |
| What the air, ground, sun and wind outside the grid are | [environment.md](environment.md) |
| Which component calls the solver, and when | [architecture.md](architecture.md#update-order) |
| The settings that scale every term | [configuration.md](configuration.md) |
| The block and planet properties these equations read | [definitions.md](definitions.md) |

---

## The solver

### Nodes

Three kinds of thermal mass exist. They integrate identically; only their capacity differs.

| Node | Capacity, J/K | Built by |
| --- | --- | --- |
| Block | `SpecificHeat × Mass / HeatTimeScale` | [ThermalNode.cs](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalNode.cs) |
| Coolant loop | `SpecificHeat × Mass / HeatTimeScale` | [CoolantLoop.cs](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoop.cs) |
| Room air | `Volume × RoomAirDensity × Pressure × 1005 / HeatTimeScale` | [RoomAir.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomAir.cs) |

Capacity is floored at `ThermalConstants.MinimumThermalMass` so a zero-mass block cannot divide by
zero. `SpecificHeat` is in real J/(kg·K); `HeatTimeScale` is the single global divisor that turns
real thermal time into playable thermal time — see
[configuration.md](configuration.md#time-and-pace).

### Integration

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

`maxRate` is the stiffest node on the grid: its total conductance plus its linearised radiative and
convective coupling, divided by its capacity.

```
rate = Σ G_links + 4 ε σ A T³ + h_conv A_exposed
```

**A handful of light fittings therefore set the substep count for a whole capital ship.** That is
the dominant performance fact about this model and it has its own page —
[stiffness.md](stiffness.md).

### The three invariants

These are the definition of correctness; everything else is tuning.

| Invariant | Why it holds | Checked by |
| --- | --- | --- |
| **Order independence** | Every exchange reads the temperatures at the start of the substep and writes into an accumulator, so no node sees another's new value. Iteration order cannot change the result, and the pass could be parallelised without changing it either. | `ConductionTests` |
| **Energy conservation** | Every internal exchange is applied equally and oppositely. `Solver.TotalEnergy` is constant on a closed grid. | `ConductionTests` |
| **Boundedness** | With `ClampConductionOvershoot` on, every pairwise exchange is capped at the energy that brings the pair to their shared equilibrium: `E_max = ΔT × (m_a m_b) / (m_a + m_b)`. | `StabilityTests`, `ConductionClampGateTests` |

Substepping keeps the answer accurate; the clamp keeps it sane when substepping alone cannot.
Reaching `MaxSubsteps` (64 as shipped) is reported as `LastStepWasClamped` and appears in the
telemetry.

---

## The mechanisms

### Conduction

Between two touching blocks, over the area where both carry a mount surface
([ThermalLink.cs](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalLink.cs)):

```
A_contact = cells_shared × coverage_a × coverage_b × gridSize²
G         = A_contact / (L_a / k_a + L_b / k_b)          W/K
watts     = G × (T_b − T_a)
```

Two conductors in series: centre of A to the interface, interface to centre of B. `L` is each
block's half-depth along the contact axis, so a long block conducts more slowly end to end than a
cube does. `k = Conductivity × ThermalConstants.ConductionScale`, where `Conductivity` is the
definition's figure in **real W/(m·K)** — mild steel 50, glass 1, copper 400 — and `ConductionScale`
(**9.6**) is one global constant setting the game's pace, so the definition stays a description of
the material rather than a balance dial. It was 2.4 until `C24`, which is the value that puts mild
steel exactly where the pre-conversion world put it; the shipped pace is four times that, and what
bought it is `G8`'s significance window — see
[balance.md](balance.md#the-route-is-chosen-and-it-is-the-one-the-cost-column-argued-against).

**A coolant loop's coupling is not this.** It is a heat transfer coefficient in W/(m²·K) — 160,
what the transfer physically is — and no conduction pace touches it, because it is a fluid against
a wall rather than a solid against a solid (`C20`). This paragraph said it was still the older 0…1
quality against a 200 W/(m·K) reference until 2026-08-24, and pointed at a page that already said
otherwise. One consequence is deliberate and is `C25`: a bolt joint carries 1,168 W/K where a sink
face carries 1,000, so in this world a steel bolt out-couples a water-cooled plate face for face.
See [definitions.md](definitions.md#conductivity-is-in-real-wmk) and
[blocks.md](blocks.md).

Blocks that touch without mount surfaces on both sides conduct **nothing**. This is what makes
armour skins, offset blocks and open frames behave differently from a solid slab.

Links are rebuilt only when the block layout changes, never per step. Adjacency comes from
`IBlockAdjacency`, which defaults to the grid's own cell map and can be replaced by a host with a
better index.

**Across a mechanical joint.** Two blocks either side of a rotor or piston belong to different grids
and therefore different solvers, so their link lives outside both, in
[ThermalBridges.cs](../Data/Scripts/Thermodynamics/Game/ThermalBridges.cs). Bridges exchange once
per ten-frame tick, under the same clamped, energy-conserving rule.

### Radiation

Every exposed face radiates to the ambient sky:

```
watts = −ε σ A_exposed × (T⁴ − T_ambient⁴)
```

σ = 5.670374419e-8. `A_exposed` is the count the surface mapper produced — see
[Exposure](#exposure). **A block with no exposed face neither radiates nor absorbs.**

**What arrives is a different coefficient from what leaves.** Emission uses `ε`; the sun and every
point source use `α`, the block's `SolarAbsorptivity`. Undeclared, `α = ε` — the grey-body
assumption, which is where this model started and what every block still does unless somebody
authors otherwise. Declaring the two apart is what a selective surface is: a real radiator sheds in
the thermal infrared at `ε ≈ 0.8` and takes in the visible at `α ≈ 0.1`, and while one number did
both jobs that surface could not be described at all. See
[definitions.md](definitions.md#emissivity-and-absorptivity-are-two-numbers).

### Convection

Into the surrounding atmosphere:

```
watts = −h_eff × A_exposed × windFactor × (T − T_ambient)
h_eff = ConvectionCoefficient × (1 + 0.1 √v_rel)
windFactor = 1 + faceWeight(wind)                (1.0 in still air)
```

`faceWeight(d)` is the exposure-weighted average of `max(0, faceNormal · d)` over the block's six
faces, so a face turned into the airflow sheds twice what one in the lee does.

**A wind never sheds less than still air.** Forced convection adds to natural convection rather
than replacing it, so the factor runs from 1 upward and a lee face keeps what it has. A floor of
0.5 gives the same two-to-one contrast and the wrong answer: most of a closed hull's exposed faces
do not point into the wind, the geometric term then loses more than the speed term gains, and a wind
under about 50 m/s is a net **warmer** — a hull making 2 MW settles 0.9 K hotter in a 40 m/s wind
than in still air.

Radiation and convection are blended by how fluid the atmosphere is:

```
total = (1 − atmosphereFactor) × radiation + atmosphereFactor × convection
atmosphereFactor = 1 − (1 − airDensity)⁴
```

A block in vacuum is pure radiation; a block at sea level is pure convection. The curve saturates
quickly: at a quarter density the air already behaves 68% like sea level.

### Room air

A sealed room holds one well-mixed air mass that exchanges with every surface bounding it
([ThermalSolver.AccumulateRoomAir](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalSolver.cs)):

```
G     = RoomConvectionCoefficient × faces × gridSize² × ExposedSurfaceMultiplier
watts = G × (T_air − T_block)
```

This is the only path heat has between two walls of a compartment that do not touch. Air has little
capacity and a great deal of contact area, so on a pressurised ship it is usually what sets the
substep count.

How a room gets its air, and what can take it away, is in [Room air](#room-air-1) below.

### Solar and point sources

Solar gain, when the grid is not occluded:

```
watts = solarEnergy × ε × faceWeight(sun) × A_exposed × litFraction
solarEnergy = SolarEnergy × (1 − occludedShare) × (1 − SolarDecay × atmosphereFactor)
```

Occlusion against the rest of the world is resolved **per grid**, every `SolarOcclusionInterval`
steps. Planets are tested analytically by angular size; terrain by walking ground heights along the
sun ray ([TerrainHorizon.cs](../Data/Scripts/Thermodynamics/Core/Simulation/TerrainHorizon.cs)), for
grids near a surface and only once the planet's own horizon test says the sun is up; voxels by a
physics raycast; other grids by a ray against their blocks. Each of the three is a separate switch,
because each costs a different amount. Being underground forces full occlusion.

The result is a fraction, not a flag: `SolarOcclusionSamples` points spread through the hull are each
tested, and `solarEnergy` is scaled by the share that reached the sun. One sample — the default — is
a single ray from the grid's centre and gives 0 or 1.

`litFraction` is **the grid's shadow on itself**, and is 1 for every block when `SolarSelfShadowing`
is off. When it is on, [SunShadowMap.cs](../Data/Scripts/Thermodynamics/Core/Simulation/SunShadowMap.cs)
walks toward the sun from the air just outside each block face, one cell at a time, until the ray
leaves the grid's bounding box: cross anything solid and that face is shadowed. It is a standard
voxel traversal, so the ray visits every cell it passes through and cannot slip diagonally between
two blocks that touch.

**The question is asked of a face, not of a block, and that distinction is the whole model.** A wall
two cells thick has an inner layer that cannot see the sun from its own centre — but the inner
layer's side faces are on the outside of the same wall, looking out of the same flank of the ship,
in full sunlight. Ask per block and a solid hull ends up lit along a single row with the rest dark,
which is wrong in the direction that matters: those flanks are most of the area.

At `SolarGridShadows = full`, other grids cast their shadows through the same walk. Each nearby grid
is folded into a single matrix — this grid's cells to metres, metres to the world, world to the
occluder's metres, its metres to its cells — and the ray is carried into that frame and walked
against its blocks. Two lattices that share no axis, origin or scale are then the same problem as
one.

A pass starts only when the sun has moved more than 2°, the grid's blocks have changed, or a
neighbouring grid has moved — seconds apart on a planet — and is spread over ticks in slices of
`SunShadowBudget` cells, with the previous answer readable until the new one completes, the same way
the room mapper spreads its flood fill.

`faceWeight` and `litFraction` answer different questions per face and both are needed: the first is
how square that face is to the sun, the second is whether anything of the ship stands in the way.
The [self-shadow scenario](../tests/Thermodynamics.Harness/Scenarios.cs) measures both on a solid
slab: the face turned to the sun is lit whole, the flanks around 80%, a recess cut into the hull 0%.

**Point sources** registered by other mods use the same equation with their own direction and
irradiance:

```
watts = irradiance × ε × faceWeight(source) × A_exposed
```

The host reduces a source to a direction and an irradiance before the solver sees it. For a source
of `P` watts at distance `r`,
[ThermalHeatSources.cs](../Data/Scripts/Thermodynamics/Game/ThermalHeatSources.cs) uses
`irradiance = P / (4π r²)`, with `r` floored at 1 m. See [api.md](api.md#heat-sources).

### Aerodynamic friction

```
if airDensity > 0.01 and v_rel > FrictionAtSpeedsAbove:
    watts = FrictionScale × airDensity × v_rel³ × A_exposed × faceWeight(wind)
```

The v³ law matches the scaling of convective heating in hypersonic flow; `FrictionScale` is a
game-feel coefficient. `v_rel` is the wind minus the grid's own velocity, composed in
`EnvironmentSample.ComposeRelativeWind`, so a stationary ship in a storm heats like a fast ship in
still air. The wind is the mod's own field rather than the engine's rating — see
[environment.md](environment.md#wind).

### Waste heat

Recomputed only when the game reports a change, never per step
([ThermalNode.RefreshHeatGeneration](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalNode.cs)):

```
watts = produced × ProducerWasteEnergy + (consumed + thrust) × ConsumerWasteEnergy
```

* `produced` and `consumed` are electrical watts, from `MyResourceSourceComponent.OutputChanged` and
  `MyResourceSinkComponent.CurrentInputChanged`. **Which of the two fractions applies to a block is
  decided by the game, not by the definition.** A reactor delivers through the source component, so
  only its producer fraction can ever heat it and its consumer fraction is dead text; a thruster is
  the reverse. This is the one thing a definition can get wrong with no other symptom — a reactor
  with `ProducerWasteEnergy` 0 reports 0 W forever — so
  `EveryPowerProducerConvertsSomeOfItsOutputToHeat` checks the producer types by name.
* `thrust = ForceMagnitude × (CurrentThrust / MaxThrust)` — the thruster's force in newtons used as
  a watt-equivalent. A balance proxy rather than a conversion, and what makes hydrogen thrusters
  heat: they draw no electricity, so thrust is the only term that can represent them.

Both fractions are balance figures rather than efficiencies. The reactor's is the clearest case:
Space Engineers rates a 3×3×3 block at 300 MW, so a real plant's efficiency applied to it would
destroy every large reactor in the game in a build no player could improve. See
[balance.md](balance.md#reactor-waste-heat) for how the shipped 0.01 was measured.

### Coolant loops

A closed ring of pipe blocks carries one parcel of coolant per pipe
([CoolantLoopBuilder.cs](../Data/Scripts/Thermodynamics/Core/Loops/CoolantLoopBuilder.cs)). It links
to the pipe blocks it runs through, and to whatever block sits behind each pipe's sink faces:

```
G_pipe  = Conductivity × ReferenceConductivity × A_pipe  / L
G_plate = Conductivity × ReferenceConductivity × A_plate / L
watts   = G × (T_loop − T_block)
```

Each parcel exchanges only with its own pipe and the blocks on that pipe's sink faces, so heat
reaches the far side of the ring only by being carried there:

```
parcels/s = (flowRate / cellSize) × sqrt(sum of pump speed × power supplied)
flowRate  = LargeGridFlowRate or SmallGridFlowRate, m/s, by the grid the ring is on
pipe i reads parcel (i − round(parcels carried)) mod N
```

Carrying the fluid is a rotation of the ring's origin rather than a shuffle of its contents. Because
a pipe's index is a whole number, the rounded offset collapses to one integer shift shared by every
pipe, which makes the mapping a bijection at any speed: no parcel is read twice and none is skipped.
It is therefore exactly conservative and costs no substeps however fast the pump runs.

It has a resolution limit, which is not the same thing. A rotation advancing by a constant `k`
parcels per substep means pipe `i` only ever reads parcels in the subgroup `k` generates modulo `N`,
so whenever `gcd(k, N) > 1` the ring silently splits into that many disjoint sets and heat cannot
cross between them. Above one parcel per substep the fluid is therefore also mixed toward the ring's
mean by `1 − 1/parcels`: nothing at one parcel per substep, half at two, complete as the rate runs
away. That is the physically right limit — a ring lapping far faster than it is observed *is* well
mixed on that timescale — and it removes the aliasing outright, because mixing couples every parcel
to every other. It is also plug flow with no numerical diffusion: a hot parcel arrives at the
radiator still hot, smoothed only by the pipes it passed through.

**Flow goes as the square root of combined pump demand**, which is real parallel-pump behaviour
against a fixed circuit where turbulent pressure loss rises with the square of flow: four pumps
carry twice one pump's flow, not four times.

Demands are **signed** by which way each pump faces — a pump drives fluid out of its outlet port, so
whether that port opens onto the next pipe round the ring or the previous one sets the sign. They
subtract before the square root is taken. A pump fitted the wrong way round therefore drives the
ring backwards rather than failing, a backwards ring transports exactly as well, and two opposed
pumps cancel to a standstill while both go on drawing power.

**A pump's own draw is linear in its speed**, which is what stops pump count being a discount. The
affinity law — power with the cube of speed, which is what a real centrifugal pump does — is an
exploit here rather than a trade: a given flow from N pumps needs each at speed `K/N`, and cubed
power makes the bill fall as `1/N²`, so ten pumps idling cost a hundredth of one pump working.
Linear closes it exactly, because flow `F` needs `Σ speed = (F/base)²` and the bill is then
`maxPower × (F/base)²` — a function of the flow alone, with the pump count cancelled out. Doubling
flow costs four times the power however it is arranged, and a second pump buys redundancy and
headroom rather than a discount.

**A large-grid pump is rated at 50 kW, and the figure is derived rather than chosen.** The ring
moves 200 kg/s of coolant — four two-and-a-half-metre parcels a second at fifty kilograms each —
and pushing that against two bar of head at seventy per cent efficiency is `ṁ ΔP / (ρ η)` = 57 kW.
The small-grid block is a fifth. **All of it becomes heat**: a circulator does no work that leaves
the system, so its shaft power dissipates as friction in the coolant it is pushing and its motor
losses stay in the block, which is why its `ConsumerWasteEnergy` is 1 where a reactor's is 0.01.

Against that, the ring carries **680 kW for every kelvin** of difference around it, so a circulator
costs well under a per cent of what it moves where a heat pump pays a third. That contrast is why
both blocks exist. A pump the grid cannot feed circulates proportionally slower rather than
stopping.

With no pump running, `parcels/s` is zero and nothing is carried. The ring still holds its coolant
and still exchanges with what it touches, so the coolant beside a reactor saturates while the
coolant at the radiator stays cold — which is what a stopped pump does. `WellMixedCoolant` reverts
to the older single-mass fluid, where the whole ring is one temperature and heat crosses it
instantly whether anything is circulating or not.

Both exchanges are clamped and energy-conserving like every other. Each loop accumulates what it
drew out of blocks and what it pushed back into them over a step, reported as `LastWattsAbsorbed`
and `LastWattsRejected`. **They are kept apart rather than summed** because a loop in balance —
drawing off a reactor at one sink and shedding into a radiator at another — has a net of about zero
exactly when it is carrying its full load, so a single net figure would describe a working loop as
an idle one.

Rings are traced from the ports each block declares, so any block size or orientation works without
special cases, and each ring is found once whichever pipe the search starts from. A loop keeps its
heat across a rebuild through an order-independent hash of its members.

Radiators are ordinary blocks with high emissivity and a surface-area multiplier. A loop dumps heat
into space by pressing a sink face against one: the panel takes the loop's heat by conduction and
sheds it by radiation from its exposed faces.

### Heat pumps

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
outside it; a host that bills for `LastDemandWatts` is charging for exactly the energy that
appeared.

Three limits bind in turn, and Carnot is the interesting one. As the cold side falls,
`T_cold / (T_hot − T_cold)` falls with it, so each further kelvin costs more power than the last.
The block runs out of electricity long before its cold side runs out of temperature — which is why
nothing clamps the cold side at a floor and nothing needs to. The remaining limit,
`(T_cold − T_min) × mass / h`, is not a balance decision but a substep guard: it stops a large
enough rating taking more heat out of a node in one substep than the node contains.

Reported figures are per step, not per substep: energy is accumulated across the substeps and
divided by the step length at the end.

### Critical temperature and thresholds

```
if T > CriticalTemperature:
    damage = (T − CriticalTemperature) × OverheatDamagePerKelvin × h
```

With `DamageIsPerSecond` on, damage is per second of simulated time and independent of `Frequency`.
Damage is applied by the server only and is not replicated: every machine derives it from its own
simulation.

Any other temperature can be watched through
[ThermalThresholds.cs](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalThresholds.cs). A
crossing is measured against the temperature at the top of the step, so a block that crosses and
recrosses within one step reports once, in the direction it ended up going. Thresholds are half open
— a block resting exactly on one cannot report twice. With none registered, the check is a single
integer comparison per step. See [api.md](api.md#thresholds).

---

## Surfaces, rooms and air

What counts as an exposed surface decides what radiates, what convects and what a room holds. Every
`A_exposed` above is this section's output.

### Surface bits

One integer per grid cell, four face-indexed groups of six
([CellSurface.cs](../Data/Scripts/Thermodynamics/Core/Model/CellSurface.cs)):

```
bits  0-5   self airtight       this cell's face seals
bits  6-11  neighbour airtight  the adjacent cell's facing side seals
bits 12-17  self mount          this cell's face carries a mount surface
bits 18-23  neighbour mount     the adjacent cell's facing side carries one
```

Faces are indexed in one canonical order — Forward, Left, Up, Down, Right, Backward — chosen so that
`face + opposite == 5`.

The self half comes from the block. The neighbour half is **always derived** by
[SurfaceMap.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/SurfaceMap.cs) from the adjacent cell's
self bits, never authored, so the two halves cannot drift apart.

A block type's bits are built once per definition by `BlockSurfaceBuilder` from two things every
host can describe: which faces seal (the definition's pressurisation table) and where the mount
rectangles are. `BlockInstance` rotates them into grid space when a block is placed.

### Two layers

`SurfaceMap` keeps every cell twice.

| Layer | Doors read as | Asked by |
| --- | --- | --- |
| Live | whatever they are doing | exposure — an open doorway does radiate |
| Structural | shut | the room mapper — a door swinging must not change the shape of the ship |

Both are written from the same block in the same call, so they stay in step. **This split is what
makes a door cheap:** rooms are a property of how the ship is *built*, so cycling a door does not
invalidate them.

### Exposure

`GetExposedFaces` counts, per face direction, how many of a block's cell faces are open to the
outside. A face counts when it is on the block's boundary, nothing on the far side seals against it,
and the cell beyond is external.

```
A_exposed = exposedFaces × gridSize² × ExposedSurfaceMultiplier
```

That area is what radiation, convection, solar gain, point sources and friction all multiply.

**A mount joint is deliberately not one of the tests.** Any joint against a block that seals is
already rejected by the second rule, so the only joints a mount test could reach are those against a
block that does *not* seal — a grating, a catwalk, a ladder. Air floods through those, which is why
the room map calls the space beyond them external, and a hull panel under a catwalk goes on
radiating and taking sunlight. The joint conducts as well; both are true at once. The audit counts
those faces as `bolted`, a subset of the exposed ones, so the population can be measured on a real
ship.

Only the block's six boundary slabs are walked, never its interior: the cost is a block's surface,
not its volume.

### The room map

[RoomMapper.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomMapper.cs) classifies every cell in
the grid's padded bounding box as external space, solid structure, or part of an enclosed room, in
two phases:

1. **External** — flood fill from a corner of the padded box, which is guaranteed to be outside the
   grid, crossing any face that does not seal structurally.
2. **Interior** — scan for unvisited cells; each one seeds a room, or is recorded as solid when it
   seals on all six faces.

The pass is **resumable**. `Step(cellBudget)` does a bounded amount of work and returns, so a large
grid spreads its mapping over frames. The budget scales with the grid's volume — `volume / 60`,
clamped to 64…4096 cells — so a big grid maps in roughly constant wall-clock time. Restart requests
coalesce, so welding a thousand blocks in a second costs one pass rather than a thousand.

Maps are double buffered: the mapper builds a fresh `RoomMap` and swaps it in only when the pass
completes, so readers never see a half-filled one. Before the first pass the published map treats
everything as external — which **fails safe**, because a block that radiates when it should not is
visible, and one that cooks silently is not.

Door cells are never classified as solid, even when a shut airtight door seals on all six faces. A
door is a volume that can open, and a portal needs a region on the door's own side to join to.

### Portals and venting

Every face of every door that opens is recorded once, at map time, as a
[RoomPortal.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomPortal.cs): the door, the face, and
the region either side of it. Whether it is currently open is read live from the door.

`RoomMap.RefreshVenting` resolves the portals into which rooms currently reach open air: union-find
over the rooms plus one node standing for open air, merging the two regions of every open portal.
Any room that ends up in open air's set is vented, and `IsExternal` then answers true for its cells —
so what faces it faces outdoors.

**The cost is the number of doors, not the number of cells.** On a forty-thousand block ship with
thirty doors, cycling an airlock costs a walk over thirty portals and an exposure refresh of the
blocks facing the rooms that changed, rather than a flood fill of the bounding box and a pass over
every node. Portals are found by walking the grid's doors, which `GridModel` keeps in their own list
for exactly this reason.

A door welded on since the last pass has no portal yet, so the map cannot answer for it and a full
remap is requested instead.

### Room air

Each sealed, unvented room can hold an air mass
([RoomAir.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomAir.cs)):

```
Volume   = cells × gridSize³
AirMass  = Volume × RoomAirDensity × Pressure
Capacity = AirMass × 1005 / HeatTimeScale          J/K
```

The air links to every block bounding the room, with conductance
`RoomConvectionCoefficient × faces × cellFaceArea`. Links are built by walking the room's own cells
and looking at their six neighbours, so the cost is the size of the room rather than the size of the
ship.

`Pressure` starts at zero and stays there until the host reports otherwise — the simulation has no
way to know whether a compartment is pressurised, and a room at zero pressure has no mass, no links
and no cost.

**Pressure is the game's answer, not this model's**, and
[RoomPressure.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomPressure.cs) is the rule for
reading it. Three things can empty a room and each can veto air on its own:

| Source | Question | Answer when it says no |
| --- | --- | --- |
| `SessionSettings.EnableOxygen` and `EnableOxygenPressurization` | does this world pressurise at all | no room anywhere holds air |
| `IMyCubeGrid.IsRoomAtPositionAirtight` | does the game call this room sealed | that room holds nothing |
| `IMyAirVent.GetOxygenLevel`, or `Depressurize` | how full is it | that much, or nothing |

**None of them can insist on air, only refuse it**: the game owns pressurisation (`C9`) and this
model has no standing to overrule it, so an answer that says *no air* is taken. **An absence of an
answer is not one of them**, and was read as one until 2026-08-24 — see below.

**The reason once given for that was that the two mistakes are unequal, and measurement says they
are unequal the other way round.** Air was described as heat capacity — a room wrongly given it
dragging its walls along, a room wrongly denied it losing only a little inertia. It is not mainly
capacity. A link's conductance is `RoomConvectionCoefficient × faces × cellFaceArea` and carries no
pressure term at all, so pressure decides whether a compartment's walls are coupled and, above zero,
nothing else. Measured on a 2,000-block census hull settled under load in vacuum: the hottest block
runs at **1,502.83 K** at every pressure from 0.2 to 1.0 — identical to two decimals across a
fivefold change in air mass — and at **1,706.08 K** with the air gone, while the hull *mean* moves
3.8 K. Room air is a **mixer, not a sink**: it barely changes the hull's energy balance and moves
its hot spot by 203 K, which is the number overheat damage is taken off.

**So the two errors are the same size and differ in sign.** A room wrongly denied air is computed
203 K too hot, and a room wrongly given it 203 K too cool. What is not symmetric is what each costs
a player: too hot destroys a block that should have survived, and too cool fails to threaten one
that should have been. The chain can only ever deny, so its bias is toward the first.

**The chain stays and the fallback moved** (`C22`, 2026-08-24). It stays because `C9` and not the
error cost is what justifies it — every veto is the game or the world *answering*, and there is
nothing for a requirement to be built out of. What did not survive is treating **silence** as a
fourth veto: a compartment the game calls airtight, on a world where pressurisation is on, that no
lookup found a level for is a lookup that missed rather than an answer of empty — and a miss is
exactly what two models with different room shapes produce. `RoomPressure.Level` returns
`AssumedWhenUnanswered` there. Any positive value would behave identically, since the measurement
above says the level does not matter above zero; what the constant decides is whether the room mixes
at all. `RoomPressureTests` pins that silence does not defeat a veto, `RoomAirCouplingTests` the
pressure-independence and `ClientInputTests` the discontinuity a client's disagreement about
pressure produces.

The game's sealing test is consulted rather than this model's own room map because the two disagree,
and the game is right: it knows the real shape of a sloped or half block where the room mapper knows
only whether a cell's faces seal. The room map still decides the *geometry* — which cells are one
room, and which surfaces face indoors — since that is what exposure needs, and it is unaffected by
whether the world models oxygen.

How full a room is comes from the game's own gas system, **per room**:

```csharp
IMyOxygenRoom room = grid.GasSystem.GetOxygenRoomForCubeGridPosition(ref cell);
float level = room.OxygenLevel(grid.GridSize);
```

Not from the air vents. A vent can only speak for the room it stands in, and **the game's rooms are
the whole connected volume where this model's are pieces of it**, because the game's sealing test is
finer than a cell and splits nothing where this splits often. Giving air only to the pieces a vent
physically touched left every other piece of the same compartment in vacuum — measured on one ship
as twelve mapped rooms, of which the game held air in nine, and only two with a vent against them:
seven compartments in hard vacuum with the doors open onto a pressurised cabin. Reading it per room
also makes the vent's own position irrelevant, which is the correct model: a cabin with no vent of
its own, joined through a doorway to one that has, is full.

The vents remain as a fallback for a world whose gas system cannot be read, and only run when
something goes unanswered. There the old limit still applies — a sealed compartment nobody ever
piped air into is indistinguishable from one nobody can measure.

Continuity across rebuilds is by anchor: a room is identified by its lexicographically lowest cell,
which is stable while the room's shape is, so building elsewhere on the ship does not cost the
compartment its heat. Air appearing in a room for the first time starts at the average temperature
of the surfaces around it — it has been sitting in there with them — rather than at a placeholder
that would make a new compartment a heat sink.

Opening a door vents the room, which removes its air in the same frame; shutting the door gives it
back, at the temperature of the walls.

### Diagnostics

`RoomAudit` checks a published map against the grid and reports disagreements: cells classified as
external that are enclosed, rooms that should have merged, and so on. Nothing in the simulation
reads it, and it is never called unless something is asking.

`DebugTextOnScreen` reports, for the cell under the crosshair, its classification, its six
neighbours' classifications, whether each face between them seals, and the raw surface bits. A hull
block that reads "external" on an inside face is the leak.

**Both of those check this model against itself**, and the failure mode that matters is one neither
can see. This model decides sealing from each definition's pressurisation table, cell by cell; the
game decides it from its own test, which knows the real shape of a sloped block where this knows a
cell. When the two disagree the fill walks in from outside and a whole compartment stops existing —
no room, no air, nothing drawn in the room overlay, and no complaint anywhere, because
**pressurisation is only ever asked about rooms this model already found**.

[UnmappedRooms.cs](../Data/Scripts/Thermodynamics/Core/Surfaces/UnmappedRooms.cs) closes it by
asking the other model. Every cell the map calls external is offered to
`MyCubeGrid.IsRoomAtPositionAirtight`, and the cells it calls airtight are grouped into connected
regions — each one a compartment this model lost. Per region it reports the air vents standing in it
and whether they say `IsPressurized`, which is the identity a player can quote, and the block
subtypes across the faces this model leaves open, which is the list the fix is made from. A face
leaving a region that this model *does* seal is not reported: there the two agree and the region
simply ends.

**The test is oxygen, not airtightness.** Both `IsRoomAtPositionAirtight` and
`IMyAirVent.IsPressurized` mean *sealed*; neither means *full*. A cupboard nobody ever piped air
into, on a ship in vacuum, is airtight and empty and both models are right about it. The level comes
from the grid's own gas system, which answers for every compartment including the ones with no vent
to ask. The report counts these under **found, dry, air in game**.

The room view draws three states, because a room with no air and a room that was never found must
not look alike:

| State | Drawn as |
| --- | --- |
| Air in it | Solid, on the temperature ramp, edged in the room's own colour |
| Dry, and the game has no air in it either | Faint grey outline, the room's colour at low alpha |
| **Dry, and the game has air in it** | **Magenta, filled, heavy edge** |
| Not found at all, and the game calls it sealed | Red, filled |

Magenta and red are deliberately off the temperature ramp. The one thing a room the model failed to
fill must never look like is a cold room.

It is a diagnostic and drives nothing. Pressurisation still comes from the map, deliberately — the
fix belongs in the surface bits, and this is the measurement that says which blocks to fix and by
how much. See [telemetry.md](telemetry.md#room-dump) for the columns and the overlay.

---

## Environment inputs

The solver consumes four figures per grid per environment refresh — ambient temperature, air
density, the relative wind vector, and solar irradiance with its occluded share. How each is decided
from latitude, ground, altitude, hour, weather and depth is in
[environment.md](environment.md#what-the-solver-computes), which also carries the equations
`EnvironmentSolver` evaluates.

---

## Where this model differs from the one it replaced

The previous per-cell implementation is preserved verbatim in
[tests/Thermodynamics.Tests/LegacyFormulas.cs](../tests/Thermodynamics.Tests/LegacyFormulas.cs), and
several tests compare against it so the differences stay pinned rather than remembered.

| Then | Now |
| --- | --- |
| Two conductances per joint, disagreeing by block size | One symmetric series conductance |
| Contact counted in whole grid faces | Contact area from block bounds and mount coverage |
| Gauss–Seidel sweep with alternating direction to hide order dependence | Order-independent accumulation |
| Fixed step, unbounded exchange | Substepping from the stiffest node, plus an equilibrium clamp |
| Damage scaled with `Frequency` | Damage per second of simulated time |
| `SpecificHeat` a flat game number | Real J/(kg·K) with one global clock |

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-25 | Stated the wind factor's floor as present-tense evidence rather than as what it *used to be* (`R12`), and absorbed the measured consequence — a 2 MW hull settling 0.9 K hotter in a 40 m/s wind — from the twelve-line comment in `ThermalSolver` that had been carrying it. The comment names this section now. |
| 2026-08-24 | **Silence stopped being a veto** (`C22`). `RoomPressure.Level` treated *nothing reported* and *reported empty* identically, though the parameter's own documentation said they were distinct. The three vetoes are each the game or the world answering; a compartment the game calls airtight, on a pressurised world, that no lookup found a level for is a lookup that missed — which is what two models with different room shapes produce — and it now takes `AssumedWhenUnanswered`. Also corrected the error-size claim above: the two mistakes are the *same* size, about 203 K, and differ in sign; what is asymmetric is that too hot destroys a block which should have survived. |
| 2026-08-24 | Corrected the reason given for the pressure veto chain. It was justified by air being heat capacity, so that denying air wrongly cost only a little inertia; measured, the link conductance carries no pressure term, so denying air removes the whole coupling and costs 203 K on the hottest block while every pressure above zero is identical to two decimals. The chain stays — `C9` justifies it — and whether the fallback should deny on uncertainty is now [backlog.md](backlog.md) `C22` ([backlog.md](backlog.md) `F21`). |
| 2026-08-22 | Wrote down what a coolant pump costs, now that it costs anything: 50 kW on a large grid, derived from the loop's own mass flow against two bar of head, all of it becoming heat because a circulator does no work that leaves the system ([backlog.md](backlog.md) `C13`). |
| 2026-08-22 | The convection wind factor runs from 1 upward rather than from 0.5 to 1. Forced convection adds to natural convection; the old floor made a wind under about 50 m/s a net warmer, because most of a closed hull's faces do not point into it ([backlog.md](backlog.md) `B29`). The two-to-one contrast between a windward face and a lee one is unchanged. |
| 2026-08-22 | Radiation in and radiation out are two coefficients. Emission keeps the emissivity; the sun and point sources read `SolarAbsorptivity`, which follows the emissivity unless authored, so the grey-body behaviour is the default rather than the only option ([backlog.md](backlog.md) `B27`). |
| 2026-08-22 | Corrected the reactor's shipped waste fraction, which this page and [tests/README.md](../tests/README.md) both quoted as 0.02 against the 0.01 in `Cubes.xml` and on [balance.md](balance.md#reactor-waste-heat). 0.02 is the value the sweep rejected: it puts a 300 MW reactor past critical *bare* in vacuum, which is a state no build can improve on. |
| 2026-08-22 | Corrected the substep cap quoted beside `LastStepWasClamped`: it read 16 and ships 64. |
| 2026-08-22 | Absorbed `surface-mapping.md`, whose subject is the geometry every area term on this page reads. Moved the environment equations to [environment.md](environment.md), leaving one home for them instead of two. Converted to present tense, with the defect narratives moved to [known-issues.md](known-issues.md) and this log. Promoted the three solver invariants into a table of their own. |
| 2026-08-20 | Made reactors generate the heat they always should have: which waste fraction applies is decided by the game's component, not by the definition, so a reactor's consumer fraction was dead text. Added the check over producer types by name. |
| 2026-08-19 | Took block conductivity in real W/(m·K) rather than a 0..1 quality figure. Reported the convection coefficient after the atmosphere blend rather than before it. |
| 2026-08-18 | Added room air as a node kind: one well-mixed mass per sealed room, coupling every surface bounding it — the only path between two walls that do not touch. Read pressure from the game's gas system per room rather than from air vents, which had left every compartment without a vent of its own in vacuum. |
| 2026-08-17 | Added self-shadowing per face rather than per block, and folded other grids' shadows into the same voxel walk. A projected-and-bucketed shadow map was measured against it first and is wrong in both directions at once: on a 76-cell structure at a real planetary sun angle it lit 38 cells where 28 are lit, ten of them behind something. |
| 2026-08-12 | Rebuilt the model per block rather than per cell: one symmetric series conductance per joint, contact area from block bounds and mount coverage, order-independent accumulation, substepping from the stiffest node, and real specific heat under one global clock. |
