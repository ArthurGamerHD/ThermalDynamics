using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class Face
    {
        public const int Forward = 0;
        public const int Left = 1;
        public const int Up = 2;
        public const int Down = 3;
        public const int Right = 4;
        public const int Backward = 5;
        public const int Count = 6;

        public static readonly Vector3I[] Offsets = new Vector3I[]
        {
            Vector3I.Forward,
            Vector3I.Left,
            Vector3I.Up,
            Vector3I.Down,
            Vector3I.Right,
            Vector3I.Backward,
        };

        public static readonly Vector3[] Normals = new Vector3[]
        {
/// <summary>Vector3 operation.</summary>
            new Vector3(0, 0, -1),
/// <summary>Vector3 operation.</summary>
            new Vector3(-1, 0, 0),
/// <summary>Vector3 operation.</summary>
            new Vector3(0, 1, 0),
/// <summary>Vector3 operation.</summary>
            new Vector3(0, -1, 0),
/// <summary>Vector3 operation.</summary>
            new Vector3(1, 0, 0),
/// <summary>Vector3 operation.</summary>
            new Vector3(0, 0, 1),
        };

        private static readonly string[] NamesByIndex = new string[]
        {
            "Forward", "Left", "Up", "Down", "Right", "Backward"
        };

/// <summary>Opposite operation.</summary>
        public static int Opposite(int face)
        {
            return 5 - face;
        }

/// <summary>Name operation.</summary>
        public static string Name(int face)
        {
            return (face >= 0 && face < Count) ? NamesByIndex[face] : "NotAFace";
        }

/// <summary>IndexOf operation.</summary>
        public static int IndexOf(Vector3I offset)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Offsets[i] == offset) return i;
            }
            return -1;
        }

/// <summary>Axis operation.</summary>
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
