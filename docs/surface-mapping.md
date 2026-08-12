# Surface mapping and room detection

Radiation, convection, solar gain and friction all scale with **exposed surface area**. A block
buried inside a hull must not radiate to space, and a block sealed inside a pressurised room
must not either.

> **Status.** The bit layout below is still exactly what the model uses
> ([Core/Model/CellSurface.cs](../Data/Scripts/Thermodynamics/Core/Model/CellSurface.cs)), but the
> code that fills it has moved and changed shape. Surface bits are now built **once per block
> definition** by
> [BlockSurfaceBuilder](../Data/Scripts/Thermodynamics/Core/Model/BlockSurfaceBuilder.cs) from the
> definition's airtightness table and mount points, and rotated into grid space per placement.
> The grid-wide map is [SurfaceMap](../Data/Scripts/Thermodynamics/Core/Surfaces/SurfaceMap.cs),
> which derives every neighbour bit rather than writing it twice, and the flood fill is
> [RoomMapper](../Data/Scripts/Thermodynamics/Core/Surfaces/RoomMapper.cs), which publishes a
> finished map rather than letting readers see a half-filled one. Exposure is counted per block
> face by `SurfaceMap.GetExposedFaces`, walking a block's boundary rather than its volume.

The mapper still runs in two stages: per-block **surface flags**, and an incremental **flood
fill** budgeted per frame until it has classified the whole grid.

## Direction indices

One order is used everywhere in the mapper and in `ThermalCell`:

| Index | Direction |
| --- | --- |
| 0 | Forward |
| 1 | Left |
| 2 | Up |
| 3 | Down |
| 4 | Right |
| 5 | Backward |

Opposite of index `i` is index `5 − i`. That identity is what all the bit shuffling relies on:
a neighbour's "self" flag for face `5 − i` becomes this cell's "neighbour" flag for face `i`.

> `Tools.IndexToDirection` / `Tools.DirectionToIndex` use a *different*, axis-sign ordering.
> They are not used by the mapper and should not be mixed with it. See
> [known-issues.md](known-issues.md).

## The surface state integer

`Surfaces` maps every occupied `Vector3I` cell (not every block — a multi-cell block occupies
several entries) to a 24-bit packed state, defined by the `SurfaceFlags` enum:

| Bits | Group | Meaning |
| --- | --- | --- |
| 0–5 | Self airtight | This cell's face seals against pressure. |
| 6–11 | Neighbour airtight | The adjacent cell's facing side seals. |
| 12–17 | Self mount point | This cell's face has an enabled mount point overlapping the face plane. |
| 18–23 | Neighbour mount point | The adjacent cell's facing side has one. |

`CalculateBlockSurfaceStates`
([ThermalGridMapper.cs:182](../Data/Scripts/Thermodynamics/ThermalGridMapper.cs#L182)) fills
this in for each cell of a block:

* **Airtight** comes from `MyCubeBlockDefinition.IsAirTight` (all six faces), or per-face from
  `IsCubePressurized`. Doors are special-cased: `PressurizedAlways` faces always seal;
  `PressurizedClosed` faces seal while the door is closed or closing; `MyAirtightSlideDoor` and
  `MyAirtightDoorGeneric` seal on specific local axes when fully closed; other doors seal on
  every face without a mount point. Doors subscribe to `DoorStateChanged` and re-run the
  calculation when they open or close.
* **Mount points** are transformed into grid space and intersected against the per-face boxes
  in `mountBounds` — thin slabs straddling each face plane, inset by 0.002 so that
  edge-touching mounts do not register.
* Neighbour bits are read from any adjacent cell already in `Surfaces`, and those neighbours
  are queued for `UpdateCell` so the relationship is written back reciprocally.

`OnBlockRemoved` erases the block's cells and clears the corresponding neighbour bits on the
surrounding cells.

## The flood fill

`BeginCrawl` resets state and seeds the external queue with `Grid.Min − 1`, a cell guaranteed
to be outside the grid. `GridMapperUpdate` then drains the queues at
`Quota = max(ceil(gridSize / 60), 1)` cells per frame, so a big grid takes many frames to
classify without stalling the sim.

Two passes share the work:

**`CrawlExternal`** — breadth-first through empty space. Any neighbour cell whose facing side
is not airtight is also external and gets enqueued. The first non-external cell encountered is
handed to the grid queue as the seed for room detection, preferring a *fully* airtight cell as
the seed when one is found (`IsFullyAirtightQueued`).

**`CrawlGrid`** — breadth-first through the interior, assigning cells to rooms. Two adjacent
non-airtight cells belong to the same room; when the crawl discovers two rooms are connected it
merges them (the lower index wins). Fully airtight cells act as separators and are not placed
in any room.

Results land in `Rooms`, a list of `HashSet<Vector3I>` with a fixed layout:

| Index | Contents |
| --- | --- |
| 0 | External cells — everything connected to vacuum |
| 1 | Edge/queued cells that are not part of a room |
| 2+ | One entry per detected sealed room |

The fill writes into `RoomBuffer` and swaps it with `Rooms` under a lock only when complete, so
readers always see a consistent classification. Completion fires the `SurfaceCheckComplete`
event, which `ThermalGrid.Init` uses to schedule `SurfaceUpdateFrame = SimulationFrame + 1`;
every cell then refreshes its counts during the next simulation pass.

## From flags to exposed faces

`GetExposedSurfacesByDirection(min, max)`
([ThermalGridMapper.cs:691](../Data/Scripts/Thermodynamics/ThermalGridMapper.cs#L691)) walks
every cell of a block and counts, per direction, the faces that are:

1. on the block's outer boundary (the neighbouring cell is outside `min…max`), **and**
2. not blocked by an airtight neighbour, **and**
3. in `Rooms[0]` — i.e. facing genuinely external space, not a sealed interior, **and**
4. not a mount-point-to-mount-point contact (both self and neighbour mount bits set).

`ThermalCell.UpdateSurfaces` stores that as six counts packed 5 bits each into
`_exposedSurfacesPacked` (so a single direction saturates at 31 faces), sums them into
`ExposedSurfaces`, and derives:

```
ExposedSurfaceArea = ExposedSurfaces × Area
Boltzmann          = −Emissivity × σ × ExposedSurfaceArea
```

The per-direction counts are what make heating directional: `DirectionalIntensity` weights each
face's count by its dot product with the sun or the wind, then normalises by the total exposed
face count.

Note that only `Rooms[0]` is consumed by the simulation today. The detected interior rooms are
computed and swapped in, but nothing yet models room air temperature, so a sealed room's cells
simply lose those faces from their exposed count.
