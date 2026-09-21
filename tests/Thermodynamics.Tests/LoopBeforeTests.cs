using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class LoopBeforeTests
    {
/// <summary>Isolated operation.</summary>
        private static ThermalSettings Isolated()
        {
            return Isolation.DeadWorld();
        }

        [Fact]
/// <summary>TheBeforeArmIsNotWhatShips operation.</summary>
        public void TheBeforeArmIsNotWhatShips()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();
            LoopThermalProperties before = LoopBefore.For(Catalog.LargeGridSize);

            Assert.NotEqual(before.HeatTransferCoefficient, shipped.HeatTransferCoefficient);
            Assert.NotEqual(before.StagnantTransferFraction, shipped.StagnantTransferFraction);

            Assert.NotEqual(
                before.MassPerPipe(Catalog.LargeGridSize),
                shipped.MassPerPipe(Catalog.LargeGridSize));

            Assert.NotEqual(
                before.MassPerPipe(Catalog.SmallGridSize),
                shipped.MassPerPipe(Catalog.SmallGridSize));
        }

        [Fact]
/// <summary>AStoppedRingCarriesWhatEveryRingCarriedBeforeTheRetune operation.</summary>
        public void AStoppedRingCarriesWhatEveryRingCarriedBeforeTheRetune()
        {
            LoopThermalProperties shipped = LoopThermalProperties.Default();

            float stopped = shipped.HeatTransferCoefficient * shipped.StagnantTransferFraction;

            Assert.Equal(LoopBefore.Coefficient, stopped, 1);
        }

        [Fact]
/// <summary>TheFlatChargeIsTwoDensitiesTwoOrdersOfMagnitudeApart operation.</summary>
        public void TheFlatChargeIsTwoDensitiesTwoOrdersOfMagnitudeApart()
        {
            LoopThermalProperties before = LoopBefore.For(Catalog.LargeGridSize);

            float large = before.MassPerPipe(Catalog.LargeGridSize) / Volume(Catalog.LargeGridSize);
            float small = before.MassPerPipe(Catalog.SmallGridSize) / Volume(Catalog.SmallGridSize);

            Assert.True(small > large * 50f,
                "the flat coolant charge was expected to be far denser on a small grid than a large"
                + " one, which is the defect `C43` corrects; it read " + small.ToString("n1")
                + " against " + large.ToString("n1") + " kg/m3");
        }

        [Fact]
/// <summary>TheRingHoldsMoreCoolantThanItUsedTo operation.</summary>
        public void TheRingHoldsMoreCoolantThanItUsedTo()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop shipped = simulation.Solver.Loops[0];

            float now = shipped.CapacityKilograms;
            float sinkNow = shipped.LinkConductance(0);

            simulation.LoopProperties = LoopBefore.For(simulation.Grid.GridSize);
            simulation.RebuildAll();

            CoolantLoop before = simulation.Solver.Loops[0];

            Assert.True(before.CapacityKilograms > 0f,
                "the before ring held nothing, so nothing here is measured");

            Assert.True(now > before.CapacityKilograms,
                "the ring holds " + now.ToString("n0") + " kg as it ships against "
                + before.CapacityKilograms.ToString("n0") + " kg before, so the correction reaches"
                + " no fluid");

            Assert.NotEqual(sinkNow, before.LinkConductance(0), 1);
        }

/// <summary>Volume operation.</summary>
        private static float Volume(float cell)
        {
            return cell * cell * cell;
        }
    }
}
