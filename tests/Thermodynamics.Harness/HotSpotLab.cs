using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

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
            ShipAssembly assembly = ship.Build();
            assembly.CollectDiagnostics(true);
            float applied = ShipLoad.Apply(assembly, scenario.Load);

            AssemblyRunner runner = new AssemblyRunner(assembly);
            runner.Environment = scenario.Environment;
            runner.Run(scenario.Seconds);

            List<Row> rows = Rows(assembly);
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

        /// <summary>
        /// The nodes this grid's room air is linked to, by index.
        ///
        /// **Air is a block's third exit and the sealed test used to miss it.** A face that looks
        /// into a sealed compartment is deliberately *not* exposed — `SurfaceMap.GetExposedFaces`
        /// requires the cell beyond it to reach the outside — so a block standing in a void inside a
        /// pressurised hull has no external face at all. If it also bolts to nothing it reads as
        /// having no exit, and it has one: `ThermalSolver.BuildRoomLinks` gives every node with a
        /// face onto a room a link to that room's air. See backlog.md
        /// `F14`.
        /// </summary>
        public static HashSet<int> NodesTouchingAir(ThermalSolver solver)
        {
            HashSet<int> touching = new HashSet<int>();
            if (solver == null) return touching;

            IList<RoomAirNode> air = solver.RoomAir;
            for (int r = 0; r < air.Count; r++)
            {
                IList<RoomLink> links = air[r].Links;
                for (int l = 0; l < links.Count; l++) touching.Add(links[l].NodeIndex);
            }

            return touching;
        }

        /// <summary>
        /// Whether a block has no way to shed heat at all: no face onto the outside, no conduction
        /// joint, and no room air against it.
        ///
        /// **One definition, two consumers** (`P5`): this report and `CorpusSurvey`'s population
        /// bound. They disagreed once already — the survey counted a block coupled to air as sealed
        /// and the corpus figure quoted in balance-lab.md was taken from that count.
        /// </summary>
        public static bool IsSealed(ThermalSolver solver, int index, HashSet<int> touchingAir)
        {
            if (solver == null || index < 0 || index >= solver.Nodes.Count) return false;
            if (solver.Nodes[index].ExposedArea > 0f) return false;
            if (solver.NodeConductanceTotal(index) > 0f) return false;

            return touchingAir == null || !touchingAir.Contains(index);
        }

        /// <summary>
        /// Every block on every ship with no exit at all — no exposed face, no conduction and no
        /// air against it.
        ///
        /// Kept beside the hot-spot dump because it is the same question asked of the whole corpus
        /// rather than of one block: a sealed block heats without bound and has no symptom but its
        /// temperature, so finding them by hand means noticing an odd number.
        /// </summary>
        public static string SealedReport(string path)
        {
            StringBuilder sb = new StringBuilder();

            string root = path ?? Blueprints.DefaultPath();
            if (root == null) return "No blueprints.\n";

            CorpusLab.Summary corpus = CorpusLab.Scan(root);
            Dictionary<string, int> counts = new Dictionary<string, int>();
            List<string> examples = new List<string>();
            int total = 0;
            int incomplete = 0;
            int roomsWithAir = 0;
            int rooms = 0;

            foreach (Blueprints.Ship ship in corpus.Usable)
            {
                ShipAssembly assembly = ship.Build();
                if (assembly.NodeCount < 2) continue;

                // Whether the room map finished. A flood fill that gave up leaves every block
                // looking un-exposed, which is indistinguishable from being buried — and a buried
                // block with no mount joint is exactly what this report calls sealed.
                for (int g2 = 0; g2 < assembly.Simulations.Count; g2++)
                {
                    if (assembly.Simulations[g2].Rooms.HasWorkPending) incomplete++;
                }

                for (int g = 0; g < assembly.Simulations.Count; g++)
                {
                    ThermalSolver solver = assembly.Simulations[g].Solver;
                    HashSet<int> touchingAir = NodesTouchingAir(solver);

                    IList<RoomAirNode> air = solver.RoomAir;
                    rooms += air.Count;
                    for (int a = 0; a < air.Count; a++) if (air[a].HasAir) roomsWithAir++;

                    for (int i = 0; i < solver.Nodes.Count; i++)
                    {
                        ThermalNode node = solver.Nodes[i];
                        if (!IsSealed(solver, i, touchingAir)) continue;

                        int count;
                        counts.TryGetValue(node.Block.Name, out count);
                        counts[node.Block.Name] = count + 1;
                        total++;

                        // The first few get explained rather than counted. "Sealed" on its own is an
                        // assertion; naming the neighbours it failed to bolt to, and which way each
                        // of them mounts, is the evidence for whether the isolation is real.
                        if (examples.Count < 5) Explain(examples, ship, assembly.Simulations[g], node);
                    }
                }
            }

            sb.Append("SEALED BLOCKS  ").Append(total).AppendLine(" across the corpus");
            sb.Append("  ").Append(incomplete).AppendLine(" grids whose room map did not finish");

            // **Air is a block's third exit, and a lab hull has none unless something pressurises
            // it.** Printed rather than assumed, because a count taken on unpressurised hulls is a
            // count of blocks sealed *in that scenario* — the same blocks in a pressurised
            // compartment have a room to shed into and are not sealed at all.
            sb.Append("  ").Append(roomsWithAir).Append(" of ").Append(rooms)
              .AppendLine(" rooms hold air, so that many blocks have a third exit");
            sb.AppendLine();

            if (examples.Count > 0)
            {
                sb.AppendLine("  why, block by block");
                foreach (string example in examples) sb.AppendLine(example);
                sb.AppendLine();
            }

            List<KeyValuePair<string, int>> rows = new List<KeyValuePair<string, int>>(counts);
            rows.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });

            foreach (KeyValuePair<string, int> row in rows)
            {
                GameBlocks.Definition definition;
                string detail = "";
                if (GameBlocks.BySubtype().TryGetValue(row.Key, out definition))
                {
                    detail = "  " + definition.Size + "  mounts declared: " + definition.HasDeclaredMounts
                        + "  airtight: " + (definition.Airtight.HasValue
                            ? definition.Airtight.Value.ToString() : "unstated");
                }

                sb.Append("  ").Append(row.Value.ToString().PadLeft(5)).Append("  ")
                    .Append(row.Key.PadRight(34)).AppendLine(detail);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes out one sealed block and every block it touches, with the mount fractions on
        /// both sides of each joint.
        ///
        /// Conduction here runs across the area where *both* blocks carry a mount surface, so a
        /// zero on either side is a joint that carries nothing. Six zeros is a sealed block, and
        /// this is what shows whether that is the geometry or the parser.
        /// </summary>
        private static void Explain(List<string> examples, Blueprints.Ship ship,
            ThermalSimulation simulation, ThermalNode node)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("    ").Append(node.Block.Name).Append(" at ").Append(node.Block.Position)
                .Append("  faces ").Append(node.TotalExposedFaces)
                .Append("  area ").Append(node.ExposedArea.ToString("n1"))
                .Append("  size ").Append(node.Block.Model.Size)
                .Append("  on ").AppendLine(Trim(ship.Name, 40));

            for (int face = 0; face < Face.Count; face++)
            {
                Vector3I probe = node.Block.Min + Face.Offsets[face];
                BlockInstance neighbour = simulation.Grid.At(probe);

                string mine = node.Block.MountFraction(face).ToString("n2");

                if (neighbour == null)
                {
                    sb.Append("      ").Append(FaceName(face).PadRight(9))
                        .Append("mine ").Append(mine).AppendLine("  nothing there");
                    continue;
                }

                sb.Append("      ").Append(FaceName(face).PadRight(9))
                    .Append("mine ").Append(mine)
                    .Append("  theirs ").Append(neighbour.MountFraction(Face.Opposite(face)).ToString("n2"))
                    .Append("  ").AppendLine(Trim(neighbour.Name, 34));
            }

            examples.Add(sb.ToString().TrimEnd());
        }

        private static string FaceName(int face)
        {
            switch (face)
            {
                case Face.Forward: return "forward";
                case Face.Backward: return "backward";
                case Face.Left: return "left";
                case Face.Right: return "right";
                case Face.Up: return "up";
                default: return "down";
            }
        }

        private static List<Row> Rows(ShipAssembly assembly)
        {
            List<Row> rows = new List<Row>(assembly.NodeCount);

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
            ThermalSolver solver = assembly.Simulations[g].Solver;
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
