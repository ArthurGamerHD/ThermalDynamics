# Surfaces, rooms and air

What counts as an exposed surface decides what radiates, what convects and what a room holds. This
is how the mod works it out.

## Surface bits

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
[SurfaceMap](../Data/Scripts/Thermodynamics/Core/Surfaces/SurfaceMap.cs) from the adjacent cell's
self bits, never authored, so the two halves cannot drift apart.

A block type's bits are built once per definition by `BlockSurfaceBuilder` from two things every
host can describe: which faces seal (the definition's pressurisation table) and where the mount
rectangles are. `BlockInstance` rotates them into grid space when a block is placed.

## Two layers

`SurfaceMap` keeps every cell twice.

| Layer | Doors read as | Asked by |
| --- | --- | --- |
| Live | whatever they are doing | exposure — an open doorway does radiate |
| Structural | shut | the room mapper — a door swinging must not change the shape of the ship |

Both are written from the same block in the same call, so they stay in step. This split is what
makes a door cheap: rooms are a property of how the ship is *built*, so cycling a door does not
invalidate them.

## Exposure

`GetExposedFaces` counts, per face direction, how many of a block's cell faces are open to the
outside. A face counts when:

* it is on the block's boundary,
* nothing on the far side seals against it,
* it is not a mount-to-mount contact — two bolted faces are a conduction joint, not a surface,
* and the cell beyond is external.

Only the block's six boundary slabs are walked, never its interior: the cost is a block's surface,
not its volume.

`ExposedArea = exposedFaces × gridSize² × SurfaceAreaScaler`, and that area is what radiation,
convection, solar gain, point sources and friction all multiply.

## The room map

[RoomMapper](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomMapper.cs) classifies every cell in
the grid's padded bounding box as external space, solid structure, or part of an enclosed room.

Two phases:

1. **External** — flood fill from a corner of the padded box, which is guaranteed to be outside the
   grid, crossing any face that does not seal structurally.
2. **Interior** — scan for unvisited cells; each one seeds a room, or is recorded as solid when it
   seals on all six faces.

The pass is **resumable**. `Step(cellBudget)` does a bounded amount of work and returns, so a large
grid spreads its mapping over frames. The budget scales with the grid's volume — `volume / 60`,
clamped to 64…4096 cells — so a big grid maps in roughly constant wall-clock time. Restart requests
coalesce, so welding a thousand blocks in a second costs one pass rather than a thousand.

Maps are double buffered: the mapper builds a fresh `RoomMap` and swaps it in only when the pass
completes, so readers never see a half-filled one. Before the first pass, the published map treats
everything as external — which fails safe, because a block that radiates when it should not is
visible, and one that cooks silently is not.

Door cells are never classified as solid, even when a shut airtight door seals on all six faces. A
door is a volume that can open, and a portal needs a region on the door's own side to join to.

## Portals and venting

Every face of every door that opens is recorded once, at map time, as a
[RoomPortal](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomPortal.cs): the door, the face, and
the region either side of it. Whether it is currently open is read live from the door.

`RoomMap.RefreshVenting` resolves the portals into which rooms currently reach open air: union-find
over the rooms plus one node standing for open air, merging the two regions of every open portal.
Any room that ends up in open air's set is vented, and `IsExternal` then answers true for its cells
— so what faces it faces outdoors.

The cost is the number of doors, not the number of cells. On a forty-thousand block ship with thirty
doors, cycling an airlock costs a walk over thirty portals and an exposure refresh of the blocks
facing the rooms that changed, rather than a flood fill of the bounding box and a pass over every
node.

Portals are found by walking the grid's doors, which `GridModel` keeps in their own list for exactly
this reason.

A door welded on since the last pass has no portal yet, so the map cannot answer for it and a full
remap is requested instead.

## Room air

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
and no cost. In Space Engineers the figure comes from air vents, the only place the game exposes it:
every eight steps each vent's oxygen level is written to the room it sits in. A sealed room with no
vent holds no air.

Continuity across rebuilds is by anchor: a room is identified by its lexicographically lowest cell,
which is stable while the room's shape is, so building elsewhere on the ship does not cost the
compartment its heat. Air appearing in a room for the first time starts at the average temperature
of the surfaces around it — it has been sitting in there with them — rather than at a placeholder
that would make a new compartment a heat sink.

Opening a door vents the room, which removes its air in the same frame; shutting the door gives it
back, at the temperature of the walls.

## Diagnostics

`RoomAudit` checks a published map against the grid and reports disagreements: cells classified as
external that are enclosed, rooms that should have merged, and so on. Nothing in the simulation
reads it, and it is never called unless something is asking.

`DebugTextOnScreen` reports, for the cell under the crosshair, its classification, its six
neighbours' classifications, whether each face between them seals, and the raw surface bits. A hull
block that reads "external" on an inside face is the leak.
