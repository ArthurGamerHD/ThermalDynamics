using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What the first substep of a step costs over every later one — the per-node environment
    /// terms that are fixed for a step and filled once.
    ///
    /// <para>
    /// The performance report already reports this, but by subtraction: it measures a whole step
    /// at one substep, takes away a separately measured prologue and write-back, and takes away a
    /// separately measured later substep. Four measurements, three subtractions, and the residue
    /// is the fill. Each of the four carries its own noise and the residue carries all of it, which
    /// is fine for a term that is a fifth of a step and useless for judging a change worth a
    /// tenth of that.
    /// </para>
    ///
    /// <para>
    /// This measures it directly instead, by turning the cache off. With
    /// <c>PrecomputeEnvironment</c> on, a step of N substeps pays one fill and N-1 cheap substeps;
    /// with it off, it pays N fills. The difference between the two steps is therefore N-1 fills,
    /// so the fill's own cost comes out of a subtraction between two figures that differ by a
    /// large amount rather than a small one — the signal is multiplied by the substep count
    /// instead of divided by it.
    /// </para>
    ///
    /// <para>
    /// Both figures come from one simulation with the flag flipped between timed blocks.
    /// <c>PrecomputedEnvironmentTests</c> is what makes that legitimate: the flag does not change
    /// the answer, so it cannot change the state the second block starts from.
    /// </para>
    /// </summary>
    public static class RowFillLab
    {
        /// <summary>Timed repeats; the fastest is reported.</summary>
        public static int Repeats = 7;

        public class Row
        {
            public string World;

            /// <summary>The substep ceiling this row ran under, or 0 for none.</summary>
            public int Cap;

            public int Nodes;
            public int Substeps;

            /// <summary>
            /// True when the grid was refused the substeps it asked for, which brings the
            /// conduction overshoot clamp live — and with it the relaxation row, which the fill
            /// writes only in that regime.
            /// </summary>
            public bool ClampLive;

            /// <summary>A step with the rows filled once and read by every later substep.</summary>
            public double CachedMs;

            /// <summary>A step with every substep filling the rows again.</summary>
            public double EveryFillMs;

            /// <summary>
            /// One fill, over and above the substep it rides on. The N-1 divisor is the number of
            /// substeps that fill in one configuration and read in the other.
            /// </summary>
            public double FillMs
            {
                get
                {
                    int extra = Substeps - 1;
                    return extra <= 0 ? 0d : (EveryFillMs - CachedMs) / extra;
                }
            }

            /// <summary>Nanoseconds a fill spends per node, which is the figure to compare.</summary>
            public double FillNsPerNode
            {
                get { return Nodes <= 0 ? 0d : FillMs * 1e6d / Nodes; }
            }

            /// <summary>What the fill is worth as a share of an ordinary step.</summary>
            public double ShareOfStep
            {
                get { return CachedMs <= 0d ? 0d : 100d * FillMs / CachedMs; }
            }
        }

        /// <summary>
        /// The worlds worth measuring, which is a statement about which terms the fill computes.
        ///
        /// Flight is the expensive case and the one to read: solar, the wind weighting, friction
        /// and convection are all live, so every branch in the fill is taken. Vacuum is the other
        /// end — solar alone, no wind sum at all — and the gap between the two rows says how much
        /// of the fill is the air terms rather than the sun.
        /// </summary>
        public static readonly string[] DefaultWorlds = { "flight", "atmosphere", "vacuum" };

        /// <summary>
        /// Substep ceilings to run each world under. 0 grants the grid what it demands, which is
        /// the ordinary case; 4 against a demand near 19 refuses it, which brings the conduction
        /// overshoot clamp live.
        ///
        /// <para>
        /// The pair is the point. The fill writes the relaxation row only while the clamp is
        /// live, so the two rows are not the same measurement at two speeds — they are two
        /// different fills, and the gap between them is what that row costs.
        /// </para>
        /// </summary>
        public static readonly int[] DefaultCaps = { 0, 4 };

        public static List<Row> Run(IList<string> worlds, IList<int> caps, int blocks, int ticks,
            Action<string> log = null)
        {
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

        private static EnvironmentSample World(string name)
        {
            if (name == "vacuum") return Worlds.Space(new VRageMath.Vector3(0.3f, 0.9f, 0.2f));
            if (name == "atmosphere") return Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 40f);
            return Worlds.Flight(1f, 300f);
        }

        private static Row Measure(string world, int cap, int blocks, int ticks)
        {
            ThermalSimulation simulation = Hulls.Driven(
                Hulls.Uncapped(cap > 0 ? cap : Hulls.Unbounded), blocks);

            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, World(world));

            float step = simulation.Settings.StepSeconds;

            // Settle the substep count and warm every row before the clock starts.
            for (int i = 0; i < 4; i++) simulation.Solver.Step(step, state);

            Row row = new Row();
            row.World = world;
            row.Cap = cap;
            row.Nodes = simulation.Solver.Nodes.Count;
            row.Substeps = simulation.Solver.LastSubsteps;
            row.ClampLive = simulation.Solver.ConductionClampLive;

            double cached = double.MaxValue;
            double every = double.MaxValue;

            int repeats = Repeats < 1 ? 1 : Repeats;
            for (int r = 0; r < repeats; r++)
            {
                // Alternate the order so neither configuration is systematically the one that
                // runs on the colder cache.
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

        public static string Table(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  world         cap  substeps  clamp      cached   every fill"
                + "     one fill   ns/node   of a step");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10}  {1,3}  {2,8:n0}  {3,-5}  {4,8:n3}ms  {5,8:n3}ms  {6,8:n4}ms"
                    + "  {7,8:n3}  {8,8:n1}%",
                    row.World, row.Cap > 0 ? row.Cap.ToString() : "-", row.Substeps,
                    row.ClampLive ? "live" : "off", row.CachedMs, row.EveryFillMs,
                    row.FillMs, row.FillNsPerNode, row.ShareOfStep));
            }

            return text.ToString();
        }

        public static string Csv(IList<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("world,cap,nodes,substeps,clamp_live,cached_ms,every_fill_ms,"
                + "fill_ms,fill_ns_per_node,share_of_step_percent");

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.World,
                    row.Cap.ToString(CultureInfo.InvariantCulture),
                    row.Nodes.ToString(CultureInfo.InvariantCulture),
                    row.Substeps.ToString(CultureInfo.InvariantCulture),
                    row.ClampLive ? "yes" : "no",
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
