using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Incrementally classifies every cell in a grid's bounding box as external space, sealed
    /// structure, or part of an enclosed room.
    ///
    /// The pass is resumable: <see cref="Step"/> does a bounded amount of work and returns, so a
    /// large grid spreads its mapping across frames. Restart requests are coalesced, so welding
    /// a thousand blocks in one second costs one pass, not a thousand.
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

        private readonly Queue<Vector3I> frontier = new Queue<Vector3I>();
        /// <summary>
        /// Cells this pass has already classified, one bit each over the search box.
        ///
        /// A bitset rather than a hash set: by the end of a pass this holds every cell of the
        /// bounding box, and at about thirty-one bytes per cell a hash set was the mod's peak
        /// allocation — 121 MB of a 400 MB peak on a 127,000-block grid. It also scales with the
        /// box rather than the grid, so a mostly empty hull pays for the empty space.
        /// </summary>
        private readonly CellBitset visited = new CellBitset();

        /// <summary>
        /// Cells belonging to a door. Never classified as solid structure even when they seal on all
        /// six faces: a door is an openable volume, and a portal needs a region on the door's own
        /// side to join to. A shut airtight hangar door is a room of one cell, which merges with the
        /// regions either side when it opens.
        /// </summary>
        private readonly HashSet<Vector3I> doorCells = new HashSet<Vector3I>(Vector3I.Comparer);

        private RoomMap working;
        private RoomMap published = RoomMap.AllExternal;

        private Phase phase = Phase.Idle;
        private bool restartRequested;

        private Vector3I searchMin;
        private Vector3I searchMaxExclusive;
        private Vector3I scanCursor;

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
            get { return frontier.Count; }
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
        /// Runs the whole pass to completion. For tests and load-time mapping; gameplay code should
        /// use the budgeted <see cref="Step"/>.
        ///
        /// <para>
        /// The limit is a guard against a pass that never finishes, not a budget. It used to be a
        /// flat twenty million cell visits, which a grid of about seven hundred thousand blocks
        /// exceeds — and exceeding it returned without publishing anything, leaving
        /// <see cref="RoomMap.AllExternal"/> in place. A hull that large therefore finished its load
        /// with no rooms and every interior block classified as facing open space: 95 % of blocks
        /// exposed against 34 % on the same shape one rung smaller, every one of them radiating and
        /// convecting to the sky, and no compartment holding air. It self-corrected only once the
        /// budgeted per-tick path had walked the same flood again, thousands of ticks later.
        /// </para>
        ///
        /// <para>
        /// The flood cannot visit a cell twice — <c>visited</c> is a bitset over the search bounds
        /// and the interior scan walks each cell once — so the work is bounded by the search volume
        /// and a limit derived from it can never bind on a grid that is going to finish. That is
        /// what a guard should be: unreachable unless something is actually wrong.
        /// </para>
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
        /// Cell visits to allow for one pass over the bounds it is about to walk.
        ///
        /// Four times the volume: the external flood and the interior scan each cover it once, and
        /// the factor leaves room for the frontier bookkeeping charged alongside them. Saturating
        /// rather than overflowing — a bounding volume large enough to overflow an <c>int</c> is
        /// exactly the case a flat limit got wrong.
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
                    if (frontier.Count == 0)
                    {
                        phase = Phase.Interior;
                        scanCursor = searchMin;
                        continue;
                    }
                    StepExternal();
                    spent++;
                    Work.RoomCellsVisited++;
                }
                else if (phase == Phase.Interior)
                {
                    if (frontier.Count > 0)
                    {
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

            working = new RoomMap();
            working.SetSearchBounds(searchMin, searchMaxExclusive);
            frontier.Clear();
            visited.Reset(searchMin, searchMaxExclusive);
            CollectDoorCells();

            if (searchMaxExclusive.X <= searchMin.X ||
                searchMaxExclusive.Y <= searchMin.Y ||
                searchMaxExclusive.Z <= searchMin.Z)
            {
                phase = Phase.Interior;
                scanCursor = searchMin;
                return;
            }

            // The corner of the padded bounding box is guaranteed to lie outside the grid.
            Vector3I seed = searchMin;
            visited.Add(seed);
            working.AddExternal(seed);
            frontier.Enqueue(seed);
            phase = Phase.External;
        }

        private void StepExternal()
        {
            Vector3I cell = frontier.Dequeue();

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I neighbour = cell + Face.Offsets[face];

                if (!GridMath.Contains(searchMin, searchMaxExclusive, neighbour)) continue;
                if (visited.Contains(neighbour)) continue;
                if (surfaces.IsFaceSealedStructurally(cell, face)) continue;

                visited.Add(neighbour);
                working.AddExternal(neighbour);
                frontier.Enqueue(neighbour);
            }
        }

        private int currentRoom = -1;

        private void StepInterior()
        {
            Vector3I cell = frontier.Dequeue();

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I neighbour = cell + Face.Offsets[face];

                if (!GridMath.Contains(searchMin, searchMaxExclusive, neighbour)) continue;
                if (visited.Contains(neighbour)) continue;
                if (surfaces.IsFaceSealedStructurally(cell, face)) continue;

                visited.Add(neighbour);
                if (IsStructure(neighbour))
                {
                    working.AddSolid(neighbour);
                }
                else
                {
                    working.AddToRoom(currentRoom, neighbour);
                    frontier.Enqueue(neighbour);
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
        /// Advances the scan cursor to the next cell no pass has classified.
        ///
        /// Charged against the tick's budget cell by cell. The walk crosses every cell of the
        /// bounding box over the course of a pass, so charging a whole walk between two rooms as one
        /// unit lets a single tick absorb an unbounded sweep — on a 127k grid, the tick finishing
        /// the pass swept the tail of a 1.5-million-cell box in one go for 77 ms. Charging per cell
        /// makes a pass take more ticks, each of them bounded.
        /// </summary>
        private ScanResult AdvanceScanToNextUnvisited(ref int spent, int budget)
        {
            while (true)
            {
                if (scanCursor.Z >= searchMaxExclusive.Z) return ScanResult.Exhausted;
                if (spent >= budget) return ScanResult.BudgetSpent;

                Vector3I cell = scanCursor;
                AdvanceCursor();

                spent++;
                Work.RoomCellsVisited++;

                if (visited.Contains(cell)) continue;

                visited.Add(cell);

                if (IsStructure(cell))
                {
                    working.AddSolid(cell);
                    continue;
                }

                currentRoom = working.BeginRoom();
                working.AddToRoom(currentRoom, cell);
                frontier.Enqueue(cell);
                return ScanResult.Found;
            }
        }

        /// <summary>True when a cell is solid structure: sealed on every face and not part of a door.</summary>
        private bool IsStructure(Vector3I cell)
        {
            if (!surfaces.IsFullySealedStructurally(cell)) return false;
            return !doorCells.Contains(cell);
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
                    doorCells.Add(cells[i]);
                }
            }
        }

        /// <summary>Doors seen by the pass currently running.</summary>
        private readonly HashSet<BlockInstance> passDoors = new HashSet<BlockInstance>();

        private void AdvanceCursor()
        {
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

            // Run after the renumbering, so a portal's region indices are the surviving ones.
            FindPortals(working);
            working.RefreshVenting();

            knownDoors.Clear();
            foreach (BlockInstance door in passDoors)
            {
                knownDoors.Add(door);
            }

            published = working;
            working = null;
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
