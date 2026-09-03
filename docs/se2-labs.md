# The SE2 labs: what the port must redesign, measured

*2026-09-02. Three labs, built to say which parts of the heat simulation survive Space Engineers 2's
architecture as they are and which must be re-keyed before a port. Figures quoted here come from
the committed artefacts in `tests/benchmarks/se2-labs/`; the labs that produced them are
`bench se2tax`, `bench coarserooms` and `bench condlayout`, and the correctness suites behind them
are `Se2RefineTests` and `CoarseRoomFloodTests`. One machine, one session (`taken_utc` in every
row); a figure travels only inside its own window (`M7`).*

## What SE2 is, read off the installed game

The facts below were read off the shipped assemblies' XML documentation and game data at
`~/Steam/SteamLibrary/steamapps/common/SpaceEngineers2` (version 2.0.1.2231), not off marketing:

* **.NET 9, server GC.** Scripts are metered by *allocation* — `InGameScriptConfig` carries memory
  budgets per Init and per Update call — so the model's allocation-free steady path is a shipping
  requirement there, not a nicety.
* **VRAGE3's core is DCS, an archetype ECS**: component data behind `EntityData<T…>` accessors, a
  source generator, and jobs ordered by declared dependencies (`Before`/`After`/`During`). The
  idiomatic sim is jobs over flat rows, not objects with update methods.
* **Grid storage is an octree with linear storage** plus dense occupancy bitsets, and grid mass is
  already computed per *sector* — Keen's own aggregation answer to the lattice.
* **The unified grid is real in the data**: armour ships in 25/50/250 cm variants on one grid. A
  2.5 m block is still one block, but it stands on a 25 cm lattice — cells multiply by a thousand,
  blocks do not.
* **No vanilla thermal system** exists in `Game2.Simulation`, and no scripting or code API has
  shipped yet (Keen, VS1.5: "they'll be released later"). The niche is intact and the port is
  design-ahead work, not migration work.

## Lab 1 — the lattice tax (`bench se2tax`)

One dealt census hull, 9,430 blocks, re-expressed by `Se2Refine` on a lattice 1, 2, 5 and 10×
finer per axis — ten being SE2's 25 cm under SE1's 2.5 m. The refinement moves no block:
`Se2RefineTests` holds node count, link count and room count invariant and every room's volume to
exactly the factor cubed, so the ladder measures the lattice and nothing else.

| factor | cells | box cells | links | rooms | retained MB |
|---:|---:|---:|---:|---:|---:|
| 1 | 9,430 | 90,576 | 19,455 | 11 | 4.9 |
| 2 | 75,440 | 671,600 | 19,455 | 11 | 11.2 |
| 5 | 1,178,750 | 10,014,368 | 19,455 | 11 | 86.9 |
| 10 | 9,430,000 | 78,859,728 | 19,455 | 11 | 687.5 |

| stage, best ms | k=1 | k=2 | k=5 | k=10 | k=10 over k=1 |
|---|---:|---:|---:|---:|---:|
| place | 1.008 | 5.010 | 75.5 | 668.3 | ×663 |
| surfaces | 1.915 | 3.065 | 76.2 | 732.8 | ×383 |
| links (build) | 0.998 | 4.364 | 56.1 | 530.2 | ×531 |
| rooms | 0.745 | 5.200 | 77.0 | 598.7 | ×803 |
| exposure | 0.473 | 3.337 | 41.1 | 242.2 | ×512 |
| **solver step** | **1.315** | **1.326** | **1.317** | **1.326** | **×1.01** |

**The solver does not see the lattice at all.** 480 substeps at every rung, best within 1 % across
a thousandfold cell change — because every physical term is per node or per link, and conductance
is invariant under refinement (contact area ×k², divided by a cell face ×k² smaller). This is the
measured form of the port's good news: the physics core — the thing pass 10's `E12` refused to
restructure — is already SE2-shaped and ports as it stands.

**Everything keyed per cell pays the full factor.** The room flood's per-cell price is flat at
7.5 ns across the whole ladder — the instrument is fine; the volume is the problem (the same
structure as `D2`: convergence is the box over the budget, and no per-cell work can move it). The
block table (`place`, with 795 MB of allocation per build at k=10), the surface map (9.4 M
dictionary entries), the exposure walk (k² boundary cells per face) and the link build's
contact-face counting (k² per touching pair) all inherit the lattice. Memory is ×140 for the same
ship.

## Lab 2 — the room map at supercell stride (`bench coarserooms`)

`CoarseRoomFlood` is a prototype flood that classifies supercells by block bounds — a supercell no
block touches is taken whole; one that structure touches is walked cell by cell against a sealing
tile of its own — so classification is O(blocks), sealing memory is proportional to the mixed
volume, and open space costs one visit per supercell. `CoarseRoomFloodTests` holds its partition
to the shipped mapper's **exactly** — external count, room count, every room's cell set under an
id bijection — on shells, sub-supercell walls, open frames, closed doors, dealt hulls and refined
hulls, and `bench coarserooms` re-verifies before it times.

On the SE1 lattice (126,731 blocks): the prototype does 2–6× fewer work units and wins nothing —
16.2 ms best at edge 2 against the mapper's 14.9 — because a hull at its own lattice is dense, so
most air sits in mixed supercells, and the mapper's span walk prices a cell at 7.5 ns where the
prototype's fine step pays div/mods and tile hops.

On the SE2 lattice (the same 9,430-block hull refined ×10, an 80 M-cell box):

| walk | best ms | work units | supercells | fine cells | sealing KB |
|---|---:|---:|---:|---:|---:|
| shipped mapper | 596.4 | 80,323,944 | — | — | 78,964 |
| edge 5 | 464.3 | 3,688,469 | 544,869 | 3,143,600 | 12,278 |
| edge 10 | 591.7 | 6,219,356 | 68,156 | 6,151,200 | 15,216 |
| edge 20 | 802.4 | 9,245,912 | 8,712 | 9,237,200 | 18,229 |

