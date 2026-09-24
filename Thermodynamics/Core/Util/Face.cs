using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Defines the six faces of a cubic block and provides face-related utilities.
    /// Used throughout the thermal simulation for surface calculations, heat transfer,
    /// and neighbor lookups. Follows the Space Engineers block coordinate system.
    /// </summary>
    public static class Face
    {
        /// <summary>
        /// Forward face index (0). Points in the +Z direction (0, 0, 1).
        /// Also known as the "back" face of a block in some coordinate conventions.
        /// </summary>
        public const int Forward = 0;

        /// <summary>
        /// Left face index (1). Points in the -X direction (-1, 0, 0).
        /// </summary>
        public const int Left = 1;

        /// <summary>
        /// Up face index (2). Points in the +Y direction (0, 1, 0).
        /// </summary>
        public const int Up = 2;

        /// <summary>
        /// Down face index (3). Points in the -Y direction (0, -1, 0).
        /// </summary>
        public const int Down = 3;

        /// <summary>
        /// Right face index (4). Points in the +X direction (1, 0, 0).
        /// </summary>
        public const int Right = 4;

        /// <summary>
        /// Backward face index (5). Points in the -Z direction (0, 0, -1).
        /// Also known as the "front" face of a block in some coordinate conventions.
        /// </summary>
        public const int Backward = 5;

        /// <summary>
        /// Total number of faces on a cubic block (6).
        /// Used for array sizing and boundary checking.
        /// </summary>
        public const int Count = 6;

        /// <summary>
        /// Unit direction vectors for each face, indexed by Face constant.
        /// Each vector points outward from the block surface.
        /// </summary>
        public static readonly Vector3I[] Offsets = new Vector3I[]
        {
            Vector3I.Forward,   // 0: Forward (+Z)
            Vector3I.Left,      // 1: Left (-X)
            Vector3I.Up,        // 2: Up (+Y)
            Vector3I.Down,      // 3: Down (-Y)
            Vector3I.Right,     // 4: Right (+X)
            Vector3I.Backward,  // 5: Backward (-Z)
        };

        /// <summary>
        /// Normalized direction vectors for each face, indexed by Face constant.
        /// Same as Offsets but as Vector3 (float) instead of Vector3I (int).
        /// </summary>
        public static readonly Vector3[] Normals = new Vector3[]
        {
            new Vector3(0, 0, -1),  // 0: Forward (-Z in right-handed)
            new Vector3(-1, 0, 0),  // 1: Left (-X)
            new Vector3(0, 1, 0),   // 2: Up (+Y)
            new Vector3(0, -1, 0),  // 3: Down (-Y)
            new Vector3(1, 0, 0),   // 4: Right (+X)
            new Vector3(0, 0, 1),   // 5: Backward (+Z)
        };

        /// <summary>
        /// Human-readable names for each face, indexed by Face constant.
        /// Used for debugging and diagnostic output.
        /// </summary>
        private static readonly string[] NamesByIndex = new string[]
        {
            "Forward", "Left", "Up", "Down", "Right", "Backward"
        };


        /// <summary>
        /// Returns the opposite face index (the face pointing in the reverse direction).
        /// Face pairs: (0↔5), (1↔4), (2↔3)
        /// </summary>
        /// <param name="face">A face index (0-5).</param>
        /// <returns>The index of the opposite face.</returns>
        public static int Opposite(int face)
        {
            return 5 - face;
        }


        /// <summary>
        /// Gets the human-readable name of a face by its index.
        /// </summary>
        /// <param name="face">A face index (0-5).</param>
        /// <returns>The face name, or "NotAFace" if index is invalid.</returns>
        public static string Name(int face)
        {
            return (face >= 0 && face < Count) ? NamesByIndex[face] : "NotAFace";
        }


        /// <summary>
        /// Finds the face index that matches a given offset direction.
        /// </summary>
        /// <param name="offset">A direction vector (should be one of the Face offsets).</param>
        /// <returns>The face index (0-5), or -1 if not a face direction.</returns>
        public static int IndexOf(Vector3I offset)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Offsets[i] == offset) return i;
            }
            return -1;
        }


        /// <summary>
        /// Gets the axis (0=X, 1=Y, 2=Z) that a face is perpendicular to.
        /// </summary>
        /// <param name="face">A face index (0-5).</param>
        /// <returns>The axis index (0 for X, 1 for Y, 2 for Z).</returns>
        public static int Axis(int face)
        {
            switch (face)
            {
                case Left:
                case Right:
                    return 0;  // X axis
                case Up:
                case Down:
                    return 1;  // Y axis
                default:
                    return 2;  // Z axis (Forward/Backward)
            }
        }
    }
}
