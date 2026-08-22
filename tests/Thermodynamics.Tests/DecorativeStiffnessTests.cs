using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Why a light fitting still sets a warship's substep count after being given a light
    /// fitting's material properties.
    ///
    /// <para>
    /// A block's stability demand is its conductance over its heat capacity, and the conductance
    /// has two halves. Conduction is what the block is made of and what it is bolted to; the
    /// environment is radiation and convection over the block's exposed area. The `Cubes.xml`
    /// entries for lights, neon and cameras addressed the first half. The second is untouched by
    /// them: exposed area comes from the cell a block occupies, so a 16 kg fitting radiates and
    /// convects as though it were a 2.5 m cube.
    /// </para>
    ///
    /// <para>
    /// That is why the predicted saving arrived in vacuum and very nearly did not in air. In the
    /// 2026-08-20 fleet dump — flown in an atmosphere averaging 0.73 air density — `SmallLight`
    /// demands 22.7 substeps at its worst with 3 % of that demand coming from conduction, and
    /// 12,764 of 123,784 blocks are stiff mostly through radiation and convection. See
    /// [stiffness.md](../../docs/stiffness.md).
    /// </para>
    /// </summary>
    public class DecorativeStiffnessTests
    {
        /// <summary>Steel on a 16 kg body: what a light inherited before it had a definition.</summary>
        private static BlockThermalProperties AsSteel()
        {
            BlockThermalProperties t = Catalog.DefaultThermal();
            t.Conductivity = 50f;
            t.SpecificHeat = 450f;
            t.Emissivity = 0.9f;
            return t;
        }

        /// <summary>The shipped `InteriorLight` entry: plastic housing, glass lens.</summary>
        private static BlockThermalProperties AsLight()
        {
            BlockThermalProperties t = AsSteel();
            t.Conductivity = 2f;
            t.SpecificHeat = 900f;
            return t;
        }

        /// <summary>
        /// One 16 kg fitting on the end of a short armour bar, and what it demands.
        ///
        /// The bar gives the fitting something to conduct into; on its own it would have no
        /// conduction half to compare the environment against.
        /// </summary>
        private static ThermalSolver.SubstepProfile Profile(
            BlockThermalProperties fitting, EnvironmentSample world)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 1, 1));
            builder.Place(BlockModel.Solid("Fitting", Vector3I.One, 16f, fitting),
                new Vector3I(4, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            simulation.StepExact(1, world);
            return simulation.Solver.ProfileSubsteps();
        }

        private static EnvironmentSample Vacuum() { return Worlds.Shadow(); }

        private static EnvironmentSample Air() { return Worlds.PlanetSurface(1f, 0.5f); }

        /// <summary>
        /// In vacuum the definition does what it was written to do. Demand falls 6.6 to 1.0 and
        /// conduction stops being the reason the fitting is stiff — 71 % of the rate to 18 %.
        /// </summary>
        [Fact]
        public void InVacuumTheMaterialPropertiesCarryTheFitting()
        {
            ThermalSolver.SubstepProfile steel = Profile(AsSteel(), Vacuum());
            ThermalSolver.SubstepProfile light = Profile(AsLight(), Vacuum());

            Assert.True(steel.WorstNodeConductionShare > 0.6f,
                "steel conduction share " + steel.WorstNodeConductionShare);
            Assert.True(light.WorstNodeDemand < steel.WorstNodeDemand * 0.25f,
                "steel " + steel.WorstNodeDemand + ", light " + light.WorstNodeDemand);
        }

        /// <summary>
        /// In air it very nearly does not. Convection over the cell's exposed area is already
        /// 85 % of the steel fitting's stability rate, so removing the conduction half removes
        /// almost nothing: 31.7 substeps to 13.7, and the remainder is 99 % environment.
        ///
        /// This is the correction to the earlier prediction that the definitions would drop
        /// uncapped demand from 21.4 to about 5.6. That figure was the conduction term, and it is
        /// not the block's demand anywhere there is air.
        /// </summary>
        [Fact]
        public void InAirTheEnvironmentIsWhatIsLeft()
        {
            ThermalSolver.SubstepProfile steel = Profile(AsSteel(), Air());
            ThermalSolver.SubstepProfile light = Profile(AsLight(), Air());

            Assert.True(steel.WorstNodeConductionShare < 0.25f,
                "even as steel the fitting is environment-limited in air: "
                + steel.WorstNodeConductionShare);

            Assert.True(light.WorstNodeDemand > steel.WorstNodeDemand * 0.35f,
                "steel " + steel.WorstNodeDemand + ", light " + light.WorstNodeDemand);
            Assert.True(light.WorstNodeDemand > 8f,
                "a fitting with real material properties still demands "
                + light.WorstNodeDemand + " substeps in air");
            Assert.True(light.WorstNodeConductionShare < 0.05f,
                "light conduction share " + light.WorstNodeConductionShare);
        }

        /// <summary>
        /// The knob that reaches the remaining half is `ExposedSurfaceMultiplier`, which the
        /// decorative entries leave at 1. A light fitting is not a 2.5 m cube of radiating and
        /// convecting surface, and the environment term charges it as though it were: at 0.1 the
        /// fitting stops setting the grid's pace and the armour it is bolted to takes over.
        ///
        /// Not applied. It also changes how much heat the block exchanges, which is a balance
        /// decision rather than a free one. This pins the size of the effect so the decision can
        /// be made against a number.
        /// </summary>
        [Fact]
        public void ExposedAreaIsTheKnobThatReachesIt()
        {
            BlockThermalProperties smaller = AsLight();
            smaller.ExposedSurfaceMultiplier = 0.1f;

            ThermalSolver.SubstepProfile full = Profile(AsLight(), Air());
            ThermalSolver.SubstepProfile reduced = Profile(smaller, Air());

            Assert.True(reduced.WorstNodeDemand < full.WorstNodeDemand * 0.2f,
                "full " + full.WorstNodeDemand + ", reduced " + reduced.WorstNodeDemand);
        }
    }
}
