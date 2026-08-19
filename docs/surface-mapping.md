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

`ExposedArea = exposedFaces × gridSize² × ExposedSurfaceMultiplier`, and that area is what radiation,
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
and no cost.

In Space Engineers the figure comes from **the game's own gas system**, per room:

```csharp
IMyOxygenRoom room = grid.GasSystem.GetOxygenRoomForCubeGridPosition(ref cell);
float level = room.OxygenLevel(grid.GridSize);
```

This used to read the air vents instead, on the belief that a vent was the only place a mod could
read a room's oxygen level. It is not, and the belief cost a great deal. **The game's rooms are the
whole connected volume; this model's are pieces of it**, because the game's sealing test is finer
than a cell and splits nothing where this splits often. Giving air only to the pieces a vent
physically touched left every other piece of the same compartment in vacuum. Measured on one ship:
twelve mapped rooms, of which the game held air in nine, and only two had a vent against them —
seven compartments in hard vacuum with the doors open onto a pressurised cabin.

Reading it per room also makes the vent's own position irrelevant, which is the correct model: a
cabin with no vent of its own, joined through a doorway to one that has, is full.

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

## Diagnostics

`RoomAudit` checks a published map against the grid and reports disagreements: cells classified as
external that are enclosed, rooms that should have merged, and so on. Nothing in the simulation
reads it, and it is never called unless something is asking.

`DebugTextOnScreen` reports, for the cell under the crosshair, its classification, its six
neighbours' classifications, whether each face between them seals, and the raw surface bits. A hull
block that reads "external" on an inside face is the leak.

### Where the audit was not enough

Both of the above check this model against itself, and the failure that got reported from the field
is one neither could see. This model decides sealing from each definition's pressurisation table,
cell by cell; the game decides it from its own test, which knows the real shape of a sloped block
where this knows a cell. When the two disagree the fill walks in from outside and a whole
compartment stops existing — no room, no air, nothing drawn in the room overlay, and no complaint
anywhere, because **pressurisation is only ever asked about rooms this model already found**. A
player stood in a sealed room with a vent reading full and there was no figure in any report that
said so.

The measured case: 12 rooms totalling 61 cells on a 1,293-block ship, 1,298 of 1,749 block cells
classified as outdoors, and only 24 of 8,702 block faces bounding a room at all. Every one of those
numbers was already in the report and none of them said what mattered.

**When the comparison was finally run, the map was right.** Twelve compartments found, **zero held
only by the game**, and eleven of the twelve agreeing with `IsRoomAtPositionAirtight`. The rooms
were being found and then given no air, for two reasons that had nothing to do with sealing — see
[known-issues.md](known-issues.md). That is the argument for measuring before fixing: the sealing
test looked guilty from the counts alone and was not.

[UnmappedRooms](../Data/Scripts/Thermodynamics/Core/Surfaces/UnmappedRooms.cs) closes it by asking
the other model. Every cell the map calls external is offered to
`MyCubeGrid.IsRoomAtPositionAirtight`, and the cells it calls airtight are grouped into connected
regions — each one a compartment this model lost. Per region it reports the air vents standing in
it and whether they say `IsPressurized`, which is the identity a player can quote, and the block
subtypes across the faces this model leaves open, which is the list the fix is made from. A face
leaving a region that this model *does* seal is not reported: there the two agree and the region
simply ends.

### Three states, not two

The room view used to draw a room with air and nothing else. A room with no air was drawn as a
wireframe box in a fully transparent colour and then skipped before its edge pass, so it rendered
as nothing — identical to a room that had never been found. That is why "is my room detected"
could not be answered by looking at it. There are three states and they are now distinct:

| State | Drawn as |
| --- | --- |
| Air in it | Solid, on the temperature ramp, edged in the room's own colour |
| Dry, and the game has no air in it either | Faint grey outline, the room's colour at low alpha |
| **Dry, and the game has air in it** | **Magenta, filled, heavy edge** |
| Not found at all, and the game calls it sealed | Red, filled — see above |

The third row is the one with no diagnostic before this. It is not a mapping failure — the
compartment was found — and not a working room either, and it is what a player sees when the map is
right and the air never arrives.

**The test is oxygen, not airtightness**, and the first version of this got that wrong. Both
`IsRoomAtPositionAirtight` and `IMyAirVent.IsPressurized` mean *sealed*; neither means *full*. A
cupboard nobody ever piped air into, on a ship in vacuum, is airtight and empty and both models are
right about it. Flagging those painted eight correct compartments as faults. The level comes from
the grid's own gas system — `IMyCubeGrid.GasSystem.GetOxygenRoomForCubeGridPosition` then
`IMyOxygenRoom.OxygenLevel` — which answers for every compartment including the ones with no vent
to ask, and falls back to a vent's reading if the gas system is unavailable. The report counts
these under **found, dry, air in game**.

Magenta is deliberately off the temperature ramp. The one thing a room the model failed to fill
must never look like is a cold room.

It is a diagnostic and drives nothing. Pressurisation still comes from the map, deliberately — the
fix belongs in the surface bits, and this is the measurement that says which blocks to fix and by
how much. See [telemetry.md](telemetry.md#room-dump) for the columns and the overlay.
