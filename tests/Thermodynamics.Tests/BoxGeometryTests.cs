using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The analytic block geometry that replaced per-cell enumeration.
    ///
    /// The cases that matter most are the ones with <em>unequal</em> block sizes. Space Engineers
    /// 1 has two block sizes and never mixes them on one grid, so every joint is between equals
    /// and a great many wrong formulas give the right answer. Space Engineers 2 ships eight block
    /// sizes on a shared lattice, so a joint between a small block and one twenty times its
    /// width is ordinary. These tests pin the behaviour for that case now, while it is still
    /// cheap to get right.
    /// </summary>
    public class BoxGeometryTests
    {
        private static Vector3I V(int x, int y, int z)
        {
            return new Vector3I(x, y, z);
        }

        // ---- intervals and touching --------------------------------------------------------

        [Fact]
        public void OverlapIsTheSharedLengthOfTwoHalfOpenIntervals()
        {
            Assert.Equal(2, BoxGeometry.Overlap(0, 4, 2, 6));
            Assert.Equal(4, BoxGeometry.Overlap(0, 4, 0, 4));
            Assert.Equal(0, BoxGeometry.Overlap(0, 4, 4, 8));   // abutting, not overlapping
            Assert.Equal(0, BoxGeometry.Overlap(0, 4, 9, 12));  // disjoint
        }

        [Fact]
        public void TwoUnitBlocksSideBySideTouchOnTheExpectedFace()
        {
            // b sits at +X of a
            int face = BoxGeometry.TouchingFace(V(0, 0, 0), V(1, 1, 1), V(1, 0, 0), V(2, 1, 1));
            Assert.Equal(Face.Right, face);

            // and the relation is mirrored from b's point of view
            int mirrored = BoxGeometry.TouchingFace(V(1, 0, 0), V(2, 1, 1), V(0, 0, 0), V(1, 1, 1));
            Assert.Equal(Face.Left, mirrored);
            Assert.Equal(Face.Opposite(face), mirrored);
        }

        [Fact]
        public void BlocksMeetingOnlyAtAnEdgeOrCornerDoNotTouch()
        {
            // share an edge: abut on X, abut on Y, so the shared area is zero
            Assert.Equal(-1, BoxGeometry.TouchingFace(V(0, 0, 0), V(1, 1, 1), V(1, 1, 0), V(2, 2, 1)));

            // share a single corner
            Assert.Equal(-1, BoxGeometry.TouchingFace(V(0, 0, 0), V(1, 1, 1), V(1, 1, 1), V(2, 2, 2)));

            // fully separated
            Assert.Equal(-1, BoxGeometry.TouchingFace(V(0, 0, 0), V(1, 1, 1), V(5, 0, 0), V(6, 1, 1)));
        }

        // ---- contact area ------------------------------------------------------------------

        [Fact]
        public void ContactAreaIsTheOverlapOfTheTwoFacesNotTheLargerOne()
        {
            // a 1x1x1 block against the face of a 3x3x1 slab: they share exactly one cell face,
            // not the slab's whole nine.
            int cells = BoxGeometry.ContactCells(
                V(0, 0, 0), V(3, 3, 1),
                V(1, 1, 1), V(2, 2, 2));

            Assert.Equal(1, cells);
        }

        [Fact]
        public void ContactAreaOfAFullyCoveredFaceIsThatWholeFace()
        {
            // two 3x3x1 slabs stacked: the whole 3x3 face is in contact
            int cells = BoxGeometry.ContactCells(
                V(0, 0, 0), V(3, 3, 1),
                V(0, 0, 1), V(3, 3, 2));

            Assert.Equal(9, cells);
        }

        [Fact]
        public void ContactAreaCountsOnlyThePartThatActuallyOverlaps()
        {
            // a 2x2 block half hanging off the edge of another
            int cells = BoxGeometry.ContactCells(
                V(0, 0, 0), V(2, 2, 1),
                V(1, 1, 1), V(3, 3, 2));

            Assert.Equal(1, cells);
        }

        [Fact]
        public void ContactAreaIsSymmetric()
        {
            int forward = BoxGeometry.ContactCells(V(0, 0, 0), V(4, 4, 4), V(1, 1, 4), V(3, 3, 6));
            int backward = BoxGeometry.ContactCells(V(1, 1, 4), V(3, 3, 6), V(0, 0, 0), V(4, 4, 4));

            Assert.Equal(4, forward);
            Assert.Equal(forward, backward);
        }

        // ---- face areas --------------------------------------------------------------------

        [Fact]
        public void FaceAreaIsTheProductOfThePerpendicularExtents()
        {
            Vector3I extents = V(2, 3, 5);

            Assert.Equal(15, BoxGeometry.FaceAreaCells(extents, Face.Right));     // Y * Z
            Assert.Equal(15, BoxGeometry.FaceAreaCells(extents, Face.Left));
            Assert.Equal(10, BoxGeometry.FaceAreaCells(extents, Face.Up));        // X * Z
            Assert.Equal(6, BoxGeometry.FaceAreaCells(extents, Face.Forward));    // X * Y
        }

        [Fact]
        public void SurfaceAreaMatchesTheSumOfTheSixFaces()
        {
            Vector3I extents = V(2, 3, 5);

            int summed = 0;
            for (int face = 0; face < Face.Count; face++)
            {
                summed += BoxGeometry.FaceAreaCells(extents, face);
            }

            Assert.Equal(summed, BoxGeometry.SurfaceAreaCells(extents));
            Assert.Equal(62, BoxGeometry.SurfaceAreaCells(extents));   // 2*(6 + 15 + 10)
        }

        [Fact]
        public void EnumeratingAFaceVisitsItsAreaAndStaysOnTheBoundary()
        {
            List<Vector3I> visited = new List<Vector3I>();
            BoxGeometry.ForEachFaceCell(V(0, 0, 0), V(2, 3, 4), Face.Right, visited.Add);

            Assert.Equal(BoxGeometry.FaceAreaCells(V(2, 3, 4), Face.Right), visited.Count);
            for (int i = 0; i < visited.Count; i++)
            {
                Assert.Equal(1, visited[i].X);   // the +X slab of a box spanning x in [0,2)
            }
        }

        // ---- conduction with unequal sizes --------------------------------------------------

        private static BlockModel Sized(string name, Vector3I size, float mass)
        {
            return BlockModel.Solid(name, size, mass, Catalog.DefaultThermal());
        }

        [Fact]
        public void ASmallBlockOnALargeOneContactsOnlyItsOwnFaceArea()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("big", V(4, 4, 4), 4000f), V(0, 0, 0));
            builder.Place(Sized("small", V(1, 1, 1), 100f), V(1, 1, 4));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            Assert.Single(simulation.Solver.Links);
            Assert.Equal(1, simulation.Solver.Links[0].ContactFaces);
        }

        [Fact]
        public void ConductanceIsIdenticalWhicheverWayRoundTheJointIsBuilt()
        {
            float forward = JointConductance(V(4, 4, 4), 4000f, V(1, 1, 1), 100f);
            float backward = JointConductance(V(1, 1, 1), 100f, V(4, 4, 4), 4000f);

            Assert.Equal(forward, backward, 4);
        }

        private static float JointConductance(Vector3I sizeA, float massA, Vector3I sizeB, float massB)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("a", sizeA, massA), V(0, 0, 0));
            builder.Place(Sized("b", sizeB, massB), V(0, 0, sizeA.Z));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            Assert.Single(simulation.Solver.Links);
            return simulation.Solver.Links[0].Conductance;
        }

        [Fact]
        public void ADeeperBlockConductsMoreSlowlyThroughTheSameContactArea()
        {
            // Same 1x1 contact face, but the second block is four cells deep instead of one.
            // Heat has further to travel, so the joint must be less conductive. The previous
            // model divided by a "largest face" heuristic and had no notion of depth at all.
            float shallow = JointConductance(V(1, 1, 1), 100f, V(1, 1, 1), 100f);
            float deep = JointConductance(V(1, 1, 1), 100f, V(1, 1, 4), 400f);

            Assert.True(deep < shallow,
                "expected a deeper block to conduct more slowly: " + deep + " vs " + shallow);
        }

        [Fact]
        public void EnergyIsConservedAcrossAJointBetweenVeryDifferentBlockSizes()
        {
            // This is the asymmetric-joint defect (M2) in its worst form: the size ratio that
            // Space Engineers 2 makes ordinary. One conductance shared by both ends means what
            // leaves one block is exactly what enters the other, whatever their sizes.
            ThermalSettings settings = new ThermalSettings
            {
                EnableEnvironment = false,
                EnableSolarHeat = false,
                EnableFriction = false,
                EnableDamage = false,
                EnableCoolantLoops = false,
            }.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("big", V(5, 5, 5), 12500f), V(0, 0, 0));
            builder.Place(Sized("small", V(1, 1, 1), 100f), V(2, 2, 5));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.Solver.GetNodeAt(V(2, 2, 5)).Temperature = 1200f;

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(500, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            Assert.Equal(before, after, Math.Abs(before) * 1e-3f);
        }

        [Fact]
        public void ALargeBlockFindsEveryNeighbourAlongItsFace()
        {
            // Nine unit blocks tiled across one face of a 3x3x1 slab. The neighbour query walks
            // only the slab's boundary, so this checks that walking the boundary does not miss
            // anything the old volume walk would have found.
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("slab", V(3, 3, 1), 900f), V(0, 0, 0));

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    builder.Place(Sized("tile" + x + y, V(1, 1, 1), 100f), V(x, y, 1));
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            Assert.Equal(10, simulation.Solver.Nodes.Count);
            Assert.Equal(9 + 12, simulation.Solver.Links.Count);   // slab-to-tile, plus tile-to-tile
        }

        [Fact]
        public void AnInteriorBlockOfALargeSolidIsNeverItsOwnNeighbour()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("solo", V(4, 4, 4), 4000f), V(0, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());

            Assert.Single(simulation.Solver.Nodes);
            Assert.Empty(simulation.Solver.Links);
        }

        // ---- exposure --------------------------------------------------------------------

        [Fact]
        public void ALargeBlockAloneIsExposedOverItsWholeSurface()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Sized("solo", V(2, 3, 4), 2400f), V(0, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            ThermalNode node = simulation.Solver.Nodes[0];

            Assert.Equal(BoxGeometry.SurfaceAreaCells(V(2, 3, 4)), node.TotalExposedFaces);
        }

        [Fact]
        public void CoveringOneFaceOfALargeBlockRemovesExactlyThatMuchExposure()
        {
            GridBuilder bare = GridBuilder.Large();
            bare.Place(Sized("slab", V(3, 3, 1), 900f), V(0, 0, 0));
            int exposedAlone = bare.BuildSimulation(new ThermalSettings()).Solver.Nodes[0].TotalExposedFaces;

            GridBuilder covered = GridBuilder.Large();
            covered.Place(Sized("slab", V(3, 3, 1), 900f), V(0, 0, 0));
            covered.Place(Sized("lid", V(3, 3, 1), 900f), V(0, 0, 1));

            ThermalSimulation simulation = covered.BuildSimulation(new ThermalSettings());
            ThermalNode slab = simulation.Solver.GetNodeAt(V(0, 0, 0));

            // the 3x3 face against the lid stops radiating
            Assert.Equal(exposedAlone - 9, slab.TotalExposedFaces);
        }
    }
}
