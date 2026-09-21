using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Incrementally classifies every cell in a grid's bounding box as external space, sealed
    /// structure, or part of an enclosed room. Resumable, and restart requests coalesce.
    /// See thermal-model.md, The room map.
    /// </summary>
    public class RoomMapper
    {
        private enum Phase
        {
            Idle,
            External,
            Interior,
            Done
        }

        private readonly SurfaceMap surfaces;

        /// <summary>
        /// The flood's frontier: a ring buffer kept between passes rather than a <c>Queue</c> grown
        /// from empty each time. A pass enqueues every air cell of the bounding volume — 6.6 million
        /// at half a million blocks — so a queue that doubles from nothing spends about twenty
        /// reallocations and thirteen million struct copies on growth alone, every pass, having
        /// already been that large on the previous one. Same order in and out, so the same map.
        /// See performance.md, Pass 3, Iteration 8.
        /// </summary>
        private Vector3I[] frontier = new Vector3I[64];
        private int frontierHead;
        private int frontierCount;

        private void FrontierClear()
        {
            frontierHead = 0;
            frontierCount = 0;
        }

        private void FrontierEnqueue(Vector3I cell)
        {
            if (frontierCount == frontier.Length)
            {
                Vector3I[] grown = new Vector3I[frontier.Length * 2];
                for (int i = 0; i < frontierCount; i++)
                {
                    grown[i] = frontier[(frontierHead + i) % frontier.Length];
                }
                frontier = grown;
                frontierHead = 0;
            }

            int at = frontierHead + frontierCount;
            if (at >= frontier.Length) at -= frontier.Length;
            frontier[at] = cell;
            frontierCount++;
        }

        private Vector3I FrontierDequeue()
        {
            Vector3I cell = frontier[frontierHead];
            frontierHead++;
            if (frontierHead == frontier.Length) frontierHead = 0;
            frontierCount--;
            return cell;
        }
        /// <summary>
        /// Cells this pass has already classified, one bit each over the search box — a set the region
        /// is dense in, which is what a bitset is for. See memory.md, 1a.
        /// </summary>
        private readonly CellBitset visited = new CellBitset();

        /// <summary>
        /// The structural self-airtight bits of every cell in the search box, one byte a cell,
        /// copied out of the surface map when a pass begins. A pass tests two faces per neighbour
        /// per cell over the whole bounding volume — fourteen times the block count on a hull — and
        /// each test was two dictionary probes; here it is two array reads. The copy is exact
        /// because a sealing change requests a restart, so a pass never reads a surface map that
        /// has moved under it. Retained between passes and regrown only when the box outgrows it.
        /// See performance.md, Iteration 4.
        /// </summary>
        private byte[] sealing = new byte[0];
        private int sealingSizeX;
        private int sealingSizeY;
        private int sealingSizeZ;

        /// <summary>
        /// Set false to answer every sealing question from the surface map's dictionaries, as the
        /// pass did before the snapshot. Test hook: <c>RoomMapSnapshotTests</c> runs the same grid
        /// both ways and compares the published maps cell for cell.
        /// </summary>
        public bool SnapshotSealing = true;

        /// <summary>Whether the pass in flight is reading the snapshot rather than the map.</summary>
        private bool snapshotLive;

        /// <summary>
        /// What each face adds to a cell's index in the snapshot and the visited bitset, which share
        /// one box and one ordering. A neighbour's index is the cell's plus this, once the
        /// neighbour is known to be inside the box.
        /// </summary>
        private readonly long[] faceDelta = new long[Face.Count];

        /// <summary>
        /// Cells belonging to a door. Never classified as solid structure even when they seal on all
        /// six faces: a door is an openable volume, and a portal needs a region on the door's own
        /// side to join to. A shut airtight hangar door is a room of one cell, which merges with the
        /// regions either side when it opens.
        /// </summary>
        private readonly HashSet<long> doorCells = new HashSet<long>();

        private RoomMap working;
        private RoomMap published = RoomMap.AllExternal;

        /// <summary>The map published before the current one, still readable and not yet reusable.</summary>
        private RoomMap supersededPrevious;

        /// <summary>A map two publishes old, which the next pass writes into instead of allocating.</summary>
        private RoomMap spare;

        private Phase phase = Phase.Idle;
        private bool restartRequested;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
        private Vector3I scanCursor;

        /// <summary>
        /// Where <see cref="scanCursor"/> sits in the snapshot and the visited bitset, which share one
        /// box and one ordering.
        ///
        /// <para>
        /// **The scan order is the index order**, exactly: the cursor advances x fastest, then y, then
        /// z, and the index is <c>((z * sizeY) + y) * sizeX + x</c> — so a step of one cell is a step
        /// of one index, wraps included. The interior scan walks every cell of a bounding volume
        /// fourteen times the block count, so deriving the index from the coordinates there was three
        /// subtractions, six compares and a multiply per cell, twice over. It is an increment.
        /// See performance.md, Iteration 10.
        /// </para>
        /// </summary>
        private long scanIndex;

        private Vector3I pendingMin;
        private Vector3I pendingMaxExclusive;
        private bool hasPendingBounds;

        /// <summary>
        /// The grid the pending pass is for, so its doors can be resolved into portals when the pass
        /// finishes. Held only between a restart request and the publish that answers it; the mapper
        /// otherwise has no knowledge of blocks.
        /// </summary>
        private GridModel pendingGrid;

        /// <summary>Raised each time a pass completes and a new map is published.</summary>
        public event Action Completed;

        /// <summary>
        /// Shared work counters. The mapper and the solver write to the same instance, so a test or
        /// report reads one figure for what an update touched rather than summing two.
        /// </summary>
        public SimulationWork Work = new SimulationWork();

        public RoomMapper(SurfaceMap surfaces)
        {
            if (surfaces == null) throw new ArgumentNullException("surfaces");
            this.surfaces = surfaces;
        }

        /// <summary>The most recently completed map. Never null, never partially built.</summary>
        public RoomMap Map
        {
            get { return published; }
        }

        public bool IsRunning
        {
            get { return phase == Phase.External || phase == Phase.Interior; }
        }

        public bool HasWorkPending
        {
            get { return IsRunning || restartRequested; }
        }

        /// <summary>Number of passes completed. Useful for tests and diagnostics.</summary>
        public int CompletedPasses { get; private set; }

        /// <summary>
        /// True when the published map already accounts for this door, so its state can be resolved
        /// through the portals rather than by remapping. False for a door placed since the last
        /// pass, and for any block that is not a door.
        /// </summary>
        public bool Knows(BlockInstance block)
        {
            if (block == null || !block.HasStateDependentSealing) return false;
            if (CompletedPasses == 0) return false;

            IList<RoomPortal> portals = published.Portals;
            for (int i = 0; i < portals.Count; i++)
            {
                if (portals[i].Block == block) return true;
            }

            // A door with no portal is one the last pass found sealed off or opening onto nothing.
            // Its state changes nothing, so the map still answers for it, provided the pass saw it.
            return knownDoors.Contains(block);
        }

        /// <summary>Doors present at the last completed pass.</summary>
        private readonly HashSet<BlockInstance> knownDoors = new HashSet<BlockInstance>();

        /// <summary>
        /// Cells waiting in the flood fill's frontier; zero when no pass is running. Reported so a
        /// session can be checked for restarts arriving faster than passes complete.
        /// </summary>
        public int PendingCells
        {
            get { return frontierCount; }
        }

        /// <summary>
        /// Marks the map stale. Cheap and idempotent: many calls before the next
        /// <see cref="Step"/> collapse into a single restart.
        /// </summary>
        public void RequestRestart(Vector3I gridMin, Vector3I gridMax)
        {
            pendingMin = gridMin - Vector3I.One;
            pendingMaxExclusive = gridMax + new Vector3I(2, 2, 2);
            hasPendingBounds = true;
            restartRequested = true;
        }

        public void RequestRestart(GridModel grid)
        {
            pendingGrid = grid;

            if (grid == null || grid.BlockCount == 0)
            {
                pendingMin = Vector3I.Zero;
                pendingMaxExclusive = Vector3I.Zero;
                hasPendingBounds = true;
                restartRequested = true;
                return;
            }
            RequestRestart(grid.Min, grid.Max);
        }

        /// <summary>
        /// Runs the whole pass to completion. For tests and load-time mapping; gameplay code uses the
        /// budgeted <see cref="Step"/>. The limit is a guard against a pass that never finishes rather
        /// than a budget, and is derived from the volume so it cannot bind on one that will.
        /// See load-and-hitching.md, The room map gave up above seven hundred thousand blocks.
        /// </summary>
        /// <param name="safetyLimit">
        /// Cell visits to allow, or zero to derive one from the search volume.
        /// </param>
        /// <returns>True when a pass completed and published a map.</returns>
        public bool RunToCompletion(int safetyLimit = 0)
        {
            long limit = safetyLimit > 0 ? safetyLimit : SafetyLimitFromBounds();

            long spent = 0;
            while (HasWorkPending && spent < limit)
            {
                spent += 4096;
                Step(4096);
            }

            return !HasWorkPending;
        }

        /// <summary>
        /// Cell visits to allow for one pass over the bounds it is about to walk: four times the
        /// volume, saturating rather than overflowing — a volume large enough to overflow an
        /// <c>int</c> is exactly the case a flat limit got wrong.
        /// </summary>
        private long SafetyLimitFromBounds()
        {
            Vector3I min = hasPendingBounds ? pendingMin : searchMin;
            Vector3I max = hasPendingBounds ? pendingMaxExclusive : searchMaxExclusive;

            long x = max.X - min.X;
            long y = max.Y - min.Y;
            long z = max.Z - min.Z;

            if (x <= 0 || y <= 0 || z <= 0) return 4096;

            long volume = x * y * z;
            return (volume * 4) + 4096;
        }

        /// <summary>
        /// Advances the pass by at most <paramref name="cellBudget"/> cells.
        /// </summary>
        /// <returns>True when a pass completed during this call.</returns>
        public bool Step(int cellBudget)
        {
            if (cellBudget <= 0) return false;

            if (restartRequested)
            {
                BeginPass();
            }

            bool completed = false;
            int spent = 0;

            while (spent < cellBudget)
            {
                if (phase == Phase.External)
                {
                    if (frontierCount == 0)
                    {
                        phase = Phase.Interior;
                        BeginInteriorScan();
                        continue;
                    }
                    if (RunWalkLive)
                    {
                        // A run is charged for every cell it took, and takes no more than the tick
                        // has left; where it stops for that reason it enqueues its own remainder.
                        int cells = StepExternalRun(cellBudget - spent);
                        spent += cells;
                        Work.RoomCellsVisited += cells;
                        continue;
                    }

                    StepExternal();
                    spent++;
                    Work.RoomCellsVisited++;
                }
                else if (phase == Phase.Interior)
                {
                    if (frontierCount > 0)
                    {
                        if (RunWalkLive)
                        {
                            int cells = StepInteriorRun(cellBudget - spent);
                            spent += cells;
                            Work.RoomCellsVisited += cells;
                            continue;
                        }

                        StepInterior();
                        spent++;
                        Work.RoomCellsVisited++;
                        continue;
                    }

                    ScanResult result = AdvanceScanToNextUnvisited(ref spent, cellBudget);
                    if (result == ScanResult.Exhausted)
                    {
                        Publish();
                        completed = true;
                        break;
                    }
                    if (result == ScanResult.BudgetSpent) break;
                }
                else
                {
                    break;
                }
            }

            return completed;
        }

        private void BeginPass()
        {
            Work.RoomPassesBegun++;
            restartRequested = false;

            if (hasPendingBounds)
            {
                searchMin = pendingMin;
                searchMaxExclusive = pendingMaxExclusive;
                hasPendingBounds = false;
            }

            // **Three slots rather than a new map a pass.** A published map is read until the
            // publish after next — `ThermalSolver.exposureMap` holds one across a budgeted refresh,
            // and a publish only schedules the restart that replaces it — so the map free to be
            // written again is the one two publishes back. It keeps every array it grew, which is
            // what makes a pass stop allocating its own map (`D20`).
            if (spare != null)
            {
                working = spare;
                spare = null;
                working.Reset();
            }
            else
            {
                working = new RoomMap();
            }

            working.SetSearchBounds(searchMin, searchMaxExclusive);

            // A rebuild is not a guess: the pass before it found this many room cells, and a hull
            // that gained or lost a block has very nearly as many. Sizing the store once here is
            // what removes the doubling copies and the trim copy from every pass after the first.
            if (published != null) working.HintRoomCells(published.RoomCellCount);
            FrontierClear();
            visited.Reset(searchMin, searchMaxExclusive);
            CollectDoorCells();
            TakeSealingSnapshot();

            if (searchMaxExclusive.X <= searchMin.X ||
                searchMaxExclusive.Y <= searchMin.Y ||
                searchMaxExclusive.Z <= searchMin.Z)
            {
                phase = Phase.Interior;
                BeginInteriorScan();
                return;
            }

            // The corner of the padded bounding box is guaranteed to lie outside the grid.
            Vector3I seed = searchMin;

            // The run walk marks and counts what it takes, and it has to be able to extend through
            // its own seed; the cell walk marks at the enqueue.
            if (!RunWalkLive)
            {
                visited.Add(seed);
                working.AddExternal(seed);
            }

            FrontierEnqueue(seed);
            phase = Phase.External;
        }

        /// <summary>
        /// Whether this pass is walking runs: the switch is on and the snapshot it needs is live.
        /// The run walk steps indices, which is only meaningful against a snapshot.
        /// </summary>
        private bool RunWalkLive
        {
            get { return SpanFlood && snapshotLive; }
        }

        private void TakeSealingSnapshot()
        {
            snapshotLive = SnapshotSealing;
            if (!snapshotLive) return;

            sealingSizeX = Math.Max(0, searchMaxExclusive.X - searchMin.X);
            sealingSizeY = Math.Max(0, searchMaxExclusive.Y - searchMin.Y);
            sealingSizeZ = Math.Max(0, searchMaxExclusive.Z - searchMin.Z);

            long cells = (long)sealingSizeX * sealingSizeY * sealingSizeZ;
            if (cells > int.MaxValue)
            {
                // A box this large is a fault elsewhere; the dictionary path still answers it.
                snapshotLive = false;
                return;
            }

            if (sealing.Length < cells) sealing = new byte[cells];
            else Array.Clear(sealing, 0, (int)cells);

            surfaces.CopyStructuralSealing(searchMin, searchMaxExclusive, sealing);

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                faceDelta[face] = offset.X + ((long)offset.Y * sealingSizeX)
                    + ((long)offset.Z * sealingSizeX * sealingSizeY);
            }
        }

        /// <summary>
        /// One flood step over a cell's six neighbours with the snapshot live: the cell's index and
        /// its own sealing byte are read once for all six faces, each neighbour's index is an add,
        /// and the box test is the only per-face geometry. Same faces in the same order as the
        /// dictionary path, so the same map — and the same order within a room, which is what keeps
        /// a room's air links, and the sum over them, bit for bit what they were.
        /// See performance.md, Pass 3, Iteration 2.
        /// </summary>
        /// <returns>True when the caller should classify the neighbour; the neighbour's index is out.</returns>
        /// <summary>
        /// Whether to walk the external air a run of cells at a time rather than a cell at a time.
        ///
        /// <para>
        /// The switch exists so the two walks can be held against each other on real hulls
        /// (`RoomSpanFloodTests`), the way `SnapshotSealing` holds the snapshot against the live
        /// query. The cell walk is the reference: it is the simpler statement of what a flood is,
        /// and the span walk is only correct if it classifies the same cells.
        /// </para>
        /// </summary>
        public bool SpanFlood = true;

        /// <summary>The face indices of −X and +X, and the four that are neither, found once from the offsets.</summary>
        private static readonly int MinusX = FaceAlong(-1, 0, 0);
        private static readonly int PlusX = FaceAlong(1, 0, 0);
        private static readonly int[] LateralFaces = BuildLateralFaces();

        private static int FaceAlong(int x, int y, int z)
        {
            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I offset = Face.Offsets[face];
                if (offset.X == x && offset.Y == y && offset.Z == z) return face;
            }
            return -1;
        }

        private static int[] BuildLateralFaces()
        {
            int[] faces = new int[Face.Count - 2];
            int at = 0;
            for (int face = 0; face < Face.Count; face++)
            {
                if (Face.Offsets[face].X != 0) continue;
                faces[at++] = face;
            }
            return faces;
        }

        /// <summary>
        /// Walks one maximal run of open air along X and returns how many cells it classified.
        ///
        /// <para>
        /// **Why a run.** The external flood is most of what a pass does — 5.1 million cells of the
        /// 7.2 million visited on a 505,566-block hull — and nearly all of it is open space around
        /// the hull, in runs averaging **84 cells**. A cell walk pays a dequeue, an index derivation
        /// and six face tests for each of those cells, and enqueues each of them. A run walk pays
        /// the dequeue and the index once, walks the run's own ±X faces as it extends, and enqueues
        /// only the *first* cell of each unvisited stretch on the four other faces — which is what
        /// keeps the frontier at tens of thousands of entries rather than millions.
        /// </para>
        ///
        /// <para>
        /// **Nothing here depends on the order cells are reached in.** External cells are counted,
        /// not stored, so the pass's outputs — the visited set, the count, and which cells are left
        /// for the interior scan — are the same set whichever walk finds them.
        /// </para>
        ///
        /// <para>
        /// A cell can be enqueued more than once, by each of up to four runs beside it; the visited
        /// test at the top is what makes that harmless, and it is why the enqueue does not mark.
        /// </para>
        ///
        /// <para>
        /// **A run never outruns the tick's budget.** `RoomMappingNeverExceedsItsBudgetInOneTick`
        /// holds the mapper to its budget on every tick however large the grid, and a run on a
        /// large hull is hundreds of cells — so a run stops at <paramref name="most"/> cells and
        /// **enqueues where it stopped**, which the next tick picks up. The cell it enqueues has
        /// already been shown reachable by the same test the extension uses, because a seed the
        /// walk cannot justify would be counted as open air on sight.
        /// </para>
        /// </summary>
        /// <param name="most">Cells this run may take, being what is left of the tick's budget.</param>
        private int StepExternalRun(int most)
        {
            Vector3I seed = FrontierDequeue();

            long index = SealingIndex(seed);
            if (index < 0 || visited.ContainsIndex(index)) return 1;

            if (most < 1) most = 1;
            int taken = 1;

            // Extend to the low side, then to the high side, stopping at a seal on either face of
            // the pair, at a cell some other run already took, at the box, or at the budget.
            int low = seed.X;
            long lowIndex = index;
            while (taken < most && ReachesLow(low, lowIndex))
            {
                low--;
                lowIndex--;
                taken++;
            }

            if (taken >= most && ReachesLow(low, lowIndex))
            {
                FrontierEnqueue(new Vector3I(low - 1, seed.Y, seed.Z));
            }

            int high = seed.X;
            long highIndex = index;
            while (taken < most && ReachesHigh(high, highIndex))
            {
                high++;
                highIndex++;
                taken++;
            }

            if (taken >= most && ReachesHigh(high, highIndex))
            {
                FrontierEnqueue(new Vector3I(high + 1, seed.Y, seed.Z));
            }

            for (long i = lowIndex; i <= highIndex; i++) visited.AddIndex(i);

            int length = (int)(highIndex - lowIndex + 1);
            working.AddExternalRun(length);

            for (int f = 0; f < LateralFaces.Length; f++)
            {
                int face = LateralFaces[f];
                Vector3I offset = Face.Offsets[face];

                int y = seed.Y + offset.Y;
                if (y < searchMin.Y || y >= searchMaxExclusive.Y) continue;

                int z = seed.Z + offset.Z;
                if (z < searchMin.Z || z >= searchMaxExclusive.Z) continue;

                long delta = faceDelta[face];
                int opposite = Face.Opposite(face);
                bool inStretch = false;

                for (long i = lowIndex; i <= highIndex; i++)
                {
                    long neighbour = i + delta;

                    if ((sealing[i] & (1 << face)) != 0
                        || visited.ContainsIndex(neighbour)
                        || (sealing[neighbour] & (1 << opposite)) != 0)
                    {
                        inStretch = false;
                        continue;
                    }

                    if (inStretch) continue;

                    inStretch = true;
                    FrontierEnqueue(new Vector3I(low + (int)(i - lowIndex), y, z));
                }
            }

            return length;
        }

        /// <summary>
        /// The same walk for the inside of a room, and it has one thing to do that the external one
        /// does not: a cell it reaches is either air, which joins the room, or sealed structure,
        /// which is recorded as the room's boundary and stops the walk going further that way.
        ///
        /// <para>
        /// **A room's cells now arrive in run order rather than in the order a queue emptied.** That
        /// is only safe because iteration 4 made a room's air a function of its contents rather than
        /// of the path through it; before that, this change would have moved every player's air
        /// temperatures in the last bits.
        /// </para>
        /// </summary>
        /// <param name="most">Cells this run may take, being what is left of the tick's budget.</param>
        private int StepInteriorRun(int most)
        {
            Vector3I seed = FrontierDequeue();

            long index = SealingIndex(seed);
            if (index < 0 || visited.ContainsIndex(index)) return 1;

            if (most < 1) most = 1;
            int taken = 1;

            int low = seed.X;
            long lowIndex = index;
            while (taken < most && ReachesLow(low, lowIndex))
            {
                taken++;
                if (TakeIfStructure(lowIndex - 1, new Vector3I(low - 1, seed.Y, seed.Z))) break;
                low--;
                lowIndex--;
            }

            if (taken >= most && ReachesLow(low, lowIndex))
            {
                FrontierEnqueue(new Vector3I(low - 1, seed.Y, seed.Z));
            }

            int high = seed.X;
            long highIndex = index;
            while (taken < most && ReachesHigh(high, highIndex))
            {
                taken++;
                if (TakeIfStructure(highIndex + 1, new Vector3I(high + 1, seed.Y, seed.Z))) break;
                high++;
                highIndex++;
            }

            if (taken >= most && ReachesHigh(high, highIndex))
            {
                FrontierEnqueue(new Vector3I(high + 1, seed.Y, seed.Z));
            }

            for (long i = lowIndex; i <= highIndex; i++)
            {
                visited.AddIndex(i);
                working.AddToRoom(currentRoom, new Vector3I(low + (int)(i - lowIndex), seed.Y, seed.Z));
            }

            for (int f = 0; f < LateralFaces.Length; f++)
            {
                int face = LateralFaces[f];
                Vector3I offset = Face.Offsets[face];

                int y = seed.Y + offset.Y;
                if (y < searchMin.Y || y >= searchMaxExclusive.Y) continue;

                int z = seed.Z + offset.Z;
                if (z < searchMin.Z || z >= searchMaxExclusive.Z) continue;

                long delta = faceDelta[face];
                int opposite = Face.Opposite(face);
                bool inStretch = false;

                for (long i = lowIndex; i <= highIndex; i++)
                {
                    long neighbour = i + delta;

                    if ((sealing[i] & (1 << face)) != 0
                        || visited.ContainsIndex(neighbour)
                        || (sealing[neighbour] & (1 << opposite)) != 0)
                    {
                        inStretch = false;
                        continue;
                    }

                    int x = low + (int)(i - lowIndex);
                    Vector3I cell = new Vector3I(x, y, z);

                    // Structure bounding the room: recorded here and not walked into, so it also
                    // breaks the stretch beside it.
                    if (TakeIfStructure(neighbour, cell))
                    {
                        inStretch = false;
                        continue;
                    }

                    if (inStretch) continue;

                    inStretch = true;
                    FrontierEnqueue(cell);
                }
            }

            return taken;
        }

        /// <summary>
        /// Records a cell as sealed structure if that is what it is, and says so. Marks it visited
        /// either way it is structure, because a boundary cell is classified once and never walked.
        /// </summary>
        private bool TakeIfStructure(long index, Vector3I cell)
        {
            if (!IsStructureAt(index, cell)) return false;

            visited.AddIndex(index);
            working.AddSolid(cell);
            return true;
        }

        /// <summary>Whether the run may extend one cell further down X from here.</summary>
        private bool ReachesLow(int x, long index)
        {
            return x > searchMin.X
                && (sealing[index] & (1 << MinusX)) == 0
                && !visited.ContainsIndex(index - 1)
                && (sealing[index - 1] & (1 << PlusX)) == 0;
        }

        /// <summary>Whether the run may extend one cell further up X from here.</summary>
        private bool ReachesHigh(int x, long index)
        {
            return x < searchMaxExclusive.X - 1
                && (sealing[index] & (1 << PlusX)) == 0
                && !visited.ContainsIndex(index + 1)
                && (sealing[index + 1] & (1 << MinusX)) == 0;
        }

        private bool Reaches(long index, int sealingHere, Vector3I neighbour, int face, out long neighbourIndex)
        {
            neighbourIndex = -1;

            if ((sealingHere & (1 << face)) != 0) return false;
            if (!GridMath.Contains(searchMin, searchMaxExclusive, neighbour)) return false;

            neighbourIndex = index + faceDelta[face];
            if (visited.ContainsIndex(neighbourIndex)) return false;

            return (sealing[neighbourIndex] & (1 << Face.Opposite(face))) == 0;
        }

        /// <summary>Index into <see cref="sealing"/>, or -1 outside the box.</summary>
        private long SealingIndex(Vector3I cell)
        {
            int x = cell.X - searchMin.X;
            if (x < 0 || x >= sealingSizeX) return -1;

            int y = cell.Y - searchMin.Y;
            if (y < 0 || y >= sealingSizeY) return -1;

            int z = cell.Z - searchMin.Z;
            if (z < 0 || z >= sealingSizeZ) return -1;

            return (((long)z * sealingSizeY) + y) * sealingSizeX + x;
        }

        /// <summary>
        /// <see cref="SurfaceMap.IsFaceSealedStructurally"/>, answered from the snapshot when one is
        /// live: either side's own airtight bit across the shared face seals it.
        /// </summary>
        private bool IsFaceSealed(Vector3I cell, int face, Vector3I neighbour)
        {
            if (!snapshotLive) return surfaces.IsFaceSealedStructurally(cell, face);

            long here = SealingIndex(cell);
            if (here >= 0 && (sealing[here] & (1 << face)) != 0) return true;

            long there = SealingIndex(neighbour);
            return there >= 0 && (sealing[there] & (1 << Face.Opposite(face))) != 0;
        }

        /// <summary><see cref="SurfaceMap.IsFullySealedStructurally"/>, from the snapshot when one is live.</summary>
        private bool IsFullySealed(Vector3I cell)
        {
            if (!snapshotLive) return surfaces.IsFullySealedStructurally(cell);

            long index = SealingIndex(cell);
            return index >= 0 && (sealing[index] & CellSurface.SelfAirtightMask) == CellSurface.SelfAirtightMask;
        }

        private void StepExternal()
        {
            Vector3I cell = FrontierDequeue();

            if (snapshotLive)
            {
                long index = SealingIndex(cell);
                int sealingHere = sealing[index];

                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I neighbour = cell + Face.Offsets[face];
                    long neighbourIndex;
                    if (!Reaches(index, sealingHere, neighbour, face, out neighbourIndex)) continue;

                    visited.AddIndex(neighbourIndex);
                    working.AddExternal(neighbour);
                    FrontierEnqueue(neighbour);
                }
                return;
            }

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I neighbour = cell + Face.Offsets[face];

                if (!GridMath.Contains(searchMin, searchMaxExclusive, neighbour)) continue;
                if (visited.Contains(neighbour)) continue;
                if (IsFaceSealed(cell, face, neighbour)) continue;

                visited.Add(neighbour);
                working.AddExternal(neighbour);
                FrontierEnqueue(neighbour);
            }
        }

        private int currentRoom = -1;

        private void StepInterior()
        {
            Vector3I cell = FrontierDequeue();

            if (snapshotLive)
            {
                long index = SealingIndex(cell);
                int sealingHere = sealing[index];

                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I neighbour = cell + Face.Offsets[face];
                    long neighbourIndex;
                    if (!Reaches(index, sealingHere, neighbour, face, out neighbourIndex)) continue;

                    visited.AddIndex(neighbourIndex);
                    if (IsStructureAt(neighbourIndex, neighbour))
                    {
                        working.AddSolid(neighbour);
                    }
                    else
                    {
                        working.AddToRoom(currentRoom, neighbour);
                        FrontierEnqueue(neighbour);
                    }
                }
                return;
            }

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I neighbour = cell + Face.Offsets[face];

                if (!GridMath.Contains(searchMin, searchMaxExclusive, neighbour)) continue;
                if (visited.Contains(neighbour)) continue;
                if (IsFaceSealed(cell, face, neighbour)) continue;

                visited.Add(neighbour);
                if (IsStructure(neighbour))
                {
                    working.AddSolid(neighbour);
                }
                else
                {
                    working.AddToRoom(currentRoom, neighbour);
                    FrontierEnqueue(neighbour);
                }
            }
        }

        /// <summary>How a slice of the interior scan ended.</summary>
        private enum ScanResult
        {
            /// <summary>A cell no fill had reached; a new room starts there.</summary>
            Found,

            /// <summary>The cursor reached the end of the search box. The pass is complete.</summary>
            Exhausted,

            /// <summary>This tick's budget ran out mid-scan. The cursor stays where it is.</summary>
            BudgetSpent
        }

        /// <summary>
        /// Advances the scan cursor to the next cell no pass has classified, charged against the tick's
        /// budget **cell by cell**: the walk between two rooms crosses the whole bounding box, so
        /// charging it as one unit lets a single tick absorb an unbounded sweep.
        /// See load-and-hitching.md, 2.
        /// </summary>
        private ScanResult AdvanceScanToNextUnvisited(ref int spent, int budget)
        {
            while (true)
            {
                if (scanCursor.Z >= searchMaxExclusive.Z) return ScanResult.Exhausted;
                if (spent >= budget) return ScanResult.BudgetSpent;

                if (snapshotLive)
                {
                    // Most of the box has been visited by the time the scan reaches it — the
                    // external flood took the air and the interior floods take the rooms — so the
                    // scan's job is mostly to skip. The bitset does that a word at a time, and the
                    // charge is per word looked at rather than per cell skipped: still cell by cell
                    // where cells are unvisited, and never more than one unit per sixty-four.
                    // **The skip stops at what the tick has left.** A word looked at is a unit
                    // charged, and the skip used to run to the end of the box in one call and then
                    // charge for all of it — so a tick could overshoot its budget by however far
                    // the last skip happened to reach. It is capped to the sixty-four cells a
                    // remaining unit buys, and the cursor resumes next tick.
                    // The window ends where the last word this tick can afford ends, so the skip
                    // examines at most as many words as there are units left — the word holding the
                    // end is examined too, which an end of `scanIndex + units * 64` would make one
                    // too many.
                    long window = ((scanIndex >> 6) + (budget - spent)) << 6;
                    if (window > sealingCells) window = sealingCells;

                    int examined;
                    long next = visited.NextClearIndex(scanIndex, window, out examined);
                    spent += examined;
                    Work.RoomCellsVisited += examined;

                    if (next >= window && window < sealingCells)
                    {
                        // The window closed before an unvisited cell turned up. Move to where it
                        // closed and let the next tick carry on from there.
                        MoveScanTo(next);
                        return ScanResult.BudgetSpent;
                    }

                    if (next > scanIndex) MoveScanTo(next);
                    if (scanCursor.Z >= searchMaxExclusive.Z) return ScanResult.Exhausted;
                }

                Vector3I cell = scanCursor;
                long index = scanIndex;
                AdvanceCursor();

                if (snapshotLive)
                {
                    // The cell at `index` is unvisited by construction of the skip above, so the
                    // charge for it was the word that found it.
                    if (IsStructureAt(index, cell))
                    {
                        visited.AddIndex(index);
                        working.AddSolid(cell);
                        continue;
                    }

                    // A run walk takes its own seed: it has to extend *through* it, so it cannot
                    // be marked or filed here. The cell walk marks at the enqueue, as before.
                    if (!RunWalkLive) visited.AddIndex(index);
                }
                else
                {
                    spent++;
                    Work.RoomCellsVisited++;

                    if (visited.Contains(cell)) continue;
                    visited.Add(cell);

                    if (IsStructure(cell))
                    {
                        working.AddSolid(cell);
                        continue;
                    }
                }

                currentRoom = working.BeginRoom();
                if (!RunWalkLive) working.AddToRoom(currentRoom, cell);
                FrontierEnqueue(cell);
                return ScanResult.Found;
            }
        }

        /// <summary>True when a cell is solid structure: sealed on every face and not part of a door.</summary>
        private bool IsStructure(Vector3I cell)
        {
            if (!IsFullySealed(cell)) return false;
            return !IsDoorCell(cell);
        }

        /// <summary>The same, for a caller that already holds the cell's index in the snapshot.</summary>
        private bool IsStructureAt(long index, Vector3I cell)
        {
            if (index < 0 || (sealing[index] & CellSurface.SelfAirtightMask) != CellSurface.SelfAirtightMask)
            {
                return false;
            }

            return !IsDoorCell(cell);
        }

        /// <summary>
        /// Whether a cell belongs to a door. The count is tested first because most grids have no
        /// door at all, and this is asked of every sealed cell in the bounding volume.
        /// </summary>
        private bool IsDoorCell(Vector3I cell)
        {
            return doorCells.Count > 0 && doorCells.Contains(GridMath.Key(cell));
        }

        private void CollectDoorCells()
        {
            doorCells.Clear();
            passDoors.Clear();
            if (pendingGrid == null) return;

            IList<BlockInstance> doors = pendingGrid.StateDependentBlocks;
            for (int d = 0; d < doors.Count; d++)
            {
                passDoors.Add(doors[d]);

                Vector3I[] cells = doors[d].Cells;
                for (int i = 0; i < cells.Length; i++)
                {
                    doorCells.Add(GridMath.Key(cells[i]));
                }
            }
        }

        /// <summary>Doors seen by the pass currently running.</summary>
        private readonly HashSet<BlockInstance> passDoors = new HashSet<BlockInstance>();

        /// <summary>Points the interior scan at the first cell of the box, with its index.</summary>
        private void BeginInteriorScan()
        {
            scanCursor = searchMin;
            scanIndex = 0;
        }

        /// <summary>Cells in the snapshot's box: the index one past the last cell of the scan.</summary>
        private long sealingCells
        {
            get { return (long)sealingSizeX * sealingSizeY * sealingSizeZ; }
        }

        /// <summary>
        /// Moves the scan to an index the bitset found, deriving the cursor from it: the inverse
        /// of the order the cursor advances in, which <see cref="AdvanceCursor"/> and
        /// <c>WalkingABoxInScanOrderAdvancesTheIndexByOne</c> hold to be the index order.
        /// </summary>
        private void MoveScanTo(long index)
        {
            scanIndex = index;
            if (index >= sealingCells)
            {
                scanCursor = new Vector3I(searchMin.X, searchMin.Y, searchMaxExclusive.Z);
                return;
            }

            long plane = (long)sealingSizeX * sealingSizeY;
            int z = (int)(index / plane);
            long rest = index - (z * plane);
            int y = (int)(rest / sealingSizeX);
            int x = (int)(rest - ((long)y * sealingSizeX));
            scanCursor = new Vector3I(searchMin.X + x, searchMin.Y + y, searchMin.Z + z);
        }

        private void AdvanceCursor()
        {
            // One cell is one index whichever way the cursor wraps, which is the whole reason the
            // index is carried rather than derived. See scanIndex.
            scanIndex++;

            scanCursor.X++;
            if (scanCursor.X < searchMaxExclusive.X) return;

            scanCursor.X = searchMin.X;
            scanCursor.Y++;
            if (scanCursor.Y < searchMaxExclusive.Y) return;

            scanCursor.Y = searchMin.Y;
            scanCursor.Z++;
        }

        private void Publish()
        {
            Work.RoomPassesCompleted++;
            working.DropEmptyRooms();

            // Run after the empty rooms are dropped, so a portal's region indices are the
            // surviving ones.
            FindPortals(working);
            working.RefreshVenting();

            knownDoors.Clear();
            foreach (BlockInstance door in passDoors)
            {
                knownDoors.Add(door);
            }

            RoomMap superseded = published;
            published = working;
            working = null;

            // The map published before this one is still readable for one more publish; the one
            // before *that* is not, and becomes the buffer the next pass writes into.
            spare = supersededPrevious;
            supersededPrevious = superseded == RoomMap.AllExternal ? null : superseded;
            phase = Phase.Done;
            CompletedPasses++;

            Action handler = Completed;
            if (handler != null) handler();
        }

        /// <summary>
        /// Records every face of every door that opens, with the region either side of it. Walks the
        /// grid's doors rather than its blocks, so the cost is the door count.
        /// </summary>
        private void FindPortals(RoomMap map)
        {
            if (pendingGrid == null) return;

            IList<BlockInstance> doors = pendingGrid.StateDependentBlocks;
            for (int d = 0; d < doors.Count; d++)
            {
                BlockInstance door = doors[d];
                Vector3I[] cells = door.Cells;

                for (int face = 0; face < Face.Count; face++)
                {
                    if (!door.IsPortalFace(face)) continue;

                    Vector3I offset = Face.Offsets[face];
                    for (int i = 0; i < cells.Length; i++)
                    {
                        Vector3I outside = cells[i] + offset;

                        // A face onto another cell of the same door leads nowhere.
                        if (pendingGrid.GetAtCell(outside) == door) continue;

                        int inner = RegionAt(map, cells[i]);
                        int outer = RegionAt(map, outside);
                        if (inner == outer) continue;

                        // Sealed on one side: the door opens onto structure and joins nothing.
                        if (inner == SolidRegion || outer == SolidRegion) continue;

                        map.AddPortal(new RoomPortal(door, face, inner, outer));
                    }
                }
            }
        }

        /// <summary>
        /// The region of a cell for portal purposes. Solid structure is not a region a door can open
        /// into, so a door sealed on one side joins nothing.
        /// </summary>
        private static int RegionAt(RoomMap map, Vector3I cell)
        {
            if (map.IsSolid(cell)) return SolidRegion;
            return map.RegionOf(cell);
        }

        /// <summary>Sentinel region for solid structure, which is neither a room nor open air.</summary>
        private const int SolidRegion = -2;
    }
}
