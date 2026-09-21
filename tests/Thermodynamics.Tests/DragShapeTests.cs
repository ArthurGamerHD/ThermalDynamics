using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DragShapeTests
    {
        private const float ThickAir = 1f;
        private const float Speed = 120f;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.EnableShapeDrag = false;
            settings.Derive();
            return settings;
        }

/// <summary>Brick operation.</summary>
        private static ThermalSimulation Brick()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

/// <summary>Wedge operation.</summary>
        private static ThermalSimulation Wedge()
        {
            GridBuilder builder = GridBuilder.Large();

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    for (int z = 0; z < 4 - y; z++)
                    {
                        builder.Place(Catalog.HeavyArmor(), new Vector3I(x, y, z));
                    }
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

/// <summary>DragWatts operation.</summary>
        private static float DragWatts(ThermalSimulation simulation)
        {
            simulation.StepExact(1, Worlds.Flight(ThickAir, Speed));
            return simulation.Solver.LastFrictionWatts;
        }

        [Fact]
/// <summary>TheWedgeIsNotJustTheBrickAgain operation.</summary>
        public void TheWedgeIsNotJustTheBrickAgain()
        {
/// <summary>Brick operation.</summary>
            ThermalSimulation brick = Brick();
/// <summary>Wedge operation.</summary>
            ThermalSimulation wedge = Wedge();

            Assert.Equal(64, brick.Solver.Nodes.Count);
            Assert.Equal(40, wedge.Solver.Nodes.Count);
        }

        [Fact]
/// <summary>ABrickAndAWedgeOfTheSameFrontalAreaDragIdentically operation.</summary>
        public void ABrickAndAWedgeOfTheSameFrontalAreaDragIdentically()
        {
/// <summary>DragWatts operation.</summary>
            float brick = DragWatts(Brick());
/// <summary>DragWatts operation.</summary>
            float wedge = DragWatts(Wedge());

            Assert.True(brick > 0f, "the brick took no drag, so this compares nothing");

            Assert.Equal(brick, wedge, 3);
        }

        [Fact]
/// <summary>HalvingTheFrontalAreaHalvesTheDrag operation.</summary>
        public void HalvingTheFrontalAreaHalvesTheDrag()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 2, 4));

            ThermalSimulation narrow = builder.BuildSimulation(Settings(), 293.15f);
            narrow.Planet = PlanetThermalProperties.Default();

/// <summary>DragWatts operation.</summary>
            float full = DragWatts(Brick());
/// <summary>DragWatts operation.</summary>
            float half = DragWatts(narrow);

            Assert.True(half > 0f);
            Assert.Equal(full / 2f, half, 2);
        }
    }
}
