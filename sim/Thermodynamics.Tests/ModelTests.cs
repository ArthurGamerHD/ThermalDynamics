using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class BlockOrientationTests
    {
        [Fact]
        public void IdentityLeavesDirectionsAlone()
        {
            BlockOrientation identity = BlockOrientation.Identity;
            for (int face = 0; face < Face.Count; face++)
            {
                Assert.Equal(Face.Offsets[face], identity.Rotate(Face.Offsets[face]));
                Assert.Equal(face, identity.RotateFace(face));
            }
        }

        [Fact]
        public void RotateAndUnrotateAreInverses()
        {
            foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    Vector3I direction = Face.Offsets[face];
                    Assert.Equal(direction, orientation.Unrotate(orientation.Rotate(direction)));
                }
            }
        }

        [Fact]
        public void EveryOrientationPermutesTheSixFaces()
        {
            foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
            {
                HashSet<int> mapped = new HashSet<int>();
                for (int face = 0; face < Face.Count; face++)
                {
                    int rotated = orientation.RotateFace(face);
                    Assert.InRange(rotated, 0, Face.Count - 1);
                    Assert.True(mapped.Add(rotated), "orientation " + orientation + " maps two faces onto " + rotated);
                }
            }
        }

        [Fact]
        public void RotationPreservesOppositePairs()
        {
            foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    int rotated = orientation.RotateFace(face);
                    int rotatedOpposite = orientation.RotateFace(Face.Opposite(face));
                    Assert.Equal(Face.Opposite(rotated), rotatedOpposite);
                }
            }
        }

        [Fact]
        public void ThereAreExactlyTwentyFourOrientations()
        {
            int count = 0;
            foreach (BlockOrientation ignored in PipeFitter.AllOrientations()) count++;
            Assert.Equal(24, count);
        }
    }

    public class BlockInstanceTests
    {
        [Fact]
        public void SingleCellBlockOccupiesOneCell()
        {
            BlockInstance block = new BlockInstance(Catalog.LightArmor(), new Vector3I(3, 4, 5), BlockOrientation.Identity);

            Assert.Single(block.Cells);
            Assert.Equal(new Vector3I(3, 4, 5), block.Cells[0]);
            Assert.Equal(new Vector3I(4, 5, 6), block.MaxExclusive);
            Assert.Equal(Vector3I.One, block.Extents);
        }

        [Fact]
        public void MultiCellBlockOccupiesEveryCellExactlyOnce()
        {
            BlockModel model = BlockModel.Solid("Slab", new Vector3I(1, 5, 2), 900f, Catalog.DefaultThermal());
            BlockInstance block = new BlockInstance(model, Vector3I.Zero, BlockOrientation.Identity);

            Assert.Equal(10, block.CellCount);

            HashSet<Vector3I> seen = new HashSet<Vector3I>(Vector3I.Comparer);
            foreach (Vector3I cell in block.Cells)
            {
                Assert.True(seen.Add(cell));
                Assert.True(GridMath.Contains(block.Min, block.MaxExclusive, cell));
            }
        }

        [Fact]
        public void RotatedBlockStillStartsAtItsMinimumCorner()
        {
            BlockModel model = BlockModel.Solid("Slab", new Vector3I(1, 5, 2), 900f, Catalog.DefaultThermal());

            foreach (BlockOrientation orientation in PipeFitter.AllOrientations())
            {
                BlockInstance block = new BlockInstance(model, new Vector3I(10, 20, 30), orientation);

                Assert.Equal(10, block.CellCount);
                Assert.Equal(new Vector3I(10, 20, 30), block.Min);

                foreach (Vector3I cell in block.Cells)
                {
                    Assert.True(
                        GridMath.Contains(block.Min, block.MaxExclusive, cell),
                        orientation + " put cell " + cell + " outside " + block.Min + ".." + block.MaxExclusive);
                }
            }
        }

        [Fact]
        public void RotationCarriesSurfaceBitsWithIt()
        {
            // A model that only mounts on its local Up face.
            BlockModel model = BlockModel.Solid("Capped", Vector3I.One, 100f, Catalog.DefaultThermal());
            model.SetLocalSurface(Vector3I.Zero, CellSurface.WithSelfMount(0, Face.Up, true));

            BlockOrientation upsideDown = new BlockOrientation(
                Base6Directions.Direction.Forward, Base6Directions.Direction.Down);

            BlockInstance block = new BlockInstance(model, Vector3I.Zero, upsideDown);

            Assert.False(CellSurface.SelfMount(block.SelfSurfaces[0], Face.Up));
            Assert.True(CellSurface.SelfMount(block.SelfSurfaces[0], Face.Down));
        }

        [Fact]
        public void OpenModelsSealNothing()
        {
            BlockInstance grating = new BlockInstance(Catalog.Grating(), Vector3I.Zero, BlockOrientation.Identity);

            for (int face = 0; face < Face.Count; face++)
            {
                Assert.False(CellSurface.SelfAirtight(grating.SelfSurfaces[0], face));
                Assert.True(CellSurface.SelfMount(grating.SelfSurfaces[0], face));
            }
        }

        [Fact]
        public void OpeningADoorClearsItsSeal()
        {
            BlockInstance door = new BlockInstance(Catalog.AirtightDoor(), Vector3I.Zero, BlockOrientation.Identity);
            Assert.True(CellSurface.SelfAirtight(door.SelfSurfaces[0], Face.Up));

            door.IsSealedByDoorState = false;
            door.RefreshSurfaces();

            for (int face = 0; face < Face.Count; face++)
            {
                Assert.False(CellSurface.SelfAirtight(door.SelfSurfaces[0], face));
                Assert.True(CellSurface.SelfMount(door.SelfSurfaces[0], face));
            }
        }

        /// <summary>
        /// Whatever a door does when it opens, what it is <em>built</em> like does not change.
        /// The room mapper walks these, and that is what lets a door cycle cost nothing.
        /// </summary>
        [Fact]
        public void StructuralSurfacesDoNotMoveWhenADoorOpens()
        {
            BlockInstance door = new BlockInstance(Catalog.AirtightDoor(), Vector3I.Zero, BlockOrientation.Identity);
            int shut = door.StructuralSurfaces[0];

            door.IsSealedByDoorState = false;
            door.RefreshSurfaces();

            Assert.Equal(shut, door.StructuralSurfaces[0]);
            Assert.True(CellSurface.IsFullySealed(door.StructuralSurfaces[0]));
        }
    }

    public class GridModelTests
    {
        [Fact]
        public void BlocksAreFoundByAnyOfTheirCells()
        {
            GridModel grid = new GridModel(2.5f);
            BlockModel model = BlockModel.Solid("Slab", new Vector3I(1, 3, 1), 300f, Catalog.DefaultThermal());
            BlockInstance block = grid.Add(model, Vector3I.Zero);

            Assert.Same(block, grid.GetAtCell(new Vector3I(0, 0, 0)));
            Assert.Same(block, grid.GetAtCell(new Vector3I(0, 2, 0)));
            Assert.Null(grid.GetAtCell(new Vector3I(0, 3, 0)));
        }

        [Fact]
        public void OverlappingPlacementIsRejected()
        {
            GridModel grid = new GridModel(2.5f);
            grid.Add(Catalog.LightArmor(), Vector3I.Zero);

            Assert.Throws<InvalidOperationException>(() => grid.Add(Catalog.HeavyArmor(), Vector3I.Zero));
        }

        [Fact]
        public void RemovingFreesEveryCell()
        {
            GridModel grid = new GridModel(2.5f);
            BlockModel model = BlockModel.Solid("Slab", new Vector3I(1, 3, 1), 300f, Catalog.DefaultThermal());
            BlockInstance block = grid.Add(model, Vector3I.Zero);

            Assert.True(grid.Remove(block));
            Assert.False(grid.IsOccupied(new Vector3I(0, 1, 0)));
            Assert.Equal(0, grid.BlockCount);

            // the cells are free again
            grid.Add(Catalog.LightArmor(), new Vector3I(0, 1, 0));
        }

        [Fact]
        public void NeighboursAreDistinctEvenWhenTheyTouchOnManyFaces()
        {
            GridModel grid = new GridModel(2.5f);
            BlockModel wall = BlockModel.Solid("Wall", new Vector3I(1, 3, 1), 300f, Catalog.DefaultThermal());

            BlockInstance a = grid.Add(wall, Vector3I.Zero);
            BlockInstance b = grid.Add(wall, new Vector3I(1, 0, 0));

            List<BlockInstance> neighbours = grid.Neighbours(a);
            Assert.Single(neighbours);
            Assert.Same(b, neighbours[0]);
            Assert.Equal(3, grid.SharedFaceCount(a, b));
        }

        [Fact]
        public void BoundsCoverEveryOccupiedCell()
        {
            GridModel grid = new GridModel(2.5f);
            grid.Add(Catalog.LightArmor(), new Vector3I(-3, 0, 2));
            grid.Add(Catalog.LightArmor(), new Vector3I(5, 4, -1));

            Assert.Equal(new Vector3I(-3, 0, -1), grid.Min);
            Assert.Equal(new Vector3I(5, 4, 2), grid.Max);
        }
    }
}
