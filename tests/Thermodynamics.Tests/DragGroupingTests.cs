using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class DragGroupingTests
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

/// <summary>Box operation.</summary>
        private static ThermalSimulation Box(Vector3I min, Vector3I maxExclusive)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), min, maxExclusive);

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
/// <summary>SplittingAHullIntoTwoGridsAddsDragThatIsNotThere operation.</summary>
        public void SplittingAHullIntoTwoGridsAddsDragThatIsNotThere()
        {
/// <summary>DragWatts operation.</summary>
            float whole = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 4)));

/// <summary>DragWatts operation.</summary>
            float front = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 2)));
/// <summary>DragWatts operation.</summary>
            float back = DragWatts(Box(new Vector3I(0, 0, 2), new Vector3I(4, 4, 4)));
            float split = front + back;

            Assert.True(whole > 0f, "the whole hull took no drag, so this compares nothing");

            Assert.Equal(whole, front, 2);
            Assert.Equal(whole, back, 2);
            Assert.Equal(2f * whole, split, 2);
        }

        [Fact]
/// <summary>ACutAlongTheWindCostsLessThanACutAcrossIt operation.</summary>
        public void ACutAlongTheWindCostsLessThanACutAcrossIt()
        {
/// <summary>DragWatts operation.</summary>
            float whole = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 4)));

/// <summary>DragWatts operation.</summary>
            float acrossFront = DragWatts(Box(Vector3I.Zero, new Vector3I(4, 4, 2)));
/// <summary>DragWatts operation.</summary>
            float acrossBack = DragWatts(Box(new Vector3I(0, 0, 2), new Vector3I(4, 4, 4)));

/// <summary>DragWatts operation.</summary>
            float alongLeft = DragWatts(Box(Vector3I.Zero, new Vector3I(2, 4, 4)));
/// <summary>DragWatts operation.</summary>
            float alongRight = DragWatts(Box(new Vector3I(2, 0, 0), new Vector3I(4, 4, 4)));

            Assert.Equal(whole, alongLeft + alongRight, 2);
            Assert.Equal(2f * whole, acrossFront + acrossBack, 2);
        }

        [Fact]
/// <summary>AGridInTheLeeOfAnotherIsNotSheltered operation.</summary>
        public void AGridInTheLeeOfAnotherIsNotSheltered()
        {
/// <summary>DragWatts operation.</summary>
            float clear = DragWatts(Box(new Vector3I(0, 0, 40), new Vector3I(1, 1, 41)));

/// <summary>DragWatts operation.</summary>
            float shadowed = DragWatts(Box(new Vector3I(0, 0, 5), new Vector3I(1, 1, 6)));

            Assert.True(clear > 0f);
            Assert.Equal(clear, shadowed, 4);
        }
    }
}
