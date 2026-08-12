# Model redesign: variable block sizes, performance, memory

Working document. Goal: one simulation model that serves SE1 and SE2, is as fast as the physics
allows, and does not waste memory. Portability here means *the model*, not a shared binary — the
two games will get separate builds and separate adapters.

Background research: [se2-research.md](se2-research.md). Current defects and measurements:
[bugs-and-performance.md](bugs-and-performance.md). Scale work:
[scale-design.md](scale-design.md).

---

## 0. Implementation status

| § | Change | Status |
| --- | --- | --- |
| 2 | Analytic contact area and conduction from block bounds | **done** — [`BoxGeometry`](../Data/Scripts/Thermodynamics/Core/Util/BoxGeometry.cs), [`ConductionBuilder`](../Data/Scripts/Thermodynamics/Core/Simulation/ThermalLink.cs) |
| 2 | Conduction path length from real block depth | **done** |
| 2 | Sealing and mounting as a per-face fraction | **done** — `BlockModel.LocalFaceMountFraction`, `BlockInstance.MountFraction` |
| 6 | Adjacency behind a host port | **done** — [`IBlockAdjacency`](../Data/Scripts/Thermodynamics/Core/Model/IBlockAdjacency.cs), `ThermalSolver.Adjacency` |
| 2 | Exposure walks surfaces, not volumes | **done** — `SurfaceMap.GetExposedFaces` |
| 2 | Block *storage* stops enumerating cells | **not done** — `GridModel.blocksByCell`, `SurfaceMap.states` and `BlockInstance.Cells` are still one entry per occupied cell |
| 4 | Room mapping off the block lattice | not done |
| 6 | Struct-of-arrays solver state | not done |
| 5 | Stiffness / multirate | not done |

### Measured effect

Counting the bolted contact area of one joint between two N×N×N blocks:

| N | cells/block | before | after | speedup |
| --- | --- | --- | --- | --- |
| 1 | 1 | 0.3 µs | 0.070 µs | 4× |
| 2 | 8 | 2.5 µs | 0.013 µs | 192× |
| 4 | 64 | 22.5 µs | 0.013 µs | 1 778× |
| 8 | 512 | 126.1 µs | 0.013 µs | 9 683× |
| 16 | 4 096 | 986.8 µs | 0.015 µs | **67 127×** |

The new cost is **flat** — the same 13 nanoseconds whatever the block size — because it is
arithmetic on two bounding boxes. The old cost grew superlinearly: it walked every cell of block
A, and for each contacting cell searched every cell of block B for a matching mount, so it was
O(V<sub>A</sub> × 6 + contactArea × V<sub>B</sub>).

At SE2's finest lattice a 5 m block occupies 8 000 cells, which would have put the old
implementation at roughly 2 ms *per joint*, on a grid with millions of joints.

Whole-stage costs for 216 blocks, showing what is fixed and what is not:

| cells/block | `RebuildLinks` | `SurfaceMap.Rebuild` |
| --- | --- | --- |
| 1 | 0.208 ms | 0.099 ms |
| 64 | 1.522 ms | 4.194 ms |
| 4 096 | 15.967 ms | 144.792 ms |

`RebuildLinks` now grows with block *surface* and sublinearly at that. `SurfaceMap.Rebuild` still
grows with block *volume*, because it writes one dictionary entry per occupied cell — that is the
"not done" row above, and it is the next thing to remove.

---

## 1. The constraint that drives everything

SE1 has two block sizes and they never mix on one grid: 2.5 m or 0.5 m, fixed per grid.

SE2 ships blocks at 0.25, 0.5, 1.0, 1.25, 1.5, 2.5, 3.5 and 5.0 m, on what appears to be a shared
0.25 m lattice. A 5 m block therefore spans **8 000 lattice cells**.

The current model is **cell-centric**. Every one of these is keyed by `Vector3I` cell:

