# Scale design: grids up to 1 000 000 blocks

Design only — nothing here is implemented. Companion to
[model-redesign.md](model-redesign.md) (variable block sizes) and
[se2-research.md](se2-research.md) (what SE2 actually provides).

The target is a single grid of **10⁶ blocks** that ticks inside a frame budget without stalling
the game, in either SE1 or SE2.

---

## 1. What a million blocks costs if nothing changes

Extrapolated from the measured 8 000-block benchmark in
[bugs-and-performance.md](bugs-and-performance.md) (22 800 links, 0.128 ms/step, 51 ms build).

| Blocks | Links | Solver step | Full topology rebuild |
| --- | --- | --- | --- |
| 8 000 | 22 800 | 0.13 ms | 0.05 s |
| 100 000 | 285 000 | 1.60 ms | 0.64 s |
| **1 000 000** | **2 850 000** | **16 ms** | **6.4 s** |

Linear extrapolation is optimistic. At 8 000 blocks the working set is ~0.5 MB and lives in L2; at
10⁶ it is ~114 MB and lives in main memory. The floor is then memory bandwidth, not arithmetic:

```
per substep:  nodes 1e6 × 44 B  +  links 2.85e6 × 28 B   ≈ 124 MB
at ~20 GB/s effective                                    ≈ 6.2 ms per substep
```

So a step is 6–16 ms *before* any stiffness substepping, against a 16.6 ms frame at 60 fps, for
**one grid**. And a single block placed triggers a 6.4-second rebuild, because `AddBlock` sets
`linksDirty` and `RebuildLinks` is O(all nodes).

Three conclusions:

1. **You cannot touch every node every step.** Not at any constant factor. The arithmetic is
   already near the bandwidth floor.
2. **You cannot rebuild topology globally.** Ever.
3. **Node count must stop being the unit of work.** Thermal *activity* has to be.

---

## 2. The one idea

> Work must scale with the number of blocks whose temperature is **changing**, not with the number
> of blocks that **exist**.

In a **steady environment** the overwhelming majority of blocks sit at equilibrium with identical
neighbours: no gradient, no heat source, nothing to compute. They are not *cheap* to simulate today
— they cost exactly as much as a reactor face — but they are *worthless* to simulate.

If 2 % of a grid is thermally active, the active set is 20 000 nodes and ~57 000 links: **0.3 ms
per step**, comfortably inside budget, on a grid 125× larger than the current benchmark.

