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

/// <summary>Quotient operation.</summary>
            public float RadiationHandicap { get { return Quotient(WasteRatio, RadiationRatio); } }
/// <summary>Quotient operation.</summary>
            public float ConductionHandicap { get { return Quotient(WasteRatio, ConductionRatio); } }
/// <summary>Quotient operation.</summary>
            public float PickupHandicap { get { return Quotient(WasteRatio, PickupRatio); } }

            public float LargeGradient;
            public float SmallGradient;

            public bool LargeUncoolable;
            public bool SmallUncoolable;

/// <summary>Quotient operation.</summary>
            private static float Quotient(float waste, float term)
            {
                return term <= 0f ? float.PositiveInfinity : waste / term;
            }
        }

/// <summary>ForcedGradient operation.</summary>
        public static float ForcedGradient(float watts, float cellSize, int faces,
            float coefficient)
        {
            if (faces < 1) faces = 1;
            float conductance = coefficient * cellSize * cellSize * faces;
            return conductance <= 0f ? float.PositiveInfinity : watts / conductance;
        }

/// <summary>Pairs operation.</summary>
        public static List<Pair> Pairs()
        {
            return Pairs(LoopThermalProperties.Default().HeatTransferCoefficient);
        }

/// <summary>Pairs operation.</summary>
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
/// <summary>Family operation.</summary>
                string family = Family(r.Subtype, r.Large);
                if (family == null) continue;

                string key = r.TypeId + "/" + family;
                if (r.Large) larges[key] = r; else smalls[key] = r;
            }

/// <summary>List operation.</summary>
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

/// <summary>Measure operation.</summary>
        private static Pair Measure(BlockHeatIndex.Reading large, BlockHeatIndex.Reading small,
            float coefficient)
        {
/// <summary>Pair operation.</summary>
            Pair pair = new Pair();
            pair.TypeId = large.TypeId;
/// <summary>Family operation.</summary>
            pair.Family = Family(large.Subtype, true);
            pair.Large = large;
            pair.Small = small;

            pair.WasteRatio = small.Watts / large.Watts;

/// <summary>Ratio operation.</summary>
            pair.RadiationRatio = Ratio(small.RadiatedWatts, large.RadiatedWatts);
/// <summary>Ratio operation.</summary>
            pair.ConductionRatio = Ratio(small.ConductedWatts, large.ConductedWatts);

            float largePickup = coefficient * Catalog.LargeGridSize * Catalog.LargeGridSize;
            float smallPickup = coefficient * Catalog.SmallGridSize * Catalog.SmallGridSize;
            pair.PickupRatio = smallPickup / largePickup;

/// <summary>ForcedGradient operation.</summary>
            pair.LargeGradient = ForcedGradient(large.Watts, Catalog.LargeGridSize,
                ReferenceSinkFaces, coefficient);
/// <summary>ForcedGradient operation.</summary>
            pair.SmallGradient = ForcedGradient(small.Watts, Catalog.SmallGridSize,
                ReferenceSinkFaces, coefficient);

            pair.LargeUncoolable = pair.LargeGradient
                > large.CriticalKelvin - BlockHeatIndex.AmbientKelvin;
            pair.SmallUncoolable = pair.SmallGradient
                > small.CriticalKelvin - BlockHeatIndex.AmbientKelvin;

            return pair;
        }

/// <summary>Ratio operation.</summary>
        private static float Ratio(float small, float large)
        {
            return large <= 0f ? float.NaN : small / large;
        }

/// <summary>Family operation.</summary>
        public static string Family(string subtype, bool large)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            string prefix = large ? "Large" : "Small";
            if (!subtype.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (subtype.Length == prefix.Length) return null;

            return subtype.Substring(prefix.Length);
        }

/// <summary>MedianHandicap operation.</summary>
        public static float MedianHandicap(IList<Pair> pairs, Func<Pair, float> term,
            float wattsFloor)
        {
/// <summary>List operation.</summary>
            List<float> values = new List<float>();
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Large.Watts < wattsFloor) continue;
/// <summary>term operation.</summary>
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

/// <summary>Table operation.</summary>
        public static string Table(IList<Pair> pairs, int limit)
        {
/// <summary>StringBuilder operation.</summary>
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
