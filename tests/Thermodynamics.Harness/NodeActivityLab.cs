using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What share of a grid is thermally quiet: per step, how many nodes move less than a stated
    /// threshold, how many links have both ends that quiet, and how a local disturbance's active
    /// set grows.
    ///
    /// <para>
    /// This is the evaluation instrument for activity tracking — `D1`'s "visit fewer elements"
    /// and the sleep threshold `G5` left open — and it measures *potential*, not a design: the
    /// quiet share is the ceiling on what any sleeping scheme could skip, reached only by a
    /// scheme with no bookkeeping cost at all. **The criteria, fixed before the first run**
    /// (`E1`, redesign.md): activity tracking earns a design row if,
    /// at a millikelvin per step — below anything a readout shows — the parked hull is more than
    /// half quiet by step 200, and a 300 K disturbance's active set stays under ten per cent of
    /// the grid across its first fifty steps. The driven hull's share is reported either way,
    /// because a working ship is the case a scheme must not make worse.
    /// </para>
    ///
    /// <para>
    /// No equilibrium is claimed anywhere (`M12`): activity is reported *as a function of steps
    /// since the last event*, which is the shape a sleeping scheme actually faces — the question
    /// is not whether a hull settles but how soon after anything happens most of it goes quiet.
    /// </para>
    /// </summary>
    public static class NodeActivityLab
    {
        /// <summary>Per-step temperature movement thresholds, in kelvin.</summary>
        public static readonly float[] Thresholds = { 0.0001f, 0.001f, 0.01f, 0.1f };

        public class Row
        {
            public string Scenario;
            public int AtStep;
            public int Nodes;
            public int Links;

            /// <summary>Per threshold: nodes whose |ΔT| over the measured step is below it.</summary>
            public int[] QuietNodes = new int[Thresholds.Length];

            /// <summary>Per threshold: links with both ends quiet.</summary>
            public int[] QuietLinks = new int[Thresholds.Length];
        }

        public class WavefrontRow
        {
            public int Step;
            public int ActiveNodes;
        }

        public class Result
        {
            public List<Row> Rows = new List<Row>();
            public List<WavefrontRow> Wavefront = new List<WavefrontRow>();
            public int WavefrontNodes;
        }

        /// <summary>
        /// The default marks. Steps 10–200 are what the criterion on redesign.md reads; the long
        /// marks were added after the first run showed 50 simulated seconds is nowhere on a
        /// thermal decay's timescale — they characterise the horizon, and the criterion itself
        /// was not moved (`E11`): its verdict is read at step 200 as written.
        /// </summary>
        public static readonly int[] DefaultMarks = { 10, 50, 200, 2000, 20000 };

        public static Result Run(int blocks, Action<string> log = null, int[] marks = null)
        {
            Result result = new Result();
            if (marks == null) marks = DefaultMarks;

            // A parked hull in air, from spread temperatures: the everyday case sleeping is for.
            if (log != null) log("parked in air");
            ThermalSimulation parked = Census(blocks);
            LoadBenchmarks.SeedSpread(parked);
            MeasureAtMarks(parked, Worlds.PlanetSurface(1f, 0.5f), "parked in air", marks, result.Rows);

            // A driven hull in vacuum: producers running at the census share, mid-transient. The
            // pessimistic case — a scheme that only helps parked ships is worth much less.
            if (log != null) log("driven in vacuum");
            ThermalSimulation driven = Hulls.Driven(Hulls.Uncapped(), blocks);
            MeasureAtMarks(driven, Worlds.Shadow(), "driven in vacuum", marks, result.Rows);

            // A single disturbed node on the parked hull, tracked step by step: how local a local
            // event stays. The parked hull has already run to the last mark above, so the
            // background is as quiet as this lab ever sees it — the first run measured the
            // wavefront on a 200-step background and the whole grid was still active, so the
            // number said nothing about the disturbance at all (`P2`: the background was the
            // blind spot).
            if (log != null) log("hot spot wavefront");
            ThermalNode victim = parked.Solver.Nodes[parked.Solver.Nodes.Count / 2];
            victim.Temperature += 300f;
            result.WavefrontNodes = parked.Solver.Nodes.Count;

            float[] before = new float[parked.Solver.Nodes.Count];
            for (int step = 1; step <= 50; step++)
            {
                Record(parked, before);
                parked.StepExact(1, Worlds.PlanetSurface(1f, 0.5f));

                int active = 0;
                IList<ThermalNode> nodes = parked.Solver.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (Math.Abs(nodes[i].Temperature - before[i]) >= 0.001f) active++;
                }
                WavefrontRow row = new WavefrontRow();
                row.Step = step;
                row.ActiveNodes = active;
                result.Wavefront.Add(row);
            }

            return result;
        }

        private static ThermalSimulation Census(int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        private static void MeasureAtMarks(ThermalSimulation simulation, EnvironmentSample sample,
            string scenario, int[] marks, List<Row> rows)
        {
            float[] before = new float[simulation.Solver.Nodes.Count];
            int step = 0;

            foreach (int mark in marks)
            {
                while (step < mark - 1)
                {
                    simulation.StepExact(1, sample);
                    step++;
                }

                // The measured step is the one that lands on the mark.
                Record(simulation, before);
                simulation.StepExact(1, sample);
                step++;

                rows.Add(Judge(simulation, before, scenario, step));
            }
        }

        private static void Record(ThermalSimulation simulation, float[] before)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) before[i] = nodes[i].Temperature;
        }

        private static Row Judge(ThermalSimulation simulation, float[] before, string scenario, int step)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            IList<ThermalLink> links = simulation.Solver.Links;

            Row row = new Row();
            row.Scenario = scenario;
            row.AtStep = step;
            row.Nodes = nodes.Count;
            row.Links = links.Count;

            // Quietness per node once, reused for every link end.
            for (int t = 0; t < Thresholds.Length; t++)
            {
                bool[] quiet = new bool[nodes.Count];
                int quietNodes = 0;
                for (int i = 0; i < nodes.Count; i++)
                {
                    quiet[i] = Math.Abs(nodes[i].Temperature - before[i]) < Thresholds[t];
                    if (quiet[i]) quietNodes++;
                }

                int quietLinks = 0;
                for (int l = 0; l < links.Count; l++)
                {
                    if (quiet[links[l].NodeA] && quiet[links[l].NodeB]) quietLinks++;
                }

                row.QuietNodes[t] = quietNodes;
                row.QuietLinks[t] = quietLinks;
            }

            return row;
        }

        // ---- reporting ---------------------------------------------------------------------

        public static string Report(int blocks, Action<string> log = null)
        {
            return Table(Run(blocks, log));
        }

        public static string Table(Result result)
        {
            StringBuilder text = new StringBuilder();
            text.Append("scenario".PadRight(20)).Append("step".PadLeft(6));
            foreach (float threshold in Thresholds)
            {
                text.Append(("quiet <" + threshold.ToString("0.####", CultureInfo.InvariantCulture) + "K").PadLeft(15));
            }
            text.Append("links quiet".PadLeft(13)).Append('\n');

            foreach (Row row in result.Rows)
            {
                text.Append(row.Scenario.PadRight(20))
                    .Append(row.AtStep.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                for (int t = 0; t < Thresholds.Length; t++)
                {
                    text.Append(((double)row.QuietNodes[t] / row.Nodes).ToString("p1", CultureInfo.InvariantCulture).PadLeft(15));
                }

                // The link column is the one the conduction pass would skip by, at the
                // millikelvin threshold.
                text.Append(((double)row.QuietLinks[1] / Math.Max(1, row.Links)).ToString("p1", CultureInfo.InvariantCulture).PadLeft(13))
                    .Append('\n');
            }

            text.Append('\n');
            text.Append("hot spot wavefront: one node +300 K on the parked hull, active nodes (|dT| >= 0.001 K) per step of ")
                .Append(result.WavefrontNodes.ToString("n0", CultureInfo.InvariantCulture)).Append('\n');

            int worst = 0;
            foreach (WavefrontRow row in result.Wavefront)
            {
                if (row.ActiveNodes > worst) worst = row.ActiveNodes;
                if (row.Step <= 10 || row.Step % 10 == 0)
                {
                    text.Append("  step ").Append(row.Step.ToString(CultureInfo.InvariantCulture).PadLeft(3))
                        .Append(": ").Append(row.ActiveNodes.ToString("n0", CultureInfo.InvariantCulture))
                        .Append('\n');
                }
            }
            text.Append("  worst: ").Append(worst.ToString("n0", CultureInfo.InvariantCulture))
                .Append(" (").Append(((double)worst / Math.Max(1, result.WavefrontNodes)).ToString("p2", CultureInfo.InvariantCulture))
                .Append(" of the grid)\n");

            return text.ToString();
        }
    }
}
