using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A grid standing in its own light.
    ///
    /// The cheap solar model lights any face pointing at the sun, whatever is built in front of
    /// it. These pin the alternative: the shadow map's own behaviour first, then the thing that
    /// actually matters, which is that a block behind another block stops being heated.
    /// </summary>
    public class SunShadowMapTests
    {
        private static readonly Vector3 SunAlongX = new Vector3(1f, 0f, 0f);

        /// <summary>Builds a completed pass in one go.</summary>
        private static SunShadowMap Build(GridModel grid, Vector3 sun)
        {
            SunShadowMap map = new SunShadowMap();
            map.Restart(grid, sun);
            map.RunToCompletion();
            return map;
        }

        [Fact]
        public void ALoneCellIsLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            // Its sunward face is lit and its far side is not: a block shadows itself, which is
            // what makes the far side of any hull cold.
            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Right));
            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Left));
        }

        [Fact]
        public void TheCellBehindAnotherIsShadowed()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));   // sunward
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);           // behind it

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            // The sunward face of the block in front is lit; the one hiding behind it is not.
            Assert.True(map.IsFaceLit(new Vector3I(1, 0, 0), Face.Right));
            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Right));
        }

        [Fact]
        public void ShadowFollowsTheSunAround()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            // From the other side the shadow is cast the other way.
            SunShadowMap map = Build(builder.Grid, -SunAlongX);

            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Left));
            Assert.False(map.IsFaceLit(new Vector3I(1, 0, 0), Face.Left));
        }

        [Fact]
        public void CellsSideBySideAcrossTheSunAreBothLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 1, 0));

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            // Neither is behind the other, so neither shadows the other.
            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Right));
            Assert.True(map.IsFaceLit(new Vector3I(0, 1, 0), Face.Right));
        }

        [Fact]
        public void ACellTheMapNeverSawIsTreatedAsLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            // Not knowing must mean "no shadow found", never "shadowed": the cheap model's answer
            // is the one to fall back to, and a wrong shadow cools a block standing in full sun.
            Assert.True(map.IsLit(new Vector3I(0, 40, 0)));
        }

        [Fact]
        public void AnUnbuiltMapLightsEverything()
        {
            SunShadowMap map = new SunShadowMap();

            Assert.False(map.IsBuilt);
            Assert.True(map.IsLit(Vector3I.Zero));
            Assert.Equal(1f, map.FaceLitFraction(null, Face.Up), 5);
        }

        // ---- shadow belongs to a face, not to a block --------------------------------------

        [Fact]
        public void TheInnerLayerOfAWallIsStillLitOnTheSidesThatFaceOut()
        {
            // Two cells thick across the sun, four tall. The back layer cannot see the sun through
            // the front layer — but its top and side faces are on the outside of the same wall and
            // are in full sunlight. Asking the question of the cell instead of the face lights a
            // hull along one row of blocks and calls the rest of it shadowed.
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(2, 4, 1));

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            BlockInstance back = builder.Grid.GetAtCell(new Vector3I(0, 1, 0));

            Assert.Equal(0f, map.FaceLitFraction(back, Face.Right), 5);    // toward the sun, buried
            Assert.Equal(1f, map.FaceLitFraction(back, Face.Forward), 5);  // out of the wall's side
            Assert.Equal(1f, map.FaceLitFraction(back, Face.Backward), 5);
        }

        [Fact]
        public void AFaceInsideARecessIsShadowedWhileTheWallAroundItIsLit()
        {
            // A slab with a bite out of it, sun coming in over the top at an angle: the floor of
            // the recess is shadowed by the wall beside it, and the top of that wall is not.
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 1, 1));
            builder.Place(Catalog.LightArmor(), new Vector3I(3, 1, 0));
            builder.Place(Catalog.LightArmor(), new Vector3I(3, 2, 0));

            SunShadowMap map = Build(builder.Grid, Vector3.Normalize(new Vector3(0.83f, 0.55f, 0f)));

            BlockInstance floor = builder.Grid.GetAtCell(new Vector3I(2, 0, 0));
            BlockInstance tower = builder.Grid.GetAtCell(new Vector3I(3, 2, 0));

            Assert.Equal(0f, map.FaceLitFraction(floor, Face.Up), 5);
            Assert.Equal(1f, map.FaceLitFraction(tower, Face.Up), 5);
        }

        [Fact]
        public void ALongBlockCanHaveOneEndInShadowAndTheOtherInTheOpen()
        {
            // A 3-cell bar with a single block standing over its far end.
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmorBar(3), Vector3I.Zero);
            BlockInstance bar = builder.Last;
            builder.Place(Catalog.LightArmor(), new Vector3I(2, 1, 0));

            SunShadowMap map = Build(builder.Grid, Vector3.Normalize(new Vector3(0f, 1f, 0f)));

            // Two of the bar's three top cell faces are open; the third is under the block.
            Assert.Equal(2f / 3f, map.FaceLitFraction(bar, Face.Up), 3);
        }

        [Fact]
        public void ARestartIsOnlyNeededOnceTheSunHasMovedFarEnough()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            Vector3 nudged = Vector3.Normalize(new Vector3(1f, 0.005f, 0f));
            Vector3 moved = Vector3.Normalize(new Vector3(1f, 1f, 0f));

            Assert.False(map.NeedsRestart(ref nudged, 0.99939f));
            Assert.True(map.NeedsRestart(ref moved, 0.99939f));
        }

        // ---- exactness, against a reference that cannot be wrong ---------------------------

        /// <summary>
        /// The answer, worked out the slow obvious way: intersect the ray with every other cell's
        /// cube and see whether it passes through any of them. O(cells) per cell and analytic
        /// rather than sampled — a sampled walk rounds at cell boundaries and cannot tell a ray
        /// that passes through a cube from one that grazes its corner, which is precisely the
        /// distinction under test.
        ///
        /// A ray that only touches a cube — entering and leaving at the same point — is not
        /// blocked by it. Anything else would have a wall of blocks shadow the cells beside it.
        /// </summary>
        private static bool ReferenceLit(GridModel grid, Vector3I cell, Vector3 sun)
        {
            sun = Vector3.Normalize(sun);
            Vector3 origin = new Vector3(cell.X, cell.Y, cell.Z);

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    if (cells[c] == cell) continue;
                    if (Penetrates(origin, sun, cells[c])) return false;
                }
            }

            return true;
        }

        /// <summary>Slab test: does the ray pass through this cell's cube with length to spare?</summary>
        private static bool Penetrates(Vector3 origin, Vector3 direction, Vector3I cell)
        {
            const float Epsilon = 1e-3f;

            float enter = 0f;
            float exit = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
                float centre = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;

                float low = centre - 0.5f;
                float high = centre + 0.5f;

                if (Math.Abs(d) < 1e-9f)
                {
                    if (o < low || o > high) return false;
                    continue;
                }

                float t1 = (low - o) / d;
                float t2 = (high - o) / d;
                if (t1 > t2)
                {
                    float swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                if (t1 > enter) enter = t1;
                if (t2 < exit) exit = t2;
            }

            return exit - enter > Epsilon && exit > Epsilon;
        }

        /// <summary>
        /// Checks every face of every block against the reference. The map answers for the air just
        /// outside a face, so that is what the reference is asked about too.
        /// </summary>
        private static void AssertMatchesReference(GridModel grid, Vector3 sun)
        {
            SunShadowMap map = Build(grid, sun);

            IList<BlockInstance> blocks = grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                Vector3I[] cells = blocks[i].Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I outside = cells[c] + Face.Offsets[face];
                        if (grid.IsOccupied(outside)) continue;

                        Assert.Equal(ReferenceLit(grid, outside, sun), map.IsFaceLit(cells[c], face));
                    }
                }
            }
        }

        /// <summary>
        /// Sun directions to check against the reference.
        ///
        /// Deliberately none of them exactly diagonal or exactly axis-diagonal. A ray at precisely
        /// 45° runs along the corners between cells, where "does this ray pass through that cube"
        /// has no answer worth defending: it touches and does not enter. Those angles get their own
        /// test below, which pins the choice rather than pretending there is a right one.
        /// </summary>
        public static IEnumerable<object[]> ObliqueSuns()
        {
            yield return new object[] { new Vector3(1f, 0f, 0f) };
            yield return new object[] { new Vector3(0.9004f, 0.1619f, -0.4038f) };  // the test world
            yield return new object[] { new Vector3(0.83f, 0.41f, 0.37f) };
            yield return new object[] { new Vector3(0.97f, 0.31f, 0.19f) };
            yield return new object[] { new Vector3(0.51f, 0.86f, 0.23f) };
            yield return new object[] { new Vector3(0.29f, 0.11f, 0.95f) };
            yield return new object[] { new Vector3(-0.71f, 0.13f, -0.69f) };
            yield return new object[] { new Vector3(-0.19f, -0.96f, 0.11f) };
        }

        [Theory]
        [MemberData(nameof(ObliqueSuns))]
        public void ASolidWallMatchesTheReferenceAtAnySunAngle(Vector3 sun)
        {
            // The shape from the test world: four cells thick, seven high, four deep.
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), new Vector3I(-3, 0, 0), new Vector3I(1, 7, 4));

            AssertMatchesReference(builder.Grid, sun);
        }

        [Theory]
        [MemberData(nameof(ObliqueSuns))]
        public void AStaircaseMatchesTheReferenceAtAnySunAngle(Vector3 sun)
        {
            // Diagonal geometry is what the bucketed version could not do: every cell sits in a
            // different column from its neighbour, which is exactly where buckets alias.
            GridBuilder builder = GridBuilder.Large();
            for (int i = 0; i < 6; i++)
            {
                builder.Place(Catalog.LightArmor(), new Vector3I(i, i, 0));
            }

            AssertMatchesReference(builder.Grid, sun);
        }

        [Theory]
        [MemberData(nameof(ObliqueSuns))]
        public void ASparseLatticeMatchesTheReferenceAtAnySunAngle(Vector3 sun)
        {
            // Isolated pillars with gaps between them: the case where an over-eager shadow puts a
            // block that is standing in the open into the dark.
            GridBuilder builder = GridBuilder.Large();
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    builder.Place(Catalog.LightArmor(), new Vector3I(x * 3, y, 0));
                }
            }

            AssertMatchesReference(builder.Grid, sun);
        }

        [Theory]
        [MemberData(nameof(ObliqueSuns))]
        public void AHollowBoxMatchesTheReferenceAtAnySunAngle(Vector3 sun)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(0, 0, 0), new Vector3I(4, 4, 4));

            AssertMatchesReference(builder.Grid, sun);
        }

        [Fact]
        public void AnExactlyDiagonalSunGrazesPastTheCornerRatherThanBeingStoppedByIt()
        {
            // 45° puts the ray exactly along the corners between cells, where touching and entering
            // are the same event. The walk steps one axis at a time and passes; the analytic
            // reference agrees, since a ray that enters and leaves a cube at the same point does not
            // pass through it. Pinned because it is a choice: a real sun is never exactly diagonal
            // for more than an instant, and either answer is defensible for the instant it is.
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 1, 0));
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            Vector3 sun = Vector3.Normalize(new Vector3(1f, 1f, 0f));
            SunShadowMap map = Build(builder.Grid, sun);

            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Right));
            Assert.Equal(
                ReferenceLit(builder.Grid, new Vector3I(1, 0, 0), sun),
                map.IsFaceLit(Vector3I.Zero, Face.Right));
        }

        [Fact]
        public void ARayCannotSlipBetweenTwoBlocksThatTouchOnlyAlongAnEdge()
        {
            // The classic voxel-traversal trap: a diagonal ray through the seam of a staircase.
            // A sampling walk can step straight over the joint; a proper traversal cannot.
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));
            builder.Place(Catalog.LightArmor(), new Vector3I(2, 1, 0));
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(builder.Grid, Vector3.Normalize(new Vector3(1f, 1f, 0f)));

            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Right));
        }

        // ---- running the walk in slices ----------------------------------------------------

        [Fact]
        public void SteppingInSmallBudgetsGivesTheSameAnswerAsRunningItAllAtOnce()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), new Vector3I(-3, 0, 0), new Vector3I(1, 7, 4));

            Vector3 sun = new Vector3(0.9004f, 0.1619f, -0.4038f);

            SunShadowMap whole = Build(builder.Grid, sun);

            SunShadowMap sliced = new SunShadowMap();
            sliced.Restart(builder.Grid, sun);

            int guard = 0;
            while (sliced.IsRunning && guard++ < 1000) sliced.Step(3);

            Assert.False(sliced.IsRunning);
            Assert.Equal(whole.ShadowedCount, sliced.ShadowedCount);

            IList<BlockInstance> blocks = builder.Grid.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    Assert.Equal(
                        whole.FaceLitFraction(blocks[i], face),
                        sliced.FaceLitFraction(blocks[i], face));
                }
            }
        }

        [Fact]
        public void ThePreviousAnswerStaysReadableWhileANewPassRuns()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(3, 0, 0));
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = Build(builder.Grid, SunAlongX);

            // Sun at +X: the near block's +X face is lit, and its -X face is not.
            Assert.True(map.IsFaceLit(new Vector3I(3, 0, 0), Face.Right));
            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Left));

            // A pass for the opposite direction begins but does not finish. Until it does, the
            // readable answer is the old one — never a half-built one.
            map.Restart(builder.Grid, -SunAlongX);
            map.Step(1);

            Assert.True(map.IsRunning);
            Assert.False(map.IsFaceLit(Vector3I.Zero, Face.Left));

            map.RunToCompletion();
            Assert.True(map.IsFaceLit(Vector3I.Zero, Face.Left));
            Assert.False(map.IsFaceLit(new Vector3I(3, 0, 0), Face.Right));
        }

        [Fact]
        public void StepReportsCompletionExactlyOnce()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 1));

            SunShadowMap map = new SunShadowMap();
            map.Restart(builder.Grid, SunAlongX);

            int completions = 0;
            for (int i = 0; i < 50; i++)
            {
                if (map.Step(2)) completions++;
            }

            Assert.Equal(1, completions);
        }
    }

    public class SolarSelfShadowingTests
    {
        /// <summary>Noon-strength sun coming straight down the +X axis, in vacuum.</summary>
        private static EnvironmentSample Sun()
        {
            return Worlds.Space(new Vector3(1f, 0f, 0f));
        }

        private static ThermalSettings Solar(bool selfShadowing)
        {
            ThermalSettings settings = Fixture.EnvironmentOnly();
            settings.EnableSolarHeat = true;
            settings.SolarSelfShadowing = selfShadowing;
            return settings;
        }

        /// <summary>
        /// Two blocks with a gap between them, the second standing in the first's shadow.
        ///
        /// The gap is the whole point. Blocks pressed together need no shadow map: the face
        /// between them is not exposed, so it takes no sunlight under either model. What the cheap
        /// model gets wrong is the face that is genuinely open to the sky and still cannot see the
        /// sun — a wall across a corridor, the back of an overhang, the recess in a doorway.
        /// </summary>
        private static ThermalSimulation Pair(ThermalSettings settings, out ThermalNode front, out ThermalNode back)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(3, 0, 0));
            BlockInstance sunward = builder.Last;
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            BlockInstance shaded = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;

            front = simulation.Solver.GetNode(sunward);
            back = simulation.Solver.GetNode(shaded);
            return simulation;
        }

        [Fact]
        public void TheCheapModelHeatsABlockStandingBehindAnother()
        {
            ThermalNode front, back;
            ThermalSimulation simulation = Pair(Solar(false), out front, out back);

            simulation.StepExact(1, Sun());

            // Both have an exposed face pointing at the sun, and without a shadow map that is all
            // it takes — the wall three cells in front counts for nothing.
            Assert.True(front.LastSolarWatts > 0f);
            Assert.True(back.LastSolarWatts > 0f);
        }

        [Fact]
        public void SelfShadowingStopsTheBlockBehindFromBeingHeated()
        {
            ThermalNode front, back;
            ThermalSimulation simulation = Pair(Solar(true), out front, out back);

            simulation.StepExact(1, Sun());

            Assert.True(front.LastSolarWatts > 0f);
            Assert.Equal(0f, back.LastSolarWatts, 5);
        }

        [Fact]
        public void SelfShadowingLeavesALoneBlockAlone()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation shadowed = builder.BuildSimulation(Solar(true), 293.15f);
            shadowed.Solver.CollectDiagnostics = true;
            shadowed.StepExact(1, Sun());

            ThermalSimulation cheap = builder.BuildSimulation(Solar(false), 293.15f);
            cheap.Solver.CollectDiagnostics = true;
            cheap.StepExact(1, Sun());

            // Nothing to hide behind, so the two models have to agree exactly.
            Assert.Equal(
                cheap.Solver.Nodes[0].LastSolarWatts,
                shadowed.Solver.Nodes[0].LastSolarWatts,
                4);
        }

        [Fact]
        public void ShadowFollowsTheSunWhenItMoves()
        {
            ThermalNode front, back;
            ThermalSimulation simulation = Pair(Solar(true), out front, out back);

            simulation.StepExact(1, Sun());
            Assert.Equal(0f, back.LastSolarWatts, 5);

            // Sun crosses to the other side: the pair swap roles.
            simulation.StepExact(1, Worlds.Space(new Vector3(-1f, 0f, 0f)));

            Assert.True(back.LastSolarWatts > 0f);
            Assert.Equal(0f, front.LastSolarWatts, 5);
        }

        [Fact]
        public void ABlockBuiltIntoTheShadowIsPickedUpWithoutTheSunMoving()
        {
            ThermalSettings settings = Solar(true);

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact(1, Sun());

            ThermalNode lone = simulation.Solver.Nodes[0];
            Assert.True(lone.LastSolarWatts > 0f);

            // A wall goes up three cells away. The sun has not moved, so only the topology change
            // can invalidate the map — which is the case that is easy to get wrong.
            simulation.AddBlock(
                new BlockInstance(Catalog.LightArmor(), new Vector3I(3, 0, 0), BlockOrientation.Identity));
            simulation.RebuildAll();

            simulation.StepExact(1, Sun());

            Assert.Equal(0f, lone.LastSolarWatts, 5);
        }
    }
}
