using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class ConductionPaceTests
    {
        private readonly ITestOutputHelper output;

/// <summary>ConductionPaceTests operation.</summary>
        public ConductionPaceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const float ShippedCoefficient = 1000f;

        private const float StoppedCoefficient = 160f;

        [Fact]
/// <summary>AStoppedRingStillCarriesTheOldCoefficient operation.</summary>
        public void AStoppedRingStillCarriesTheOldCoefficient()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

            Assert.Equal(StoppedCoefficient,
                properties.HeatTransferCoefficient * properties.StagnantTransferFraction, 1);
        }

        [Fact]
/// <summary>TheFluidCouplingIsStillWhatTheCoolingFiguresWereMeasuredAt operation.</summary>
        public void TheFluidCouplingIsStillWhatTheCoolingFiguresWereMeasuredAt()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

            Assert.True(
                System.Math.Abs(properties.HeatTransferCoefficient - ShippedCoefficient)
                    < 0.001f * ShippedCoefficient,
                "the fluid couples at " + properties.HeatTransferCoefficient + " W/(m2 K) against"
                + " the " + ShippedCoefficient + " every cooling figure in balance.md was measured"
                + " at. Moving it is a balance decision and wants those figures re-derived with it.");
        }

        [Fact]
/// <summary>TheSameFluidCouplesTheSameWhateverSizeTheGridIs operation.</summary>
        public void TheSameFluidCouplesTheSameWhateverSizeTheGridIs()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

/// <summary>GridModel operation.</summary>
            GridModel large = new GridModel(2.5f);
/// <summary>GridModel operation.</summary>
            GridModel small = new GridModel(0.5f);

            float perAreaLarge = CoolantLoopBuilder.PlateConductance(large, properties) / large.CellFaceArea;
            float perAreaSmall = CoolantLoopBuilder.PlateConductance(small, properties) / small.CellFaceArea;

            Assert.Equal(perAreaLarge, perAreaSmall, 3);
            Assert.Equal(ShippedCoefficient, perAreaLarge, 3);
        }

        [Fact]
/// <summary>TheShippedSolidPaceIsFourTimesWhatTheConversionCalibratedTo operation.</summary>
        public void TheShippedSolidPaceIsFourTimesWhatTheConversionCalibratedTo()
        {
            const float Calibrated = 2.4f;

            Assert.Equal(9.6f, ThermalConstants.ConductionScale, 4);
            Assert.Equal(4f, ThermalConstants.ConductionScale / Calibrated, 4);
        }

        [Fact]
/// <summary>ASmallGridLoopCouplesLessHardThanItUsedTo operation.</summary>
        public void ASmallGridLoopCouplesLessHardThanItUsedTo()
        {
/// <summary>Sets the tle.</summary>
            float now = Settle(160f);
/// <summary>Sets the tle.</summary>
            float before = Settle(800f);

            output.WriteLine("small-grid ring source: {0:n1} K at 160 W/(m2 K), {1:n1} K at the 800"
                + " the old form implied", now, before);

            Assert.True(now > before,
                "the source settled at " + now + " K on the corrected coupling against " + before
                + " K on the old one, so the correction did not reach the rig");

            Assert.True(now - before > 1f,
                "the two couplings are " + (now - before) + " K apart, which is not a measurement");
        }

/// <summary>Sets the tle.</summary>
        private static float Settle(float coefficient)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[2] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Small();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 4, 4);
            PipeFitter.BuildRing(builder, cells, -1, sinks);

            builder.Place(Catalog.Reactor(), cells[2] + Vector3I.Down).Wasting(20000f);
            BlockInstance source = builder.Last;

/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(settings, builder.Grid);

            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.HeatTransferCoefficient = coefficient;
            simulation.LoopProperties = properties;

            for (int i = 0; i < builder.Placed.Count; i++)
            {
                simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            }
            simulation.RebuildAll();

            Assert.NotEmpty(simulation.Solver.Loops);
            Assert.Equal(coefficient, simulation.Solver.Loops[0].Properties.HeatTransferCoefficient, 3);

            simulation.StepExact(4000, Worlds.Shadow());
            return simulation.Solver.GetNode(source).Temperature;
        }
    }
}
