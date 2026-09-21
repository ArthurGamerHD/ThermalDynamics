using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ProfileLab
    {
        public const float LoadWatts = 200000f;

        public class Row
        {
            public string Name;
            public string Intent;

            public float HeatTimeScale;
            public float ConductionPace;

            public float InitialRateKelvinPerSecond;

            public float SettledKelvin;

            public float PanelKelvin;

            public bool Converged;

            public float SubstepsPerStep;

            public float LinkVisitsPerSecond;

            public float SimulatedSeconds;

            public long Steps;

            public float KelvinFromPhysical;
        }

/// <summary>Rig operation.</summary>
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

            builder.Place(BlockModel.Solid("Foil", Vector3I.One, 10f,
                profile.Material(Catalog.DefaultThermal())), new Vector3I(0, 0, 1));

            return builder.BuildSimulation(profile.ToSettings(), 293.15f);
        }

/// <summary>Measure operation.</summary>
        public static List<Row> Measure()
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();
            foreach (BalanceProfile profile in BalanceProfile.All())
            {
                rows.Add(MeasureOne(profile));
            }

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

/// <summary>MeasureOne operation.</summary>
        private static Row MeasureOne(BalanceProfile profile)
        {
            BlockInstance source;
            BlockInstance panel;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(profile, out source, out panel);

            Row row = new Row
            {
                Name = profile.Name,
                Intent = profile.Intent,
                HeatTimeScale = profile.HeatTimeScale,
                ConductionPace = profile.ConductionPace,
            };

/// <summary>ScenarioRunner operation.</summary>
            ScenarioRunner opening = new ScenarioRunner(simulation);
            opening.Environment = t => Worlds.Shadow();
            opening.Track("source", source);
            opening.Run(1f, 1f);

            float after;
            opening.Final.Tracked.TryGetValue("source", out after);
            row.InitialRateKelvinPerSecond = after - 293.15f;

            float seconds = 4000f * (225f / Math.Max(1f, profile.HeatTimeScale));
            seconds = Math.Min(seconds, 120000f);

/// <summary>ScenarioRunner operation.</summary>
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

            IList<Sample> samples = runner.Samples;
            if (samples.Count >= 2)
            {
                float previous;
                samples[samples.Count - 2].Tracked.TryGetValue("source", out previous);
                row.Converged = Math.Abs(previous - settled) < 0.1f;
            }

            float substeps = 0f;
            for (int i = 0; i < samples.Count; i++) substeps += samples[i].Substeps;
            row.SubstepsPerStep = samples.Count > 0 ? substeps / samples.Count : 0f;

            ThermalSettings settings = simulation.Settings;
            row.LinkVisitsPerSecond = simulation.Solver.LinkCount
                * row.SubstepsPerStep * settings.StepsPerSecond;

            return row;
        }


/// <summary>N operation.</summary>
        private static string N(float value, int decimals = 1)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

/// <summary>Report operation.</summary>
        public static string Report()
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
/// <summary>Measure operation.</summary>
            List<Row> rows = Measure();

            sb.AppendLine("PROFILES  (" + N(LoadWatts / 1000f, 0)
                + " kW into one block with a panel on it, shadow)");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-11} {1,7} {2,7} {3,9} {4,10} {5,9} {6,7} {7,10} {8,13}",
                "profile", "clock", "cond", "K in 1s", "settled K", "panel K", "conv", "substeps", "visits/s"));

            foreach (Row row in rows)
            {
                sb.AppendLine(string.Format("{0,-11} {1,7} {2,7} {3,9} {4,10} {5,9} {6,7} {7,10} {8,13}",
/// <summary>N operation.</summary>
                    row.Name, N(row.HeatTimeScale, 0), N(row.ConductionPace, 2),
                    N(row.InitialRateKelvinPerSecond, 2), N(row.SettledKelvin, 1), N(row.PanelKelvin, 1),
/// <summary>N operation.</summary>
                    row.Converged ? "yes" : "NO", N(row.SubstepsPerStep, 2),
                    N(row.LinkVisitsPerSecond, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("fidelity, against the physical profile's settling point:");
            foreach (Row row in rows)
            {
                sb.AppendLine(string.Format("  {0,-11} {1,10} K   ({2} steps over {3} simulated s)",
/// <summary>N operation.</summary>
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

/// <summary>Wrap operation.</summary>
        private static string Wrap(string text, int width, string indent)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            int line = 0;
            foreach (string word in text.Split(' '))
            {
                if (line > 0 && line + word.Length + 1 > width)
                {
                    sb.Append(Environment.NewLine).Append(indent);
                    line = 0;
                }
/// <summary>if operation.</summary>
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
