using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class ReactorLab
    {
        public static readonly float[] Fractions = { 0f, 0.01f, 0.02f, 0.05f, 0.1f, 0.25f };

        public static readonly float[] Loads = { 0.1f, 0.5f, 1f };

        public class Row
        {
            public string Subtype;
            public float RatedMegawatts;
            public float WasteFraction;
            public float LoadFraction;

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


        public static List<Row> Sweep()
        {

            return SweepAt(Fractions);
        }


        public static List<Row> Shipped()
        {
            return SweepAt(new float[] { ShippedBlocks.FunctionOf("Reactor").ProducerWasteEnergy });
        }


        public static List<Row> Run(float fraction)
        {

            return SweepAt(new float[] { fraction });
        }


        private static readonly FractionCache<Row> Cache = new FractionCache<Row>(At);


        private static List<Row> SweepAt(float[] fractions)
        {
            return Cache.SweepAt(fractions);
        }


        private static List<Row> At(float fraction)
        {

            List<Row> rows = new List<Row>();

            foreach (Vanilla.Block reactor in Vanilla.Reference)
            {
                if (reactor.TypeId != "Reactor") continue;

                BlockThermalProperties shipped = reactor.Thermal;

                foreach (float load in Loads)
                {
                    float watts = reactor.PowerOutputMegawatts * ThermalConstants.MegawattsToWatts * load;

                    rows.Add(new Row
                    {
                        Subtype = reactor.Subtype,
                        RatedMegawatts = reactor.PowerOutputMegawatts,
                        WasteFraction = fraction,
                        LoadFraction = load,
                        WasteWatts = watts * fraction,

                        BareKelvin = Settled(reactor, shipped, fraction, watts, false),

                        SkinnedKelvin = Settled(reactor, shipped, fraction, watts, true),
                        CriticalKelvin = shipped.CriticalTemperature,
                    });
                }
            }

            return rows;
        }


        private static float Settled(Vanilla.Block reactor, BlockThermalProperties shipped,
            float fraction, float producedWatts, bool skinned)
        {
            BlockThermalProperties thermal = SoloBlockRig.Clone(shipped);
            thermal.ProducerWasteEnergy = fraction;

            return SoloBlockRig.Settled(reactor, thermal, producedWatts,
                SoloBlockRig.Power.Produced, skinned);
        }


        public static string Report()
        {

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("REACTOR WASTE HEAT  (one reactor, shadow, 4 h to steady state)");
            sb.AppendLine("  bare     alone on the grid, every face radiating: the best case there is");
            sb.AppendLine("  skinned  under one cell of light armour: how a reactor is actually installed");
            sb.AppendLine();
            sb.AppendLine("block                         rated MW  fraction  load     waste W    bare K  margin  skin K  margin");

            foreach (Row row in Sweep())
            {
                sb.Append(row.Subtype.PadRight(29));
                sb.Append(row.RatedMegawatts.ToString("n2").PadLeft(8));
                sb.Append(row.WasteFraction.ToString("n3").PadLeft(10));
                sb.Append((row.LoadFraction * 100f).ToString("n0").PadLeft(5)).Append('%');
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
