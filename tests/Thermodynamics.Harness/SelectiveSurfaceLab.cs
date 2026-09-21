using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class SelectiveSurfaceLab
    {
        public const float SourceWatts = 75000f;

        private const float Seconds = 14400f;

        public class Row
        {
            public string Surface;

            public float Emissivity;
            public float Absorptivity;

            public int Radiators;

            public float SunlitKelvin;
            public float ShadowKelvin;

            public float SunPenaltyKelvin
            {
                get { return SunlitKelvin - ShadowKelvin; }
            }
        }

/// <summary>Surfaces operation.</summary>
        public static List<Row> Surfaces()
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            rows.Add(Surface("shipped", 0.35f, -1f));

            rows.Add(Surface("selective", 0.35f, 0.10f));

            rows.Add(Surface("second-surface mirror", 0.80f, 0.10f));

            rows.Add(Surface("emissive only", 0.80f, -1f));

            return rows;
        }

/// <summary>Surface operation.</summary>
        private static Row Surface(string name, float emissivity, float absorptivity)
        {
/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Surface = name;
            row.Emissivity = emissivity;
            row.Absorptivity = absorptivity;
            return row;
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(int radiators)
        {
/// <summary>Surfaces operation.</summary>
            List<Row> rows = Surfaces();

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Radiators = radiators;
/// <summary>Sets the tle.</summary>
                rows[i].SunlitKelvin = Settle(rows[i], radiators, true);
/// <summary>Sets the tle.</summary>
                rows[i].ShadowKelvin = Settle(rows[i], radiators, false);
            }

            return rows;
        }

/// <summary>Sets the tle.</summary>
        private static float Settle(Row row, int radiators, bool sunlit)
        {
/// <summary>Resurfaced operation.</summary>
            BlockModel radiator = Resurfaced(row.Emissivity, row.Absorptivity);

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LargeReactor(), Vector3I.Zero).Wasting(SourceWatts);
            BlockInstance source = builder.Last;

            int height = 3;
            for (int i = 0; i < radiators; i++)
            {
                builder.Place(radiator, new Vector3I(0, height, 0));
                height += radiator.Size.Y;
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);

            EnvironmentSample sky = sunlit ? Worlds.Space(Vector3.UnitX) : Worlds.Shadow();
            simulation.StepExact((int)(Seconds / simulation.Settings.StepSeconds), sky);

            return simulation.Solver.GetNode(source).Temperature;
        }

/// <summary>Resurfaced operation.</summary>
        private static BlockModel Resurfaced(float emissivity, float absorptivity)
        {
            BlockModel model = Catalog.Radiator();
            BlockThermalProperties thermal = model.Thermal.Clone();

            thermal.Emissivity = emissivity;
            thermal.SolarAbsorptivity = absorptivity;

            model.Thermal = thermal;
            return model;
        }

/// <summary>Report operation.</summary>
        public static string Report(int radiators = 8)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("SELECTIVE SURFACE  (a source under " + radiators
                + " radiators, run to equilibrium)");
            sb.AppendLine();
            sb.Append("  ").Append(SourceWatts.ToString("n0"))
              .AppendLine(" W of heat, sun square on the stack, against the same rig in shadow");
            sb.AppendLine("  absorptivity '-' means it follows the emissivity, which is what ships");
            sb.AppendLine();
            sb.AppendLine("  surface                  emis   absorp     sunlit K    shadow K   sun costs");

/// <summary>Run operation.</summary>
            List<Row> rows = Run(radiators);

            foreach (Row row in rows)
            {
                sb.Append("  ").Append(row.Surface.PadRight(24))
                  .Append(row.Emissivity.ToString("n2").PadLeft(6))
                  .Append((row.Absorptivity < 0f ? "-" : row.Absorptivity.ToString("n2")).PadLeft(9))
                  .Append(row.SunlitKelvin.ToString("n1").PadLeft(13))
                  .Append(row.ShadowKelvin.ToString("n1").PadLeft(12))
                  .Append((row.SunPenaltyKelvin.ToString("n1") + " K").PadLeft(12))
                  .AppendLine();
            }

            return sb.ToString();
        }
    }
}
