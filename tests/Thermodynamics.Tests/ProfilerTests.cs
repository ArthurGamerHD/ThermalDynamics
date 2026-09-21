using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class ProfilerTests
    {
        private class RecordingProfiler : ISimulationProfiler
        {
/// <summary>List operation.</summary>
            public readonly List<string> Events = new List<string>();
            public readonly Dictionary<SimulationPhase, int> Depth = new Dictionary<SimulationPhase, int>();
            public bool Unbalanced;

/// <summary>Begin operation.</summary>
            public void Begin(SimulationPhase phase)
            {
                int depth;
                Depth.TryGetValue(phase, out depth);
                if (depth != 0) Unbalanced = true;
                Depth[phase] = depth + 1;
                Events.Add("begin " + phase);
            }

/// <summary>End operation.</summary>
            public void End(SimulationPhase phase)
            {
                int depth;
                Depth.TryGetValue(phase, out depth);
                if (depth != 1) Unbalanced = true;
                Depth[phase] = depth - 1;
                Events.Add("end " + phase);
            }

/// <summary>Count operation.</summary>
            public int Count(string what)
            {
                int count = 0;
                for (int i = 0; i < Events.Count; i++)
                {
                    if (Events[i] == what) count++;
                }
                return count;
            }
        }

/// <summary>Builds the API method table.</summary>
        private static ThermalSimulation BuildSimulation()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            return builder.BuildSimulation(new ThermalSettings(), 293.15f);
        }

        [Fact]
/// <summary>EveryPhaseIsBracketed operation.</summary>
        public void EveryPhaseIsBracketed()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
/// <summary>RecordingProfiler operation.</summary>
            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.RebuildAll();
            for (int i = 0; i < 20; i++)
            {
                simulation.Update(1f / 6f, Worlds.Shadow());
            }

            Assert.False(profiler.Unbalanced);
            foreach (KeyValuePair<SimulationPhase, int> entry in profiler.Depth)
            {
                Assert.Equal(0, entry.Value);
            }
        }

        [Fact]
/// <summary>RebuildAllReportsTopologyRoomMappingAndExposure operation.</summary>
        public void RebuildAllReportsTopologyRoomMappingAndExposure()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
/// <summary>RecordingProfiler operation.</summary>
            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.RebuildAll();

            Assert.Equal(2, profiler.Count("begin " + SimulationPhase.Topology));
            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.RoomMapping));
            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Exposure));
            Assert.Equal(0, profiler.Count("begin " + SimulationPhase.Solver));
        }

        [Fact]
/// <summary>EveryUpdateReportsTheSolver operation.</summary>
        public void EveryUpdateReportsTheSolver()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
            simulation.RebuildAll();

/// <summary>RecordingProfiler operation.</summary>
            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.Update(0.001f, Worlds.Shadow());

            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Solver));
            Assert.Equal(1, profiler.Count("end " + SimulationPhase.Solver));

            Assert.Equal(0, simulation.Solver.StepCount);
            Assert.True(simulation.StepInFlight);
        }

        [Fact]
/// <summary>PlacingABlockReportsATopologyRebuild operation.</summary>
        public void PlacingABlockReportsATopologyRebuild()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(2, 2, 2));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

/// <summary>RecordingProfiler operation.</summary>
            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.AddBlock(new BlockInstance(Catalog.LightArmor(), new Vector3I(5, 0, 0), BlockOrientation.Identity));
            simulation.Update(1f / 6f, Worlds.Shadow());

            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Topology));
        }

        [Fact]
/// <summary>ProfilingChangesNothingAboutTheResult operation.</summary>
        public void ProfilingChangesNothingAboutTheResult()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation instrumented = BuildSimulation();
/// <summary>RecordingProfiler operation.</summary>
            instrumented.Profiler = new RecordingProfiler();

/// <summary>Builds the method table.</summary>
            ThermalSimulation plain = BuildSimulation();

            instrumented.RebuildAll();
            plain.RebuildAll();

            for (int i = 0; i < 60; i++)
            {
                instrumented.Update(1f / 6f, Worlds.Shadow());
                plain.Update(1f / 6f, Worlds.Shadow());
            }

            IList<ThermalNode> a = instrumented.Solver.Nodes;
            IList<ThermalNode> b = plain.Solver.Nodes;

            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(b[i].Temperature, a[i].Temperature);
            }
        }

        [Fact]
/// <summary>CollectingDiagnosticsChangesNothingAboutTheResult operation.</summary>
        public void CollectingDiagnosticsChangesNothingAboutTheResult()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation instrumented = BuildSimulation();
            instrumented.Solver.CollectDiagnostics = true;

/// <summary>Builds the method table.</summary>
            ThermalSimulation plain = BuildSimulation();
            Assert.False(plain.Solver.CollectDiagnostics);

            for (int i = 0; i < 60; i++)
            {
                instrumented.Update(1f / 6f, Worlds.Shadow());
                plain.Update(1f / 6f, Worlds.Shadow());
            }

            IList<ThermalNode> a = instrumented.Solver.Nodes;
            IList<ThermalNode> b = plain.Solver.Nodes;

            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(b[i].Temperature, a[i].Temperature);
            }

            Assert.True(a[0].LastRadiationWatts != 0f);
            Assert.Equal(0f, b[0].LastRadiationWatts);
        }

        [Fact]
/// <summary>ATemperatureWrittenFromOutsideIsPickedUp operation.</summary>
        public void ATemperatureWrittenFromOutsideIsPickedUp()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
            simulation.RebuildAll();
            simulation.Update(1f / 6f, Worlds.Shadow());

            ThermalNode node = simulation.Solver.Nodes[0];
            node.Temperature = 900f;

            simulation.Update(1f / 6f, Worlds.Shadow());

            Assert.True(node.Temperature > 700f, "temperature was " + node.Temperature);
            Assert.True(node.Temperature < 900f, "the block should have cooled, not held at 900");
        }

        [Fact]
/// <summary>AMassChangeIsPickedUp operation.</summary>
        public void AMassChangeIsPickedUp()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
            simulation.RebuildAll();
            simulation.Update(1f / 6f, Worlds.Shadow());

            ThermalNode node = simulation.Solver.Nodes[0];
            float before = node.ThermalMass;

            node.Block.Mass *= 10f;
            node.RefreshThermalMass();

            Assert.True(node.ThermalMass > before);

            float start = node.Temperature;
            simulation.Update(1f / 6f, Worlds.Shadow());

            Assert.NotEqual(start, node.Temperature);
        }

        [Fact]
/// <summary>NoProfilerMeansNoInstrumentation operation.</summary>
        public void NoProfilerMeansNoInstrumentation()
        {
/// <summary>Builds the method table.</summary>
            ThermalSimulation simulation = BuildSimulation();
            Assert.Null(simulation.Profiler);

            simulation.RebuildAll();
            simulation.Update(1f / 6f, Worlds.Shadow());
        }
    }
}
