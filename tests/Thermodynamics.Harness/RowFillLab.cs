using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class RowFillLab
    {
        public static int Repeats = 7;

        public class Row
        {
            public string World;

            public int Cap;

            public int Nodes;
            public int Substeps;

            public bool ClampLive;

            public double BoundPercent;

            public double CachedMs;

            public double EveryFillMs;

            public double FillMs
            {
                get
                {
                    int extra = Substeps - 1;
                    return extra <= 0 ? 0d : (EveryFillMs - CachedMs) / extra;
                }
            }

            public double FillNsPerNode
            {
                get { return Nodes <= 0 ? 0d : FillMs * 1e6d / Nodes; }
            }

            public double ShareOfStep
            {
                get { return CachedMs <= 0d ? 0d : 100d * FillMs / CachedMs; }
            }
        }

        public static readonly string[] DefaultWorlds = { "flight", "atmosphere", "vacuum" };

        public static readonly int[] DefaultCaps = { 0, 4 };

/// <summary>Run operation.</summary>
        public static List<Row> Run(IList<string> worlds, IList<int> caps, int blocks, int ticks,
            Action<string> log = null)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            for (int w = 0; w < worlds.Count; w++)
            {
                for (int c = 0; c < caps.Count; c++)
                {
                    if (log != null)
                    {
                        log("row fill: " + worlds[w]
                            + (caps[c] > 0 ? ", cap " + caps[c] : ", uncapped"));
                    }
                    rows.Add(Measure(worlds[w], caps[c], blocks, ticks));
                }
            }

            return rows;
        }

/// <summary>World operation.</summary>
        private static EnvironmentSample World(string name)
        {
            if (name == "vacuum") return Worlds.Space(new VRageMath.Vector3(0.3f, 0.9f, 0.2f));
            if (name == "atmosphere") return Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 40f);
            return Worlds.Flight(1f, 300f);
        }

/// <summary>Measure operation.</summary>
        private static Row Measure(string world, int cap, int blocks, int ticks)
        {
            ThermalSimulation simulation = Hulls.Driven(
                Hulls.Uncapped(cap > 0 ? cap : Hulls.Unbounded), blocks);

            EnvironmentState state = EnvironmentSolver.Solve(
/// <summary>World operation.</summary>
                simulation.Settings, simulation.Planet, World(world));

            float step = simulation.Settings.StepSeconds;

            for (int i = 0; i < 4; i++) simulation.Solver.Step(step, state);

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.World = world;
            row.Cap = cap;
            row.Nodes = simulation.Solver.Nodes.Count;
            row.Substeps = simulation.Solver.LastSubsteps;
            row.ClampLive = simulation.Solver.ConductionClampLive;
/// <summary>BoundShare operation.</summary>
            row.BoundPercent = BoundShare(simulation, state, row.Substeps);

            double cached = double.MaxValue;
            double every = double.MaxValue;

            int repeats = Repeats < 1 ? 1 : Repeats;
            for (int r = 0; r < repeats; r++)
            {
                if ((r & 1) == 0)
                {
                    cached = Math.Min(cached, Time(simulation, state, ticks, true));
                    every = Math.Min(every, Time(simulation, state, ticks, false));
                }
                else
                {
                    every = Math.Min(every, Time(simulation, state, ticks, false));
                    cached = Math.Min(cached, Time(simulation, state, ticks, true));
                }
            }

            row.CachedMs = cached;
            row.EveryFillMs = every;

            if (row.Substeps < 2)
            {
                throw new InvalidOperationException(
                    "the hull took " + row.Substeps + " substeps, so there is no substep that"
                    + " fills in one configuration and reads in the other and the fill cannot be"
                    + " separated");
            }

            return row;
        }

/// <summary>BoundShare operation.</summary>
        private static double BoundShare(ThermalSimulation simulation, EnvironmentState state,
            int granted)
        {
            ThermalSolver solver = simulation.Solver;
            int nodes = solver.Nodes.Count;
            if (nodes <= 0) return 0d;

            int bound = 0;
            for (int i = 0; i < nodes; i++)
            {
                if (solver.NodeSubstepDemand(i, ref state) > granted) bound++;
            }

            return 100d * bound / nodes;
        }

/// <summary>Time operation.</summary>
        private static double Time(ThermalSimulation simulation, EnvironmentState state, int ticks,
            bool precompute)
        {
            simulation.Solver.PrecomputeEnvironment = precompute;
            float step = simulation.Settings.StepSeconds;

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) simulation.Solver.Step(step, state);
            watch.Stop();

            return watch.Elapsed.TotalMilliseconds / ticks;
        }

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.AppendLine("  world         cap  substeps  clamp    bound      cached"
                + "   every fill     one fill   ns/node   of a step");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10}  {1,3}  {2,8:n0}  {3,-5}  {4,5:n1}%  {5,8:n3}ms  {6,8:n3}ms"
                    + "  {7,8:n4}ms  {8,8:n3}  {9,8:n1}%",
                    row.World, row.Cap > 0 ? row.Cap.ToString() : "-", row.Substeps,
                    row.ClampLive ? "live" : "off", row.BoundPercent, row.CachedMs,
                    row.EveryFillMs, row.FillMs, row.FillNsPerNode, row.ShareOfStep));
            }

            return text.ToString();
        }

/// <summary>Csv operation.</summary>
        public static string Csv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.AppendLine("world,cap,nodes,substeps,clamp_live,bound_percent,cached_ms,"
                + "every_fill_ms,fill_ms,fill_ns_per_node,share_of_step_percent");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.World,
                    row.Cap.ToString(CultureInfo.InvariantCulture),
                    row.Nodes.ToString(CultureInfo.InvariantCulture),
                    row.Substeps.ToString(CultureInfo.InvariantCulture),
                    row.ClampLive ? "yes" : "no",
                    row.BoundPercent.ToString("r", CultureInfo.InvariantCulture),
                    row.CachedMs.ToString("r", CultureInfo.InvariantCulture),
                    row.EveryFillMs.ToString("r", CultureInfo.InvariantCulture),
                    row.FillMs.ToString("r", CultureInfo.InvariantCulture),
                    row.FillNsPerNode.ToString("r", CultureInfo.InvariantCulture),
                    row.ShareOfStep.ToString("r", CultureInfo.InvariantCulture)));
            }

            return text.ToString();
        }
    }
}
