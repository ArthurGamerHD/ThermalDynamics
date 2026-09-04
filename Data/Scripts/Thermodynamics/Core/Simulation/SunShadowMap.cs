using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Which of a grid's own cells the sun can reach, resolved **per face** — the empty cell just
    /// outside each one, where its surface sits. Asked per cell instead, a solid hull ends up lit
    /// along a single row. A voxel walk from there toward the sun, exact at cell resolution, budgeted
    /// across ticks with the last completed answer readable meanwhile.
    /// See thermal-model.md, Solar and point sources.
    /// </summary>
    public class SunShadowMap
    {
        /// <summary>Cells found to be in shadow by the last completed pass.</summary>
        private readonly HashSet<Vector3I> shadowed = new HashSet<Vector3I>();

        /// <summary>Cells found so far by the pass currently running.</summary>
        private readonly HashSet<Vector3I> building = new HashSet<Vector3I>();

        /// <summary>Air cells the running pass has yet to walk.</summary>
        private readonly List<Vector3I> pending = new List<Vector3I>();

        /// <summary>Air cells already queued, so a cell shared by several faces is walked once.</summary>
        private readonly HashSet<Vector3I> queued = new HashSet<Vector3I>();

        /// <summary>
        /// Another grid that may stand in the way, with the transform into its cell space.
        ///
        /// The transform avoids reasoning about two lattices at once: a ray is carried into the
        /// occluder's frame and walked there by the same traversal the grid uses on itself.
        /// </summary>
        public struct Occluder
        {
            public GridModel Model;

            /// <summary>Maps a point in this grid's cell space into the occluder's.</summary>
            public MatrixD ToOccluder;

            /// <summary>Identity of the occluding grid, so a changed set can be detected.</summary>
            public long Id;
        }

        /// <summary>Grids other than this one that the running pass is testing against.</summary>
        private readonly List<Occluder> occluders = new List<Occluder>();

        /// <summary>Sun direction carried into each occluder's frame, one per occluder.</summary>
        private readonly List<Vector3D> occluderSun = new List<Vector3D>();

        private GridModel grid;

        /// <summary>The grid the completed answer describes, for occupancy questions.</summary>
        private GridModel resultGrid;

        private Vector3 sun;
        private Vector3 passSun;
        private int cursor;

        /// <summary>True once a pass has completed and there is an answer to read.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary>True while a pass is part way through.</summary>
        public bool IsRunning
        {
            get { return cursor < pending.Count; }
        }

        /// <summary>The direction the completed answer was built for, in grid-local space.</summary>
        public Vector3 SunDirection { get { return sun; } }

        /// <summary>Air cells the completed pass found to be in shadow.</summary>
        public int ShadowedCount { get { return shadowed.Count; } }

        /// <summary>Cells the running pass has left to walk.</summary>
        public int PendingCells { get { return Math.Max(0, pending.Count - cursor); } }

        /// <summary>
        /// True when the completed answer no longer matches this sun direction by more than the
        /// caller's tolerance.
        /// </summary>
        public bool NeedsRestart(ref Vector3 sunLocal, float cosineTolerance)
        {
            if (!IsBuilt && !IsRunning) return true;

            // A pass already running for nearly this direction is finished rather than restarted;
            // otherwise a slowly moving sun never lets one complete.
            Vector3 reference = IsRunning ? passSun : sun;
            return Vector3.Dot(reference, sunLocal) < cosineTolerance;
        }

        public void Clear()
        {
            shadowed.Clear();
            building.Clear();
            pending.Clear();
            queued.Clear();
            cursor = 0;
            grid = null;
            resultGrid = null;
            IsBuilt = false;
        }

        /// <summary>
        /// Begins a pass for a sun direction, in grid-local space and pointing at the sun. The
        /// previous answer stays readable until this one finishes.
        /// </summary>
        public void Restart(GridModel model, Vector3 sunLocal)
        {
            Restart(model, sunLocal, null);
        }

        /// <summary>
        /// Begins a pass that also tests against other grids. Their shadows fall on this grid's faces
        /// exactly as its own do, so a station overhead darkens only the hull beneath it.
        /// </summary>
        public void Restart(GridModel model, Vector3 sunLocal, IList<Occluder> others)
        {
            building.Clear();
            pending.Clear();
            queued.Clear();
            occluders.Clear();
            occluderSun.Clear();
            cursor = 0;

            grid = model;
            if (grid == null || sunLocal.LengthSquared() < 1e-6f) return;

            passSun = Vector3.Normalize(sunLocal);

            if (others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    Occluder occluder = others[i];
                    if (occluder.Model == null) continue;

                    // The direction is transformed once per occluder rather than per ray: the sun
                    // is the same for every face, and a per-face transform would dominate the walk.
                    Vector3D direction = Vector3D.TransformNormal(passSun, occluder.ToOccluder);
                    if (direction.LengthSquared() < 1e-12) continue;

                    occluders.Add(occluder);
                    occluderSun.Add(Vector3D.Normalize(direction));
                }
            }

            // The air one cell outside every block face: the grid's skin. Interior air is included
            // and is correctly shadowed by the hull around it, since a face looking into a sealed
            // room sees no sun.
            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I outside = cells[c] + Face.Offsets[face];

                        if (grid.IsOccupied(outside)) continue;
                        if (!queued.Add(outside)) continue;

                        pending.Add(outside);
                    }
                }
            }
        }

        /// <summary>
        /// Walks up to <paramref name="budget"/> cells. Returns true on the tick that completes a
        /// pass, which is the only tick on which the answer changes.
        /// </summary>
        public bool Step(int budget)
        {
            if (!IsRunning) return false;

            // Guarded against overflow rather than clamped afterwards: RunToCompletion passes
            // int.MaxValue, and adding the cursor to that wraps negative, which reads as no work
            // remaining and never completes.
            int slice = Math.Max(1, budget);
            int end = slice >= pending.Count - cursor ? pending.Count : cursor + slice;

            for (; cursor < end; cursor++)
            {
                if (Blocked(pending[cursor])) building.Add(pending[cursor]);
            }

            if (IsRunning) return false;

            shadowed.Clear();
            foreach (Vector3I cell in building) shadowed.Add(cell);

            building.Clear();
            pending.Clear();
            queued.Clear();
            cursor = 0;

            sun = passSun;
            resultGrid = grid;
            IsBuilt = true;
            return true;
        }

        /// <summary>Finishes the running pass in one call. Used for a full rebuild and by tests.</summary>
        public void RunToCompletion()
        {
            while (IsRunning) Step(int.MaxValue);
        }

        /// <summary>
        /// True when nothing on the grid stands between this cell of air and the sun. A cell no
        /// completed pass has seen returns true, because a missing shadow only reproduces the model
        /// with self-shadowing off, where a false one cools a block standing in full sunlight.
        /// </summary>
        public bool IsLit(Vector3I cell)
        {
            return !IsBuilt || !shadowed.Contains(cell);
        }

        /// <summary>
        /// True when the sun reaches this face of this cell. Tested from the air just outside the
        /// face, which is where the surface sits.
        ///
        /// A face with a block against it returns false: there is no air outside it and no sky
        /// beyond. The model never queries those faces, which carry no exposed area either.
        /// </summary>
        public bool IsFaceLit(Vector3I cell, int face)
        {
            Vector3I outside = cell + Face.Offsets[face];

            if (resultGrid != null && resultGrid.IsOccupied(outside)) return false;
            return IsLit(outside);
        }

        /// <summary>
        /// Fraction of one side of a block the sun reaches, 0..1.
        ///
        /// Averaged over cell faces rather than taken per block, since a multi-cell block can have
        /// one end shadowed and the other in the open.
        /// </summary>
        public float FaceLitFraction(BlockInstance block, int face)
        {
            if (!IsBuilt || block == null) return 1f;

            BoxGeometry.FaceSpan span = BoxGeometry.Span(block.Min, block.MaxExclusive, face);

            int cells = 0;
            int lit = 0;

            for (int a = span.MinU; a < span.MaxExclusiveU; a++)
            {
                for (int b = span.MinV; b < span.MaxExclusiveV; b++)
                {
                    Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, span.Axis, span.Slab);
                    cell = BoxGeometry.WithComponent(cell, span.U, a);
                    cell = BoxGeometry.WithComponent(cell, span.V, b);

                    cells++;
                    if (IsFaceLit(cell, face)) lit++;
                }
            }

            return cells == 0 ? 1f : lit / (float)cells;
        }

        /// <summary>Grids other than this one that the running pass is testing against.</summary>
        public int OccluderCount { get { return occluders.Count; } }

        private bool Blocked(Vector3I start)
        {
            if (BlockedBySelf(start)) return true;

            Vector3D origin = new Vector3D(start.X, start.Y, start.Z);

            for (int i = 0; i < occluders.Count; i++)
            {
                Occluder occluder = occluders[i];

                Vector3D from = Vector3D.Transform(origin, occluder.ToOccluder);
                if (VoxelWalk.Blocked(occluder.Model, from, occluderSun[i])) return true;
            }

            return false;
        }

        private bool BlockedBySelf(Vector3I start)
        {
            Vector3I min = grid.Min;
            Vector3I max = grid.Max;

            // How far the ray stays inside the grid's box. The walk cannot stop the first time it
            // steps outside: the cells being walked are the air just outside the hull, so most start
            // outside the box, and one that re-enters along a flank would be called lit before
            // reaching the wall in its way.
            float exit = BoxExit(start, min, max);
            if (exit <= 0f) return false;

            int x = start.X, y = start.Y, z = start.Z;

            int stepX = Sign(passSun.X), stepY = Sign(passSun.Y), stepZ = Sign(passSun.Z);

            float tMaxX = Boundary(passSun.X);
            float tMaxY = Boundary(passSun.Y);
            float tMaxZ = Boundary(passSun.Z);

            float tDeltaX = Delta(passSun.X);
            float tDeltaY = Delta(passSun.Y);
            float tDeltaZ = Delta(passSun.Z);

            // Bounded twice: by the distance the ray stays inside the box, which is the real limit,
            // and by a step count that stops a degenerate direction from looping.
            int limit = (2 * ((max.X - min.X) + (max.Y - min.Y) + (max.Z - min.Z))) + 8;

            for (int i = 0; i < limit; i++)
            {
                float t;

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY <= tMaxZ)
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (t > exit) return false;

                if (grid.IsOccupied(new Vector3I(x, y, z))) return true;
            }

            return false;
        }

        /// <summary>
        /// Distance along the ray at which it leaves the grid's box, or 0 when it never enters. The
        /// box is the block bounds grown by half a cell, since cells are cubes centred on integers.
        /// </summary>
        private float BoxExit(Vector3I start, Vector3I min, Vector3I max)
        {
            float enter = 0f;
            float exit = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float origin = BoxGeometry.Component(start, axis);
                float direction = axis == 0 ? passSun.X : axis == 1 ? passSun.Y : passSun.Z;

                float low = BoxGeometry.Component(min, axis) - 0.5f;
                float high = BoxGeometry.Component(max, axis) + 0.5f;

                if (Math.Abs(direction) < 1e-6f)
                {
                    if (origin < low || origin > high) return 0f;
                    continue;
                }

                float t1 = (low - origin) / direction;
                float t2 = (high - origin) / direction;

                if (t1 > t2)
                {
                    float swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                if (t1 > enter) enter = t1;
                if (t2 < exit) exit = t2;
            }

            return exit < enter ? 0f : exit;
        }

        private static int Sign(float value)
        {
            if (value > 0f) return 1;
            if (value < 0f) return -1;
            return 0;
        }

        /// <summary>
        /// Distance along the ray to the first cell boundary. Cells are unit cubes centred on
        /// integers, so the ray starts half a cell from the boundary on every axis it moves along.
        /// An axis it does not move along is never selected.
        /// </summary>
        private static float Boundary(float component)
        {
            float magnitude = Math.Abs(component);
            return magnitude < 1e-6f ? float.MaxValue : 0.5f / magnitude;
        }

        private static float Delta(float component)
        {
            float magnitude = Math.Abs(component);
            return magnitude < 1e-6f ? float.MaxValue : 1f / magnitude;
        }
    }
}
