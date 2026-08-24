# Memory

Where a grid's memory goes, measured; and what can be given back.

Companion to [load-and-hitching.md](load-and-hitching.md), which covers time rather than space, and
to [scale-design.md §6](scale-design.md#6-data-structures), which budgets ~110 bytes a node for a
million-block grid. This page is about the distance between that budget and where the code is.

> The rules argued here are stated canonically in [rules.md](rules.md): `E3` `M4` `M7`.

Reproduce with `bench memory --size N`, from `tests/`. Build first: `dotnet run --no-build`
against a stale output is how two of the figures below were first reported as unchanged by a change
that halved them.

**A figure here belongs to the hull it was taken on, not only to the code.** The benchmark hull was
replaced in the middle of this page's history, and the row that moved most moved because of that
rather than because of anything in the solver. Every table below says which hull it is.

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

Measured at 126,731 blocks. The first two columns are the original armour-and-grating benchmark hull
before and after the four §1 changes measured on it; the third is the same measurement on the hull
the benchmarks build **now**, which also carries 1e.

| Structure | armour hull, before | armour hull, after 1a–1d | census hull, today | Scales with |
| --- | ---: | ---: | ---: | --- |
| `BlockInstance` | 456 | 240 | **240** | blocks, and their **cells** |
| `GridModel` indexes | 122 | 122 | **86** | blocks, and their **cells** |
| `SurfaceMap` | 69 | 69 | **68** | **cells** |
| Solver | 537 | 468 | **501** | nodes and links |
| `RoomMap` retained | 1,126 | 32 | **128** | structure, and cells **in rooms** |
| **Total retained** | **2,311** | **932** | **1,023** | |
| **Total peak** | **3,309** | **960** | **1,179** | |

In megabytes at that size: 279 MB retained and 400 MB peak became 113 MB and 116 MB on the armour
hull, and are **126 MB and 145 MB** on the census hull.

> **The third column is not a regression, and reading it as one wasted an afternoon.** The
> benchmark hull changed: it used to be heavy armour with a grating in eight, and it is now a
> measured block census from a telemetry dump — the change argued in
> [load-and-hitching.md](load-and-hitching.md), made because a hull's substep cost is set by its
> *lightest* block. The armour hull sealed everywhere, so its interior classified as one open
> region and the map stored nothing but the solid shell: 3.9 MB, which is a `HashSet` of 128,820
> cells and nothing else. The census hull's interior maps as **41 enclosed rooms of 280,645
> cells**, and room cells are stored where external cells are only counted. The same code, on a
> ship that has compartments.
>
> The two solver figures differ for a different reason and a real one: the precomputed environment
> rows and the fixed source row are per-node arrays that did not exist when the second column was
> taken. That is 33 B/block bought deliberately, and
> [`bench report`](benchmarks.md) is where the time it buys is recorded.

**Per-block cost is no longer flat with grid size.** At 505,566 blocks the census hull measures
1,141 B/block retained against 1,023 at 126,731, and the whole of that difference is the room map:
128 B/block against 229. Room cells scale with the enclosed *volume* of a ship rather than with the
number of blocks in it, and a bigger ship encloses disproportionately more. §4 is the row that
matters now, and §1b's answer — count the cells instead of storing them — does not reach it,
because a room cell has to be enumerable.

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

## What the five local changes bought

All five are in force, and together they are worth 60 % of what a grid retained and 71 % of its
peak. Each carries the figure it replaced, because a before-and-after is the evidence that the
structure is the shape this page says it is.

### 1a. The mapper's visited set is a bitset

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

### 1b. Open air is counted, not stored

`RoomMap` kept a `HashSet<Vector3I>` of every cell classified as open air: about nine tenths of the
bounding box, 1.3 million of 1.5 million cells here, held at roughly forty bytes each to record the
absence of anything. It is **derivable** — a cell inside the box that is neither solid nor in a
room is external, by definition — so the set is now a count.

One caller wanted the cells themselves: `UnmappedRooms`, which walks them looking for compartments
the game seals and this model does not. It walks the box directly instead, in scan order, which is
more repeatable than a hash set's ordering was. It is a diagnostic, it stops at a cell limit, and
it only runs when something is asking.

**The largest single saving on this page**: the room map went from 1,126 to 32 bytes a block —
on the armour hull, whose interior was one open region. On the census hull the same change is worth
the same 1,094 bytes a block of open air, and what is left behind it is the 128 bytes of *rooms*
that the armour hull did not have. See the note under the table.

### 1c. Face fractions are shared per model and orientation

Every `BlockInstance` allocated four `float[6]` arrays for its mount and seal fractions — 192 of
its 456 bytes — and filled them with the same twenty-four numbers as every identically placed block
on the ship. All four are pure functions of `(model, orientation)`, of which there are at most
twenty-four per block type, so they are built once and shared. The door state that varies picks
*which* to read at the point of asking, which also means a door cycling no longer rewrites anything.

**Best return per line changed**: `BlockInstance` went from 456 to 240 bytes a block.

### 1d. Solver arrays grow by a quarter, not by double

`EnsureBuffers` allocated `nodes.Count * 2`. Around fourteen arrays are indexed by node, so a
settled grid carried a whole spare copy of each. Doubling is the right growth policy for something
appended to in a tight loop; these grow when a block is placed, which is not that.

---

### 1e. A grid keeps one index on a block key, not two

`GridModel` kept `blocksByKey` and `blockSlots` as separate dictionaries on the same key, the
second added for O(1) removal. A slot *is* a block, so the key map was redundant: it is gone, and
`GetByKey` goes through the slot map to the flat list. Folding the two into a struct, which is what
this was first written down as, was never needed.
`bench memory --size 126731` reads the *GridModel indexes* row at **86 B/block against 120**, and
the retained total at 1,061 against 1,095. `LookupByKeySurvivesARemovalFromTheMiddle` pins the one
thing the change put at risk: the slot map is the structure a removal rewrites, moving the list's
last entry into the hole.

## What is worth doing next

The solver is the largest consumer at 501 bytes a block — half the total — and it is all per-node
rather than per-volume, so it is a different kind of problem from the ones above. The room map is
second at 128 B/block and it is the only row that still climbs with grid size, so on a large ship
it is first.

### 2. ~~Drop the solver's node lookup dictionary~~ — **done**, 49 B/block

`nodesByKey` was a `Dictionary<long, ThermalNode>` mapping a block to its node; `BlockInstance`
carries `NodeIndex` instead, so every lookup is an array index rather than a hash. **Measured on a
20,000-block ship, the solver row fell from 524 to 475 bytes a block** and the whole retained set
from 1,097 to 1,048 — 49 B/block against the 36 estimated here, which is the dictionary's bucket
and entry arrays rather than its entries alone.

The coupling it introduces is that a block belongs to one solver, which was already true — and
where it is not, the index is checked rather than trusted: a reader confirms the node it lands on is
that block's, so an index left behind by a second solver resolves to *no node* exactly as a
dictionary miss did. `NodeIndexTests` pins the whole of that against the dictionary rebuilt as an
oracle, through both of the removal paths that move a node between slots.

### 3. Pack the per-node face data — **the counts are done**, 48 B/block; ~48 left

`ExposedFaces` was an `int[6]` on every node: 48 bytes of header and reference to hold 24 bytes of
payload. It is one packed `long` now, ten bits a face — 1,023 against a real worst case of about a
hundred, since the widest vanilla block is ten cells across — read and written through
`GetExposedFaces` and `SetExposedFaces`. **Measured on a 20,000-block ship the solver row fell from
475 to 427 bytes a block**, which is the array header and reference exactly. A count past the
packing is clamped rather than wrapped, because wrapping would turn a fully exposed face into a bare
one and a block that stops radiating looks like physics; `FacePackingTests` pins that and the
round-trip.

**The other two halves are not done, and they are a different kind of trade.** The solver mirrors
six floats of face weight and six of sun-lit fraction per node in flat arrays — 24 bytes each, with
no per-node header to save. The weights are derivable from the counts, but only by putting a divide
back into the hot loop the flat arrays exist to feed; the sun-lit array is meaningful only when
`SolarSelfShadowing` is on, which is the shipped default, so allocating it lazily buys nothing for
most worlds. Both are a cost measurement rather than a packing job (`D7`).

### 4. Rooms as one cell array with per-room ranges — **half done**; ~9 MB at 126k left

Room cells are held twice: once per room, and once in `Dictionary<Vector3I, int> roomIndexByCell`.

The per-room half is done. Those were `HashSet<Vector3I>` and are now `List<Vector3I>`, trimmed
when the pass completes, because **nothing ever asked a room whether it contained a cell** — that
question goes to `roomIndexByCell`, which answers it for every room at once. A set was paying about
seventeen bytes a cell for a lookup nobody performed. Worth 38 B/block at 126k blocks and 80 at
500k, where the room map is the row that dominates.

The one caller that did search a room is the room *diagnostic*, which asks whether a vent opens
onto a compartment. It builds a set for one room at a time and reuses it, so the cost is bounded by
the largest compartment during a scan rather than by every compartment for the life of the grid.

What is left is `roomIndexByCell`: about 31 bytes for every cell in a room, 8.7 MB at 126k blocks
and 47 MB at 500k. A single `Vector3I[]` of all room cells sorted by room, with an `int[]` of range
starts, holds both halves once at 12 bytes a cell — but the dictionary answers `RegionOf` on the
exposure path, so replacing it means giving that lookup a different shape rather than deleting
it.

### 5. Move the node diagnostics out of the node — ~24 B/block

`ThermalNode` carries eight floats of per-mechanism watts that only exist for the telemetry report
and the debug overlay. `CollectDiagnostics` is false in ordinary play, so a shipping server is
paying 32 bytes a node to store nothing. A side array, allocated when diagnostics are switched on
and dropped when they are switched off, costs a null check on a path that already has one.

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

This is [scale-design.md](scale-design.md#cell-centric--boundary-centric), and it is the difference between the model
supporting SE2 and the storage refusing to.

### 9. For SE2: the room map off the block lattice

The bounding-volume rows scale with the cube of lattice resolution. The same ship measured on a
0.25 m lattice instead of 2.5 m has a thousand times the cells, so even at one bit each the
mapper's set is gigabytes.

The flood fill has to run on a coarser lattice than the blocks, or on the host's own gas system
where one exists. [scale-design.md](scale-design.md#room-mapping-is-the-one-that-has-to-change-shape) sets this out; nothing here changes it.

---

## Summary

| | 126k blocks retained | 500k retained | SE2 outlook |
| --- | ---: | ---: | --- |
| armour hull, before 1a–1d | 279 MB (2,311 B/block) | — | hopeless |
| armour hull, after 1a–1d | 113 MB (932 B/block) | 459 MB (952 B/block) | still needs §8 and §9 |
| **census hull, today** | **126 MB (1,023 B/block)** | **552 MB (1,141 B/block)** | still needs §8 and §9 |
| after 2, 3, 4 and 5 | ~90 MB (~730 B/block) | ~390 MB (~800 B/block) | unchanged |

Nothing is indexed by *bounding volume* any more, which is what the second row was celebrating. It
is still indexed by *enclosed* volume, which is why the third row climbs with grid size where the
second did not: a bigger ship is a larger fraction rooms. Everything else on the page is honest
per-node and per-block state.

Items 2 to 5 are local changes with no design work behind them and would take another 20 %. They
do not change the SE2 picture, because that is not about constants — it is about which things are
counted per cell, which is §8 and §9.

---

## Change log

| Date | Change |
| --- | --- |
| 2026-08-23 | **§3's counts are packed**: `ExposedFaces` is one `long` rather than an `int[6]`, and the solver row falls 475 → **427 bytes a block** on a 20,000-block ship — the array's header and reference exactly. With §2 the same row is 524 → 427 and the whole retained set 1,097 → 1,000. [backlog.md](backlog.md) `E2`. |
| 2026-08-23 | **§2 is done and it was worth more than it was estimated at.** The solver's node dictionary is gone and the block carries the index: 524 → **475 bytes a block** on the solver row, 1,097 → 1,048 retained, measured on a 20,000-block ship rather than counted. [backlog.md](backlog.md) `E1`. |
| 2026-08-22 | Put the five completed changes in the present tense — each is a structure the code has, not a thing that was done — and moved the fifth up beside the other four instead of leaving it struck through in the list of what is still worth doing. |
| 2026-08-22 | Added the standard header and this change log. |
| 2026-08-21 | Held a room's cells in a list, and dropped a grid's second index on the key it already had — 34 B/block and 38 B/block back respectively. Corrected a page measured on a hull that no longer exists. |
| 2026-08-18 | Corrected the largest row: the page had called bounding volume its biggest cost after that had stopped being true. Took 60% of what a grid holds with four local changes. Opened the page by measuring where a grid's memory goes, and corrected the 2.5 GB figure it first reported — that was `GetTotalMemory` without a collection and counted garbage. |