| Structure | Entries | Cost for one 5 m block |
| --- | --- | --- |
| `GridModel.blocksByCell` | one per occupied cell | 8 000 |
| `SurfaceMap.states` | one per occupied cell | 8 000 |
| `BlockInstance.gridCells[]` + `gridSurfaces[]` | one per occupied cell | 8 000 × 16 B |
| `RoomMap.solid` / `roomIndexByCell` | one per cell in the bounding volume | grows as the cube of ship size |
| `SurfaceMap.GetExposedFaces` | iterates cells × 6 faces | 48 000 iterations |
| `GridModel.Neighbours` | iterates cells × 6 faces, with `List.Contains` | 48 000 iterations |

None of that survives SE2. Keen reached the same conclusion: `CubeBlockDefinition` stores
`OccupiedGridCellsGroups` as an `ImmutableArray<BoundingBoxI>` — **boxes, not cells** — and the
octree stores a large block as one leaf.

> **The redesign is one idea: stop enumerating cells. Work on block boundaries.**

Everything below follows from that. It is also a large win in SE1, where it deletes two
dictionaries and the per-cell surface map.

---

## 2. Cell-centric → boundary-centric

The thermal model never actually needed interior cells. It needs four things, and all four are
computable analytically from integer AABBs.

### Contact area between two blocks — O(1)

Two blocks touch across axis `a` when `A.MaxExclusive[a] == B.Min[a]` (or the mirror). The contact
rectangle is the AABB overlap on the other two axes:

```
overlap[b] = min(A.MaxExclusive[b], B.MaxExclusive[b]) - max(A.Min[b], B.Min[b])
contactCells = overlap[b1] * overlap[b2]          // lattice cells²,  0 if either ≤ 0
contactArea  = contactCells * lattice * lattice   // m²
```

Constant time regardless of block size. Replaces `ConductionBuilder.CountContactFaces` and
`GridModel.SharedFaceCount`, both of which walk every cell.

### Conduction path length — falls out of the AABB

```
L_A = (A.Extents[a] * lattice) / 2         // half the block's thickness along the contact axis
G   = contactArea / (L_A / k_A + L_B / k_B)
```

This is strictly better than what `ThermalLink` does today. The current formula divides by
`LargestFaceArea`, a heuristic that exists only because the original had no real notion of path
length. With variable block sizes the physical form is *required* — a 5 m block genuinely does
conduct more slowly end-to-end than a 0.25 m one, and the heuristic does not capture that.

### Exposed area per face direction — O(neighbours)

```
faceArea[f]  = product of the two extents perpendicular to f      // lattice cells²
exposed[f]   = faceArea[f] - Σ contactCells(neighbours on f) - sealed[f]
```

Note this becomes an **area**, not a face count. `ThermalNode.ExposedFaces` is `int[6]` today
because every cell face has the same area; with variable sizes that stops being true. Six floats.

### Sealing — a fraction, not a bit

SE2 exposes `EffectivePermeability` as a float driven by build progress. Store exposure as a
`float` per face and SE1 simply supplies 0 or 1. Costs nothing, buys SE2 and half-welded walls.

---

## 3. Feature inventory

Every feature in the current mod, what it *must* deliver, and what can be cut or approximated.
"Cost" is the shape of the work per simulation step or per topology change.

### Solver core

| # | Feature | Must have | Shortcut available | Cost |
| --- | --- | --- | --- | --- |
| F1 | **Conduction** between touching blocks | Symmetric, energy-conserving; area and path length must respond to block size | None — this is the mod | O(links)/step |
| F2 | **Radiation** to ambient | `εσA(T⁴ - T_amb⁴)` on exposed area | Precompute `εσA` per block (already done). `T_amb⁴` once per grid per step (already done) | O(nodes)/step |
| F3 | **Convection** in atmosphere | Blend with radiation by air density; still air must still convect | Coefficient is per-grid, not per-block | O(nodes)/step |
| F4 | **Solar gain** | Per-face directional weighting against sun vector | Per-grid occlusion flag is enough (per-block self-shadowing is **M6**, currently unimplemented and can stay that way) | O(nodes)/step |
| F5 | **Aerodynamic friction** | v³ on windward faces | Only active above a speed threshold — skip the whole loop when `!FrictionActive` | O(nodes)/step, usually skipped |
| F6 | **Waste heat** from power/thrust | Watts per block | Recompute only on host notification, never per step (already done) | O(1)/step |
| F7 | **Overheat damage** | Per-second, not per-step | Only nodes above critical enter the list; most grids produce zero | O(nodes)/step, branch-predicted |
| F8 | **Coolant loops** | Lumped ring temperature, conducts to pipes and sink plates | Loop count is tiny; keep as a separate small array | O(loop links)/step |

