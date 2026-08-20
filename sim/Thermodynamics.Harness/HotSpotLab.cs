using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Why one block on one ship is the hottest thing on it.
    ///
    /// A peak temperature says a ship has a problem; it does not say whose. This runs a single
    /// ship through a single scenario and then dumps the blocks at the top of the distribution with
    /// the three figures that decide where each of them landed: what it generates, what it can
    /// radiate through its own faces, and what it can conduct into its neighbours.
    ///
    /// A block with a large generation and a small conductance and no exposure has to run a wide
    /// gradient to shed what it makes, and that is a *layout* result rather than a solver one.
    /// Telling those apart by hand is exactly what a hot spot needs and what a matrix cannot do.
    /// </summary>
    public static class HotSpotLab
    {
        public class Row
        {
            public string Subtype;
            public string Position;
            public float Kelvin;
            public float CriticalKelvin;
            public float GenerationWatts;
            public int ExposedFaces;
            public float ExposedArea;
            public float ConductanceOut;
            public float ThermalMass;

            /// <summary>
            /// Kelvin of gradient the block must run to conduct away what it makes, if conduction
            /// were its only exit. The back-of-envelope check on whether a temperature is a
            /// consequence or a defect.
            /// </summary>
            public float ImpliedGradient
            {
                get { return ConductanceOut <= 0f ? 0f : GenerationWatts / ConductanceOut; }
            }
        }

        public static string Report(string path, string shipMatch, string scenarioName, int top)
        {
            StringBuilder sb = new StringBuilder();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null)
            {
                sb.AppendLine("No blueprints. The corpus lives at " + Blueprints.CorpusPath());
                return sb.ToString();
            }

            CorpusLab.Summary corpus = CorpusLab.Scan(root);

            Blueprints.Ship ship = null;
            foreach (Blueprints.Ship candidate in corpus.Usable)
            {
                if (shipMatch == null
                    || candidate.Name.IndexOf(shipMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ship = candidate;
                    break;
                }
            }

            if (ship == null)
            {
                sb.AppendLine("No usable ship matching '" + shipMatch + "' under " + root);
                return sb.ToString();
            }

            Battery.Scenario scenario = null;
            foreach (Battery.Scenario candidate in Battery.All())
            {
                if (candidate.Name == (scenarioName ?? "full-electrical")) scenario = candidate;
            }

            if (scenario == null)
            {
                sb.AppendLine("No scenario called '" + scenarioName + "'");
                return sb.ToString();
            }

            // Re-run the scenario here rather than reading a matrix, because the per-block state
            // this needs is gone by the time an outcome has been summarised.
            ThermalSimulation simulation = ship.Build();
            simulation.Solver.CollectDiagnostics = true;
            float applied = ShipLoad.Apply(simulation, scenario.Load);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = scenario.Environment;
            runner.Run(scenario.Seconds, scenario.Seconds / 8f);

            List<Row> rows = Rows(simulation);
            rows.Sort(delegate (Row a, Row b) { return b.Kelvin.CompareTo(a.Kelvin); });

            sb.AppendLine("HOT SPOT");
            sb.AppendLine();
            sb.Append("  ship      ").AppendLine(ship.Name);
            sb.Append("  scenario  ").Append(scenario.Name).Append("  — ").AppendLine(scenario.Question);
            sb.Append("  load      ").Append((applied / 1000f).ToString("n0"))
                .AppendLine(" kW of heat across the grid");
            sb.AppendLine();

            sb.AppendLine("  block                          cell            K   crit K     gen kW  faces   area  W/K out  implied dT");
            int take = Math.Min(top > 0 ? top : 12, rows.Count);
            for (int i = 0; i < take; i++)
            {
                Row r = rows[i];
                sb.Append("  ").Append(Trim(r.Subtype, 28).PadRight(29));
                sb.Append(r.Position.PadRight(14));
                sb.Append(r.Kelvin.ToString("n0").PadLeft(6));
                sb.Append(r.CriticalKelvin.ToString("n0").PadLeft(8));
                sb.Append((r.GenerationWatts / 1000f).ToString("n1").PadLeft(11));
                sb.Append(r.ExposedFaces.ToString().PadLeft(7));
                sb.Append(r.ExposedArea.ToString("n0").PadLeft(7));
                sb.Append(r.ConductanceOut.ToString("n0").PadLeft(9));
                sb.Append(r.ImpliedGradient.ToString("n0").PadLeft(12));
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  the grid's biggest heat makers");
            rows.Sort(delegate (Row a, Row b) { return b.GenerationWatts.CompareTo(a.GenerationWatts); });
            take = Math.Min(8, rows.Count);
            for (int i = 0; i < take; i++)
            {
                Row r = rows[i];
                if (r.GenerationWatts <= 0f) break;
                sb.Append("  ").Append(Trim(r.Subtype, 28).PadRight(29));
                sb.Append((r.GenerationWatts / 1000f).ToString("n1").PadLeft(11)).Append(" kW at ")
                    .Append(r.Kelvin.ToString("n0")).AppendLine(" K");
            }

            return sb.ToString();
        }

        private static List<Row> Rows(ThermalSimulation simulation)
        {
            ThermalSolver solver = simulation.Solver;
            List<Row> rows = new List<Row>(solver.Nodes.Count);

            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                ThermalNode node = solver.Nodes[i];

                rows.Add(new Row
                {
                    Subtype = node.Block.Name,
                    Position = node.Block.Position.ToString(),
                    Kelvin = node.Temperature,
                    CriticalKelvin = node.Block.Thermal.CriticalTemperature,
                    GenerationWatts = node.HeatGenerationWatts,
                    ExposedFaces = node.TotalExposedFaces,
                    ExposedArea = node.ExposedArea,
                    ConductanceOut = solver.NodeConductanceTotal(i),
                    ThermalMass = node.ThermalMass,
                });
            }

            return rows;
        }

        private static string Trim(string text, int width)
        {
            if (text == null) return "";
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
