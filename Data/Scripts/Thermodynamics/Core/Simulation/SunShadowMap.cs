using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Which of a grid's own cells the sun can actually reach.
    ///
    /// The cheap solar model asks one question per face — how square is it to the sun — and that
    /// question has no answer for a face standing in the ship's own shadow. A wall inside a doorway
    /// recess, the sunward face of a block under an overhang, the far side of a hangar: all of them
    /// face the sun and none of them see it.
    ///
    /// The question is asked of a face, not of a block. A wall two cells thick has an outer layer
    /// and an inner one, and the inner layer's side faces are just as open to the sky as the outer
    /// layer's — they are on the same wall, looking out of the same side of the ship. Asking
    /// whether the *cell* can see the sun buries every one of them, and a solid hull ends up lit
    /// along a single row of blocks. So what is traced is the empty cell just outside each face:
    /// stand where the face's surface is and look at the sun.
    ///
    /// The walk itself goes one cell at a time from there toward the sun until the grid runs out.
    /// Cross anything solid and that face is shadowed. Exact at cell resolution.
    ///
    /// The obvious cheaper structure — project every cell onto a plane facing the sun, bucket it,
    /// keep whichever is nearest — was built first and then thrown away. Buckets are axis-aligned
    /// and the sun is not, so a column crossing the grid diagonally scatters across neighbouring
    /// buckets: it leaks sunlight onto shadowed cells, and the tolerance that closes the leak
    /// invents shadows on cells standing in the open. Measured against a real ship at an oblique
    /// sun, and against seven test geometries at eight sun angles, every setting of it was wrong in
    /// both directions at once.
    ///
    /// The walk costs more, so it is spread over ticks the way the room mapper spreads its flood
    /// fill, and the last answer stays readable while the next is being built. A pass only starts
    /// when the sun has moved enough to matter or the grid's blocks have changed — on a planet,
    /// seconds apart.
    /// </summary>
    public class SunShadowMap
    {
        /// <summary>Cells found to be in shadow by the last completed pass.</summary>
        private readonly HashSet<Vector3I> shadowed = new HashSet<Vector3I>();

        /// <summary>Cells found so far by the pass currently running.</summary>
        private readonly HashSet<Vector3I> building = new HashSet<Vector3I>();

        /// <summary>Air cells the running pass has yet to walk.</summary>
        private readonly List<Vector3I> pending = new List<Vector3I>();

        /// <summary>Air cells already queued, so a cell shared by six faces is walked once.</summary>
        private readonly HashSet<Vector3I> queued = new HashSet<Vector3I>();

        /// <summary>
        /// Another grid that may be standing in the way, and how to get into its cell space.
        ///
        /// The transform is what makes this tractable. Rather than reasoning about two lattices at
        /// once, a ray is carried into the occluder's own frame and walked there exactly as the
        /// grid walks itself — same traversal, same guarantees, a different set of blocks.
        /// </summary>
        public struct Occluder
        {
            public GridModel Model;

            /// <summary>Maps a point in this grid's cell space into the occluder's.</summary>
            public MatrixD ToOccluder;

            /// <summary>Identity of the occluding grid, so a changed set can be noticed.</summary>
            public long Id;
        }

        /// <summary>Grids the running pass is testing against, beside this one.</summary>
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
        /// True when the answer no longer matches this sun direction. A shadow that lags the sun by
        /// a fraction of a degree is invisible; rebuilding for one is not.
        /// </summary>
        public bool NeedsRestart(ref Vector3 sunLocal, float cosineTolerance)
        {
            if (!IsBuilt && !IsRunning) return true;

            // A pass already running for very nearly this direction is worth finishing rather than
            // restarting, or a sun that creeps never lets one complete.
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
        /// Begins a pass that also tests against other grids. Their shadows land on this grid's
        /// faces the same way its own do, so a station overhead darkens the hull under it and
        /// nothing else.
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

                    // The direction is carried in once per occluder rather than per ray: it is the
                    // same sun for every face, and a normal transform per face would be most of
                    // the cost of the walk it feeds.
                    Vector3D direction = Vector3D.TransformNormal(passSun, occluder.ToOccluder);
                    if (direction.LengthSquared() < 1e-12) continue;

                    occluders.Add(occluder);
                    occluderSun.Add(Vector3D.Normalize(direction));
                }
            }

            // The air on the outside of every block face — the ship's skin, one cell out. Interior
            // air is in there too, and is shadowed by the hull around it, which is correct: a face
            // looking into a sealed room sees no sun.
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
            // int.MaxValue, and cursor + that wraps negative, which reads as "nothing to do" and
            // spins forever.
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

        /// <summary>Finishes the running pass in one go. For a full rebuild, and for tests.</summary>
        public void RunToCompletion()
        {
            while (IsRunning) Step(int.MaxValue);
        }

        /// <summary>
        /// True when nothing on the grid stands between this patch of air and the sun.
        ///
        /// A cell no completed pass has seen answers true: an unbuilt map, or a cell built since the
        /// last pass, means "not known to be shadowed", and the cheap model's answer is the one to
        /// fall back to. Inventing a shadow is the worse of the two errors — it cools a block
        /// standing in full sunlight, which is a temperature nobody can account for, where a missing
        /// shadow is only the behaviour the setting is switched off for.
        /// </summary>
        public bool IsLit(Vector3I cell)
        {
            return !IsBuilt || !shadowed.Contains(cell);
        }

        /// <summary>
        /// True when the sun reaches this face of this cell: the test is made from the air just
        /// outside it, which is where the surface actually is.
        ///
        /// A face with a block pressed against it sees nothing at all — no air to stand in, and no
        /// sky beyond. The model never asks about those, since they carry no exposed area either,
        /// but answering "lit" would be a trap for anything that did.
        /// </summary>
        public bool IsFaceLit(Vector3I cell, int face)
        {
            Vector3I outside = cell + Face.Offsets[face];

            if (resultGrid != null && resultGrid.IsOccupied(outside)) return false;
            return IsLit(outside);
        }

        /// <summary>
        /// The fraction of one side of a block the sun reaches, 0..1.
        ///
        /// Per cell face rather than per block, because a long block can have one end in a shadow
        /// and the other in the open, and because a block is only ever lit on the sides that face
        /// outward in the first place.
        /// </summary>
        public float FaceLitFraction(BlockInstance block, int face)
        {
            if (!IsBuilt || block == null) return 1f;

            Vector3I offset = Face.Offsets[face];
            int axis = Face.Axis(face);
            bool positive = BoxGeometry.Component(offset, axis) > 0;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            int slab = positive
                ? BoxGeometry.Component(maxExclusive, axis) - 1
                : BoxGeometry.Component(min, axis);

            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            int cells = 0;
            int lit = 0;

            for (int a = BoxGeometry.Component(min, u); a < BoxGeometry.Component(maxExclusive, u); a++)
            {
                for (int b = BoxGeometry.Component(min, v); b < BoxGeometry.Component(maxExclusive, v); b++)
                {
                    Vector3I cell = BoxGeometry.WithComponent(Vector3I.Zero, axis, slab);
                    cell = BoxGeometry.WithComponent(cell, u, a);
                    cell = BoxGeometry.WithComponent(cell, v, b);

                    cells++;
                    if (IsFaceLit(cell, face)) lit++;
                }
            }

            return cells == 0 ? 1f : lit / (float)cells;
        }

        /// <summary>
        /// Walks from a cell toward the sun until the grid's bounding box runs out, and reports
        /// whether anything solid was in the way.
        ///
        /// A standard voxel traversal: keep the distance along the ray to the next boundary on each
        /// axis, and step across whichever is nearest. It visits every cell the ray actually passes
        /// through and no others, so a ray cannot slip diagonally between two blocks that touch.
        /// </summary>
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

            // How far the ray stays inside the grid's box. It cannot simply stop the first time it
            // steps outside: the cells being walked are the air just outside the hull, so most of
            // them start outside the box already, and one that steps in along a flank would be
            // called lit before it ever reached the wall standing in its way.
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

            // Bounded twice over: by the distance the ray stays in the box, and by a step count no
            // sane geometry reaches. Both are needed — the first is the real limit, the second
            // stops a degenerate direction spinning.
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
        /// How far along the ray the grid's box is left behind, or 0 when the ray never reaches it.
        /// The box is the block bounds grown by half a cell, since cells are cubes centred on
        /// integers.
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
        /// An axis it does not move along never comes up for selection.
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
