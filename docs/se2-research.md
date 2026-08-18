# SE2 research notes

What the Space Engineers 2 assemblies in
`~/Steam/SteamLibrary/steamapps/common/SpaceEngineers2/Game2` actually contain, gathered for the
question "can the thermal model serve both games". Version stamp seen in shipped `.def` files:
**Game2 2.0.1.2232** (some older assets say 2.0.1.1811).

Everything under "Verified" was read by reflection over the real DLLs with
`System.Reflection.MetadataLoadContext`. Everything under "Inferred" is reasoning from shipped
content and is flagged where it matters.

---

## 1. Platform

| | SE1 | SE2 |
| --- | --- | --- |
| Runtime | `net48`, `LangVersion 6` (script compiler) | **`net9.0`** |
| Math namespace | `VRageMath` (VRage.Math.dll) | **`Keen.VRage.Library.Mathematics`** (VRage.Library.dll) |
| Integer vector | `Vector3I` | `Vector3I` — same name, different namespace |
| Architecture | `MyGameLogicComponent` on entities | **DCS**, Keen's ECS: `Keen.VRage.DCS.Components.Entity` + `GameComponent` |
| Grid type | `MyCubeGrid` | `CubeGridComponent` |
| Block type | `IMySlimBlock` / `MyCubeBlock` | `CubeBlockComponent` (a component on an `Entity`) |
| Mod API | `Sandbox.ModAPI` + whitelist | **none public yet** |

There is no `VRage.Math.dll` in SE2 at all.

### Modding status

Only UGC *management* types are public (`Keen.Game2.Client.UI.Shared.UGC.Mods.*` — view models
and configuration). `VRage.Scripting` contains `ScriptWhitelist`, `IScriptWhitelistProvider`,
`WhitelistDiagnosticAnalyzer` and `ScriptCompiler`, so a scripting surface is clearly planned,
but nothing equivalent to `Sandbox.ModAPI` exists today. **An SE2 adapter cannot be written
yet.** This does not affect the model design, only when it can be hosted.

---

## 2. Blocks live on an integer lattice — but at many scales

This is the finding that drives the redesign.

### Verified

`CubeBlockComponent`:

```csharp
BoundingBoxI      AABB;                     // integer AABB in grid space
IntegerOrientation BlockOrientation;
BoundingBox       LocalBounds;              // float
CubeBlockDefinition Definition;
CubeGridComponent Grid;
float             AbsoluteHealth, HealthIntegrity, EffectiveIntegrity;
float             BuildProgress, EffectiveBuildProgress;
float             EffectivePermeability;    // note: float, not bool
bool              Generated;
```

`CubeBlockDefinition`:

```csharp
float                             Mass;
BoundingBoxI                      BoundingBox;
ImmutableArray<BoundingBoxI>      OccupiedGridCellsGroups;   // cells as BOXES, not a cell list
BlockSizeDefinition               RelativeBlockSize;
bool                              Permeability;
float                             GetBlockPermeability(float buildProgress);
ListDictionaryReader<Base6Directions.Direction, MountPointsGroupData> MountPointsGroupsPerDirection;
float                             DisplaySize;
float?                            DisplaySizeOverride;
CubeBlockDensityDefinition        Density;
CubeBlockFragilityDefinition      Fragility;
float                             DeformationResistance, DestructionThreshold, WeakThreshold;
OccupiedGridCellsEnumerator       GetTransformedOccupiedCellGroups(RelativeTransform);
(BoundingBoxI, IntegerOrientation) ComputeBlockBoundsAndRotation(RelativeTransform);
```

`BlockSizeDefinition.RelativeBlockSize` is an **`Int32`**. Four are shipped, in
`GameData/Vanilla/Content/UI/Screens/GScreen/BlockSizes/`, with values **1, 2, 3, 4**. These are
size *tiers* (linear, not powers of two).

### Inferred

`GameData/Vanilla/Content` groups blocks into directories named for a number, and those numbers
line up with centimetres:

| Directory | Block defs | Reading |
| --- | --- | --- |
| `25` | 4 | 0.25 m |
| `50` | 47 | 0.5 m |
| `100` | 1 | 1.0 m |
| `125` | 8 | 1.25 m |
| `150` | 1 | 1.5 m |
| `250` | 102 | 2.5 m |
| `350` | 3 | 3.5 m |
| `500` | 14 | 5.0 m |

