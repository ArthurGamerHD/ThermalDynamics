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
/// <summary>WideKeyRoundTripsAcrossTheWholeUsefulRange operation.</summary>
        public void WideKeyRoundTripsAcrossTheWholeUsefulRange()
        {
            int[] coordinates = { -100000, -4096, -513, -512, -1, 0, 1, 511, 512, 4096, 100000 };

            foreach (int x in coordinates)
            {
                foreach (int y in coordinates)
                {
                    foreach (int z in coordinates)
                    {
/// <summary>Vector3I operation.</summary>
                        Vector3I position = new Vector3I(x, y, z);
                        Assert.Equal(position, GridMath.FromKey(GridMath.Key(position)));
                    }
                }
            }
        }

        [Fact]
/// <summary>WideKeyIsUniqueForNearbyPositions operation.</summary>
        public void WideKeyIsUniqueForNearbyPositions()
        {
/// <summary>HashSet operation.</summary>
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
/// <summary>LegacyKeyRoundTripsInsideItsSafeRange operation.</summary>
        public void LegacyKeyRoundTripsInsideItsSafeRange()
        {
            int[] coordinates = { -511, -256, -1, 0, 1, 256, 511 };

            foreach (int x in coordinates)
            {
                foreach (int y in coordinates)
                {
                    foreach (int z in coordinates)
                    {
/// <summary>Vector3I operation.</summary>
                        Vector3I position = new Vector3I(x, y, z);
                        Assert.True(GridMath.IsLegacySafe(position));
                        Assert.Equal(position, GridMath.LegacyUnflatten(GridMath.LegacyFlatten(position)));
                    }
                }
            }
        }

        [Fact]
/// <summary>LegacyKeyAliasesOutsideItsSafeRange operation.</summary>
        public void LegacyKeyAliasesOutsideItsSafeRange()
        {
/// <summary>Vector3I operation.</summary>
            Vector3I inRange = new Vector3I(0, 0, 0);
/// <summary>Vector3I operation.</summary>
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
/// <summary>LargestFaceAreaIsTheProductOfTheTwoLargestSides operation.</summary>
        public void LargestFaceAreaIsTheProductOfTheTwoLargestSides(int x, int y, int z, int expected)
        {
            Assert.Equal(expected, GridMath.LargestFaceArea(new Vector3I(x, y, z)));
        }

        [Fact]
/// <summary>LargestFaceAreaFixesTheOriginalOrderDependentResult operation.</summary>
        public void LargestFaceAreaFixesTheOriginalOrderDependentResult()
        {
            Assert.Equal(10, GridMath.LargestFaceArea(new Vector3I(1, 5, 2)));
            Assert.Equal(5, LegacyFormulas.LargestFace(new Vector3I(1, 5, 2)));

            Assert.Equal(
                GridMath.LargestFaceArea(new Vector3I(2, 3, 4)),
                LegacyFormulas.LargestFace(new Vector3I(2, 3, 4)));
        }

        [Fact]
/// <summary>ContainsUsesAnExclusiveUpperBound operation.</summary>
        public void ContainsUsesAnExclusiveUpperBound()
        {
/// <summary>Vector3I operation.</summary>
            Vector3I min = new Vector3I(0, 0, 0);
/// <summary>Vector3I operation.</summary>
            Vector3I max = new Vector3I(2, 2, 2);

            Assert.True(GridMath.Contains(min, max, new Vector3I(0, 0, 0)));
            Assert.True(GridMath.Contains(min, max, new Vector3I(1, 1, 1)));
            Assert.False(GridMath.Contains(min, max, new Vector3I(2, 1, 1)));
            Assert.False(GridMath.Contains(min, max, new Vector3I(-1, 0, 0)));
        }

        [Fact]
/// <summary>CellCountMatchesTheBoxVolume operation.</summary>
        public void CellCountMatchesTheBoxVolume()
        {
            Assert.Equal(8, GridMath.CellCount(Vector3I.Zero, new Vector3I(2, 2, 2)));
            Assert.Equal(1, GridMath.CellCount(Vector3I.Zero, Vector3I.One));
            Assert.Equal(0, GridMath.CellCount(Vector3I.Zero, Vector3I.Zero));
        }

        [Fact]
/// <summary>AKeyIsLinearInTheCellSoANeighboursKeyIsAnAddition operation.</summary>
        public void AKeyIsLinearInTheCellSoANeighboursKeyIsAnAddition()
        {
            int checkedPairs = 0;
            for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            {
/// <summary>Vector3I operation.</summary>
                Vector3I cell = new Vector3I(x * 7, y * 5, z * 11);
                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I neighbour = cell + Face.Offsets[face];
                    Assert.True(GridMath.Key(neighbour) == GridMath.Key(cell) + GridMath.KeyByFace[face],
                        cell + " + " + Face.Name(face) + " keys to " + GridMath.Key(neighbour)
                        + " and by addition to " + (GridMath.Key(cell) + GridMath.KeyByFace[face]));
                    checkedPairs++;
                }
            }

            Assert.Equal(343 * 6, checkedPairs);
            Assert.Equal(Face.Count, GridMath.KeyByFace.Length);
        }
    }
}
