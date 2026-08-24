using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What a selective surface on the radiator would be worth**, which is the measurement
    /// [backlog.md](../../docs/backlog.md) `C15` asks for before anyone authors one.
    ///
    /// <para>
    /// `SolarAbsorptivity` is separate from `Emissivity` and no shipped block declares one, so
    /// every block absorbs sunlight at the rate it emits it. Real spacecraft radiators are the one
    /// surface where that is plainly wrong: a second-surface mirror runs `α ≈ 0.08` against
    /// `ε ≈ 0.8`, a factor of ten, because a radiator's whole job is to emit in the infrared
    /// without collecting in the visible. The mod's radiator is authored at `ε 0.35` and therefore
    /// absorbs a third of the sunlight that lands on it.
    /// </para>
    ///
    /// <para>
    /// **The rig is a source with radiators stacked on it, in sun and in shadow**, so the two
    /// halves of the question are separated: absorptivity can only matter where there is sunlight,
    /// and emissivity matters in both. The shadow column is the control — a surface change that
    /// moved it would be a bug in the rig rather than a finding.
    /// </para>
    /// </summary>
    public static class SelectiveSurfaceLab
    {
        /// <summary>Watts of heat the source puts into the stack. As `ModHardwareRetest`.</summary>
        public const float SourceWatts = 75000f;

        /// <summary>Simulated seconds each rig runs for. Long enough for a stack to saturate.</summary>
        private const float Seconds = 14400f;

        /// <summary>One surface, in one sky.</summary>
        public class Row
        {
            public string Surface;

            /// <summary>What the radiator emits with, and what it absorbs sunlight with.</summary>
            public float Emissivity;
            public float Absorptivity;

            public int Radiators;

            /// <summary>Where the source settled in full sun, and in shadow.</summary>
            public float SunlitKelvin;
            public float ShadowKelvin;

            /// <summary>What the sun costs this surface: sunlit minus shadowed.</summary>
            public float SunPenaltyKelvin
            {
                get { return SunlitKelvin - ShadowKelvin; }
            }
        }

        /// <summary>The surfaces worth asking about, and why each one is here.</summary>
        public static List<Row> Surfaces()
        {
            List<Row> rows = new List<Row>();

            // What ships: one number doing both jobs.
            rows.Add(Surface("shipped", 0.35f, -1f));

            // The same radiator with a selective finish: it emits exactly as it does now and stops
            // collecting sunlight. This is the change `C15` is about, and nothing else moves.
            rows.Add(Surface("selective", 0.35f, 0.10f));

            // A real spacecraft radiator: a second-surface mirror, which is both a better emitter
            // and a worse absorber. Here to say how much of the gap is the absorptivity and how
            // much is the emissivity the block is authored at.
            rows.Add(Surface("second-surface mirror", 0.80f, 0.10f));

            // And the emissivity alone, so the two halves of the row above are separable.
            rows.Add(Surface("emissive only", 0.80f, -1f));

            return rows;
        }

        private static Row Surface(string name, float emissivity, float absorptivity)
        {
            Row row = new Row();
            row.Surface = name;
            row.Emissivity = emissivity;
            row.Absorptivity = absorptivity;
            return row;
        }

        /// <summary>Every surface, at one stack size.</summary>
        public static List<Row> Run(int radiators)
        {
            List<Row> rows = Surfaces();

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Radiators = radiators;
                rows[i].SunlitKelvin = Settle(rows[i], radiators, true);
                rows[i].ShadowKelvin = Settle(rows[i], radiators, false);
            }

            return rows;
        }

        /// <summary>
        /// The source's settled temperature with this surface on its radiators.
        ///
        /// **The sun comes across the stack rather than along it.** A radiator is 1x5x2 and its
        /// broad faces are the 5x2 ones, so a sun along the stack's axis lights one end cap of one
        /// block and the rig would answer a question about geometry instead of about surface.
        /// </summary>
        private static float Settle(Row row, int radiators, bool sunlit)
        {
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

        /// <summary>The shipped radiator with one surface property changed, and nothing else.</summary>
        private static BlockModel Resurfaced(float emissivity, float absorptivity)
        {
            BlockModel model = Catalog.Radiator();
            BlockThermalProperties thermal = model.Thermal.Clone();

            thermal.Emissivity = emissivity;
            thermal.SolarAbsorptivity = absorptivity;

            model.Thermal = thermal;
            return model;
        }

        public static string Report(int radiators = 8)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("SELECTIVE SURFACE  (a source under " + radiators
                + " radiators, run to equilibrium)");
            sb.AppendLine();
            sb.Append("  ").Append(SourceWatts.ToString("n0"))
              .AppendLine(" W of heat, sun square on the stack, against the same rig in shadow");
            sb.AppendLine("  absorptivity '-' means it follows the emissivity, which is what ships");
            sb.AppendLine();
            sb.AppendLine("  surface                  emis   absorp     sunlit K    shadow K   sun costs");

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
