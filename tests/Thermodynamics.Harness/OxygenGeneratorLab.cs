using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class OxygenGeneratorLab
    {
        public static readonly float[] Fractions = { 0.2f, 0.3f, 0.4f, 0.6f, 1f };

        public const float SourcedLow = 0.2f;

        public const float SourcedHigh = 0.4f;

        public const float ObservedDuty = 0.063f;

        public enum Draw
        {
            Standby,

            Observed,

            Operational,
        }

        public static readonly Draw[] Draws = { Draw.Standby, Draw.Observed, Draw.Operational };

        public class Row
        {
            public string Subtype;

            public string Name;

            public bool Large;

            public int Cells;

            public float OperationalMegawatts;

            public float WasteFraction;
            public Draw Load;

            public float DrawWatts;

            public float WasteWatts;

            public float BareKelvin;

            public float SkinnedKelvin;

            public float CriticalKelvin;

            public float BareMarginKelvin
            {
                get { return CriticalKelvin - BareKelvin; }
            }

            public float SkinnedMarginKelvin
            {
                get { return CriticalKelvin - SkinnedKelvin; }
            }
        }

/// <summary>Sweep operation.</summary>
        public static List<Row> Sweep()
        {
/// <summary>SweepAt operation.</summary>
            return SweepAt(Fractions);
        }

/// <summary>Shipped operation.</summary>
        public static List<Row> Shipped()
        {
            return SweepAt(new float[]
                { ShippedBlocks.FunctionOf("OxygenGenerator").ConsumerWasteEnergy });
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(float fraction)
        {
/// <summary>SweepAt operation.</summary>
            return SweepAt(new float[] { fraction });
        }

/// <summary>FractionCache operation.</summary>
        private static readonly FractionCache<Row> Cache = new FractionCache<Row>(At);

/// <summary>SweepAt operation.</summary>
        private static List<Row> SweepAt(float[] fractions)
        {
            return Cache.SweepAt(fractions);
        }

/// <summary>At operation.</summary>
        private static List<Row> At(float fraction)
        {
/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            foreach (Vanilla.Block generator in Vanilla.Reference)
            {
                if (generator.TypeId != "OxygenGenerator") continue;

                BlockThermalProperties shipped = generator.Thermal;

                foreach (Draw load in Draws)
                {
/// <summary>DrawWattsOf operation.</summary>
                    float watts = DrawWattsOf(generator, load);

                    rows.Add(new Row
                    {
                        Subtype = generator.Subtype,
/// <summary>NameOf operation.</summary>
                        Name = NameOf(generator),
                        Large = generator.Large,
                        Cells = generator.CellCount,
                        OperationalMegawatts = generator.PowerDrawMegawatts,
                        WasteFraction = fraction,
                        Load = load,
                        DrawWatts = watts,
                        WasteWatts = watts * fraction,
/// <summary>Sets the tled.</summary>
                        BareKelvin = Settled(generator, shipped, fraction, watts, false),
/// <summary>Sets the tled.</summary>
                        SkinnedKelvin = Settled(generator, shipped, fraction, watts, true),
                        CriticalKelvin = shipped.CriticalTemperature,
                    });
                }
            }

            return rows;
        }

/// <summary>DrawWattsOf operation.</summary>
        public static float DrawWattsOf(Vanilla.Block generator, Draw load)
        {
            float operational = generator.PowerDrawMegawatts * ThermalConstants.MegawattsToWatts;

            switch (load)
            {
                case Draw.Standby:
                    return generator.StandbyDrawMegawatts * ThermalConstants.MegawattsToWatts;
                case Draw.Observed:
                    return operational * ObservedDuty;
                default:
                    return operational;
            }
        }

/// <summary>NameOf operation.</summary>
        public static string NameOf(Vanilla.Block generator)
        {
            return generator.Subtype.Length > 0
                ? generator.Subtype
                : generator.TypeId + " (no subtype)";
        }

/// <summary>Sets the tled.</summary>
        private static float Settled(Vanilla.Block generator, BlockThermalProperties shipped,
            float fraction, float drawnWatts, bool skinned)
        {
            BlockThermalProperties thermal = SoloBlockRig.Clone(shipped);
            thermal.ConsumerWasteEnergy = fraction;

            return SoloBlockRig.Settled(generator, thermal, drawnWatts,
                SoloBlockRig.Power.Consumed, skinned);
        }

/// <summary>LabelOf operation.</summary>
        private static string LabelOf(Draw load)
        {
            switch (load)
            {
                case Draw.Standby: return "standby";
                case Draw.Observed: return "observed";
                default: return "operational";
            }
        }

/// <summary>Report operation.</summary>
        public static string Report()
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("OXYGEN GENERATOR WASTE HEAT  (one generator, shadow, 4 h to steady state)");
            sb.AppendLine("  bare     alone on the grid, every face radiating: the best case there is");
            sb.AppendLine("  skinned  under one cell of light armour: how a generator is actually installed");
            sb.AppendLine();
            sb.AppendLine("  standby      StandbyPowerConsumption, from the definition");
            sb.AppendLine("  observed     " + (ObservedDuty * 100f).ToString("n1")
                + " % of rating, the mean of 346 field instances — one sample");
            sb.AppendLine("  operational  OperationalPowerConsumption, from the definition");
            sb.AppendLine();
            sb.AppendLine("  shipped fraction 0.60 (invented); water electrolysis sources "
                + SourcedLow.ToString("n2") + "-" + SourcedHigh.ToString("n2"));
            sb.AppendLine();
            sb.AppendLine("block                             cells  rated MW  fraction  load           draw W"
                + "     waste W    bare K  margin  skin K  margin");

            foreach (Row row in Sweep())
            {
                sb.Append(row.Name.PadRight(34));
                sb.Append(row.Cells.ToString().PadLeft(5));
                sb.Append(row.OperationalMegawatts.ToString("n2").PadLeft(10));
                sb.Append(row.WasteFraction.ToString("n2").PadLeft(10));
                sb.Append("  ").Append(LabelOf(row.Load).PadRight(13));
                sb.Append(row.DrawWatts.ToString("n0").PadLeft(9));
                sb.Append(row.WasteWatts.ToString("n0").PadLeft(12));
                sb.Append(row.BareKelvin.ToString("n1").PadLeft(10));
                sb.Append(row.BareMarginKelvin.ToString("n1").PadLeft(8));
                sb.Append(row.SkinnedKelvin.ToString("n1").PadLeft(8));
                sb.Append(row.SkinnedMarginKelvin.ToString("n1").PadLeft(8));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
