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
    /// recess, the sunward face of a block buried under a hull plate, the far side of a hangar: all
    /// of them face the sun and none of them see it.
    ///
    /// This is a shadow map, in the grid's own cell space rather than in pixels. Every occupied
    /// cell is projected onto the plane perpendicular to the sun and dropped into a bucket one cell
    /// across; the bucket keeps the depth of whichever cell sits closest to the sun. A cell is lit
    /// when it is that cell. Everything behind it in the same column is shadowed by it.
    ///
    /// The cost is one pass over the grid's cells to build and a dictionary lookup to query, and
    /// the build only happens when the sun has actually moved — on a planet, seconds apart. That is
    /// what makes it affordable at all: the alternative, a ray per face per step, is the same work
    /// repeated for every face that shares a column.
    /// </summary>
    public class SunShadowMap
    {
        /// <summary>
        /// How far behind its column's leading cell a cell may still be counted as lit, in cells.
        ///
        /// Buckets are axis-aligned and the sun is not, so a column crossing the grid diagonally
        /// gathers cells whose depths differ by up to half a cell either way through nothing but
        /// quantisation. Half the diagonal of a cell is the smallest tolerance that does not throw
        /// away genuinely lit surfaces; the price is a little bleed at glancing angles, which reads
        /// as a soft shadow edge rather than as a wrong answer.
        /// </summary>
        public const float DepthTolerance = 0.87f;

        private readonly Dictionary<long, float> leading = new Dictionary<long, float>();

        private Vector3 sun;
        private Vector3 right;
        private Vector3 up;

        /// <summary>True once <see cref="Build"/> has run against a usable sun direction.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary>The direction the map was built for, in grid-local space.</summary>
        public Vector3 SunDirection { get { return sun; } }

        /// <summary>Cells the last build placed in a column.</summary>
        public int CellCount { get; private set; }

        /// <summary>Columns the last build found. Roughly the grid's silhouette area, in cells.</summary>
        public int ColumnCount { get { return leading.Count; } }

        /// <summary>
        /// True when the map is stale for this sun direction. A shadow that lags the sun by a
        /// fraction of a degree is invisible; rebuilding for one is not.
        /// </summary>
        public bool NeedsRebuild(ref Vector3 sunLocal, float cosineTolerance)
        {
            if (!IsBuilt) return true;
            return Vector3.Dot(sun, sunLocal) < cosineTolerance;
        }

        public void Clear()
        {
            leading.Clear();
            IsBuilt = false;
            CellCount = 0;
        }

        /// <summary>
        /// Rebuilds the map for a sun direction, in grid-local space and pointing at the sun.
        /// </summary>
        public void Build(GridModel grid, Vector3 sunLocal)
        {
            Clear();
            if (grid == null) return;

            if (sunLocal.LengthSquared() < 1e-6f) return;

            sun = Vector3.Normalize(sunLocal);
            Basis(ref sun, out right, out up);

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    Insert(cells[c]);
                }
            }

            IsBuilt = true;
        }

        /// <summary>
        /// True when nothing on the grid stands between this cell and the sun.
        ///
        /// A cell the map never saw answers true: an unbuilt map, or a cell added since the last
        /// build, means "not known to be shadowed", and the cheap model's answer is the one to fall
        /// back to. Reporting a shadow the grid does not have would cool a block that is standing
        /// in full sunlight.
        /// </summary>
        public bool IsLit(Vector3I cell)
        {
            if (!IsBuilt) return true;

            float depth;
            if (!leading.TryGetValue(Key(cell), out depth)) return true;

            return Depth(cell) >= depth - DepthTolerance;
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
                if (IsLit(cells[i])) lit++;
            }

            return lit / (float)cells.Length;
        }

        private void Insert(Vector3I cell)
        {
            long key = Key(cell);
            float depth = Depth(cell);

            CellCount++;

            float existing;
            if (!leading.TryGetValue(key, out existing) || depth > existing)
            {
                leading[key] = depth;
            }
        }

        /// <summary>Distance along the sun axis. Larger is closer to the sun.</summary>
        private float Depth(Vector3I cell)
        {
            return (cell.X * sun.X) + (cell.Y * sun.Y) + (cell.Z * sun.Z);
        }

        /// <summary>The column a cell falls in: its position on the plane facing the sun.</summary>
        private long Key(Vector3I cell)
        {
            float u = (cell.X * right.X) + (cell.Y * right.Y) + (cell.Z * right.Z);
            float v = (cell.X * up.X) + (cell.Y * up.Y) + (cell.Z * up.Z);

            // Rounding rather than flooring so a cell sits in the column its centre is nearest to,
            // which keeps a flat wall square to the sun in one column per cell instead of two.
            long a = (long)Math.Round(u);
            long b = (long)Math.Round(v);

            return (a << 32) ^ (b & 0xFFFFFFFFL);
        }

        /// <summary>Any two axes perpendicular to the sun. Which two does not matter.</summary>
        private static void Basis(ref Vector3 direction, out Vector3 right, out Vector3 up)
        {
            Vector3 seed = Math.Abs(direction.X) < 0.9f ? Vector3.Right : Vector3.Up;

            right = Vector3.Normalize(Vector3.Cross(seed, direction));
            up = Vector3.Cross(direction, right);
        }
    }
}
