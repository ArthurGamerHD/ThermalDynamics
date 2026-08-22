using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What is the best thing you can bolt to a reactor, and how far does adding more of it get
    /// you?**
    ///
    /// <para>
    /// <see cref="BalanceLab"/> already asks a narrow version of this — the shipped radiator against
    /// an armour slab of the same volume — and answers it on a synthetic heater. That comparison
    /// decides whether the radiator beats *doing nothing in particular*. It does not decide whether
    /// the radiator beats the best thing a player could build without the mod installed, which is
    /// the question that says whether these blocks earn their place.
    /// </para>
    ///
    /// <para>
    /// So: one heat source, held fixed and driven at its plate rating, and every block that has a
    /// plausible claim to being the best cooling in the game mounted against it the same way, in
    /// steadily increasing numbers. The source is the largest reactor the installed game has,
    /// because a cooling block's worth is decided at the top of the load range, not the middle.
    /// </para>
    ///
    /// <para>
    /// **The candidates are chosen by the model, not by taste.** Radiative cooling scales with
    /// exposed area and emissivity, so the shortlist is every block whose thermal derivation gives
    /// it more surface than an ordinary cube of the same size — heat vents at three times, exhausts
    /// at two, thrusters and turbines and ladders at one and a half — plus the mod's own radiator,
    /// plus a plain armour cube as the control. If something with an ordinary surface beats the
    /// radiator, that is a finding about the model rather than about the block.
    /// </para>
    ///
    /// <para>
    /// **Coolant loops and heat pumps are deliberately absent.** They are not per-block coolers and
    /// a ladder is the wrong shape for them: one pipe cools nothing, because a loop needs a ring, a
    /// pump and a sink face before it moves a watt. <see cref="BalanceLab"/>'s coolant ring table
    /// is where ring length belongs.
    /// </para>
    /// </summary>
    public static class CoolingLadder
    {
        /// <summary>Block counts to try. Doubling, because the interesting part is the curve's knee.</summary>
        private static readonly int[] Counts = { 1, 2, 4, 8, 16, 32 };

        /// <summary>Simulated seconds each rig is run for, and the sample interval.</summary>
        private const float Seconds = 14400f;

        /// <summary>One block type at one count.</summary>
        public class Row
        {
            public string Block;
            public int Count;

            /// <summary>Where the reactor settled. Lower is better cooling.</summary>
            public float SourceKelvin;

            /// <summary>The hottest block anywhere in the rig.</summary>
            public float PeakKelvin;

            /// <summary>Watts leaving the rig at equilibrium.</summary>
            public float VentedWatts;

            /// <summary>Kilograms of cooling added — the count times the block.</summary>
            public float Mass;

            /// <summary>Kelvin below the bare source. The whole point of the row.</summary>
            public float Saved;

            /// <summary>Kelvin saved per tonne of cooling, which is how a ship pays for it.</summary>
            public float SavedPerTonne
            {
                get { return Mass <= 0f ? 0f : Saved / (Mass / 1000f); }
            }

            /// <summary>Kelvin the previous rung on the ladder did not already save.</summary>
            public float Marginal;

            /// <summary>
            /// The far end of the stack, K, and the column that says which kind of flat a flat
            /// ladder is.
            ///
            /// A stack that has saturated is hot at the top: the heat reached it and there was
            /// nowhere further to send it. A stack that is not bolted together sits at the
            /// temperature it was built at, having never received a watt — which reads in the
            /// saving column exactly like saturation and is a different fact about the block.
            /// </summary>
            public float TopKelvin;
        }

        /// <summary>
        /// The blocks worth trying, largest surface advantage first, with the mod's radiator and a
        /// plain cube for reference. A subtype the installed game does not carry is skipped rather
        /// than faked.
        /// </summary>
        private static List<KeyValuePair<string, BlockModel>> Candidates()
        {
            List<KeyValuePair<string, BlockModel>> candidates =
                new List<KeyValuePair<string, BlockModel>>();

            BlockModel radiator = null;
            try
            {
                radiator = ShippedBlocks.Model("Gauge_LG_Radiator");
            }
            catch
            {
                radiator = null;
            }

            if (radiator != null)
            {
                candidates.Add(new KeyValuePair<string, BlockModel>("Gauge_LG_Radiator", radiator));
            }

            string[] vanilla =
            {
                "LargeHeatVentBlock",     // 3x surface: the best the game gives away
                "LargeExhaustPipe",       // 2x
                "LargeBlockLargeThrust",  // 1.5x, and one a ship already carries
                "LargeBlockWindTurbine",  // 1.5x
                "LadderShaft",            // 1.5x, and nearly free
                "LargeBlockArmorBlock",   // 1x: the control
            };

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            foreach (string subtype in vanilla)
            {
                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(subtype, out definition))
                {
                    // **Named, not skipped quietly.** These subtype ids are written here by hand and
                    // the game renames blocks between versions. A candidate that silently vanishes
                    // turns this into a table comparing the radiator against nothing, which reads
                    // exactly like the radiator winning.
                    missing.Add(subtype);
                    continue;
                }

                candidates.Add(new KeyValuePair<string, BlockModel>(
                    subtype, Blueprints.Model(definition)));
            }

            return candidates;
        }

        /// <summary>Candidate subtypes the installed game did not carry under that name.</summary>
        private static readonly List<string> missing = new List<string>();

        /// <summary>The largest reactor the game ships, which is the load this is decided at.</summary>
        private static GameBlocks.Definition BiggestReactor()
        {
            GameBlocks.Definition biggest = null;

            foreach (GameBlocks.Definition definition in GameBlocks.All())
            {
                if (definition.TypeId != "Reactor") continue;
                if (!definition.SubtypeId.StartsWith("Large")) continue;

                if (biggest == null || definition.PowerOutputWatts > biggest.PowerOutputWatts)
                {
                    biggest = definition;
                }
            }

            return biggest;
        }

        /// <summary>
        /// The reactor with <paramref name="count"/> coolers stacked against it in a single column,
        /// run to equilibrium in shadow.
        ///
        /// <para>
        /// A column, and the same column for every candidate, because the comparison has to hold
        /// the mounting fixed — a block that wins only because it was given more faces to the sky
        /// has not won. It does mean the far end of a tall stack is several joints from the source
        /// and conducting through everything below it, which is exactly what happens on a ship and
        /// is part of what is being measured: a cooler that cannot get the heat *into* itself is no
        /// better than one that cannot radiate it away.
        /// </para>
        /// </summary>
        private static Row Rung(string name, BlockModel cooler, GameBlocks.Definition reactor,
            int count, float bare)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Blueprints.Model(reactor), Vector3I.Zero);
            builder.Last.PowerProducedWatts = reactor.PowerOutputWatts;
            BlockInstance source = builder.Last;

            int height = Math.Max(1, reactor.Size.Y);
            float mass = 0f;
            BlockInstance top = null;

            for (int i = 0; i < count; i++)
            {
                builder.Place(cooler, new Vector3I(0, height, 0));
                mass += builder.Last.Mass;
                top = builder.Last;
                height += Math.Max(1, cooler.Size.Y);
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.StepExact((int)(Seconds / simulation.Settings.StepSeconds), Worlds.Shadow());

            ThermalNode node = simulation.Solver.GetNode(source);
            float peak = 0f;
            for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
            {
                float kelvin = simulation.Solver.Nodes[i].Temperature;
                if (kelvin > peak) peak = kelvin;
            }

            Row row = new Row
            {
                Block = name,
                Count = count,
                SourceKelvin = node == null ? 0f : node.Temperature,
                PeakKelvin = peak,
                VentedWatts = simulation.VentedWatts,
                Mass = mass,
            };

            ThermalNode far = top == null ? null : simulation.Solver.GetNode(top);
            row.TopKelvin = far == null ? 0f : far.Temperature;

            row.Saved = bare - row.SourceKelvin;
            return row;
        }

        /// <summary>Every candidate at every count, plus the bare reactor they are measured against.</summary>
        public static List<Row> Run(out float bare)
        {
            List<Row> rows = new List<Row>();
            bare = 0f;

            GameBlocks.Definition reactor = BiggestReactor();
            if (reactor == null) return rows;

            // The reactor on its own. Every saving below is measured from here, so it is run first
            // and with the same clock as the rest.
            Row alone = Rung("(bare reactor)", null, reactor, 0, 0f);
            bare = alone.SourceKelvin;
            alone.Saved = 0f;
            rows.Add(alone);

            foreach (KeyValuePair<string, BlockModel> candidate in Candidates())
            {
                float previous = 0f;

                foreach (int count in Counts)
                {
                    Row row = Rung(candidate.Key, candidate.Value, reactor, count, bare);
                    row.Marginal = row.Saved - previous;
                    previous = row.Saved;
                    rows.Add(row);
                }
            }

            return rows;
        }

        public static string Report()
        {
            if (!GameBlocks.IsInstalled)
            {
                return "COOLING LADDER\n\n  needs a game install; set SE_BIN.\n";
            }

            float bare;
            List<Row> rows = Run(out bare);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("COOLING LADDER");
            sb.AppendLine();
            sb.Append("  the largest reactor at its plate rating, in shadow, with coolers stacked ")
                .AppendLine("against it");
            sb.Append("  bare, it settles at ").Append(bare.ToString("n0")).AppendLine(" K");
            sb.AppendLine();
            sb.AppendLine("  block                       n   source K   saved K   per tonne   marginal      top K");

            foreach (Row row in rows)
            {
                sb.Append("  ").Append(row.Block.PadRight(26));
                sb.Append(row.Count.ToString().PadLeft(3));
                sb.Append(row.SourceKelvin.ToString("n1").PadLeft(11));

                // Two places on the saving and three on the marginal, because a bolted stack of
                // radiators saves a fraction of a kelvin per block and whole kelvin cannot tell
                // that from nothing at all. Reading the column as flat was the first thing this
                // table did after it was wired up, and it was the rounding rather than the physics.
                sb.Append(row.Saved.ToString("n2").PadLeft(10));
                sb.Append(row.SavedPerTonne.ToString("n3").PadLeft(12));
                sb.Append(row.Marginal.ToString("n3").PadLeft(11));
                sb.Append(row.TopKelvin.ToString("n1").PadLeft(11));
                sb.AppendLine();
            }

            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  candidates this install does not carry under that name:");
                foreach (string subtype in missing) sb.Append("    ").AppendLine(subtype);
                sb.AppendLine("  Until those are named correctly the comparison is short of contenders.");
            }

            sb.AppendLine();
            sb.Append("  A candidate that beats the radiator is a finding about the radiator. ")
                .AppendLine("A row whose");
            sb.AppendLine("  marginal saving has gone flat is the point past which more of it buys nothing —");
            sb.Append("  and the top K column says which flat it is: hot at the far end means the ")
                .AppendLine("stack");
            sb.AppendLine("  saturated, cold means the heat never reached it.");
            return sb.ToString();
        }
    }
}
