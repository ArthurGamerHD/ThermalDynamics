using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DragProfileTests
    {
        private const float ThickAir = 1f;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings(bool solar = false)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();

            settings.EnableEnvironment = solar;
            settings.EnableSolarHeat = solar;
            settings.EnableDamage = false;
            settings.Derive();
            return settings;
        }

/// <summary>Hull operation.</summary>
        private static ThermalSimulation Hull(bool solar = false)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(solar), 293.15f);

            if (solar) simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

/// <summary>Profile operation.</summary>
        private static void Profile(ThermalSimulation simulation, DragProfile profile)
        {
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                simulation.Solver.Nodes[i].Drag = profile;
            }
        }

/// <summary>DragWatts operation.</summary>
        private static float DragWatts(ThermalSimulation simulation)
        {
            simulation.StepExact(1, Worlds.Flight(ThickAir, 120f));
            return simulation.Solver.LastFrictionWatts;
        }

        [Fact]
/// <summary>AProfileReducesTheDrag operation.</summary>
        public void AProfileReducesTheDrag()
        {
/// <summary>DragWatts operation.</summary>
            float plain = DragWatts(Hull());

/// <summary>Hull operation.</summary>
            ThermalSimulation slippery = Hull();
            Profile(slippery, DragProfile.Of(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f));

            Assert.True(plain > 0f);
            Assert.Equal(plain * 0.5f, DragWatts(slippery), 2);
        }

        [Fact]
/// <summary>OnlyTheFacesTheWindIsOnMatter operation.</summary>
        public void OnlyTheFacesTheWindIsOnMatter()
        {
/// <summary>DragWatts operation.</summary>
            float plain = DragWatts(Hull());

/// <summary>Hull operation.</summary>
            ThermalSimulation sides = Hull();
            Profile(sides, DragProfile.Of(0f, 0f, 1f, 1f, 0f, 0f));

/// <summary>Hull operation.</summary>
            ThermalSimulation windward = Hull();
            Profile(windward, DragProfile.Of(1f, 1f, 0f, 0f, 1f, 1f));

/// <summary>DragWatts operation.</summary>
            float sidesWatts = DragWatts(sides);
/// <summary>DragWatts operation.</summary>
            float windwardWatts = DragWatts(windward);

            Assert.True(sidesWatts == plain || windwardWatts == plain,
                "neither axis was the windward one, so this test is not aimed at the flow");
            Assert.NotEqual(sidesWatts, windwardWatts);
        }

        [Fact]
/// <summary>AProfileCannotAddDrag operation.</summary>
        public void AProfileCannotAddDrag()
        {
/// <summary>DragWatts operation.</summary>
            float plain = DragWatts(Hull());

/// <summary>Hull operation.</summary>
            ThermalSimulation greedy = Hull();
            Profile(greedy, DragProfile.Of(5f, 5f, 5f, 5f, 5f, 5f));

            Assert.Equal(plain, DragWatts(greedy), 2);
        }

        [Fact]
/// <summary>NonsenseReadsAsNoChange operation.</summary>
        public void NonsenseReadsAsNoChange()
        {
/// <summary>DragWatts operation.</summary>
            float plain = DragWatts(Hull());

/// <summary>Hull operation.</summary>
            ThermalSimulation nonsense = Hull();
            Profile(nonsense, DragProfile.Of(
                float.NaN, float.NegativeInfinity, -1f, float.PositiveInfinity, float.NaN, -0.5f));

            Assert.Equal(plain, DragWatts(nonsense), 2);
        }

        [Fact]
/// <summary>AnUnsetProfileChangesNothing operation.</summary>
        public void AnUnsetProfileChangesNothing()
        {
            Assert.False(default(DragProfile).IsSet);
            for (int f = 0; f < Face.Count; f++) Assert.Equal(1f, default(DragProfile)[f]);

/// <summary>Hull operation.</summary>
            ThermalSimulation cleared = Hull();
            Profile(cleared, default(DragProfile));

            Assert.Equal(DragWatts(Hull()), DragWatts(cleared), 2);
        }

        [Fact]
/// <summary>AProfileDoesNotDimTheSun operation.</summary>
        public void AProfileDoesNotDimTheSun()
        {
/// <summary>Hull operation.</summary>
            ThermalSimulation plain = Hull(solar: true);
/// <summary>Hull operation.</summary>
            ThermalSimulation slippery = Hull(solar: true);
            Profile(slippery, DragProfile.Of(0f, 0f, 0f, 0f, 0f, 0f));

            plain.StepExact(4, Worlds.Space(Vector3.Forward));
            slippery.StepExact(4, Worlds.Space(Vector3.Forward));

            float plainSolar = 0f, slipperySolar = 0f;
            for (int i = 0; i < plain.Solver.Nodes.Count; i++)
            {
                plainSolar += plain.Solver.Nodes[i].LastSolarWatts;
                slipperySolar += slippery.Solver.Nodes[i].LastSolarWatts;
            }

            Assert.True(plainSolar > 0f, "no sunlight was absorbed, so this proves nothing");
            Assert.Equal(plainSolar, slipperySolar, 2);
        }
    }
}
