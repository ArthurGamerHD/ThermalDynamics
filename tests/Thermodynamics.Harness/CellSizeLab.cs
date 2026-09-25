using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class CellSizeLab
    {
        public const int ReferenceSinkFaces = 1;

        public class Pair
        {
            public string Family;
            public string TypeId;

            public BlockHeatIndex.Reading Large;
            public BlockHeatIndex.Reading Small;

            public float WasteRatio;
            public float RadiationRatio;
            public float ConductionRatio;
            public float PickupRatio;


            public float RadiationHandicap { get { return Quotient(WasteRatio, RadiationRatio); } }

            public float ConductionHandicap { get { return Quotient(WasteRatio, ConductionRatio); } }

            public float PickupHandicap { get { return Quotient(WasteRatio, PickupRatio); } }

            public float LargeGradient;
            public float SmallGradient;

            public bool LargeUncoolable;
            public bool SmallUncoolable;


            private static float Quotient(float waste, float term)
            {
                return term <= 0f ? float.PositiveInfinity : waste / term;
            }
        }


        public static float ForcedGradient(float watts, float cellSize, int faces,
            float coefficient)
        {
            if (faces < 1) faces = 1;
            float conductance = coefficient * cellSize * cellSize * faces;
            return conductance <= 0f ? float.PositiveInfinity : watts / conductance;
        }


        public static List<Pair> Pairs()
        {
            return Pairs(LoopThermalProperties.Default().HeatTransferCoefficient);
        }


        public static List<Pair> Pairs(float coefficient)
        {
            Dictionary<string, BlockHeatIndex.Reading> larges =
                new Dictionary<string, BlockHeatIndex.Reading>(StringComparer.Ordinal);
            Dictionary<string, BlockHeatIndex.Reading> smalls =
                new Dictionary<string, BlockHeatIndex.Reading>(StringComparer.Ordinal);

            List<BlockHeatIndex.Reading> readings = BlockHeatIndex.All();
            for (int i = 0; i < readings.Count; i++)
            {
                BlockHeatIndex.Reading r = readings[i];

                string family = Family(r.Subtype, r.Large);
                if (family == null) continue;

                string key = r.TypeId + "/" + family;
                if (r.Large) larges[key] = r; else smalls[key] = r;
            }


            List<Pair> pairs = new List<Pair>();

            foreach (KeyValuePair<string, BlockHeatIndex.Reading> entry in larges)
            {
                BlockHeatIndex.Reading small;
                if (!smalls.TryGetValue(entry.Key, out small)) continue;

                BlockHeatIndex.Reading large = entry.Value;
                if (large.Watts <= 0f) continue;

                pairs.Add(Measure(large, small, coefficient));
            }

            pairs.Sort(delegate (Pair a, Pair b)
            {
                return b.PickupHandicap.CompareTo(a.PickupHandicap);
            });
            return pairs;
        }


        private static Pair Measure(BlockHeatIndex.Reading large, BlockHeatIndex.Reading small,
            float coefficient)
        {

            Pair pair = new Pair();
            pair.TypeId = large.TypeId;

            pair.Family = Family(large.Subtype, true);
            pair.Large = large;
            pair.Small = small;

            pair.WasteRatio = small.Watts / large.Watts;


            pair.RadiationRatio = Ratio(small.RadiatedWatts, large.RadiatedWatts);

            pair.ConductionRatio = Ratio(small.ConductedWatts, large.ConductedWatts);

            float largePickup = coefficient * Catalog.LargeGridSize * Catalog.LargeGridSize;
            float smallPickup = coefficient * Catalog.SmallGridSize * Catalog.SmallGridSize;
            pair.PickupRatio = smallPickup / largePickup;


            pair.LargeGradient = ForcedGradient(large.Watts, Catalog.LargeGridSize,
                ReferenceSinkFaces, coefficient);

            pair.SmallGradient = ForcedGradient(small.Watts, Catalog.SmallGridSize,
                ReferenceSinkFaces, coefficient);

            pair.LargeUncoolable = pair.LargeGradient
                > large.CriticalKelvin - BlockHeatIndex.AmbientKelvin;
            pair.SmallUncoolable = pair.SmallGradient
                > small.CriticalKelvin - BlockHeatIndex.AmbientKelvin;

            return pair;
        }


        private static float Ratio(float small, float large)
        {
            return large <= 0f ? float.NaN : small / large;
        }


        public static string Family(string subtype, bool large)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            string prefix = large ? "Large" : "Small";
            if (!subtype.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (subtype.Length == prefix.Length) return null;

            return subtype.Substring(prefix.Length);
        }


        public static float MedianHandicap(IList<Pair> pairs, Func<Pair, float> term,
            float wattsFloor)
        {

            List<float> values = new List<float>();
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Large.Watts < wattsFloor) continue;

                float value = term(pairs[i]);
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                values.Add(value);
            }

            if (values.Count == 0) return float.NaN;
            values.Sort();

            int middle = values.Count / 2;
            return (values.Count % 2) == 1
                ? values[middle]
                : 0.5f * (values[middle - 1] + values[middle]);
        }


        public static string Table(IList<Pair> pairs, int limit)
        {

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("{0,-38}{1,10}{2,10}{3,8}{4,8}{5,8}{6,8}{7,11}{8,11}",
                "family", "large kW", "small kW", "waste", "rad", "cond", "pickup",
                "large K", "small K"));

            for (int i = 0; i < pairs.Count && (limit <= 0 || i < limit); i++)
            {
                Pair p = pairs[i];
                sb.AppendLine(string.Format(
                    "{0,-38}{1,10:n1}{2,10:n1}{3,8:n3}{4,8:n2}{5,8:n2}{6,8:n2}{7,11:n0}{8,11:n0}",
                    p.Family, p.Large.Watts / 1000f, p.Small.Watts / 1000f,
                    p.WasteRatio, p.RadiationHandicap, p.ConductionHandicap, p.PickupHandicap,
                    p.LargeGradient, p.SmallGradient));
            }

            return sb.ToString();
        }
    }
}
