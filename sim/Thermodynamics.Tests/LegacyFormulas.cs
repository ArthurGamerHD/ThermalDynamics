using System;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The original mod's formulas, reproduced verbatim so tests can demonstrate exactly how the
    /// rewritten model differs. Nothing in the shipping code calls any of this.
    /// </summary>
    public static class LegacyFormulas
    {
        /// <summary>
        /// <c>ToolHelper.LargestFace</c> as written: both maxima start at 1 and the runner-up is
        /// only updated when a new maximum is found.
        /// </summary>
        public static int LargestFace(Vector3I vector)
        {
            int s1 = 1;
            int s2 = 1;
            for (int i = 0; i < 3; i++)
            {
                if (vector[i] >= s1)
                {
                    s2 = s1;
                    s1 = vector[i];
                }
            }
            return s1 * s2;
        }

        /// <summary>A cell's per-neighbour conduction coefficient, as the original computed it.</summary>
        public sealed class Cell
        {
            public float Conductivity;
            public float SpecificHeat;
            public float Mass;
            public float GridSize;
            public float SurfaceAreaScaler = 1f;
            public Vector3I Extents = Vector3I.One;
            public float Temperature;

            public float Area
            {
                get { return GridSize * GridSize * SurfaceAreaScaler; }
            }

            /// <summary>C = 1 / (SpecificHeat * Mass * GridSize) * TimeScaleRatio</summary>
            public float C(float timeScaleRatio)
            {
                return (1f / (SpecificHeat * Mass * GridSize)) * timeScaleRatio;
            }

            /// <summary>k = Conductivity * (SpecificHeat * Mass * GridSize) / (5 * Area * largestFace)</summary>
            public float K()
            {
                return Conductivity * (SpecificHeat * Mass * GridSize)
                    / (5f * Area * LargestFace(Extents));
            }
        }

        /// <summary>
        /// The temperature change the original applied to <paramref name="self"/> from one
        /// neighbour, for one update.
        /// </summary>
        public static float ConductionDelta(Cell self, Cell neighbour, int touchingSurfaces, float timeScaleRatio)
        {
            float area = Math.Min(self.Area, neighbour.Area);
            float kA = self.K() * area * touchingSurfaces;
            return self.C(timeScaleRatio) * kA * (neighbour.Temperature - self.Temperature);
        }
    }
}
