using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GridMathTests
    {
        [Fact]
        public void WideKeyRoundTripsAcrossTheWholeUsefulRange()
        {
            int[] coordinates = { -100000, -4096, -513, -512, -1, 0, 1, 511, 512, 4096, 100000 };

            foreach (int x in coordinates)
            {
                foreach (int y in coordinates)
                {
                    foreach (int z in coordinates)
                    {
                        Vector3I position = new Vector3I(x, y, z);
                        Assert.Equal(position, GridMath.FromKey(GridMath.Key(position)));
                    }
                }
            }
        }

        [Fact]
        public void WideKeyIsUniqueForNearbyPositions()
        {
            HashSet<long> seen = new HashSet<long>();
            for (int x = -20; x <= 20; x++)
            {
                for (int y = -20; y <= 20; y++)
                {
                    for (int z = -20; z <= 20; z++)
                    {
                        Assert.True(seen.Add(GridMath.Key(new Vector3I(x, y, z))));
                    }
                }
            }
        }

        [Fact]
        public void LegacyKeyRoundTripsInsideItsSafeRange()
        {
            int[] coordinates = { -511, -256, -1, 0, 1, 256, 511 };

            foreach (int x in coordinates)
            {
                foreach (int y in coordinates)
                {
                    foreach (int z in coordinates)
                    {
                        Vector3I position = new Vector3I(x, y, z);
                        Assert.True(GridMath.IsLegacySafe(position));
                        Assert.Equal(position, GridMath.LegacyUnflatten(GridMath.LegacyFlatten(position)));
                    }
                }
            }
        }

        /// <summary>
        /// Documents the ceiling on the original save format: X and Y must stay inside
        /// [-512, 511] or two different blocks share a key and one overwrites the other.
        /// </summary>
        [Fact]
        public void LegacyKeyAliasesOutsideItsSafeRange()
        {
            Vector3I inRange = new Vector3I(0, 0, 0);
            Vector3I outOfRange = new Vector3I(1024, -1, 0);

            Assert.False(GridMath.IsLegacySafe(outOfRange));
            Assert.Equal(GridMath.LegacyFlatten(inRange), GridMath.LegacyFlatten(outOfRange));
            Assert.NotEqual(GridMath.Key(inRange), GridMath.Key(outOfRange));
        }

        [Theory]
        [InlineData(1, 1, 1, 1)]
        [InlineData(1, 5, 2, 10)]
        [InlineData(5, 1, 2, 10)]
        [InlineData(2, 1, 5, 10)]
        [InlineData(2, 3, 4, 12)]
        [InlineData(5, 1, 1, 5)]
        [InlineData(3, 3, 3, 9)]
        public void LargestFaceAreaIsTheProductOfTheTwoLargestSides(int x, int y, int z, int expected)
        {
            Assert.Equal(expected, GridMath.LargestFaceArea(new Vector3I(x, y, z)));
        }

        /// <summary>
        /// The original implementation seeded both running maxima at 1 and only updated the
        /// runner-up when a new maximum arrived, so it under-reported whenever the largest side
        /// came before a middle one. 1x5x2 is the smallest shipped block shape that hits it —
        /// the radiator.
        /// </summary>
        [Fact]
        public void LargestFaceAreaFixesTheOriginalOrderDependentResult()
        {
            Assert.Equal(10, GridMath.LargestFaceArea(new Vector3I(1, 5, 2)));
            Assert.Equal(5, LegacyFormulas.LargestFace(new Vector3I(1, 5, 2)));

            // ascending input happened to work, which is why this survived
            Assert.Equal(
                GridMath.LargestFaceArea(new Vector3I(2, 3, 4)),
                LegacyFormulas.LargestFace(new Vector3I(2, 3, 4)));
        }

        [Fact]
        public void ContainsUsesAnExclusiveUpperBound()
        {
            Vector3I min = new Vector3I(0, 0, 0);
            Vector3I max = new Vector3I(2, 2, 2);

            Assert.True(GridMath.Contains(min, max, new Vector3I(0, 0, 0)));
            Assert.True(GridMath.Contains(min, max, new Vector3I(1, 1, 1)));
            Assert.False(GridMath.Contains(min, max, new Vector3I(2, 1, 1)));
            Assert.False(GridMath.Contains(min, max, new Vector3I(-1, 0, 0)));
        }

        [Fact]
        public void CellCountMatchesTheBoxVolume()
        {
            Assert.Equal(8, GridMath.CellCount(Vector3I.Zero, new Vector3I(2, 2, 2)));
            Assert.Equal(1, GridMath.CellCount(Vector3I.Zero, Vector3I.One));
            Assert.Equal(0, GridMath.CellCount(Vector3I.Zero, Vector3I.Zero));
        }
    }
}
