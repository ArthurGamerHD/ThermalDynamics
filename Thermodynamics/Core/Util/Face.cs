using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The six block faces, in the one canonical order used everywhere in the simulation.
    /// The ordering matters: <see cref="Opposite"/> relies on index + opposite == 5, and the
    /// surface bit flags are packed in this order.
    /// </summary>
    public static class Face
    {
        public const int Forward = 0;
        public const int Left = 1;
        public const int Up = 2;
        public const int Down = 3;
        public const int Right = 4;
        public const int Backward = 5;
        public const int Count = 6;

        /// <summary>Unit offsets, indexed by face.</summary>
        public static readonly Vector3I[] Offsets = new Vector3I[]
        {
            Vector3I.Forward,
            Vector3I.Left,
            Vector3I.Up,
            Vector3I.Down,
            Vector3I.Right,
            Vector3I.Backward,
        };

        /// <summary>Float unit offsets, indexed by face.</summary>
        public static readonly Vector3[] Normals = new Vector3[]
        {
            new Vector3(0, 0, -1),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 1),
        };

        private static readonly string[] NamesByIndex = new string[]
        {
            "Forward", "Left", "Up", "Down", "Right", "Backward"
        };

        /// <summary>The face pointing the other way.</summary>
        public static int Opposite(int face)
        {
            return 5 - face;
        }

        public static string Name(int face)
        {
            return (face >= 0 && face < Count) ? NamesByIndex[face] : "NotAFace";
        }

        /// <summary>
        /// Face index for a single-axis unit offset, or -1 when the vector is not one.
        /// </summary>
        public static int IndexOf(Vector3I offset)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Offsets[i] == offset) return i;
            }
            return -1;
        }

        /// <summary>
        /// The axis this face lies on: 0 = X, 1 = Y, 2 = Z.
        /// </summary>
        public static int Axis(int face)
        {
            switch (face)
            {
                case Left:
                case Right:
                    return 0;
                case Up:
                case Down:
                    return 1;
                default:
                    return 2;
            }
        }
    }
}
