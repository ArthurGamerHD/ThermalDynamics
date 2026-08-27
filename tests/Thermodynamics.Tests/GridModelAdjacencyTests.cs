using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// `GridModel.GetNeighbours` answers a one-cell block with six probes and no deduplication, and
    /// is held to the boundary-walking query it short-cuts — the same neighbours in the same order,
    /// for every block of a census hull and of a grid that mixes one-cell blocks with bars and cubes
    /// (`D8`). Order matters here beyond correctness: the link list is built in this order, and the
    /// conduction sum accumulates in link order, so a permutation would move the last bit of a
    /// temperature.
    /// </summary>
    public class GridModelAdjacencyTests
    {
        private static void AssertSame(GridModel grid, string what)
        {
            List<BlockInstance> fast = new List<BlockInstance>();
            List<BlockInstance> walked = new List<BlockInstance>();
            int answers = 0;
            int oneCell = 0;

            for (int b = 0; b < grid.Blocks.Count; b++)
            {
                BlockInstance block = grid.Blocks[b];
                fast.Clear(); walked.Clear();
                grid.GetNeighbours(block, fast);
                grid.GetNeighboursWalkingTheBoundary(block, walked);

                Assert.True(fast.Count == walked.Count,
                    what + ": " + block + " has " + fast.Count + " neighbours by the short path and " + walked.Count + " by the walk");
                for (int i = 0; i < fast.Count; i++)
                {
                    Assert.True(ReferenceEquals(fast[i], walked[i]),
                        what + ": " + block + " neighbour " + i + " is " + fast[i] + " by the short path and " + walked[i] + " by the walk");
                }
                answers += fast.Count;
                if (block.CellCount == 1) oneCell++;
            }

            Assert.True(answers > 0, what + ": no block has a neighbour, so nothing was compared");
            Assert.True(oneCell > 0, what + ": no one-cell block, so the short path never ran");
        }

        /// <summary>
        /// The face the walk reports for a neighbour is the face `ConductionBuilder.ContactFace`
        /// works out from the two boxes — which is what lets the link builder take the walk's
        /// answer instead of asking. A box touches another on at most one face, so there is one
        /// right answer; this holds them equal for every neighbour of every block of a census hull
        /// and of the mixed grid, on both the one-cell path and the boundary walk.
        /// </summary>
        [Fact]
        public void TheWalkReportsTheFaceContactFaceWouldFind()
        {
            int compared = 0;
            int multiCell = 0;

            foreach (GridModel grid in new[] { CensusGrid(), MixedGrid() })
            {
                List<BlockInstance> neighbours = new List<BlockInstance>();
                List<int> faces = new List<int>();

                for (int b = 0; b < grid.Blocks.Count; b++)
                {
                    BlockInstance block = grid.Blocks[b];
                    neighbours.Clear(); faces.Clear();
                    grid.GetNeighbours(block, neighbours, faces);

                    Assert.Equal(neighbours.Count, faces.Count);
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        int expected = ConductionBuilder.ContactFace(block, neighbours[i]);
                        Assert.True(expected == faces[i],
                            block + " meets " + neighbours[i] + " across " + Face.Name(faces[i])
                            + " by the walk and " + Face.Name(expected) + " by the boxes");
                        compared++;
                    }
                    if (block.CellCount > 1) multiCell++;
                }
            }

            Assert.True(compared > 0, "no neighbour was compared");
            Assert.True(multiCell > 0, "no multi-cell block across either fixture, so the boundary walk's face was never checked");
        }

        private static GridModel CensusGrid()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", 2000));
            return builder.Grid;
        }

        [Fact]
        public void ACensusHullAnswersTheSameNeighboursByBothPaths()
        {
            AssertSame(CensusGrid(), "census hull");
        }

        /// <summary>
        /// Bars and a cube beside unit blocks, so a one-cell block has multi-cell neighbours on
        /// several faces and a multi-cell block has many one-cell neighbours on one face — both of
        /// the shapes the deduplication exists for, on the side of the query that skips it.
        /// </summary>
        private static GridModel MixedGrid()
        {
            GridBuilder builder = MixedBuilder();
            return builder.Grid;
        }

        private static GridBuilder MixedBuilder()
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel unit = Catalog.LightArmor();
            BlockModel bar = Catalog.LightArmorBar(3);
            BlockModel cube = Catalog.LightArmorCube(3);

            builder.Place(cube, new Vector3I(0, 0, 0));
            builder.Place(bar, new Vector3I(3, 0, 0));
            builder.Place(bar, new Vector3I(3, 1, 0), new BlockOrientation(Base6Directions.Direction.Up, Base6Directions.Direction.Forward));
            for (int x = -1; x <= 6; x++)
            for (int z = -1; z <= 3; z++)
            {
                if (builder.Grid.IsOccupied(new Vector3I(x, 3, z))) continue;
                builder.Place(unit, new Vector3I(x, 3, z));
            }
            for (int y = 0; y < 3; y++) builder.Place(unit, new Vector3I(-1, y, 1));

            return builder;
        }

        [Fact]
        public void AMixedGridAnswersTheSameNeighboursByBothPaths()
        {
            GridBuilder builder = MixedBuilder();

            int multi = 0;
            for (int b = 0; b < builder.Grid.Blocks.Count; b++) if (builder.Grid.Blocks[b].CellCount > 1) multi++;
            Assert.True(multi >= 3, "the mixed grid holds " + multi + " multi-cell blocks");

            AssertSame(builder.Grid, "mixed grid");
        }
    }
}
