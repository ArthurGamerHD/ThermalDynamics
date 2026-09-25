using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public static class SolverAb
    {

        public static float[] Temperatures(ThermalSimulation simulation)
        {
            return GridState.Temperatures(simulation);
        }

        public static readonly string[] Mechanisms =
        {
            "radiation", "convection", "solar", "friction", "heat source", "conduction",
        };


        public static float[] Diagnostics(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count * Mechanisms.Length];

            for (int i = 0; i < nodes.Count; i++)
            {
                ThermalNode node = nodes[i];
                int b = i * Mechanisms.Length;
                values[b] = node.LastRadiationWatts;
                values[b + 1] = node.LastConvectionWatts;
                values[b + 2] = node.LastSolarWatts;
                values[b + 3] = node.LastFrictionWatts;
                values[b + 4] = node.LastHeatSourceWatts;
                values[b + 5] = node.LastConductionWatts;
            }

            return values;
        }


        public static void AssertVaried(float[] state, string what)
        {
            Assert.True(state.Length > 0, what + ": nothing was captured");

            for (int i = 1; i < state.Length; i++)
            {
                if (!state[i].Equals(state[0])) return;
            }

            Assert.Fail(what + ": every one of " + state.Length + " figures is "
                + state[0].ToString("r") + ", so an identical result proves nothing");
        }


        public static void AssertIdentical(float[] expected, float[] actual,
            string what, string expectedIs, string actualIs, IList<string> perElement = null)
        {
            Assert.Equal(expected.Length, actual.Length);
            AssertVaried(expected, what);

            int stride = perElement == null ? 1 : perElement.Count;

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i].Equals(actual[i])) continue;

                string element = perElement == null
                    ? "block " + i
                    : "block " + (i / stride) + " " + perElement[i % stride];

                Assert.Fail(what + ": " + element + " is " + actual[i].ToString("r")
                    + " " + actualIs + " and " + expected[i].ToString("r") + " " + expectedIs);
            }
        }
    }
}
