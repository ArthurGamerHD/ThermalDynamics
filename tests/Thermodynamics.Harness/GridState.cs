using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class GridState
    {

        public static float[] Temperatures(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            float[] values = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) values[i] = nodes[i].Temperature;
            return values;
        }


        public static void Restore(ThermalSimulation simulation, float[] temperatures)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = Math.Min(nodes.Count, temperatures.Length);
            for (int i = 0; i < count; i++) nodes[i].Temperature = temperatures[i];
        }
    }
}
