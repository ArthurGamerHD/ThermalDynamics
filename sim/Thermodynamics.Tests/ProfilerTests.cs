using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The instrumentation hook the telemetry module hangs its stage timings on.
    ///
    /// What matters is that every stage is bracketed correctly and that a simulation with no
    /// profiler attached behaves exactly as it did before the hook existed — that is the whole
    /// basis of "comprehensive when testing, free in production".
    /// </summary>
    public class ProfilerTests
    {
        private class RecordingProfiler : ISimulationProfiler
        {
            public readonly List<string> Events = new List<string>();
            public readonly Dictionary<SimulationPhase, int> Depth = new Dictionary<SimulationPhase, int>();
            public bool Unbalanced;

            public void Begin(SimulationPhase phase)
            {
                int depth;
                Depth.TryGetValue(phase, out depth);
                if (depth != 0) Unbalanced = true;
                Depth[phase] = depth + 1;
                Events.Add("begin " + phase);
            }

            public void End(SimulationPhase phase)
            {
                int depth;
                Depth.TryGetValue(phase, out depth);
                if (depth != 1) Unbalanced = true;
                Depth[phase] = depth - 1;
                Events.Add("end " + phase);
            }

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

        private static ThermalSimulation BuildSimulation()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            return builder.BuildSimulation(new ThermalSettings(), 293.15f);
        }

        [Fact]
        public void EveryPhaseIsBracketed()
        {
            ThermalSimulation simulation = BuildSimulation();
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
        public void RebuildAllReportsTopologyRoomMappingAndExposure()
        {
            ThermalSimulation simulation = BuildSimulation();
            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.RebuildAll();

            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Topology));
            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.RoomMapping));
            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Exposure));
            Assert.Equal(0, profiler.Count("begin " + SimulationPhase.Solver));
        }

        [Fact]
        public void AnIdleUpdateDoesNotReportTheSolver()
        {
            ThermalSimulation simulation = BuildSimulation();
            simulation.RebuildAll();

            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            // Far too short a frame to owe a step at the default four per second.
            simulation.Update(0.001f, Worlds.Shadow());

            Assert.Equal(0, profiler.Count("begin " + SimulationPhase.Solver));
        }

        [Fact]
        public void PlacingABlockReportsATopologyRebuild()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(2, 2, 2));
            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            RecordingProfiler profiler = new RecordingProfiler();
            simulation.Profiler = profiler;

            simulation.AddBlock(new BlockInstance(Catalog.LightArmor(), new Vector3I(5, 0, 0), BlockOrientation.Identity));
            simulation.Update(1f / 6f, Worlds.Shadow());

            Assert.Equal(1, profiler.Count("begin " + SimulationPhase.Topology));
        }

        /// <summary>
        /// The production path: no profiler, and the simulation has to produce exactly the same
        /// temperatures it would have produced before the hook was added.
        /// </summary>
        [Fact]
        public void ProfilingChangesNothingAboutTheResult()
        {
            ThermalSimulation instrumented = BuildSimulation();
            instrumented.Profiler = new RecordingProfiler();

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
        public void NoProfilerMeansNoInstrumentation()
        {
            ThermalSimulation simulation = BuildSimulation();
            Assert.Null(simulation.Profiler);

            // Nothing to assert beyond this not throwing: the point is that the simulation runs
            // its whole update with the hooks unattached.
            simulation.RebuildAll();
            simulation.Update(1f / 6f, Worlds.Shadow());
        }
    }
}
