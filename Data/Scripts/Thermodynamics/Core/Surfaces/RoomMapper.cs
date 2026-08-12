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
        private readonly HashSet<Vector3I> visited = new HashSet<Vector3I>(Vector3I.Comparer);

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

        /// <summary>Raised each time a pass completes and a new map is published.</summary>
        public event Action Completed;

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
        /// Runs the whole pass to completion. Convenient for tests and for load-time mapping;
        /// gameplay code should use the budgeted <see cref="Step"/>.
        /// </summary>
        public void RunToCompletion(int safetyLimit = 20000000)
        {
            int spent = 0;
            while (HasWorkPending && spent < safetyLimit)
            {
                spent += 4096;
                Step(4096);
            }
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
                }
                else if (phase == Phase.Interior)
                {
                    if (frontier.Count > 0)
                    {
                        StepInterior();
                        spent++;
                        continue;
                    }

                    if (!AdvanceScanToNextUnvisited())
                    {
                        Publish();
                        completed = true;
                        break;
                    }
                    spent++;
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
            restartRequested = false;

            if (hasPendingBounds)
            {
                searchMin = pendingMin;
                searchMaxExclusive = pendingMaxExclusive;
                hasPendingBounds = false;
            }

            working = new RoomMap();
            frontier.Clear();
            visited.Clear();

            if (searchMaxExclusive.X <= searchMin.X ||
                searchMaxExclusive.Y <= searchMin.Y ||
                searchMaxExclusive.Z <= searchMin.Z)
            {
                phase = Phase.Interior;
                scanCursor = searchMin;
                return;
            }

            // The corner of the padded bounding box is guaranteed to be outside the grid.
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
                if (surfaces.IsFaceSealed(cell, face)) continue;

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
                if (surfaces.IsFaceSealed(cell, face)) continue;

                visited.Add(neighbour);
                if (surfaces.IsFullySealed(neighbour))
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

        private bool AdvanceScanToNextUnvisited()
        {
            while (true)
            {
                if (scanCursor.Z >= searchMaxExclusive.Z) return false;

                Vector3I cell = scanCursor;
                AdvanceCursor();

                if (visited.Contains(cell)) continue;

                visited.Add(cell);

                if (surfaces.IsFullySealed(cell))
                {
                    working.AddSolid(cell);
                    continue;
                }

                currentRoom = working.BeginRoom();
                working.AddToRoom(currentRoom, cell);
                frontier.Enqueue(cell);
                return true;
            }
        }

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
            working.DropEmptyRooms();
            published = working;
            working = null;
            phase = Phase.Done;
            CompletedPasses++;

            Action handler = Completed;
            if (handler != null) handler();
        }
    }
}
