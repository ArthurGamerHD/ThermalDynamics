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

        [Fact]
        public void ALoneCellIsLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = new SunShadowMap();
            map.Build(builder.Grid, SunAlongX);

            Assert.True(map.IsLit(Vector3I.Zero));
            Assert.Equal(1, map.ColumnCount);
        }

        [Fact]
        public void TheCellBehindAnotherIsShadowed()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));   // sunward
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);           // behind it

            SunShadowMap map = new SunShadowMap();
            map.Build(builder.Grid, SunAlongX);

            Assert.True(map.IsLit(new Vector3I(1, 0, 0)));
            Assert.False(map.IsLit(Vector3I.Zero));
        }

        [Fact]
        public void ShadowFollowsTheSunAround()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), new Vector3I(1, 0, 0));
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = new SunShadowMap();

            // From the other side the shadow is cast the other way.
            map.Build(builder.Grid, -SunAlongX);

            Assert.True(map.IsLit(Vector3I.Zero));
            Assert.False(map.IsLit(new Vector3I(1, 0, 0)));
        }

        [Fact]
        public void CellsSideBySideAcrossTheSunAreBothLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            builder.Place(Catalog.LightArmor(), new Vector3I(0, 1, 0));

            SunShadowMap map = new SunShadowMap();
            map.Build(builder.Grid, SunAlongX);

            Assert.True(map.IsLit(Vector3I.Zero));
            Assert.True(map.IsLit(new Vector3I(0, 1, 0)));
            Assert.Equal(2, map.ColumnCount);
        }

        [Fact]
        public void ACellTheMapNeverSawIsTreatedAsLit()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = new SunShadowMap();
            map.Build(builder.Grid, SunAlongX);

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
            Assert.Equal(1f, map.LitFraction(null), 5);
        }

        [Fact]
        public void RebuildIsOnlyNeededOnceTheSunHasMovedFarEnough()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            SunShadowMap map = new SunShadowMap();
            map.Build(builder.Grid, SunAlongX);

            Vector3 nudged = Vector3.Normalize(new Vector3(1f, 0.005f, 0f));
            Vector3 moved = Vector3.Normalize(new Vector3(1f, 1f, 0f));

            Assert.False(map.NeedsRebuild(ref nudged, 0.99939f));
            Assert.True(map.NeedsRebuild(ref moved, 0.99939f));
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