> **Correction.** An earlier draft of this section justified that 2 % by claiming most blocks are
> *interior*. Measurement says otherwise — see [§10](#10-grid-shape-changes-the-arithmetic). On a
> solid cube 27 % of blocks are exposed; on a ship it is **73–82 %**. Ships are mostly skin.
>
> Exposure is not the same as activity, so the steady-state argument survives: an exposed block in
> a constant environment is still settled. But the *exposed* fraction is exactly the set that wakes
> when ambient conditions change, so on a real hull a day/night transition or an atmospheric entry
> wakes four fifths of the grid, not a thin shell. Staggering (§5) is therefore **mandatory, not an
> optimisation** — it is the difference between the design working and not.

Everything in the rest of this document is machinery to make that true and keep it correct.

---

## 3. Three structural changes

### 3.1 Chunking — the addressing change

Stop treating a grid as one flat node array. Partition the lattice into fixed **chunks**
(64 × 64 × 64 lattice cells — 16 m at SE2's 0.25 m lattice, 160 m at SE1's 2.5 m). A block belongs
to the chunk containing its `Min`. Every SE2 block size fits inside one chunk (5 m = 20 cells).

Nodes are stored **sorted by chunk**, so a chunk is a contiguous index range. That single ordering
decision buys spatial locality for free: neighbours in space are neighbours in memory.

Per chunk:

```
int   firstNode, nodeCount        // contiguous range
int   firstInternalLink, count    // both endpoints inside this chunk
int   firstBoundaryLink, count    // crossing into a neighbouring chunk
float meanTemperature, minT, maxT // aggregate state
float activity                    // max |ΔT| observed last update
int   lastUpdatedStep
byte  flags                       // topologyDirty, exposureDirty, sleeping, lumped
```

What chunking unlocks:

* **Incremental topology.** Placing a block dirties one chunk and at most six neighbours' boundary
  link sets. The 6.4-second global rebuild becomes O(1) amortised. This alone is the difference
  between usable and unusable.
* **A scheduling unit.** Chunks are the granularity for sleeping, budgeting and prioritisation.
* **A parallelism unit.** Internal links touch only their own chunk, so chunk interiors are
  embarrassingly parallel; only boundary links need care.
* **A persistence unit.** Only dirty chunks are re-serialised.
* **A natural fit to SE2.** The octree is already a spatial hierarchy; chunks can align to octree
  nodes rather than being an independent partition.

### 3.2 Activity tracking — the work elimination

A chunk **sleeps** when its `activity` stays below a threshold for N consecutive updates. Sleeping
chunks are skipped entirely.

Wake conditions, all local:

| Trigger | Mechanism |
| --- | --- |
| Neighbour chunk grew a gradient | Boundary link ΔT exceeds threshold → wake the sleeper |
| A block's heat generation changed | Host event → wake owning chunk |
| Topology changed | Add/remove/damage → wake owning chunk |
| Exposure changed | Room map pass altered an exposed face → wake |
| Environment changed materially | See §5 — must **not** wake everything at once |

Wake propagates across chunk boundaries at roughly the speed heat actually diffuses, which is
physically correct rather than a hack: a reactor coming online wakes an expanding shell, not the
whole ship.

**Granularity: per chunk, not per node.** Per-node activity bookkeeping costs about as much as the
work it saves, and heat diffusion is spatially coherent, so chunk-level is the right resolution.

### 3.3 Lumping — the memory and precision win

A sleeping chunk is, by definition, near-uniform in temperature. Collapse it to **one lumped node**
carrying the chunk's total thermal mass, coupled to the outside world through its boundary links
only. ~4 000 nodes become 1.

On wake, expand: every node in the chunk was within the sleep threshold of the mean, so restoring
`meanTemperature` to all of them is accurate to within that threshold by construction. If you want
exactness, store a per-node `sbyte` delta against the mean — 1 byte per node instead of 4, and it
compresses to nothing when uniform.

Lumping is what makes a 10⁶-block grid genuinely *small* rather than merely *skipped*: it shrinks
the resident working set, not just the per-step iteration.

This is adaptive mesh coarsening. The block octree is the natural hierarchy to hang it on when SE2
modding arrives.

---

## 4. Multirate stepping unifies two problems

[model-redesign.md §5](model-redesign.md) established that variable block sizes make small blocks
quadratically stiffer (`G/C ∝ 1/size²`; 400× between SE2's 0.25 m and 5 m blocks), so a global step
must be sized for the stiffest node on the grid.

That is the same problem as scale, seen from the other end. Both are solved by letting **different
regions advance at different rates**.

Assign each chunk a **rate tier** — a power-of-two multiple of the base step:

```
tier 0 : every step        stiff, active, small blocks
tier 1 : every 2 steps
tier 2 : every 4 steps
...
tier k : every 2^k steps   quiescent bulk structure
```

A chunk's tier is `min(stability tier, activity tier)`: stiffness sets a ceiling on the step it can
take, activity sets how large a step it *needs*.

Two rules keep this conservative:

* **Adjacent chunks differ by at most one tier.** Standard multirate practice; prevents a stiff
  region from being fed by a wildly stale neighbour.
* **A boundary link is integrated at the finer of its two tiers**, and the energy it moves is
  applied to both sides at that rate. Because `ThermalSolver` already accumulates watts and applies
  them together, an exchange stays exactly equal and opposite regardless of tier — energy
  conservation survives, which is the property that matters most.

This replaces global `MaxSubsteps` clamping, which at 10⁶ mixed-size blocks would otherwise be
permanently saturated.

---

## 5. Per-subsystem requirements at scale

| Subsystem | Breaks at 10⁶ because | Required change |
| --- | --- | --- |
| **Topology rebuild** | `RebuildLinks` is O(all nodes), triggered by one block | Per-chunk incremental rebuild; dirty set, never global |
| **Node removal** | `RemoveBlock` does `List.RemoveAt` + reindexes every later node — O(n) per removal, and invalidates every cached link index | Stable indices with a free list and tombstones; compact per chunk, offline |
| **Conduction pass** | 2.85 M links, bandwidth bound | Active chunks only; CSR adjacency (§6) for sequential access |
| **Environment pass** | 10⁶ nodes × 5 terms | Active chunks only; already one fused pass — keep it fused |
| **Room mapping** | Floods the bounding volume; ~10⁸ cells at SE2 lattice | Host gas system, else coarse lattice **and** per-chunk incremental flood with boundary reconciliation ([model-redesign.md §4](model-redesign.md)) |
| **Exposure refresh** | `RefreshExposure` loops every node on every room pass | Only nodes in chunks the pass actually changed |
| **Environment sampling** | One `EnvironmentSample` for a 1 km structure is physically wrong, and a per-grid solar occlusion flag is meaningless at that size | Sample per chunk, interpolated from a coarse per-grid field; occlusion per chunk |
| **Environment change** | A day/night transition dirties every exposed chunk simultaneously — the worst-case wake storm | Treat ambient as a slowly-varying field; wake exposed chunks **staggered** across the tier cycle, never in one frame |
| **Persistence** | 12 MB raw, 16 MB base64 per grid | Per-chunk; sleeping uniform chunk = one float; RLE over lumped regions; only dirty chunks rewritten |
| **Scheduling** | A work quota cannot bound frame time | Wall-clock budget: process chunks by priority until the budget is spent (§7) |
| **Split / merge** | Rebuilding both sides is 6.4 s each | Chunks move wholesale between grids; SE2's octree `UnionFind` already yields split groups |
| **Damage events** | Fine — inherently sparse | None |
| **Networking** | 10⁶ temperatures cannot replicate | Server authoritative; clients receive per-chunk aggregates for display, full detail only for the chunk under the crosshair |
| **Diagnostics** | `TotalEnergy` sums 10⁶ float32 — catastrophic cancellation | Accumulate in `double`, or per-chunk then combine |

---

## 6. Data structures

### Zero managed references in the hot arrays

At 10⁶ blocks, an `object[] hostHandle` is 10⁶ references the GC must **trace on every
collection**. In SE1 that GC runs on the game thread.

Keep the simulation arrays entirely blittable — `float[]`, `int[]`, `byte[]` and structs of those.
The core then contains no GC roots at all and is invisible to tracing. The adapter owns the
`int index → IMySlimBlock / CubeBlockComponent` mapping, and pays for it once, outside the
simulation.

This is a stronger constraint than the SoA change in [model-redesign.md §6](model-redesign.md), and
at this scale it matters more than the byte count.

### CSR adjacency instead of a link list

Store links as compressed sparse row, sorted by node within chunk:

```
int[]   linkStart;      // per node, offset into the arrays below   (nodeCount + 1)
int[]   linkTarget;     // other endpoint
float[] linkConductance;
```

Each link appears twice (once per endpoint), which costs ~40 % more memory than the current
symmetric single-entry list but makes traversal purely sequential per node and removes the
scattered read-modify-write on `nodeWatts` that the current `for (links)` loop performs. At
bandwidth-bound scale that trade is worth taking — and CSR is also exactly the format a conjugate
gradient solver wants, if the implicit route in
[model-redesign.md §5](model-redesign.md) is ever taken.

Keep `contactArea` out of the hot arrays; it is diagnostic only.

### Memory budget

| Array | Bytes/node | 10⁶ nodes |
| --- | --- | --- |
| `temperature`, `thermalMass`, `radiationCoefficient`, `heatGenerationWatts`, `exposedArea`, `watts` | 24 | 24 MB |
| `exposedAreaByFace` (6 × float) | 24 | 24 MB |
| `propertyIndex` (→ shared property table) | 1 | 1 MB |
| AABB: `Min` packed 21-bit long + `Extents` 3 × byte | 11 | 11 MB |
| CSR `linkStart` | 4 | 4 MB |
| **Node subtotal** | **64** | **64 MB** |
| CSR `linkTarget` + `linkConductance`, 2 × 2.85 M × 8 B | | 46 MB |
| Chunk records (~4 000 chunks × 64 B) | | negligible |
| **Total** | | **~110 MB** |

Reductions available, in order of value:

* **Lumping (§3.3)** is the big one — a sleeping chunk needs `meanTemperature` plus an optional
  `sbyte` delta per node, not 64 B. A grid that is 90 % asleep drops to ~20 MB resident.
* `exposedAreaByFace` at half precision, or derived on wake rather than stored, saves 12–24 MB.
* Blocks of identical type in identical exposure share derived values — `radiationCoefficient` is a
  pure function of `(propertyIndex, exposedArea)` and could be a small lookup.

110 MB for a maximal, fully-awake 10⁶ grid is acceptable. The design should be judged on the
*sleeping* figure, because that is the steady state.

---

## 7. Scheduling: budget, not quota

`SimulationScheduler` accumulates fractional step credit and answers "how many steps are due". At
10⁶ blocks that question is unanswerable, because one step is not affordable.

Replace with a **wall-clock budget**:

```
budget = 2 ms per frame per grid (configurable)
order chunks by priority = activity × (now - lastUpdatedStep) × tierWeight
process until the budget is spent
chunks not reached accumulate owed time and rise in priority
```

Properties this needs:

* **Bounded frame cost by construction**, independent of grid size. This is the property the
  current quota scheduler cannot provide.
* **Starvation-free** — owed time raises priority monotonically, so nothing is skipped forever.
* **Graceful degradation** — an over-subscribed server slows heat propagation rather than dropping
  frames. Heat visibly diffusing a little slower is a far better failure mode than a stutter.
* **Determinism must be chosen, not assumed.** A wall-clock budget is non-deterministic across
  machines. For multiplayer, either the server alone simulates (recommended — see the networking
  row in §5), or the budget is expressed in work units rather than milliseconds on all peers.

---

## 8. Invariants worth stating

Properties the design must preserve as it gets clever. These are the regression tests.

1. **Energy is conserved** across every pairwise exchange, at every tier boundary, and across
   lump/expand cycles. This is the one property that makes the rest safe to approximate.
2. **Order independence.** Results must not depend on chunk processing order — already true of the
   accumulate-then-apply solver, and it must survive multirate and parallelism.
3. **Sleep is invisible.** A sleeping chunk and an awake one at equilibrium must produce the same
   answer. A test that sleeps half a grid and compares against the fully-awake result is the
   single highest-value test in this design.
4. **Lump/expand round-trips within threshold.** Collapse and restore must not move total energy.
5. **Wake is local.** No event may wake more than its neighbourhood in one frame — including
   day/night, which is the one that would otherwise wake a whole hemisphere.
6. **Topology work is proportional to what changed**, never to grid size.

---

## 9. Risks and open questions

* **Is a 10⁶-block grid a real target, or a stress bound?** The design differs: if real grids top
  out at 10⁵, chunking plus activity tracking suffices and lumping is unnecessary complexity.
  Worth deciding before building.
* **Wake storms are the failure mode.** Environment transitions, a ship entering atmosphere, a
  large explosion, a blueprint paste. Each needs an explicit staggering strategy; without one, the
  worst case is *worse* than having no sleeping at all, because it pays wake bookkeeping on top of
  full work.
* **Sleep threshold is a gameplay knob, not just a performance one.** Too high and slow heat soak
  through a hull stops happening at all — the exact behaviour a thermal mod exists to model. It
  needs to be tunable and documented alongside `Frequency`.
* **Chunk size interacts with block size.** 64³ lattice cells is 16 m in SE2 and 160 m in SE1. It
  probably wants to be defined in metres and converted, not in cells.
* **SE2's octree may make chunking redundant** — if octree nodes are exposed to mods with stable
  identity, align to them instead of imposing a second partition. Unknowable until the mod API
  ships.
* **None of this is worth building before `Core/` runs in SE1 and produces real numbers.** Every
  figure in this document is extrapolated from bench measurements on synthetic grids.

---

## 10. Grid shape changes the arithmetic

Measured with `GridShapes` and `GridMetrics` in the harness, ~8 000 blocks of light armour each
(`l-junction` is larger; per-block columns are normalised).

| shape | nodes | bbox fill | exposed | links/node | diameter | build ns/blk | step ns/blk |
|---|---|---|---|---|---|---|---|
| solid-cube | 8 000 | 100 % | **27 %** | **2.85** | **57** | 4.87 | 20.16 |
| hollow-box | 7 778 | 15 % | 100 % | 2.00 | 108 | 7.99 | 17.99 |
| stick | 8 000 | 100 % | 100 % | 1.00 | **7 999** | 5.28 | 12.76 |
| plate | 8 010 | 100 % | 100 % | 1.98 | 177 | 2.99 | 15.49 |
| dumbbell | 6 910 | 44 % | 36 % | 2.78 | 97 | 3.26 | 19.73 |
| l-junction | 19 257 | **7 %** | 49 % | 2.71 | 398 | 8.11 | 15.82 |
| truss | 4 900 | 28 % | 100 % | 1.14 | **707** | 2.06 | 9.30 |
| **ship** | 5 125 | **13 %** | **79 %** | 2.36 | 81 | 3.19 | 12.94 |
| accreted | 8 000 | 21 % | 40 % | 2.49 | 55 | 3.91 | 14.14 |

The solid cube — the shape every existing benchmark and nearly every existing test uses — is the
**best case on three of the four axes that this design depends on**, and the worst case on the
fourth:

* **Exposed fraction 27 %, against 79 % for a ship.** This is the correction in §2. The
  environment pass does real work for three times as many blocks on a real hull, and three times
  as many blocks wake when ambient conditions change.
* **Bounding fill 100 %, against 13 % for a ship and 7 % for an L.** The room mapper floods the
  bounding volume, so per block it does **~8× more work on a ship** and ~14× more on an L-shape
  than the cube benchmark suggests. This is the strongest evidence yet for [§4 of
  model-redesign.md](model-redesign.md) — the flood fill must not be volume-proportional.
* **Diameter 57, against 707 for a truss and 7 999 for a stick.** Diameter sets how many
  conduction hops a transient needs to cross the grid, and therefore how many steps chunks stay
  awake. An activity-based scheduler is far less effective on elongated structures — which is what
  station spines and long ships are.
* **Links per node 2.85 is the cube's one pessimism** — every other shape is sparser, so the
  conduction pass is *cheaper* per block than the benchmark implies. This partly offsets the
  above, but only partly, and not on the axes that scale worst.

Practical consequence: **the 51 ms build / 0.128 ms step figures are a floor, not a typical
case.** Any future benchmark should report the ship and truss shapes alongside the cube, and the
scale design should be validated against `stick` and `truss` specifically, because they are where
sleeping and chunking are least effective.
