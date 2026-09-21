using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GridShadowTests
    {
/// <summary>Vector3 operation.</summary>
        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

/// <summary>Above operation.</summary>
        private static SunShadowMap.Occluder Above(GridModel model, Vector3D offsetInCells)
        {
            return new SunShadowMap.Occluder
            {
                Model = model,
                Id = 1,

                ToOccluder = MatrixD.CreateTranslation(-offsetInCells),
            };
        }

/// <summary>Builds the API method table.</summary>
        private static SunShadowMap Build(GridModel grid, Vector3 sun, params SunShadowMap.Occluder[] others)
        {
/// <summary>SunShadowMap operation.</summary>
            SunShadowMap map = new SunShadowMap();
            map.Restart(grid, sun, others);
            map.RunToCompletion();
            return map;
        }

        [Fact]
/// <summary>APlateOverheadShadesTheFacesUnderItAndNoOthers operation.</summary>
        public void APlateOverheadShadesTheFacesUnderItAndNoOthers()
        {
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-1, 0, 0), new Vector3I(2, 1, 1));

/// <summary>Builds the method table.</summary>
            SunShadowMap map = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(0, 4, 0)));

            for (int x = -1; x <= 1; x++)
            {
                Assert.False(map.IsFaceLit(new Vector3I(x, 0, 0), Face.Up),
                    "cell " + x + " is under the plate and should be shaded");
            }

            for (int x = -4; x <= 4; x++)
            {
                if (x >= -1 && x <= 1) continue;
                Assert.True(map.IsFaceLit(new Vector3I(x, 0, 0), Face.Up),
                    "cell " + x + " is clear of the plate and should be lit");
            }
        }

        [Fact]
/// <summary>TheShadowMovesWithTheOccluder operation.</summary>
        public void TheShadowMovesWithTheOccluder()
        {
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Place(Catalog.LightArmor(), Vector3I.Zero);

/// <summary>Builds the method table.</summary>
            SunShadowMap left = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(-3, 4, 0)));
            Assert.False(left.IsFaceLit(new Vector3I(-3, 0, 0), Face.Up));
            Assert.True(left.IsFaceLit(new Vector3I(3, 0, 0), Face.Up));

/// <summary>Builds the method table.</summary>
            SunShadowMap right = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(3, 4, 0)));
            Assert.True(right.IsFaceLit(new Vector3I(-3, 0, 0), Face.Up));
            Assert.False(right.IsFaceLit(new Vector3I(3, 0, 0), Face.Up));
        }

        [Fact]
/// <summary>AnOccluderTurnedFortyFiveDegreesStillShadowsWhatItCovers operation.</summary>
        public void AnOccluderTurnedFortyFiveDegreesStillShadowsWhatItCovers()
        {
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, -2), new Vector3I(3, 1, 3));

            MatrixD rotation = MatrixD.CreateRotationY(MathHelper.ToRadians(45));
            MatrixD toOccluder = MatrixD.CreateTranslation(new Vector3D(0, -4, 0)) * rotation;

/// <summary>Builds the method table.</summary>
            SunShadowMap map = Build(floor.Grid, Up, new SunShadowMap.Occluder
            {
                Model = plate.Grid,
                Id = 2,
                ToOccluder = toOccluder,
            });

            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Up));

            Assert.True(map.IsFaceLit(new Vector3I(4, 0, 0), Face.Up));
        }

        [Fact]
/// <summary>AnOccluderBehindTheGridDoesNotShadowIt operation.</summary>
        public void AnOccluderBehindTheGridDoesNotShadowIt()
        {
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, 0), new Vector3I(3, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, 0), new Vector3I(3, 1, 1));

/// <summary>Builds the method table.</summary>
            SunShadowMap map = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(0, -4, 0)));

            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Up));
        }

        [Fact]
/// <summary>SelfShadowAndGridShadowBothApply operation.</summary>
        public void SelfShadowAndGridShadowBothApply()
        {
            GridBuilder ship = GridBuilder.Large();
            ship.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 2, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Place(Catalog.LightArmor(), Vector3I.Zero);

/// <summary>Builds the method table.</summary>
            SunShadowMap map = Build(ship.Grid, Up, Above(plate.Grid, new Vector3D(3, 5, 0)));

            Assert.False(map.IsFaceLit(new Vector3I(0, 0, 0), Face.Up));

            Assert.True(map.IsFaceLit(new Vector3I(0, 1, 0), Face.Up));

            Assert.False(map.IsFaceLit(new Vector3I(3, 1, 0), Face.Up));
        }

        [Fact]
/// <summary>NoOccludersIsTheSameAnswerAsBefore operation.</summary>
        public void NoOccludersIsTheSameAnswerAsBefore()
        {
            GridBuilder ship = GridBuilder.Large();
            ship.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 2, 1));

/// <summary>SunShadowMap operation.</summary>
            SunShadowMap without = new SunShadowMap();
            without.Restart(ship.Grid, Up);
            without.RunToCompletion();

/// <summary>Builds the method table.</summary>
            SunShadowMap empty = Build(ship.Grid, Up);

            Assert.Equal(0, empty.OccluderCount);

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
/// <summary>Vector3I operation.</summary>
                        Vector3I cell = new Vector3I(x, y, 0);
                        Assert.Equal(without.IsFaceLit(cell, face), empty.IsFaceLit(cell, face));
                    }
                }
            }
        }

        [Fact]
/// <summary>AnOccluderWithNoModelIsIgnoredRatherThanCrashing operation.</summary>
        public void AnOccluderWithNoModelIsIgnoredRatherThanCrashing()
        {
            GridBuilder ship = GridBuilder.Large();
            ship.Place(Catalog.LightArmor(), Vector3I.Zero);

/// <summary>Builds the method table.</summary>
            SunShadowMap map = Build(ship.Grid, Up, new SunShadowMap.Occluder { Model = null, Id = 3 });

            Assert.Equal(0, map.OccluderCount);
            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Up));
        }
    }
}
