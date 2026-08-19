using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Runs one rig under every <see cref="BalanceProfile"/> and reports what each buys and costs.
    ///
    /// Three axes, because a profile can only be judged on all three at once:
    ///
    /// * **Responsiveness** — how fast heat actually moves, as kelvin per second on a known load.
    ///   The number a player perceives.
    /// * **Cost** — substeps per step and link visits per simulated second. Machine-independent,
    ///   so the figures survive being read on a different box than they were taken on.
    /// * **Fidelity** — where the rig settles, and whether the blocks still rank in the right
    ///   order. Equilibrium is the claim the whole model rests on: pace is documented and pinned
    ///   not to move it, so a profile whose equilibrium *has* moved has stopped simulating and
    ///   started approximating.
    ///
    /// The rig is deliberately small. A profile at a real thermal clock is 225 times slower than
    /// the shipped one, so reaching the same thermal progress takes 225 times the simulated
    /// seconds; a large grid would make that unrunnable and the comparison would quietly become a
    /// comparison of the profiles that were affordable.
    /// </summary>
    public static class ProfileLab
    {
        /// <summary>Watts of heat into the rig. Stated as heat, not power — the rig's fraction is 1.</summary>
        public const float LoadWatts = 200000f;

        /// <summary>One profile, measured.</summary>
        public class Row
        {
            public string Name;
            public string Intent;

            public float HeatTimeScale;
            public float ConductionPace;

            /// <summary>Kelvin per second at the moment the load is applied, from 293 K.</summary>
            public float InitialRateKelvinPerSecond;

            /// <summary>Where the source settled, K. Empty when the run did not converge.</summary>
            public float SettledKelvin;

            /// <summary>Where the panel settled, K.</summary>
            public float PanelKelvin;

            /// <summary>True when the last two samples agreed to within a tenth of a kelvin.</summary>
            public bool Converged;

            /// <summary>Mean substeps a step asked for.</summary>
            public float SubstepsPerStep;

            /// <summary>Conduction link visits per simulated second — the cost that scales.</summary>
            public float LinkVisitsPerSecond;

            /// <summary>Simulated seconds the run needed to get there.</summary>
            public float SimulatedSeconds;

            /// <summary>Solver steps the run took. What reaching that answer actually cost.</summary>
            public long Steps;

            /// <summary>Kelvin between this profile's settling point and the physical one.</summary>
            public float KelvinFromPhysical;
        }

        /// <summary>
        /// A heat source with a panel on it, and a hull under it. Small enough that a real thermal
        /// clock is still affordable, complete enough that conduction, radiation and a mounted
        /// panel are all in play.
        /// </summary>
        private static ThermalSimulation Rig(BalanceProfile profile, out BlockInstance source,
            out BlockInstance panel)
        {
            GridBuilder builder = GridBuilder.Large();

            BlockThermalProperties heaterThermal = profile.Material(Catalog.ReactorThermal());
            heaterThermal.ProducerWasteEnergy = 1f;
            heaterThermal.ConsumerWasteEnergy = 1f;

            builder.Place(BlockModel.Solid("Heater", Vector3I.One, 3000f, heaterThermal), Vector3I.Zero);
            builder.Last.PowerConsumedWatts = LoadWatts;
            source = builder.Last;

            BlockModel armour = BlockModel.Solid("Armour", Vector3I.One, 500f,
                profile.Material(Catalog.DefaultThermal()));
            builder.Place(armour, new Vector3I(1, 0, 0));
            builder.Place(armour, new Vector3I(-1, 0, 0));

            ShippedBlocks.Definition radiator = ShippedBlocks.Get("Gauge_LG_Radiator");
            BlockModel panelModel = BlockModel.Solid("Radiator", radiator.Size, radiator.Mass,
                profile.Material(radiator.Thermal));
            foreach (Vector3I cell in panelModel.LocalCells())
            {
                int state = CellSurface.SelfAirtightMask;
                state = CellSurface.WithSelfMount(state, Face.Up, true);
                state = CellSurface.WithSelfMount(state, Face.Down, true);
                panelModel.SetLocalSurface(cell, state);
            }
            builder.Place(panelModel, new Vector3I(0, 1, 0));
            panel = builder.Last;

            // A foil bolted to the heater. Stiffness is conductance over capacity, so the stiffest
            // thing a player can build cheaply is something very light against something very hot,
            // and it is the only part of a rig that makes the substep estimate say anything. Without
            // it every profile reports one substep and the cost column compares nothing.
            builder.Place(BlockModel.Solid("Foil", Vector3I.One, 10f,
                profile.Material(Catalog.DefaultThermal())), new Vector3I(0, 0, 1));

            return builder.BuildSimulation(profile.ToSettings(), 293.15f);
        }

        /// <summary>Every profile, measured against the physical one.</summary>
        public static List<Row> Measure()
        {
            List<Row> rows = new List<Row>();
            foreach (BalanceProfile profile in BalanceProfile.All())
            {
                rows.Add(MeasureOne(profile));
            }

            // Fidelity is stated against the physical profile, so it has to be measured first and
            // the differences filled in afterwards.
            float physical = 0f;
            foreach (Row row in rows)
            {
                if (row.Name == "physical") physical = row.SettledKelvin;
            }
            foreach (Row row in rows)
            {
                row.KelvinFromPhysical = row.SettledKelvin - physical;
            }
            return rows;
        }

        private static Row MeasureOne(BalanceProfile profile)
        {
            BlockInstance source;
            BlockInstance panel;
            ThermalSimulation simulation = Rig(profile, out source, out panel);

            Row row = new Row
            {
                Name = profile.Name,
                Intent = profile.Intent,
                HeatTimeScale = profile.HeatTimeScale,
                ConductionPace = profile.ConductionPace,
            };

            // Responsiveness: the rise over the first second, from a cold start. Independent of how
            // long the run goes on for, and the figure a player actually perceives.
            ScenarioRunner opening = new ScenarioRunner(simulation);
            opening.Environment = t => Worlds.Shadow();
            opening.Track("source", source);
            opening.Run(1f, 1f);

            float after;
            opening.Final.Tracked.TryGetValue("source", out after);
            row.InitialRateKelvinPerSecond = after - 293.15f;

            // Thermal progress per simulated second scales with the clock, so equal progress needs
            // simulated seconds inversely proportional to it. Capped, because the physical clock
            // would otherwise ask for millions of steps to say what pace equivalence already pins.
            float seconds = 4000f * (225f / Math.Max(1f, profile.HeatTimeScale));
            seconds = Math.Min(seconds, 120000f);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);
            runner.Track("panel", panel);
            runner.Run(seconds, seconds / 20f);

            float settled;
            float panelKelvin;
            runner.Final.Tracked.TryGetValue("source", out settled);
            runner.Final.Tracked.TryGetValue("panel", out panelKelvin);
            row.SettledKelvin = settled;
            row.PanelKelvin = panelKelvin;
            row.SimulatedSeconds = seconds + 1f;
            row.Steps = (long)(seconds * simulation.Settings.StepsPerSecond);

            // Converged when the last two samples agree: a profile that ran out of budget before
            // settling must say so rather than have its last reading read as an equilibrium.
            IList<Sample> samples = runner.Samples;
            if (samples.Count >= 2)
            {
                float previous;
                samples[samples.Count - 2].Tracked.TryGetValue("source", out previous);
                row.Converged = Math.Abs(previous - settled) < 0.1f;
            }

            // Cost. Substeps are averaged over the samples; link visits are per simulated second so
            // the figure is comparable between profiles that ran for different lengths of time.
            float substeps = 0f;
            for (int i = 0; i < samples.Count; i++) substeps += samples[i].Substeps;
            row.SubstepsPerStep = samples.Count > 0 ? substeps / samples.Count : 0f;

            ThermalSettings settings = simulation.Settings;
            row.LinkVisitsPerSecond = simulation.Solver.LinkCount
                * row.SubstepsPerStep * settings.StepsPerSecond;

            return row;
        }

        // ---- reporting ---------------------------------------------------------------------------

        private static string N(float value, int decimals = 1)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Report()
        {
            StringBuilder sb = new StringBuilder();
            List<Row> rows = Measure();

            sb.AppendLine("PROFILES  (" + N(LoadWatts / 1000f, 0)
                + " kW into one block with a panel on it, shadow)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-11} {1,7} {2,7} {3,9} {4,10} {5,9} {6,7} {7,10} {8,13}",
                "profile", "clock", "cond", "K in 1s", "settled K", "panel K", "conv", "substeps", "visits/s"));

            foreach (Row row in rows)
            {
                sb.AppendLine(string.Format("{0,-11} {1,7} {2,7} {3,9} {4,10} {5,9} {6,7} {7,10} {8,13}",
                    row.Name, N(row.HeatTimeScale, 0), N(row.ConductionPace, 2),
                    N(row.InitialRateKelvinPerSecond, 2), N(row.SettledKelvin, 1), N(row.PanelKelvin, 1),
                    row.Converged ? "yes" : "NO", N(row.SubstepsPerStep, 2),
                    N(row.LinkVisitsPerSecond, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("fidelity, against the physical profile's settling point:");
            foreach (Row row in rows)
            {
                sb.AppendLine(string.Format("  {0,-11} {1,10} K   ({2} steps over {3} simulated s)",
                    row.Name, N(row.KelvinFromPhysical, 1), row.Steps, N(row.SimulatedSeconds, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("what no profile can fix — the structural gaps:");
            foreach (string gap in BalanceProfile.RealismGaps)
            {
                sb.AppendLine();
                sb.AppendLine("  * " + Wrap(gap, 92, "    "));
            }

            return sb.ToString();
        }

        private static string Wrap(string text, int width, string indent)
        {
            StringBuilder sb = new StringBuilder();
            int line = 0;
            foreach (string word in text.Split(' '))
            {
                if (line > 0 && line + word.Length + 1 > width)
                {
                    sb.Append(Environment.NewLine).Append(indent);
                    line = 0;
                }
                else if (line > 0)
                {
                    sb.Append(' ');
                    line++;
                }
                sb.Append(word);
                line += word.Length;
            }
            return sb.ToString();
        }
    }
}
