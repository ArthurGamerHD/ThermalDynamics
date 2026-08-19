using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using VRage.Noise.Combiners;
using VRageMath;

namespace Thermodynamics
{
    internal static class ToolHelper
    {
        private const int size = 1024;
        private const int sizeSquared = size * size;

        /// <summary>Flattens a grid position into a single integer key.</summary>
        public static int Flatten(this Vector3I vector) 
        {
            return (sizeSquared * vector.Z) + (size * vector.Y) + vector.X;
        }

        /// <summary>Inverse of the flatten above: recovers x, y and z from a key.</summary>
        public static Vector3I Unflatten(this Vector3I vector, long flatVector)
        {
            vector.Z = (int)(flatVector / sizeSquared);
            flatVector %= sizeSquared;
            vector.Y = (int)(flatVector / size);
            vector.X = (int)(flatVector % size);

            return vector;
        }

        /// <summary>
        /// Area, in square cells, of the largest face of a box with these cell extents:
        /// the product of the two largest dimensions.
        /// </summary>
        /// <remarks>
        /// Both running maxima are seeded from the extents and the second is updated independently of
        /// the first. Seeding at 1 and updating the second only on a new maximum returns 5 instead of
        /// 10 for a 1x5x2 shape, while producing the correct answer for ascending inputs.
        /// </remarks>
        public static int LargestFace(this Vector3I vector)
        {
            int a = Math.Max(1, vector.X);
            int b = Math.Max(1, vector.Y);
            int c = Math.Max(1, vector.Z);

            int smallest = Math.Min(a, Math.Min(b, c));
            return (a * b * c) / smallest;
        }


    }
}