F2–F5 all iterate every node and all are cheap arithmetic. They should be **one fused pass over
SoA arrays**, not four passes — which is what `AccumulateEnvironment` already does. Keep that.

### Topology and geometry

| # | Feature | Must have | Shortcut available | Cost |
| --- | --- | --- | --- | --- |
| F9 | **Neighbour discovery** | Which blocks touch which | **Ask the host.** SE1: block neighbours. SE2: `GetConnectedCubeBlocks` / `HasBlockConnection` — the octree already maintains this graph | O(blocks) on change |
| F10 | **Contact area / conductance** | Per link | Analytic AABB overlap (§2) | O(links) on change |
| F11 | **Surface / mount bits** | Whether a joint is bolted or just abutting, and whether a face seals | Per *face direction of a block*, not per cell. 6 values, not 6 × cells | O(blocks) on change |
| F12 | **Room / external detection** | "Does this face see space or a room?" | See §4 — biggest single saving | see §4 |
| F13 | **Grid split / merge** | Carry temperatures across | Key on position; SE2 has `UnionFind` split groups already | rare |

### Host-facing

| # | Feature | Must have | Shortcut available | Cost |
| --- | --- | --- | --- | --- |
| F14 | **Environment sampling** | Ambient, air density, wind, sun, underground | Once per grid per step, already a single struct (`EnvironmentSample`). Sun raycast can be cached across many frames — **P7** | O(1)/grid/step |
| F15 | **Scheduling** | Fractional step accumulation | Already rate-independent (`SimulationScheduler`). Poll at `EACH_10TH_FRAME` in SE1 | O(1) |
| F16 | **Persistence** | Temperature per block, per loop | Keyed by position; already v2 with a v1 reader | on save |
| F17 | **Thermal mass tracking** | Respond to build progress and damage | Event-driven only (`BlockHealthChanged`, `BlockBuildProgressChanged`) — never poll | on change |
| F18 | **Definitions** | Per-block-type thermal properties | Shared immutable `BlockThermalProperties` per type; never per instance | load time |
| F19 | **HUD / debug** | Temperature readout and colouring | Client-only, draw-rate not step-rate. Terminal `AppendingCustomInfo` avoids the HUD dependency | per draw |
| F20 | **Networking** | Currently unimplemented | Server-authoritative; sync only what is displayed, on demand | — |

### Things to delete

* `ThermalNode.LinkIndices` — a `List<int>` per block, written on every rebuild, **never read by
  the solver**. Only reader is a debug `.Count` in `Scenarios.cs`.
* `ThermalRadiationNode.cs` — unreferenced in the legacy tree.
* `MyFreeList<T>` — unused.
* The alternating sweep direction and `LastTemprature` bookkeeping — already gone in `Core/`,
  still present in the legacy path.

---

## 4. Room mapping is the one that has to change shape

`RoomMapper` floods the grid's whole bounding volume, cell by cell. At SE1's 2.5 m lattice a
100 m ship is 40³ = 64 000 cells. At SE2's 0.25 m lattice the same ship is 400³ = **64 million**.
It cannot be ported as-is.

The must-have is small: *for each exposed block face, does it radiate to space or into a room?*

Ranked options:

1. **Use the host's pressurisation system.** SE1 has `IMyGridGasSystem.GetOxygenRoomForCubeGridPosition`
   — an O(1) lookup, incrementally maintained, with the engine's own door handling
   ([engine-api-notes.md §1](engine-api-notes.md)). SE2 almost certainly has an equivalent; it is
   open question 4 in the research notes. This deletes 725 lines in SE1 and the whole problem in
   SE2, **and** unlocks room air temperature, which the mod cannot model at all today.
