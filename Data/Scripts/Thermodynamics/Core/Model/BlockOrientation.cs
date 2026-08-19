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

        /// <summary>Rotates a block-local integer direction into grid space.</summary>
        public Vector3I Rotate(Vector3I local)
        {
            Matrix m = GetMatrix();
            Vector3I result;
            Vector3I.Transform(ref local, ref m, out result);
            return result;
        }

        /// <summary>Rotates a grid-space integer direction back into block-local space.</summary>
        public Vector3I Unrotate(Vector3I grid)
        {
            Matrix m = GetMatrix();
            m.TransposeRotationInPlace();
            Vector3I result;
            Vector3I.Transform(ref grid, ref m, out result);
            return result;
        }

        /// <summary>Rotates a block-local face index into a grid-space face index.</summary>
        public int RotateFace(int localFace)
        {
            return Face.IndexOf(Rotate(Face.Offsets[localFace]));
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
