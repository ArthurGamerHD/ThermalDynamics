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
        /// In air the environment is most of what is left, and `C24` took it from nearly all of it
        /// to about three fifths.
        ///
        /// <para>
        /// Convection over the cell's exposed area used to be 85 % of the steel fitting's
        /// stability rate, so removing the conduction half removed almost nothing. At four times
        /// the conduction pace the same fitting is **41 % conduction** in air, and removing that
        /// half now removes rather more. The claim survives in its weaker form — a fitting with
        /// real material properties is still environment-limited in air, and a light one is still
        /// close to a steel one there — and the figure is asserted where it is rather than where
        /// it was.
        /// </para>
        ///
        /// This is the correction to the earlier prediction that the definitions would drop
        /// uncapped demand from 21.4 to about 5.6. That figure was the conduction term, and it is
        /// not the block's demand anywhere there is air.
        /// </summary>
        [Fact]
        public void InAirTheEnvironmentIsMostOfWhatIsLeft()
        {
            ThermalSolver.SubstepProfile steel = Profile(AsSteel(), Air());
            ThermalSolver.SubstepProfile light = Profile(AsLight(), Air());

            Assert.True(steel.WorstNodeConductionShare < 0.5f,
                "even as steel the fitting is environment-limited in air: "
                + steel.WorstNodeConductionShare);

            // **And the definition reaches the air case now, which it did not.** This asked for
            // the light fitting to stay within a third of the steel one, because at the pace the
            // conversion calibrated to the air term was 85 % of both and the material barely
            // showed: 31.7 substeps to 13.7. At four times the conduction pace it is 18.29 to
            // **5.69**, so writing a real conductivity on a decorative block takes 69 % of its
            // demand off in air as well as in vacuum. That is the correction to this page's own
            // correction, and `ExposedSurfaceMultiplier` is no longer the only knob that reaches
            // a fitting in atmosphere.
            Assert.True(light.WorstNodeDemand < steel.WorstNodeDemand * 0.5f,
                "the light fitting demands " + light.WorstNodeDemand + " against the steel one's "
                + steel.WorstNodeDemand + ", so the definitions have stopped reaching a fitting in"
                + " air and the note above needs rewriting");

            Assert.True(light.WorstNodeDemand > 4f,
                "a fitting with real material properties demands only "
                + light.WorstNodeDemand + " substeps in air, so the environment half has gone too"
                + " and this rig is no longer about a decorative block in atmosphere");
            // The light fitting is what is left when the material is right: 5 % conduction against
            // the steel one's 41 %, where it was under 1 % against 15 %. Both ends moved by the
            // pace and the ordering is the claim.
            Assert.True(light.WorstNodeConductionShare < 0.1f,
                "light conduction share " + light.WorstNodeConductionShare);
            Assert.True(light.WorstNodeConductionShare < steel.WorstNodeConductionShare * 0.5f,
                "the light fitting is " + light.WorstNodeConductionShare + " conduction against"
                + " the steel one's " + steel.WorstNodeConductionShare
                + ", so the material has stopped deciding which term carries the fitting");
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