2. **Flood a coarse lattice, not the block lattice.** Rooms are human-scale; nothing thermal
   depends on resolving a void pocket below ~1 m. Flood at a fixed 2.5 m coarse cell in both
   games. Cost becomes independent of block size and of which game is hosting. A coarse cell is
   solid when the sealing blocks overlapping it cover it.
3. **Do nothing** — treat every uncovered face as external. Cheapest, and what the original mod
   did before rooms existed. Wrong for ship interiors, but it is a legitimate low setting.

Recommendation: **1 with 2 as the fallback**, exactly the arrangement
[engine-api-notes.md](engine-api-notes.md) already proposes for SE1. The existing `RoomMapper`
becomes option 2 after being retargeted from the block lattice onto the coarse lattice — the
algorithm is unchanged, only the cell size it walks.

---

## 5. Variable block size breaks the integrator — this is not obvious

Explicit stability needs substeps proportional to the stiffest node, `max(ΣG / C)`:

* Heat capacity `C ∝ mass ∝ size³`
* Conductance `G ∝ area / length ∝ size² / size = size`

So `G/C ∝ 1/size²`. **Small blocks are quadratically stiffer.**

| Pair | Size ratio | Stiffness ratio |
| --- | --- | --- |
| SE1 large ↔ small grid (never mixed today) | 5× | 25× |
| SE2 0.25 m ↔ 5 m block, **on the same grid** | 20× | **400×** |

`ThermalSolver.MaxSubsteps = 16` would clamp permanently on any SE2 grid mixing sizes.
`ClampExchange` keeps the result *bounded* — it will not blow up — but bounded is not accurate,
and `LastStepWasClamped` would be true forever.

Three ways out:

* **(a) Implicit solver.** Conjugate gradient on the sparse conductance matrix. Unconditionally
  stable, deletes `MaxSubsteps` and `ClampExchange` entirely. Correct endgame; a big change.
* **(b) Multirate integration.** Substep only the stiff subset; most of the grid steps once. Keeps
  the current solver, adds a per-node substep tier and a partition pass.
* **(c) Lumping.** When a small block's coupling time constant is far below the step, it is
  already at equilibrium with its large neighbour — merge them into one node. Physically
  justified, and it *shrinks* the problem instead of growing it.

**Recommendation: (c) now, (a) if it is not enough.** Lumping is cheap, reduces node count on
exactly the grids that would otherwise be slowest, and is a topology-time decision rather than a
per-step one. Instrument `LastStepWasClamped` in SE1 first to find out whether real grids ever hit
the cap today.

---

## 6. Proposed data structures

### Layout: struct-of-arrays, indexed by node

The solver is already half SoA — `nodeWatts`, `nodeTemperatures`, `nodeConductanceTotal` are flat
arrays — and pays for the other half. Every substep it copies temperature *out* of 8 000 class
objects and writes it back:

```csharp
for (int i = 0; i < nodeCount; i++) {
    nodeWatts[i] = 0f;
    nodeTemperatures[i] = nodes[i].Temperature;   // pointer-chase
}
...
node.Temperature = updated;                        // and back
```

Move state into the arrays and that disappears.

```csharp
// hot: touched every substep
float[]  temperature;
float[]  thermalMass;            // = SpecificHeat * Mass
float[]  radiationCoefficient;   // = ε σ A_exposed
float[]  heatGenerationWatts;
float[]  exposedArea;            // total, m²
float[]  exposedAreaByFace;      // stride 6 — used by solar and wind
float[]  watts;                  // scratch
byte[]   propertyIndex;          // → shared BlockThermalProperties table

// cold: topology only
int[]    minX, minY, minZ;       // or a packed Int3[]
int[]    extX, extY, extZ;       // extents in lattice cells
long[]   positionKey;
object[] hostHandle;             // IMySlimBlock / CubeBlockComponent, adapter-owned

// links
struct ThermalLink { int A; int B; float Conductance; float ContactArea; }   // 16 B
ThermalLink[] links;
```

