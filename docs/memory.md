# Memory

Where a grid's memory goes, measured; and what can be given back.

Companion to [load-and-hitching.md](load-and-hitching.md), which covers time rather than space, and
to [scale-design.md §6](scale-design.md#6-data-structures), which budgets ~110 bytes a node for a
million-block grid. This page is about the distance between that budget and where the code is.

Reproduce with `bench memory --size N`.

---

## The measurement, and a correction

Earlier figures in this repository put a million-block grid at 2.5 GB. That number came from
`GC.GetTotalMemory(false)` taken straight after a build, and it counted garbage that had not been
collected yet. It was not wrong about the high-water mark, but it was wrong about what the grid
*holds*.

`bench memory` builds a grid in stages and collects between them, so it separates the two:

| | 126,731 blocks, 1,499,616 cell bounding box |
| --- | --- |
| **retained** | what the grid holds for as long as it exists |
| **peak** | what the process must have room for while a room mapping pass runs |

Both matter and they fail differently. Retained decides how many ships a server can hold; peak
decides whether one of them can be loaded at all.

> **A measurement trap worth recording.** The first version of this benchmark reported the retained
> total as zero and the room map as having *freed* 142 MB. The grid was a local variable, nothing
> referenced it after the last stage, and the collection inside the final reading was entitled to
> reclaim all of it. `GC.KeepAlive` at the end of the method is what makes the numbers real.

---

## Where it goes

Measured at 126,731 blocks, after the change described in §1:

| Structure | MB | B/block | Scales with |
| --- | ---: | ---: | --- |
| `BlockInstance` | 55.1 | 456 | blocks, and their **cells** |
| `GridModel` indexes | 14.7 | 122 | blocks, and their **cells** |
| `SurfaceMap` | 8.4 | 69 | **cells** |
| Solver | 64.9 | 537 | nodes and links |
| `RoomMap` retained | 70.1 | 580 | **bounding volume** |
| **Total retained** | **213.2** | **1,764** | |
| Mapper transient, on top | 65.1 | 539 | **bounding volume** |
| **Total peak** | **278.3** | **2,303** | |

Three of those rows scale with something other than block count, and that is the whole story. A
hull encloses about fifteen times more empty space than it has blocks, so anything indexed by
bounding volume dominates; and a block that spans many lattice cells multiplies everything indexed
by cell.

**For SE1 this is survivable.** A 20,000-block ship retains about 35 MB.

**For SE2 it is not**, and not because blocks get more numerous. On a 0.25 m lattice a 1 m block
spans 64 cells and a 5 m block spans 8,000. Every per-cell row above multiplies by that, and the
bounding volume — already the largest row — multiplies by a thousand for the same ship measured on
a lattice ten times finer in each axis. Five times the blocks is the least of it.

---

## What has been done

### 1. The mapper's visited set is a bitset — *done*

A flood fill accumulates, by the end of a pass, every cell of its bounding box. It held them in a
`HashSet<Vector3I>`, which spends about 31 bytes a cell once buckets, hash codes and load factor
are counted. That was the high-water mark of the entire mod.

`CellBitset` is one bit per cell of a known box: 248 times smaller, and it is a set the region is
dense in, which is exactly what a bitset is for.

| | before | after |
| --- | ---: | ---: |
| retained | 279.3 MB | **213.2 MB** |
| peak | 399.9 MB | **278.3 MB** |

At a million blocks the same change is worth about 440 MB, because it scales with the bounding box
rather than with the ship.

---

## What is worth doing next

Ranked by what they return, with the measured basis for each estimate.

### 2. Stop storing external cells — ~50 MB at 126k, more as grids grow

`RoomMap` keeps a `HashSet<Vector3I>` of every cell classified as open air. On a hull that is about
nine tenths of the bounding box — 1.3 million of 1.5 million cells here — and it is **derivable**: a
cell that is not solid and not in a room is external, by definition. The set could be a count.

One caller wants the cells themselves: `UnmappedRooms`, which walks them to find compartments the
game seals and this model does not. It is a diagnostic, and it could walk the box directly instead.

**Largest remaining win, and a pure one** — the information is already implied.

### 3. Rooms as one cell array with per-room ranges — ~20 MB at 126k

Room cells are held twice: once in `List<HashSet<Vector3I>> rooms` and once in
`Dictionary<Vector3I, int> roomIndexByCell`. A single `Vector3I[]` of all room cells, sorted by
room, with an `int[]` of range starts, holds the same information once — 12 bytes a cell against
about 70 for the two.

### 4. Share the per-block face fractions — ~20 MB at 126k

Every `BlockInstance` allocates four `float[6]` arrays for mount and seal fractions — 160 of its
456 bytes. All four are pure functions of `(model, orientation)`, of which there are 24 per model,
so they can be built once per pair and shared. Nothing about them is per instance; the door state
that varies picks *which* array to read, and both are already stored.

**Best return per line changed of anything on this page.**

### 5. Move the node diagnostics out of the node — ~4 MB at 126k

`ThermalNode` carries eight floats of per-mechanism watts that only exist for the telemetry report
and the debug overlay. `CollectDiagnostics` is false in ordinary play, so a shipping server is
paying 32 bytes a node to store nothing. A side array, allocated when diagnostics are switched on
and dropped when they are switched off, costs a null check on a path that already has one.

### 6. Grow the solver's arrays by a quarter, not by double — ~10 MB at 126k

`EnsureBuffers` allocates `nodes.Count * 2`. Around fourteen arrays are indexed by node, so a
settled grid carries a full copy of each in slack. Doubling is the right growth policy for
something appended to in a tight loop; these grow when a block is placed, which is not that.

### 7. Fold the block slot map into the block key map — ~4 MB at 126k

`GridModel` keeps `blocksByKey` and `blockSlots` as separate dictionaries on the same key, the
second added for O(1) removal. One dictionary to a small struct holds both.

### 8. For SE2: blocks as boxes, not cells — the only one that matters at that scale

Everything above is a fraction of a fixed per-block cost. This one is a multiplier.

`GridModel.blocksByCell`, `SurfaceMap.states` and `BlockInstance.Cells` hold one entry per occupied
*cell*. On SE1 that is one entry per block and the rows above are the whole cost. On SE2's 0.25 m
lattice a 5 m block occupies 8,000 cells, so those three rows alone would cost roughly **half a
megabyte for one block**.

Keen did not make that choice: `CubeBlockDefinition.OccupiedGridCellsGroups` is an array of
`BoundingBoxI`, and the engine never enumerates a block's cells. Neither does this model's
*geometry* — contact area, face area, depth and exposure all fall out of two integer AABBs in
constant time, and `Se2LatticeTests` holds that. It is only the *indexing* that still walks cells.

This is [model-redesign.md §2](model-redesign.md), and it is the difference between the model
supporting SE2 and the storage refusing to.

### 9. For SE2: the room map off the block lattice

The bounding-volume rows scale with the cube of lattice resolution. The same ship measured on a
0.25 m lattice instead of 2.5 m has a thousand times the cells, so even at one bit each the
mapper's set is gigabytes.

The flood fill has to run on a coarser lattice than the blocks, or on the host's own gas system
where one exists. [model-redesign.md §4](model-redesign.md) sets this out; nothing here changes it.

---

## Summary

| | 126k blocks retained | after 2–7 | SE2 outlook |
| --- | ---: | ---: | --- |
| today | 213 MB | ~105 MB | unusable without §8 and §9 |

Items 2 to 7 roughly halve what a grid holds and are all local changes with no design work behind
them. They do not change the SE2 picture, because that is not about constants — it is about which
things are counted per cell.
