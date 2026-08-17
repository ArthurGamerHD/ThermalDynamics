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
    /// The answer is found by walking, one cell at a time, from each cell toward the sun until the
    /// grid runs out. If the walk crosses anything solid, that cell is shadowed. Exact at cell
    /// resolution, which matters more here than it looks.
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

        /// <summary>Cells the running pass has yet to walk.</summary>
        private readonly List<Vector3I> pending = new List<Vector3I>();

        private GridModel grid;
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

        /// <summary>Cells the completed pass found to be in shadow.</summary>
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
            cursor = 0;
            grid = null;
            IsBuilt = false;
        }

        /// <summary>
        /// Begins a pass for a sun direction, in grid-local space and pointing at the sun. The
        /// previous answer stays readable until this one finishes.
        /// </summary>
        public void Restart(GridModel model, Vector3 sunLocal)
        {
            building.Clear();
            pending.Clear();
            cursor = 0;

            grid = model;
            if (grid == null || sunLocal.LengthSquared() < 1e-6f) return;

            passSun = Vector3.Normalize(sunLocal);

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    pending.Add(cells[c]);
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
            cursor = 0;

            sun = passSun;
            IsBuilt = true;
            return true;
        }

        /// <summary>Finishes the running pass in one go. For a full rebuild, and for tests.</summary>
        public void RunToCompletion()
        {
            while (IsRunning) Step(int.MaxValue);
        }

        /// <summary>
        /// True when nothing on the grid stands between this cell and the sun.
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

        /// <summary>The fraction of a block's cells the sun reaches, 0..1.</summary>
        public float LitFraction(BlockInstance block)
        {
            if (!IsBuilt || block == null) return 1f;

            Vector3I[] cells = block.Cells;
            if (cells.Length == 0) return 1f;

            int lit = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (!shadowed.Contains(cells[i])) lit++;
            }

            return lit / (float)cells.Length;
        }

        /// <summary>
        /// Walks from a cell toward the sun until the grid's bounding box runs out, and reports
        /// whether anything solid was in the way.
        ///
        /// A standard voxel traversal: keep the distance along the ray to the next boundary on each
        /// axis, and step across whichever is nearest. It visits every cell the ray actually passes
        /// through and no others, so a ray cannot slip diagonally between two blocks that touch.
        /// </summary>
        private bool Blocked(Vector3I start)
        {
            Vector3I min = grid.Min;
            Vector3I max = grid.Max;

            int x = start.X, y = start.Y, z = start.Z;

            int stepX = Sign(passSun.X), stepY = Sign(passSun.Y), stepZ = Sign(passSun.Z);

            float tMaxX = Boundary(passSun.X);
            float tMaxY = Boundary(passSun.Y);
            float tMaxZ = Boundary(passSun.Z);

            float tDeltaX = Delta(passSun.X);
            float tDeltaY = Delta(passSun.Y);
            float tDeltaZ = Delta(passSun.Z);

            // The grid is finite, so the walk is: the longest path through it is its box's diagonal
            // in cells. The bounds check below normally ends the walk long before this does.
            int limit = (max.X - min.X) + (max.Y - min.Y) + (max.Z - min.Z) + 3;

            for (int i = 0; i < limit; i++)
            {
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY <= tMaxZ)
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (x < min.X || x > max.X || y < min.Y || y > max.Y || z < min.Z || z > max.Z)
                {
                    return false;
                }

                if (grid.IsOccupied(new Vector3I(x, y, z))) return true;
            }

            return false;
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
