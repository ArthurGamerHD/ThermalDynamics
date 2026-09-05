using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// A grid's temperature field as a flat array, and putting one back.
    ///
    /// Restore writes only <c>Temperature</c> on purpose: it is the one value a host may write
    /// from outside a step — exactly what loading a saved world does — and the solver re-reads
    /// it. Three copies of the snapshot and two of the restore had accumulated across the A/B
    /// oracle and the two client labs; this is the one statement.
    /// </summary>
    public static class GridState
    {
        /// <summary>Every node's temperature, in node order.</summary>
        public static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }

        /// <summary>Writes a snapshot back, over as many nodes as both sides have.</summary>
        public static void Restore(ThermalSimulation simulation, float[] temperatures)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = Math.Min(nodes.Count, temperatures.Length);
            for (int i = 0; i < count; i++) nodes[i].Temperature = temperatures[i];
        }
    }
}
