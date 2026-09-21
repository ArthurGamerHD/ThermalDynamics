using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DecorativeStiffnessTests
    {
/// <summary>AsSteel operation.</summary>
        private static BlockThermalProperties AsSteel()
        {
            BlockThermalProperties t = Catalog.DefaultThermal();
            t.Conductivity = 50f;
            t.SpecificHeat = 450f;
            t.Emissivity = 0.9f;
            return t;
        }

/// <summary>AsLight operation.</summary>
        private static BlockThermalProperties AsLight()
        {
/// <summary>AsSteel operation.</summary>
            BlockThermalProperties t = AsSteel();
            t.Conductivity = 2f;
            t.SpecificHeat = 900f;
            return t;
        }

/// <summary>Profile operation.</summary>
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
/// <summary>Vector3I operation.</summary>
                new Vector3I(4, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());

            simulation.StepExact(1, world);
            return simulation.Solver.ProfileSubsteps();
        }

/// <summary>Vacuum operation.</summary>
        private static EnvironmentSample Vacuum() { return Worlds.Shadow(); }

/// <summary>Air operation.</summary>
        private static EnvironmentSample Air() { return Worlds.PlanetSurface(1f, 0.5f); }

        [Fact]
/// <summary>InVacuumTheMaterialPropertiesCarryTheFitting operation.</summary>
        public void InVacuumTheMaterialPropertiesCarryTheFitting()
        {
/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile steel = Profile(AsSteel(), Vacuum());
/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile light = Profile(AsLight(), Vacuum());

            Assert.True(steel.WorstNodeConductionShare > 0.6f,
                "steel conduction share " + steel.WorstNodeConductionShare);
            Assert.True(light.WorstNodeDemand < steel.WorstNodeDemand * 0.25f,
                "steel " + steel.WorstNodeDemand + ", light " + light.WorstNodeDemand);
        }

        [Fact]
/// <summary>InAirTheEnvironmentIsMostOfWhatIsLeft operation.</summary>
        public void InAirTheEnvironmentIsMostOfWhatIsLeft()
        {
/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile steel = Profile(AsSteel(), Air());
/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile light = Profile(AsLight(), Air());

            Assert.True(steel.WorstNodeConductionShare < 0.5f,
                "even as steel the fitting is environment-limited in air: "
                + steel.WorstNodeConductionShare);

            Assert.True(light.WorstNodeDemand < steel.WorstNodeDemand * 0.5f,
                "the light fitting demands " + light.WorstNodeDemand + " against the steel one's "
                + steel.WorstNodeDemand + ", so the definitions have stopped reaching a fitting in"
                + " air and the note above needs rewriting");

            Assert.True(light.WorstNodeDemand > 4f,
                "a fitting with real material properties demands only "
                + light.WorstNodeDemand + " substeps in air, so the environment half has gone too"
                + " and this rig is no longer about a decorative block in atmosphere");
            Assert.True(light.WorstNodeConductionShare < 0.1f,
                "light conduction share " + light.WorstNodeConductionShare);
            Assert.True(light.WorstNodeConductionShare < steel.WorstNodeConductionShare * 0.5f,
                "the light fitting is " + light.WorstNodeConductionShare + " conduction against"
                + " the steel one's " + steel.WorstNodeConductionShare
                + ", so the material has stopped deciding which term carries the fitting");
        }

        [Fact]
/// <summary>ExposedAreaIsTheKnobThatReachesIt operation.</summary>
        public void ExposedAreaIsTheKnobThatReachesIt()
        {
/// <summary>AsLight operation.</summary>
            BlockThermalProperties smaller = AsLight();
            smaller.ExposedSurfaceMultiplier = 0.1f;

/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile full = Profile(AsLight(), Air());
/// <summary>Profile operation.</summary>
            ThermalSolver.SubstepProfile reduced = Profile(smaller, Air());

            Assert.True(reduced.WorstNodeDemand < full.WorstNodeDemand * 0.2f,
                "full " + full.WorstNodeDemand + ", reduced " + reduced.WorstNodeDemand);
        }
    }
}