**Thirteen to twenty-two times fewer work units buys parity, not a win.** The exterior collapses
into supercells exactly as designed, but a ship's interior defeats block-bounds classification —
bulkheads every few coarse cells mean nearly every interior supercell touches structure, so the
interior air (millions of cells at k=10) is still walked finely, at the prototype's higher
per-cell price. An engineered fine walk would claw back some constant factor; it cannot change the
class of the answer.

**So the finding is a design boundary: no exact fine-lattice room map is affordable at SE2 scale.**
What is affordable is the *semantic* answer the refinement invariance already proves lossless:
flood at coarse resolution — 2.5 m cells over the 25 cm lattice, sealing derived per coarse cell
from the blocks inside it. On any hull whose sealing is block-aligned (every hull `Se2RefineTests`
can express), the coarse partition *is* the fine partition with every volume ÷k³: the k=1 `rooms`
row — 0.745 ms — is the price, an 800× recovery, and misaligned 25 cm greebles on a seal boundary
become the one approximation, priced as a fidelity dial like every other (`P14`). This is also
`B31`'s "coarser map" rung, measured at last: at SE1's own lattice it buys nothing (the parity
above), so it ships as the SE2 keying, not as an SE1 optimisation.

## Lab 3 — the conduction kernel in job shapes (`bench condlayout`)

The solver's own graph at 126,731 blocks (247,350 links, a settled hull's temperatures), the plain
flux arithmetic in four shapes. Kernels compare with each other, not with `bench stepphases` — no
clamps, no telemetry.

| kernel | threads | best ms | ns/link-visit | vs serial scatter |
|---|---:|---:|---:|---:|
| scatter (shipped shape) | 1 | 0.206 | 0.82 | 1.00× |
| CSR gather | 1 | 0.288 | 0.57 | 0.72× |
| CSR gather | 8 | 0.054 | 0.11 | 3.80× |
| CSR gather | 32 | 0.052 | 0.10 | 3.96× |
| Morton-ordered gather | 1 | 0.413 | 0.82 | 0.50× |

Three answers. **CSR's tax is real**: every link read twice costs 40 % serially, which is what the
races-free shape a job system can split costs to buy. **Parallelism pays it back fourfold and
saturates at 8 threads** — 0.10 ns per visit is the memory wall, and more cores read no faster.
**Morton ordering makes it worse, not better** (−50 % serially): the census placement order is
already near-optimal because the dealt hull is laid down in axis runs, so an octree-order
renumbering scrambles locality this graph already has. A port should keep placement-order
numbering and spend nothing on space-filling curves. The parallel rows' bests were capped rather
than confirmed (`E9`) — scheduler jitter — but their medians tell the same story (0.079 ms at ×8
against 0.210 serial).

Scaled to the million-block hull (1.83 M links), the conduction phase is ~1.5 ms serial and
~0.4 ms under jobs — conduction is not where the step's 61 ms lives. The step's other sliceable
phases (environment, apply, publish) are per-node with no scatter at all, so they parallelise
without even the CSR tax; the same memory-bandwidth ceiling applies, and ~4× on the whole step is
the honest expectation from data-parallel jobs alone. The rest must come from `D5`/`D6`'s lumping
and multirate, which the lattice ladder now motivates from the other side: since the solver is
lattice-blind, the port can hold node count at one-per-block whatever the build resolution — SE2's
25 cm greebles must become *thermal parts of their host block's node*, never nodes of their own.

## The port design that falls out

1. **The solver core ports unchanged.** Per-node and per-link state, allocation-free steps,
   order-independent arithmetic — measured lattice-blind and already the shape DCS jobs want. Keep
   the scatter conduction loop on the main thread or pay CSR's 40 % once to run it as jobs at ~4×.
2. **One thermal node per block, at any build resolution.** The lattice ladder is the evidence;
   the solver's flatness is the reward. A 25 cm greeble contributes mass and surface to its host,
   not a node.
3. **Every cell-keyed structure re-keys to coarse cells.** Block table, surface map, exposure and
   contact counting move to 2.5 m-cell keying with per-coarse-cell occupancy masks (SE2's own
   `OccupancyBitset` shape) for the partial-cell cases. The alternative — the fine lattice with a
   cleverer walk — was built and measured here, and it reaches parity at best.
4. **The room map floods coarse cells, semantically.** Provably identical rooms on block-aligned
   sealing, ÷k³ the cost, with misalignment as a declared approximation — not an exact
   hierarchical accelerator, which lab 2 shows cannot win.
5. **Allocation discipline is now a platform rule.** SE2 meters scripts by allocation per update;
   the steady path already allocates nothing, and the room pass's ~38 MB-per-remap (`D20`) becomes
   a port blocker rather than a nicety.

## What these labs do not say

The prototype flood is not budgeted or resumable, so it says nothing about hitching — only about
totals. The kernel lab prices arithmetic shapes, not the shipped conduction phase with its clamps.
The refined hulls drop coolant and heat-pump port geometry (a design question, not a measurement
one), and door-bearing hulls break the ×k³ room-volume invariant by the door rule itself. And no
SE2 code API exists yet to hold any of this against; when one ships, the boundary contract
(`IBlockAdjacency`, `EnvironmentSample`, results out) is where its adapter goes.

## Change log

| Date | Change |
| --- | --- |
| 2026-09-02 | Created: the three labs built and run, the SE2 facts read off the installed game, and the port design the measurements settle. Artefacts committed under `tests/benchmarks/se2-labs/`. |