(Armour, for example, ships in `Armors/Half Long Slope/250/` and `.../50/` — the same 2.5 m and
0.5 m as SE1's large and small grid, plus six more sizes.)

The GCD of that set is **25 cm**, so a single common lattice exists at 0.25 m. `AABB` and
`OccupiedGridCellsGroups` being `BoundingBoxI` is consistent with all blocks being expressed on
one lattice.

*Not verified:* that the lattice really is 0.25 m rather than each `RelativeBlockSize` tier
having its own coordinate space, and whether all eight sizes can genuinely coexist on one grid.
`GameData` also contains a stray `"CellSize": 64` whose meaning was not tracked down (likely
voxel or texture, not block lattice). **Confirm both in game before committing.**

### Why it matters

If the lattice is 0.25 m, a 5 m block spans **20 × 20 × 20 = 8 000 lattice cells**. The current
model stores one dictionary entry per occupied cell in `GridModel.blocksByCell` *and* one in
`SurfaceMap.states`, and `BlockInstance` materialises a `Vector3I[]` of every cell it occupies.
One 5 m block would cost 16 000 dictionary entries and an 8 000-element array.

**Per-cell enumeration is not survivable in SE2.** That is the single constraint the redesign has
to answer.

Note also `OccupiedGridCellsGroups` is an array of *boxes*, not a cell list — Keen made the same
call. The engine never enumerates a block's cells either.

---

## 3. The octree

`Keen.Game2.Simulation.WorldObjects.CubeGrids.BlockOctrees.BlockOctreeComponent`
— `Component`, implements `ICubeBlockStorage` and `IInSceneListener`.

```csharp
// ICubeBlockStorage
bool  IsPositionEmpty(Vector3I);
bool  IsAreaEmpty(BoundingBoxI);
void  GetCubeBlocks(BoundingBoxI, BufferReference<CubeBlockComponent>);
void  GetConnectedCubeBlocks(BoundingBoxI, Vector3I, CubeBlockComponent, BufferReference<CubeBlockComponent>);
CubeBlockComponent TryGetCubeBlock(Vector3I);
Span<CubeBlockComponent> GetAllCubeBlocks();
BoundingBoxI Boundary { get; }
int CubeBlockCount { get; }
```

Internals worth knowing:

```csharp
FreeList<BlockOctreeNode>   _nodes;
FreeList<CubeBlockComponent> _cubeBlocks;
FreeList<BlockShape>        _largeIndex;
UnionFind                   _disconnectedGroups;
HashSet<ulong>              _dirtyEdges;        // Pack2NodesAndDirection(int, int, Direction)
HashSet<int>                _dirtyNodes;

struct BlockOctreeNode { bool IsEmpty; int LeafData; int FirstChild; /* packed in TheeOneInt */ }
struct BlockShape       { int Block; int Shape; }   // leaf → block index + shape index

bool HasBlockConnection(Vector3I, Base6Directions.Direction);
bool CanTraverse(Vector3I, Base6Directions.Direction);
void ComputeConnectivity(int);
void UpdateDirtyNeighborConnections();
```

Two things follow.

**The octree is a spatial index, not a replacement for the lattice.** Queries are still
`Vector3I` and `BoundingBoxI`. The tree exists so that a 5 m block is *one leaf* instead of 8 000
cells, and so range queries do not scan empty space.

**The engine already maintains a block adjacency graph.** `ComputeConnectivity`,
`TryConnectNodes`, `GroupFaceSharingNeighbors`, `_dirtyEdges` keyed on (nodeA, nodeB, direction),
union-find for split detection — that is structurally the same graph `ThermalSolver.links` is.
`HasBlockConnection(cell, direction)` and `GetConnectedCubeBlocks(...)` expose it.

The mod should **query** that graph, not rebuild one.

---

## 4. Grid-level API

```csharp
// CubeGridComponent
ICubeBlockStorage CubeBlockStorage { get; }
Entity            GetBlock(Vector3I);
WorldTransform    GetWorldTransform(Vector3I);
bool              IsAreaEmpty(BoundingBoxI);
void              CommitBlockChanges();
void              RemoveBlock(CubeBlockComponent);
void              BlockDestroyed(CubeBlockComponent);
void              BlockHealthChanged(CubeBlockComponent);
void              BlockBuildProgressChanged(CubeBlockComponent);
void              BlockColliderChanged(CubeBlockComponent);
void              GridsConnectionChanged(CubeGridComponent, CubeGridComponent,
                                         ImmutableArray<ConnectionGroupDefinition>, bool);
IEnumerable<Entity> GetAllGridsInShip();
void VisitAllBlocksWithComponent<T, TVisitor>(ref TVisitor, bool);
void VisitAllBlocksWithComponent<T>(Action<T>, bool);
```

Compared with SE1 this is strictly better for this mod:

* `CommitBlockChanges` is the batch-settled signal. SE1 finding **P1** (room map restarts once per
  block) exists because SE1 has no such hook; SE2 hands it over.
* `BlockHealthChanged` / `BlockBuildProgressChanged` are exactly what
  `ThermalNode.RefreshThermalMass()` needs, and SE1 only offers `OnBlockIntegrityChanged`.
* `GridsConnectionChanged` replaces the per-piston/per-rotor `AttachedEntityChanged` hooks.

`VisitAllBlocksWithComponent<T, TVisitor>(ref TVisitor, bool)` — a **by-ref struct visitor** — is
the house idiom. SE2 code is allocation-averse throughout (`FreeList<T>`, `PooledList<T>`,
`BufferReference<T>`, `Span<T>`, `ListReader<T>`). A mod that allocates per block per frame will
look wrong there.

---

## 5. Permeability replaces airtightness

SE1 airtightness is a boolean per face, and `ThermalGridMapper` reimplements the engine's rule.

SE2 has `CubeBlockDefinition.Permeability` (bool), `GetBlockPermeability(float buildProgress)`
(float), and `CubeBlockComponent.EffectivePermeability` (float). Sealing is **continuous and
build-progress dependent** — a half-welded wall is partly leaky.

For the thermal model this is an opportunity rather than a problem: an exposure *fraction* per
face is strictly more general than an exposed/not-exposed bit, and it degrades to the SE1 boolean
by treating it as 0 or 1. Designing the model around a float exposure factor costs nothing in SE1
and buys SE2 for free.

---

## 5a. What is tested today

`Se2LatticeTests` and `CoreIsolationTests` hold the SE2-facing properties, so a change that breaks
them fails the build rather than being discovered when an adapter is written.

| Test | Property |
| --- | --- |
| `TheCoreReferencesNothingButMathsAndTheFramework` | the simulation depends on `VRage.Math` and the framework, nothing else |
| `NoPublicApiInTheCoreSpeaksAGameType` | and no game type reaches its public surface indirectly either |
| `AHostCanDriveTheSimulationThroughTheCoreAlone` | layout in, sample in, temperatures out — written the way an adapter would write it |
| `GeometryIsAnsweredFromBoundsAndNotFromCells` | contact, face, surface and depth for a pair of 64-million-cell boxes, exact and in under a millisecond |
| `ContactBetweenDifferentSizesIsTheOverlapAndIsSymmetric` | a 0.5 m block on a 5 m face shares four cells, both ways round |
| `EveryBlockSizeSe2ShipsCoexistsOnOneLattice` | all eight shipped sizes on one 0.25 m grid, linked and simulated |
| `AJointBetweenTheSmallestAndLargestBlockConservesEnergy` | the 1-cell to 20-cell joint, which is the asymmetry the old model leaked at |
| `TheSubstepEstimateRespondsToTheBlockSizeRatio` | the 1/size² stiffness reaches the integrator |
| `OverALongStepTheSmallBlockForcesMoreSubstepsThanTheLargeOne` | and turns into substeps rather than staying a number |
| `TheStiffestPairingOnTheLatticeStaysBounded` | 400 steps of the worst pairing without diverging |
| `IncrementalTopologyHoldsOnAMixedSizeLattice` | placing blocks one at a time builds the same graph as building whole |
| `GrindingAMixedSizeLatticeLeavesTheSameGraphAsARebuild` | and removing them unpicks it correctly |
| `ASpreadStepIsIdenticalOnAMixedSizeLattice` | a step spread across frames is bit-identical on mixed sizes |

The last three exist because the solver was rebuilt around incremental topology and frame-spread
stepping while this document was open, and neither change had been exercised on anything but
one-cell blocks.

**What none of them claim is that SE2 is supported.** They cover the *model*: the geometry, the
integrator and the graph all work from integer AABBs and are indifferent to a block's volume. The
*storage* is not there — `GridModel`, `SurfaceMap` and `BlockInstance` still hold one entry per
occupied cell, so one 5 m block costs 16,000 dictionary entries and an 8,000-element array. That is
§2 of [model-redesign.md](model-redesign.md) and it is the whole remaining distance.

## 6. Reproducing this survey

```bash
# net9.0 console app, PackageReference System.Reflection.MetadataLoadContext 9.0.0
var files = Directory.GetFiles(@"...\SpaceEngineers2\Game2", "*.dll");
var mlc = new MetadataLoadContext(new PathAssemblyResolver(
    files.Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll"))));
// then LoadFromAssemblyPath + GetTypes, catching ReflectionTypeLoadException and using e.Types
```

Most SE2 types of interest are `internal`, so enumerate with
`BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly`.
Shipped content is JSON `.def` files under `GameData/Vanilla/Content`, keyed by `$Type` naming the
object-builder type.

---

## 7. Open questions to settle in game

1. Is the shared lattice really 0.25 m? Read `CubeBlockComponent.AABB` for a 0.5 m and a 5 m block
   on the same grid and compare extents.
2. Can all eight block sizes coexist on one grid, or does `RelativeBlockSize` partition them?
3. What is the largest block size a modder can define — is 4 a hard tier cap?
4. Does SE2 have a pressurisation/room system, and does it expose rooms the way SE1's
   `IMyGridGasSystem` does? If so the room mapper can be deleted in both games.
5. What does `"CellSize": 64` in `GameData` refer to?
