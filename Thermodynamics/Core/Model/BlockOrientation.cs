using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A block's axis-aligned orientation, mirroring the game's <c>MyBlockOrientation</c> so an
    /// adapter can convert with a two-field copy.
    /// </summary>
    public struct BlockOrientation : IEquatable<BlockOrientation>
    {
        public Base6Directions.Direction Forward;
        public Base6Directions.Direction Up;

        public BlockOrientation(Base6Directions.Direction forward, Base6Directions.Direction up)
        {
            Forward = forward;
            Up = up;
        }

        /// <summary>Forward = -Z, Up = +Y. The identity.</summary>
        public static BlockOrientation Identity
        {
            get { return new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up); }
        }

        /// <summary>
        /// The rotation taking a direction in block-local space to grid space. Constructed the same
        /// way the game constructs it, so the results match.
        /// </summary>
        public Matrix GetMatrix()
        {
            return Matrix.CreateWorld(
                Vector3.Zero,
                Base6Directions.GetVector(Forward),
                Base6Directions.GetVector(Up));
        }

        /// <summary>Slots in the tables below: six forward directions by six up directions.</summary>
        private const int Slots = 36;

        /// <summary>
        /// Where each of the three local axes lands in grid space, per orientation, three entries a
        /// slot; and each local face's grid face, six a slot. Built once from <see cref="GetMatrix"/>
        /// itself, so the table is the matrix path's own answer and not a second derivation of it.
        ///
        /// <para>
        /// An axis-aligned rotation is a signed permutation, so transforming an integer vector is
        /// three multiplies by ±1 and three adds — exact in either form, which is what lets the
        /// cached form replace the matrix bit for bit (`BlockOrientationCacheTests`). Building the
        /// matrix cost more than the rotation: a block instance rotated every cell and every face
        /// of every cell through a fresh one at construction, ten matrices a cell on the load path.
        /// See performance.md, Iteration 3.
        /// </para>
        /// </summary>
        private static readonly Vector3I[] rotatedAxes = new Vector3I[Slots * 3];
        private static readonly int[] rotatedFaces = new int[Slots * Face.Count];

        /// <summary>
        /// Slots whose forward and up are perpendicular. The other twelve pairs are not orientations;
        /// they keep the matrix path, whatever it answers, rather than a table entry built from it.
        /// </summary>
        private static readonly bool[] legal = new bool[Slots];

        static BlockOrientation()
        {
            for (int f = 0; f < 6; f++)
            {
                for (int u = 0; u < 6; u++)
                {
                    BlockOrientation orientation = new BlockOrientation(
                        (Base6Directions.Direction)f, (Base6Directions.Direction)u);

                    Vector3 forward = Base6Directions.GetVector(orientation.Forward);
                    Vector3 up = Base6Directions.GetVector(orientation.Up);
                    if (Math.Abs(Vector3.Dot(forward, up)) > 0.001f) continue;

                    int slot = orientation.Slot;
                    legal[slot] = true;

                    rotatedAxes[slot * 3] = orientation.RotateByMatrix(Vector3I.UnitX);
                    rotatedAxes[(slot * 3) + 1] = orientation.RotateByMatrix(Vector3I.UnitY);
                    rotatedAxes[(slot * 3) + 2] = orientation.RotateByMatrix(Vector3I.UnitZ);

                    for (int face = 0; face < Face.Count; face++)
                    {
                        rotatedFaces[(slot * Face.Count) + face] =
                            Face.IndexOf(orientation.RotateByMatrix(Face.Offsets[face]));
                    }
                }
            }
        }

        private int Slot
        {
            get { return ((int)Forward * 6) + (int)Up; }
        }

        private bool IsLegal
        {
            get
            {
                int slot = Slot;
                return slot >= 0 && slot < Slots && legal[slot];
            }
        }

        /// <summary>The matrix path, kept as the oracle the table is built from and checked against.</summary>
        public Vector3I RotateByMatrix(Vector3I local)
        {
            Matrix m = GetMatrix();
            Vector3I result;
            Vector3I.Transform(ref local, ref m, out result);
            return result;
        }

        /// <summary>The matrix path for <see cref="Unrotate"/>.</summary>
        public Vector3I UnrotateByMatrix(Vector3I grid)
        {
            Matrix m = GetMatrix();
            m.TransposeRotationInPlace();
            Vector3I result;
            Vector3I.Transform(ref grid, ref m, out result);
            return result;
        }

        /// <summary>Rotates a block-local integer direction into grid space.</summary>
        public Vector3I Rotate(Vector3I local)
        {
            if (!IsLegal) return RotateByMatrix(local);

            int b = Slot * 3;
            Vector3I x = rotatedAxes[b];
            Vector3I y = rotatedAxes[b + 1];
            Vector3I z = rotatedAxes[b + 2];

            return new Vector3I(
                (local.X * x.X) + (local.Y * y.X) + (local.Z * z.X),
                (local.X * x.Y) + (local.Y * y.Y) + (local.Z * z.Y),
                (local.X * x.Z) + (local.Y * y.Z) + (local.Z * z.Z));
        }

        /// <summary>Rotates a grid-space integer direction back into block-local space.</summary>
        public Vector3I Unrotate(Vector3I grid)
        {
            if (!IsLegal) return UnrotateByMatrix(grid);

            // The inverse of a signed permutation is its transpose: each local component is the
            // grid vector's projection onto where that local axis landed.
            int b = Slot * 3;
            Vector3I x = rotatedAxes[b];
            Vector3I y = rotatedAxes[b + 1];
            Vector3I z = rotatedAxes[b + 2];

            return new Vector3I(
                (grid.X * x.X) + (grid.Y * x.Y) + (grid.Z * x.Z),
                (grid.X * y.X) + (grid.Y * y.Y) + (grid.Z * y.Z),
                (grid.X * z.X) + (grid.Y * z.Y) + (grid.Z * z.Z));
        }

        /// <summary>Rotates a block-local face index into a grid-space face index.</summary>
        public int RotateFace(int localFace)
        {
            if (localFace < 0 || localFace >= Face.Count) return -1;
            if (!IsLegal) return Face.IndexOf(RotateByMatrix(Face.Offsets[localFace]));

            return rotatedFaces[(Slot * Face.Count) + localFace];
        }

        public bool Equals(BlockOrientation other)
        {
            return Forward == other.Forward && Up == other.Up;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockOrientation && Equals((BlockOrientation)obj);
        }

        public override int GetHashCode()
        {
            return ((int)Forward * 6) + (int)Up;
        }

        public override string ToString()
        {
            return "F:" + Forward + " U:" + Up;
        }
    }
}
