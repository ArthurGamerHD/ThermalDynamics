using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class NodeActivityLab
    {
        public static readonly float[] Thresholds = { 0.0001f, 0.001f, 0.01f, 0.1f };

        public class Row
        {
            public string Scenario;
            public int AtStep;
            public int Nodes;
            public int Links;

            public int[] QuietNodes = new int[Thresholds.Length];

            public int[] QuietLinks = new int[Thresholds.Length];
        }

        public class WavefrontRow
        {
            public int Step;
            public int ActiveNodes;
        }

        public class Result
        {
/// <summary>List operation.</summary>
            public List<Row> Rows = new List<Row>();
/// <summary>List operation.</summary>
            public List<WavefrontRow> Wavefront = new List<WavefrontRow>();
            public int WavefrontNodes;
        }

        public static readonly int[] DefaultMarks = { 10, 50, 200, 2000, 20000 };

/// <summary>Run operation.</summary>
        public static Result Run(int blocks, Action<string> log = null, int[] marks = null)
        {
/// <summary>Result operation.</summary>
            Result result = new Result();
            if (marks == null) marks = DefaultMarks;

            if (log != null) log("parked in air");
/// <summary>Census operation.</summary>
            ThermalSimulation parked = Census(blocks);
            LoadBenchmarks.SeedSpread(parked);
            MeasureAtMarks(parked, Worlds.PlanetSurface(1f, 0.5f), "parked in air", marks, result.Rows);

            if (log != null) log("driven in vacuum");
            ThermalSimulation driven = Hulls.Driven(Hulls.Uncapped(), blocks);
            MeasureAtMarks(driven, Worlds.Shadow(), "driven in vacuum", marks, result.Rows);

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
/// <summary>WavefrontRow operation.</summary>
                WavefrontRow row = new WavefrontRow();
                row.Step = step;
                row.ActiveNodes = active;
                result.Wavefront.Add(row);
            }

            return result;
        }

/// <summary>Census operation.</summary>
        private static ThermalSimulation Census(int blocks)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));
/// <summary>ThermalSimulation operation.</summary>
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

/// <summary>MeasureAtMarks operation.</summary>
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

                Record(simulation, before);
                simulation.StepExact(1, sample);
                step++;

                rows.Add(Judge(simulation, before, scenario, step));
            }
        }

/// <summary>Record operation.</summary>
        private static void Record(ThermalSimulation simulation, float[] before)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) before[i] = nodes[i].Temperature;
        }

/// <summary>Judge operation.</summary>
        private static Row Judge(ThermalSimulation simulation, float[] before, string scenario, int step)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            IList<ThermalLink> links = simulation.Solver.Links;

/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Scenario = scenario;
            row.AtStep = step;
            row.Nodes = nodes.Count;
            row.Links = links.Count;

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


/// <summary>Report operation.</summary>
        public static string Report(int blocks, Action<string> log = null)
        {
            return Table(Run(blocks, log));
        }

/// <summary>Table operation.</summary>
        public static string Table(Result result)
        {
/// <summary>StringBuilder operation.</summary>
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
