using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A block's **effective surface normal**: which way the hull faces where this block sits,
    /// smoothed over the cells around it rather than quantised to the six axis directions.
    ///
    /// <para>
    /// **This exists because a projected area is not a shape, and no sum over the six face normals
    /// can become one.** The friction term weights each exposed face by `max(0, n_f · ŵ)`, which is
    /// linear in incidence where Newtonian impact theory is cubic — and a hull built of cells has
    /// every surface element at 0° or 90°, so a 45° slope is a staircase of squares that projects
    /// onto exactly the area a flat plate would. `DragShapeTests` is the measurement.
    /// See thermal-model.md, The shape term, and backlog.md `K22`.
    /// </para>
    ///
    /// <para>
    /// **A block's own exposed faces are not enough, and that is worth stating because it is the
    /// obvious cheap answer.** The area-weighted sum of a node's exposed face normals is already
    /// computed, and on a staircase cell it gives 45° — but only where the step's two faces are
    /// both exposed *and* both weighted, and into an axial wind the tread's normal is perpendicular
    /// to the flow and drops out. The flow does not see the steps, it sees the mean surface, and a
    /// mean over one cell is the cell. So the neighbourhood has to be wider than the block.
    /// </para>
    ///
    /// <para>
    /// **The normal is a function of the hull's geometry alone**, which is what makes it
    /// affordable: it is rebuilt when blocks are added or removed, on the exposure pass that
    /// already visits every node, and *not* when the wind moves — unlike the shielding pass, whose
    /// direction turns with the ship. What a step costs is a dot product.
    /// </para>
    /// </summary>
    public static class ShapeNormal
    {
        /// <summary>
        /// Cells beyond a block's own box that the gradient reads, each way on each axis.
        ///
        /// <para>
        /// **One, which is the smallest radius that can see a slope at all.** A 3x3x3 neighbourhood
        /// straddling a 45° boundary has about half its cells occupied and all of them on one side,
        /// so the gradient lands on the diagonal. It cannot tell a 45° slope from a 27° one — that
        /// needs a wider read and costs the cube of this — and the first build is deliberately the
        /// cheap one, because the cost of the pass is what decides whether the feature can ship.
        /// </para>
        /// </summary>
        public const int Radius = 1;

        /// <summary>
        /// The outward normal at <paramref name="block"/>, or <see cref="Vector3.Zero"/> where the
        /// neighbourhood says nothing — an isolated block, or one whose surroundings are symmetric
        /// enough to cancel.
        ///
        /// <para>
        /// **Zero means *no correction*, not *no drag*.** A caller reads it as a factor of one and
        /// gets the model as it was, which is the conservative direction: an unrecognised shape is
        /// charged the full projected area rather than let off.
        /// </para>
        /// </summary>
        public static Vector3 Of(CellBitset occupancy, BlockInstance block)
        {
            return Of(occupancy, block, Radius);
        }

        /// <summary>
        /// The same at a stated radius, so the choice of radius can be measured rather than
        /// argued. The shipped path always passes <see cref="Radius"/>.
        /// </summary>
        public static Vector3 Of(CellBitset occupancy, BlockInstance block, int radius)
        {
            if (occupancy == null || block == null || radius < 1) return Vector3.Zero;

            // **A one-cell block's neighbourhood is the same 26 offsets every time**, and so are
            // their unit vectors — the block's centre is its own cell, so the offsets are the
            // integers from (-1,-1,-1) to (1,1,1) whatever the block's position. The general walk
            // recomputes 26 square roots and 26 divisions per block to arrive at constants, and a
            // census hull is mostly one-cell blocks. Same values in the same summation order, which
            // is what `ShapeNormalOneCellTests` holds it to. See performance.md.
            if (radius == 1 && block.CellCount == 1) return OneCell(occupancy, block.Min);

            return Walking(occupancy, block, radius);
        }

        /// <summary>
        /// The general walk: every cell within <paramref name="radius"/> of the block's box, which a
        /// multi-cell block needs and a one-cell block does not. Public so a test can hold the
        /// one-cell path to it.
        /// </summary>
        public static Vector3 Walking(CellBitset occupancy, BlockInstance block, int radius)
        {
            if (occupancy == null || block == null || radius < 1) return Vector3.Zero;

            Vector3I min = block.Min;
            Vector3I maxExclusive = block.MaxExclusive;

            // The block's own centre in cell coordinates, so a multi-cell block measures from its
            // middle rather than from a corner it happens to be indexed by.
            Vector3 centre = new Vector3(
                (min.X + maxExclusive.X - 1) * 0.5f,
                (min.Y + maxExclusive.Y - 1) * 0.5f,
                (min.Z + maxExclusive.Z - 1) * 0.5f);

            Vector3 sum = Vector3.Zero;

            for (int x = min.X - radius; x < maxExclusive.X + radius; x++)
            {
                for (int y = min.Y - radius; y < maxExclusive.Y + radius; y++)
                {
                    for (int z = min.Z - radius; z < maxExclusive.Z + radius; z++)
                    {
                        // The block cannot be its own neighbourhood: its cells are occupied by
                        // definition and would pull the normal toward whichever corner is furthest
                        // from the centre.
                        if (x >= min.X && x < maxExclusive.X
                            && y >= min.Y && y < maxExclusive.Y
                            && z >= min.Z && z < maxExclusive.Z)
                        {
                            continue;
                        }

                        if (!occupancy.Contains(new Vector3I(x, y, z))) continue;

                        Vector3 offset = new Vector3(x - centre.X, y - centre.Y, z - centre.Z);
                        float length = offset.Length();
                        if (length <= 0f) continue;

                        // Away from mass. Unit vectors rather than raw offsets, so a diagonal
                        // neighbour does not outweigh a face neighbour for being further off.
                        sum -= offset / length;
                    }
                }
            }

            float magnitude = sum.Length();
            if (magnitude <= Epsilon) return Vector3.Zero;

            return sum / magnitude;
        }

        /// <summary>
        /// Below this the gradient is cancellation rather than a direction — a block in open space,
        /// or one wrapped evenly on every side.
        /// </summary>
        private const float Epsilon = 1e-4f;

        /// <summary>The 26 neighbours of a one-cell block, in the order the general walk visits them.</summary>
        private static readonly Vector3I[] OneCellOffsets = BuildOneCellOffsets();

        /// <summary>Their unit vectors, which the general walk arrives at by square root each time.</summary>
        private static readonly Vector3[] OneCellUnits = BuildOneCellUnits();

        private static Vector3I[] BuildOneCellOffsets()
        {
            Vector3I[] offsets = new Vector3I[26];
            int i = 0;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        offsets[i++] = new Vector3I(x, y, z);
                    }
                }
            }

            return offsets;
        }

        private static Vector3[] BuildOneCellUnits()
        {
            Vector3I[] offsets = BuildOneCellOffsets();
            Vector3[] units = new Vector3[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                // The same two operations the general walk does, done once: a block's centre is its
                // own cell, so `cell - centre` is exactly this offset.
                Vector3 offset = new Vector3(offsets[i].X, offsets[i].Y, offsets[i].Z);
                units[i] = offset / offset.Length();
            }

            return units;
        }

        /// <summary>
        /// The one-cell case: the same sum, over a table rather than a walk. Iterated in the
        /// general walk's own order, because float addition is not associative and a different
        /// order is a different answer.
        /// </summary>
        private static Vector3 OneCell(CellBitset occupancy, Vector3I min)
        {
            Vector3 sum = Vector3.Zero;

            for (int i = 0; i < OneCellOffsets.Length; i++)
            {
                if (!occupancy.Contains(min + OneCellOffsets[i])) continue;

                sum -= OneCellUnits[i];
            }

            float magnitude = sum.Length();
            if (magnitude <= Epsilon) return Vector3.Zero;

            return sum / magnitude;
        }

        /// <summary>
        /// The Newtonian pressure factor for a surface facing <paramref name="normal"/> in a wind
        /// running along <paramref name="wind"/>, both unit vectors, and **1 where the normal is
        /// zero**.
        ///
        /// <para>
        /// `Cp = 2 sin²θ` and the projected area already carries one factor of `sinθ`, so what is
        /// left to apply to it is `sin²θ` — where `sinθ` is the *true* surface inclination rather
        /// than the cell face's. That is the whole correction.
        /// </para>
        ///
        /// <para>
        /// **Bounded to 0..1, so the term may only reduce**, which is the same bound
        /// <see cref="DragProfile"/> carries and for the same reason: a shape may say the air slips
        /// past more easily than the projection suggests, never that a hull has more surface than
        /// it has. Switching it on lowers heating and drag or leaves them alone; it cannot raise
        /// either.
        /// </para>
        /// </summary>
        public static float Factor(Vector3 normal, Vector3 wind)
        {
            if (normal.X == 0f && normal.Y == 0f && normal.Z == 0f) return 1f;

            float dot = Vector3.Dot(normal, wind);
            if (dot <= 0f) return 0f;

            return dot > 1f ? 1f : dot * dot;
        }
    }
}
