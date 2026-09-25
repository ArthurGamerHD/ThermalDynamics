using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class GlowChannelLab
    {
        public const double JustNoticeable = 2.3d;

        public class Band
        {
            public string TypeId;
            public float CriticalKelvin;

            public float StartKelvin;

            public bool BelowDraper;

            public double DeltaE;

            public bool Visible
            {
                get { return DeltaE >= JustNoticeable; }
            }
        }


        public static double[] Lab(Vector3 srgb)
        {

            double r = Linear(srgb.X);

            double g = Linear(srgb.Y);

            double b = Linear(srgb.Z);

            double x = (0.4124564d * r) + (0.3575761d * g) + (0.1804375d * b);
            double y = (0.2126729d * r) + (0.7151522d * g) + (0.0721750d * b);
            double z = (0.0193339d * r) + (0.1191920d * g) + (0.9503041d * b);


            double fx = F(x / 0.95047d);

            double fy = F(y / 1.00000d);

            double fz = F(z / 1.08883d);

            return new[]
            {
                (116d * fy) - 16d,
                500d * (fx - fy),
                200d * (fy - fz)
            };
        }


        private static double Linear(double channel)
        {
            if (channel <= 0.04045d) return channel / 12.92d;
            return Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
        }


        private static double F(double t)
        {
            if (t > 216d / 24389d) return Math.Pow(t, 1d / 3d);
            return ((24389d / 27d * t) + 16d) / 116d;
        }


        public static double DeltaE(Vector3 a, Vector3 b)
        {

            double[] first = Lab(a);

            double[] second = Lab(b);

            double dl = first[0] - second[0];
            double da = first[1] - second[1];
            double db = first[2] - second[2];

            return Math.Sqrt((dl * dl) + (da * da) + (db * db));
        }


        public static List<Band> Bands()
        {

            List<Band> bands = new List<Band>();

            foreach (BlockCatalogLab.TypeRow row in BlockCatalogLab.Catalog())
            {
                float critical = row.Properties.CriticalTemperature;
                if (critical <= 0f) continue;

                float start = Incandescence.GlowStartKelvin(critical);

                bands.Add(new Band
                {
                    TypeId = row.TypeId,
                    CriticalKelvin = critical,
                    StartKelvin = start,
                    BelowDraper = critical < Incandescence.DraperKelvin,

                    DeltaE = DeltaE(Incandescence.Colour(start), Incandescence.Colour(critical))
                });
            }

            bands.Sort(delegate (Band a, Band b) { return b.DeltaE.CompareTo(a.DeltaE); });
            return bands;
        }


        public static double AcrossBlocks(out float coolest, out float hottest)
        {
            coolest = float.PositiveInfinity;
            hottest = 0f;

            foreach (Band band in Bands())
            {
                if (band.CriticalKelvin < coolest) coolest = band.CriticalKelvin;
                if (band.CriticalKelvin > hottest) hottest = band.CriticalKelvin;
            }

            if (float.IsInfinity(coolest)) return 0d;

            return DeltaE(Incandescence.Colour(coolest), Incandescence.Colour(hottest));
        }


        public static double BestBand(out float atKelvin)
        {
            double best = 0d;
            atKelvin = 0f;

            for (float start = Incandescence.ColourFirstKelvin - 200f; start <= 3000f; start += 5f)
            {

                double delta = DeltaE(Incandescence.Colour(start),
                    Incandescence.Colour(start + Incandescence.GlowBandKelvin));

                if (delta <= best) continue;

                best = delta;
                atKelvin = start;
            }

            return best;
        }
    }
}
