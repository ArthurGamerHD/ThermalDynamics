using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// One grid's shadow falling on another's faces.
    ///
    /// The interesting part is not that a shadow exists — the whole-grid occlusion ray already knew
    /// that — but where it lands. A station overhead should darken the hull under it and nothing
    /// else, and that means reasoning about two cell lattices that share no axes, no origin and no
    /// scale. The ray is carried into the occluder's frame and walked there, so these check the
    /// transform as much as the walk: offset, rotation, and the shadow moving when the occluder
    /// does.
    /// </summary>
    public class GridShadowTests
    {
        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

        /// <summary>
        /// An occluder placed a given number of cells away, axis-aligned, same cell size — the
        /// ordinary case of one ship parked over another.
        /// </summary>
        private static SunShadowMap.Occluder Above(GridModel model, Vector3D offsetInCells)
        {
            return new SunShadowMap.Occluder
            {
                Model = model,
                Id = 1,

                // Cells of the shaded grid to cells of the occluder: shift by where it sits.
                ToOccluder = MatrixD.CreateTranslation(-offsetInCells),
            };
        }

        private static SunShadowMap Build(GridModel grid, Vector3 sun, params SunShadowMap.Occluder[] others)
        {
            SunShadowMap map = new SunShadowMap();
            map.Restart(grid, sun, others);
            map.RunToCompletion();
            return map;
        }

        [Fact]
        public void APlateOverheadShadesTheFacesUnderItAndNoOthers()
        {
            // A row of nine blocks, and a three-block plate hanging four cells above its middle.
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-1, 0, 0), new Vector3I(2, 1, 1));

            SunShadowMap map = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(0, 4, 0)));

            // Directly under the plate: dark.
            for (int x = -1; x <= 1; x++)
            {
                Assert.False(map.IsFaceLit(new Vector3I(x, 0, 0), Face.Up),
                    "cell " + x + " is under the plate and should be shaded");
            }

            // Beside it: lit. A shadow that swallowed the whole grid would be no better than the
            // ray it replaced.
            for (int x = -4; x <= 4; x++)
            {
                if (x >= -1 && x <= 1) continue;
                Assert.True(map.IsFaceLit(new Vector3I(x, 0, 0), Face.Up),
                    "cell " + x + " is clear of the plate and should be lit");
            }
        }

        [Fact]
        public void TheShadowMovesWithTheOccluder()
        {
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap left = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(-3, 4, 0)));
            Assert.False(left.IsFaceLit(new Vector3I(-3, 0, 0), Face.Up));
            Assert.True(left.IsFaceLit(new Vector3I(3, 0, 0), Face.Up));

            SunShadowMap right = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(3, 4, 0)));
            Assert.True(right.IsFaceLit(new Vector3I(-3, 0, 0), Face.Up));
            Assert.False(right.IsFaceLit(new Vector3I(3, 0, 0), Face.Up));
        }

        [Fact]
        public void AnOccluderTurnedFortyFiveDegreesStillShadowsWhatItCovers()
        {
            // Two grids never share an orientation in practice. The plate here is rotated about the
            // sun axis, so its cells line up with nothing in the shaded grid's lattice.
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-4, 0, 0), new Vector3I(5, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, -2), new Vector3I(3, 1, 3));

            MatrixD rotation = MatrixD.CreateRotationY(MathHelper.ToRadians(45));
            MatrixD toOccluder = MatrixD.CreateTranslation(new Vector3D(0, -4, 0)) * rotation;

            SunShadowMap map = Build(floor.Grid, Up, new SunShadowMap.Occluder
            {
                Model = plate.Grid,
                Id = 2,
                ToOccluder = toOccluder,
            });

            // The plate is five cells across and turned, so it still covers the middle of the row.
            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Up));

            // And it is not infinite: the far end of the row is clear of it.
            Assert.True(map.IsFaceLit(new Vector3I(4, 0, 0), Face.Up));
        }

        [Fact]
        public void AnOccluderBehindTheGridDoesNotShadowIt()
        {
            // The plate is on the far side from the sun. Nothing it does can matter.
            GridBuilder floor = GridBuilder.Large();
            floor.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, 0), new Vector3I(3, 1, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Fill(Catalog.LightArmor(), new Vector3I(-2, 0, 0), new Vector3I(3, 1, 1));

            SunShadowMap map = Build(floor.Grid, Up, Above(plate.Grid, new Vector3D(0, -4, 0)));

            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Up));
        }

        [Fact]
        public void SelfShadowAndGridShadowBothApply()
        {
            // A two-storey hull with a plate over one end: the lower deck is shaded by its own hull,
            // and the upper deck by the neighbour. Neither test may cancel the other.
            GridBuilder ship = GridBuilder.Large();
            ship.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 2, 1));

            GridBuilder plate = GridBuilder.Large();
            plate.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(ship.Grid, Up, Above(plate.Grid, new Vector3D(3, 5, 0)));

            // Own hull above it.
            Assert.False(map.IsFaceLit(new Vector3I(0, 0, 0), Face.Up));

            // Top deck, out from under the plate.
            Assert.True(map.IsFaceLit(new Vector3I(0, 1, 0), Face.Up));

            // Top deck, under the plate.
            Assert.False(map.IsFaceLit(new Vector3I(3, 1, 0), Face.Up));
        }

        [Fact]
        public void NoOccludersIsTheSameAnswerAsBefore()
        {
            GridBuilder ship = GridBuilder.Large();
            ship.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 2, 1));

            SunShadowMap without = new SunShadowMap();
            without.Restart(ship.Grid, Up);
            without.RunToCompletion();

            SunShadowMap empty = Build(ship.Grid, Up);

            Assert.Equal(0, empty.OccluderCount);

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I cell = new Vector3I(x, y, 0);
                        Assert.Equal(without.IsFaceLit(cell, face), empty.IsFaceLit(cell, face));
                    }
                }
            }
        }

        [Fact]
        public void AnOccluderWithNoModelIsIgnoredRatherThanCrashing()
        {
            GridBuilder ship = GridBuilder.Large();
            ship.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(ship.Grid, Up, new SunShadowMap.Occluder { Model = null, Id = 3 });

            Assert.Equal(0, map.OccluderCount);
            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Up));
        }
    }
}
