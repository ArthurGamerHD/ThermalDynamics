using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HeatSourceTests
    {
/// <summary>WithSource operation.</summary>
        private static EnvironmentSample WithSource(Vector3 direction, float irradiance)
        {
            EnvironmentSample sample = Worlds.Shadow();
/// <summary>HeatSourceState operation.</summary>
            sample.HeatSources = new HeatSourceState[] { new HeatSourceState(direction, irradiance) };
            sample.HeatSourceCount = 1;
            return sample;
        }

/// <summary>OneBlock operation.</summary>
        private static ThermalSimulation OneBlock(ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            return builder.BuildSimulation(settings, 200f);
        }

        [Fact]
/// <summary>ASourceHeatsAnExposedBlock operation.</summary>
        public void ASourceHeatsAnExposedBlock()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation simulation = OneBlock(new ThermalSettings());
            simulation.StepExact(20, WithSource(Vector3.Right, 5000f));

            Assert.True(simulation.Solver.Nodes[0].Temperature > 200f);
        }

        [Fact]
/// <summary>NoSourceMeansNoGain operation.</summary>
        public void NoSourceMeansNoGain()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation lit = OneBlock(new ThermalSettings());
            lit.StepExact(20, WithSource(Vector3.Right, 5000f));

/// <summary>OneBlock operation.</summary>
            ThermalSimulation dark = OneBlock(new ThermalSettings());
            dark.StepExact(20, Worlds.Shadow());

            Assert.True(lit.Solver.Nodes[0].Temperature > dark.Solver.Nodes[0].Temperature);
        }

        [Fact]
/// <summary>TheSwitchTurnsThemOff operation.</summary>
        public void TheSwitchTurnsThemOff()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableHeatSources = false;
            settings.Derive();

/// <summary>OneBlock operation.</summary>
            ThermalSimulation off = OneBlock(settings);
            off.StepExact(20, WithSource(Vector3.Right, 5000f));

/// <summary>OneBlock operation.</summary>
            ThermalSimulation none = OneBlock(new ThermalSettings());
            none.StepExact(20, Worlds.Shadow());

            Assert.Equal(none.Solver.Nodes[0].Temperature, off.Solver.Nodes[0].Temperature, 4);
        }

        [Fact]
/// <summary>BrighterSourcesDeliverMore operation.</summary>
        public void BrighterSourcesDeliverMore()
        {
/// <summary>OneBlock operation.</summary>
            ThermalSimulation dim = OneBlock(new ThermalSettings());
            dim.StepExact(20, WithSource(Vector3.Right, 1000f));

/// <summary>OneBlock operation.</summary>
            ThermalSimulation bright = OneBlock(new ThermalSettings());
            bright.StepExact(20, WithSource(Vector3.Right, 8000f));

            Assert.True(bright.Solver.Nodes[0].Temperature > dim.Solver.Nodes[0].Temperature);
        }

        [Fact]
/// <summary>SourcesAddToOneAnother operation.</summary>
        public void SourcesAddToOneAnother()
        {
            EnvironmentSample two = Worlds.Shadow();
            two.HeatSources = new HeatSourceState[]
            {
/// <summary>HeatSourceState operation.</summary>
                new HeatSourceState(Vector3.Right, 4000f),
/// <summary>HeatSourceState operation.</summary>
                new HeatSourceState(Vector3.Left, 4000f),
            };
            two.HeatSourceCount = 2;

/// <summary>OneBlock operation.</summary>
            ThermalSimulation pair = OneBlock(new ThermalSettings());
            pair.StepExact(20, two);

/// <summary>OneBlock operation.</summary>
            ThermalSimulation single = OneBlock(new ThermalSettings());
            single.StepExact(20, WithSource(Vector3.Right, 4000f));

            Assert.True(pair.Solver.Nodes[0].Temperature > single.Solver.Nodes[0].Temperature);
        }

        [Fact]
/// <summary>CountBoundsTheBufferSoAHostCanReuseIt operation.</summary>
        public void CountBoundsTheBufferSoAHostCanReuseIt()
        {
            EnvironmentSample sample = Worlds.Shadow();
            sample.HeatSources = new HeatSourceState[]
            {
/// <summary>HeatSourceState operation.</summary>
                new HeatSourceState(Vector3.Right, 4000f),
/// <summary>HeatSourceState operation.</summary>
                new HeatSourceState(Vector3.Left, 100000f),   // stale entry, must be ignored
            };
            sample.HeatSourceCount = 1;

/// <summary>OneBlock operation.</summary>
            ThermalSimulation bounded = OneBlock(new ThermalSettings());
            bounded.StepExact(20, sample);

/// <summary>OneBlock operation.</summary>
            ThermalSimulation reference = OneBlock(new ThermalSettings());
            reference.StepExact(20, WithSource(Vector3.Right, 4000f));

            Assert.Equal(reference.Solver.Nodes[0].Temperature, bounded.Solver.Nodes[0].Temperature, 4);
        }

        [Fact]
/// <summary>ABuriedBlockWithNoExposedFaceGainsNothing operation.</summary>
        public void ABuriedBlockWithNoExposedFaceGainsNothing()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 200f);
            ThermalNode centre = simulation.Solver.GetNodeAt(new Vector3I(1, 1, 1));
            Assert.Equal(0, centre.TotalExposedFaces);

            simulation.StepExact(5, WithSource(Vector3.Right, 20000f));
            Assert.Equal(0f, centre.LastHeatSourceWatts, 5);
        }
    }
}
