using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct BlockOrientation : IEquatable<BlockOrientation>
    {
        public Base6Directions.Direction Forward;
        public Base6Directions.Direction Up;


        public BlockOrientation(Base6Directions.Direction forward, Base6Directions.Direction up)
        {
            Forward = forward;
            Up = up;
        }

        public static BlockOrientation Identity
        {

            get { return new BlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up); }
        }


        public Matrix GetMatrix()
        {
            return Matrix.CreateWorld(
                Vector3.Zero,
                Base6Directions.GetVector(Forward),
                Base6Directions.GetVector(Up));
        }

        private const int Slots = 36;

        private static readonly Vector3I[] rotatedAxes = new Vector3I[Slots * 3];
        private static readonly int[] rotatedFaces = new int[Slots * Face.Count];

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


        public Vector3I RotateByMatrix(Vector3I local)
        {

            Matrix m = GetMatrix();
            Vector3I result;
            Vector3I.Transform(ref local, ref m, out result);
            return result;
        }


        public Vector3I UnrotateByMatrix(Vector3I grid)
        {

            Matrix m = GetMatrix();
            m.TransposeRotationInPlace();
            Vector3I result;
            Vector3I.Transform(ref grid, ref m, out result);
            return result;
        }


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


        public Vector3I Unrotate(Vector3I grid)
        {
            if (!IsLegal) return UnrotateByMatrix(grid);

            int b = Slot * 3;
            Vector3I x = rotatedAxes[b];
            Vector3I y = rotatedAxes[b + 1];
            Vector3I z = rotatedAxes[b + 2];

            return new Vector3I(
                (grid.X * x.X) + (grid.Y * x.Y) + (grid.Z * x.Z),
                (grid.X * y.X) + (grid.Y * y.Y) + (grid.Z * y.Z),
                (grid.X * z.X) + (grid.Y * z.Y) + (grid.Z * z.Z));
        }


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