`ThermalNode` survives as a **view struct** over the arrays (`readonly struct NodeRef { int i;
ThermalSolver s; }`) so the HUD, scenarios and tests keep reading `node.Temperature` unchanged.
The public API does not have to get worse to make the layout better.

### Adjacency is a host port, not a mod-owned index

Do not maintain `blocksByCell`. Both games already have a spatial index — SE1's per-cell block
array, SE2's octree — and both are better than anything the mod would build.

```csharp
interface IBlockTopologySource
{
    void GetNeighbours(int nodeIndex, List<int> results);   // or a struct visitor
    bool IsFaceSealed(int nodeIndex, int face);
    float Permeability(int nodeIndex, int face);            // SE1 returns 0 or 1
}
```

The test harness implements this over a plain dictionary; the size of that dictionary is a test
concern, not a runtime one.

This is the change that makes the model genuinely portable — the model stops caring how blocks are
stored and only consumes *(who touches whom, over what area)*.

### Surface state: per block face, not per cell

`SurfaceMap`'s dictionary goes away. `CellSurface`'s 24-bit layout stays useful but is stored once
per block, not once per cell — six sealing fractions and six mount bits.

---

## 7. Memory

Minor consideration, per the brief, but the numbers are worth having. 8 000 blocks, 22 800 links.

| | Current | Proposed |
| --- | --- | --- |
| Per-node state | class + `int[6]` + `List<int>` ≈ 3 heap objects, ~150 B | ~64 B in arrays, 0 heap objects |
| `blocksByCell` | ~40 B/cell | gone |
| `SurfaceMap.states` | ~30 B/cell | gone (6 floats + 6 bits per block) |
| `RoomMap` | 3 collections over the bounding volume | coarse lattice, ~1/1000 the cells in SE2 |
| Links | `List<ThermalLink>` 16 B | `ThermalLink[]` 16 B (unchanged) |
| **Heap objects** | ~24 000 for nodes alone | ~12 arrays total |

The heap-object count matters more than the bytes: SE1 runs a non-server GC on the game thread,
and 24 000 live objects per grid is collection pressure the mod does not need to create.

---

## 8. Order of work

1. **Land `Core/` in SE1 on the main thread, unchanged.** It is written, tested, and referenced by
   nothing outside itself. This banks the P1/P2/C3 fixes, which are the actual stalls today.
   Nothing below is worth doing before real in-game numbers exist.
2. **Instrument.** `LastStepWasClamped`, links per node, room-map pass cost, topology rebuild cost.
   Decide §5 from data rather than from the arithmetic above.
3. ~~**Boundary-centric geometry (§2).**~~ **Done for conduction and exposure** — see §0. What
   remains is block *storage*: `GridModel` and `SurfaceMap` still key on occupied cells, which is
   fine at SE1 block sizes and is not at SE2's. Replacing them means an interval or octree index
   keyed on block bounds rather than a cell dictionary.
4. **Room mapping via the host gas system, coarse flood as fallback (§4).**
5. **SoA conversion (§6).** Mechanical once §3 has removed the per-cell arrays.
6. ~~**Adjacency as a host port (§6).**~~ **Done** — `IBlockAdjacency`. An SE2 adapter now supplies
   neighbours from the octree without the mod maintaining any index of its own.
7. **Stiffness handling (§5)** — only once SE2 modding exists and real mixed-size grids can be
   measured.

Steps 3–6 each stand alone in SE1 and each pay for themselves there. That matters, because SE2
modding does not exist yet and this should not become a bet on it.

---

## 9. What this does *not* solve

* **SE2 has no mod API.** Adapter timing is outside our control ([se2-research.md §1](se2-research.md)).
* **The lattice assumption is unverified.** If SE2 does not use one shared lattice, §2's AABB
  overlap arithmetic needs a scale factor per block — a small change, but confirm before building
  on it.
* **Unit system (M5).** Specific heat is ~250× below physical. Variable block sizes make this
  worse, not better, because thermal mass now varies over four orders of magnitude across one
  grid. Decide the unit question before retuning.
* **Threading.** Still not the bottleneck. The solver is 0.128 ms for 8 000 blocks; the costs are
  all topology. Revisit only after step 2.
