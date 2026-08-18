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

Measured at 126,731 blocks, before and after the four changes in §1:

| Structure | B/block before | B/block now | Scales with |
| --- | ---: | ---: | --- |
| `BlockInstance` | 456 | **240** | blocks, and their **cells** |
| `GridModel` indexes | 122 | 122 | blocks, and their **cells** |
| `SurfaceMap` | 69 | 69 | **cells** |
| Solver | 537 | **468** | nodes and links |
| `RoomMap` retained | 1,126 | **32** | ~~bounding volume~~ rooms and structure |
| **Total retained** | **2,311** | **932** | |
| **Total peak** | **3,309** | **960** | |

In megabytes at that size: 279 MB retained and 400 MB peak became **113 MB and 116 MB**. At
505,566 blocks it measures 952 B/block retained against 932 at 126,731 — **flat**, where it used
to climb, because nothing significant is indexed by bounding volume any more. The gap between
retained and peak has all but closed too: there is no longer a transient that dwarfs what the grid
holds.

**Bounding volume used to be the whole story and now is not.** A hull encloses about fifteen times
more empty space than it has blocks, and two of the structures above were indexed by that space
rather than by the ship in it; between them they were more than half of everything. Both are gone,
which is why the per-block figure stopped climbing with grid size.

What is left divides into two kinds. Most of it is per node and per link — the solver's mirrored
arrays, the node objects, the conduction graph — and that is honest state whose size is the model's
own shape. The rest is per **cell**: `GridModel.blocksByCell`, `SurfaceMap` and
`BlockInstance.Cells` still hold one entry for every cell a block occupies.

**For SE1 that distinction does not matter.** A block occupies one cell, so per-cell is per-block,
and a 20,000-block ship retains about 19 MB.

**For SE2 it is the only thing that matters.** On a 0.25 m lattice a 1 m block spans 64 cells and a
5 m block spans 8,000, so those three structures multiply by the volume of every block while
everything else stays put. Five times the blocks is the least of it — see §8.

---

## What has been done

Four changes, all local, together worth 60 % of what a grid retained and 71 % of its peak.

### 1a. The mapper's visited set is a bitset — *done*

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

### 1b. Open air is counted, not stored — *done*

`RoomMap` kept a `HashSet<Vector3I>` of every cell classified as open air: about nine tenths of the
bounding box, 1.3 million of 1.5 million cells here, held at roughly forty bytes each to record the
absence of anything. It is **derivable** — a cell inside the box that is neither solid nor in a
room is external, by definition — so the set is now a count.

One caller wanted the cells themselves: `UnmappedRooms`, which walks them looking for compartments
the game seals and this model does not. It walks the box directly instead, in scan order, which is
more repeatable than a hash set's ordering was. It is a diagnostic, it stops at a cell limit, and
it only runs when something is asking.

**The largest single saving on this page**: the room map went from 1,126 to 32 bytes a block.

### 1c. Face fractions are shared per model and orientation — *done*

Every `BlockInstance` allocated four `float[6]` arrays for its mount and seal fractions — 192 of
its 456 bytes — and filled them with the same twenty-four numbers as every identically placed block
on the ship. All four are pure functions of `(model, orientation)`, of which there are at most
twenty-four per block type, so they are built once and shared. The door state that varies picks
*which* to read at the point of asking, which also means a door cycling no longer rewrites anything.

**Best return per line changed**: `BlockInstance` went from 456 to 240 bytes a block.

### 1d. Solver arrays grow by a quarter, not by double — *done*

`EnsureBuffers` allocated `nodes.Count * 2`. Around fourteen arrays are indexed by node, so a
settled grid carried a whole spare copy of each. Doubling is the right growth policy for something
appended to in a tight loop; these grow when a block is placed, which is not that.

---

## What is worth doing next

The solver is now the largest consumer at 468 bytes a block — half the total — and it is all
per-node rather than per-volume, so it is a different kind of problem from the ones above.

### 2. Drop the solver's node lookup dictionary — ~36 B/block

`nodesByKey` is a `Dictionary<long, ThermalNode>` mapping a block to its node. `BlockInstance`
could carry the node index directly, which removes the dictionary and makes every lookup an array
index instead of a hash. The coupling it introduces is that a block belongs to one solver, which
is already true.

### 3. Pack the per-node face data — ~70 B/block

Each node keeps `ExposedFaces` as an `int[6]` — 48 bytes with its header and reference, to hold six
small counts — and the solver mirrors six floats of face weights and six of sun-lit fraction beside
it. The counts fit in one packed `int`, the weights are derivable from them, and the sun-lit array
is only meaningful when self-shadowing is switched on.

### 4. Rooms as one cell array with per-room ranges — ~20 MB at 126k

Room cells are held twice: once in `List<HashSet<Vector3I>> rooms` and once in
`Dictionary<Vector3I, int> roomIndexByCell`. A single `Vector3I[]` of all room cells, sorted by
room, with an `int[]` of range starts, holds the same information once — 12 bytes a cell against
about 70 for the two.

### 5. Move the node diagnostics out of the node — ~24 B/block

`ThermalNode` carries eight floats of per-mechanism watts that only exist for the telemetry report
and the debug overlay. `CollectDiagnostics` is false in ordinary play, so a shipping server is
paying 32 bytes a node to store nothing. A side array, allocated when diagnostics are switched on
and dropped when they are switched off, costs a null check on a path that already has one.

### 6. Fold the block slot map into the block key map — ~30 B/block

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

| | 126k blocks retained | 500k retained | SE2 outlook |
| --- | ---: | ---: | --- |
| before | 279 MB (2,311 B/block) | — | hopeless |
| **now** | **113 MB (932 B/block)** | **459 MB (952 B/block)** | still needs §8 and §9 |
| after 2–6 | ~90 MB (~770 B/block) | | unchanged |

Per-block cost is flat across a fourfold size increase now, which it was not before. What remains
is honest per-node and per-block state rather than an index of empty space.

Items 2 to 6 are local changes with no design work behind them and would take another 20 %. They
do not change the SE2 picture, because that is not about constants — it is about which things are
counted per cell, which is §8 and §9.
