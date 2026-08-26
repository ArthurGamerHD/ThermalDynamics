using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What a 0.5 m cell changes, term by term**, for the pairs of blocks the game ships at both
    /// sizes.
    ///
    /// <para>
    /// Every path heat takes off a block is proportional to an area or to a length, and a small
    /// grid divides those by 25 and by 5 while dividing the heat by something else entirely. So
    /// "small grids are behind" is not one statement — it is four, one per term, and they do not
    /// agree in sign. This computes all four from the definitions alone, so the claim that
    /// [balance.md] makes about grid size can be checked against the blocks rather than against
    /// the arithmetic of a cell face.
    /// </para>
    ///
    /// <para>
    /// The four terms, and how each scales with the cell edge `g`:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>**waste** — the game's number, and the only one with no `g` in it. Measured.</item>
    /// <item>**radiation** — `εσA`, and `A ∝ g²`: a twenty-fifth.</item>
    /// <item>**conduction into the hull** — `kA/L`, and `A/L ∝ g`: a fifth.</item>
    /// <item>**loop pickup** — `h·A` over a sink face, `∝ g²`: a twenty-fifth.</item>
    /// </list>
    ///
    /// <para>
    /// A term is *behind* on a small grid where its ratio is smaller than the waste ratio, and
    /// *ahead* where it is larger. The handicap this reports is that quotient, so 1 is parity and 2
    /// means the small variant has half the shedding per watt its large counterpart has.
    /// </para>
    ///
    /// <para>
    /// It is definitions only — no session, no rig — which is what makes it a gate rather than a
    /// measurement to be repeated. See balance.md, *What a small cell is behind on, and what it is
    /// not*, and backlog.md `C43`.
    /// </para>
    /// </summary>
    public static class CellSizeLab
    {
        /// <summary>
        /// The most sink faces a routed ring presents to a source, and `C42`'s reference case.
        ///
        /// One, because a rectangle laid past a one-cell source gives one face and the face count is
        /// not a lever: a pipe carries at most two sink faces and a pump none, by design. The
        /// gradient scales inversely, so a three-face job is this divided by three.
        /// </summary>
        public const int ReferenceSinkFaces = 1;

        /// <summary>One paired family: the same block, built at both cell sizes.</summary>
        public class Pair
        {
            /// <summary>The subtype with its `Large`/`Small` prefix removed, and its type.</summary>
            public string Family;
            public string TypeId;

            public BlockHeatIndex.Reading Large;
            public BlockHeatIndex.Reading Small;

            /// <summary>Small over large, for each of the four terms.</summary>
            public float WasteRatio;
            public float RadiationRatio;
            public float ConductionRatio;
            public float PickupRatio;

            /// <summary>
            /// How far behind the small variant is on a term, per watt it makes: the waste ratio
            /// over the term's ratio. **1 is parity, above 1 is behind, below 1 is ahead.**
            /// </summary>
            public float RadiationHandicap { get { return Quotient(WasteRatio, RadiationRatio); } }
            public float ConductionHandicap { get { return Quotient(WasteRatio, ConductionRatio); } }
            public float PickupHandicap { get { return Quotient(WasteRatio, PickupRatio); } }

            /// <summary>
            /// Kelvin above the coolant the block is forced to sit at to push its whole waste
            /// through <see cref="ReferenceSinkFaces"/> sink faces, at each size. `C42`'s number.
            /// </summary>
            public float LargeGradient;
            public float SmallGradient;

            /// <summary>
            /// Whether the forced gradient alone already exceeds the block's margin over ambient —
            /// the block has no answer at this face count however good the rest of the loop is.
            /// </summary>
            public bool LargeUncoolable;
            public bool SmallUncoolable;

            private static float Quotient(float waste, float term)
            {
                return term <= 0f ? float.PositiveInfinity : waste / term;
            }
        }

        /// <summary>
        /// Kelvin above the coolant a block must sit at to push `watts` through `faces` sink faces
        /// on a grid of this cell size, at the given coefficient.
        ///
        /// **`W / (h·A·n)`, and there is no thermal mass or radiator in it.** It is the gradient the
        /// joint forces whatever is hung off the far end of the loop, which is what makes it the
        /// first thing to fail and the thing `C42` measured the pickup by.
        /// </summary>
        public static float ForcedGradient(float watts, float cellSize, int faces,
            float coefficient)
        {
            if (faces < 1) faces = 1;
            float conductance = coefficient * cellSize * cellSize * faces;
            return conductance <= 0f ? float.PositiveInfinity : watts / conductance;
        }

        /// <summary>
        /// Every block the game ships at both cell sizes, worst pickup handicap first.
        ///
        /// <para>
        /// Paired on the game's own naming convention — the same type id, and the same subtype once
        /// a leading `Large` or `Small` is removed — which is how the definitions themselves
        /// distinguish the two builds of one block. A subtype that carries no such prefix cannot be
        /// paired and is left out; so is a pair whose large variant makes no measurable waste, since
        /// a ratio against zero says nothing.
        /// </para>
        /// </summary>
        public static List<Pair> Pairs()
        {
            return Pairs(LoopThermalProperties.Default().HeatTransferCoefficient);
        }

        /// <summary>The same, at a stated pickup coefficient, so a candidate can be measured.</summary>
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

            // Radiation and conduction are read at each block's own critical temperature, which is
            // what BlockHeatIndex already computes and is the only temperature either term has a
            // reason to be quoted at. The two variants of one block share a critical temperature —
            // it comes off the same components — so the ratio is a ratio of geometry, not of limits.
            pair.RadiationRatio = Ratio(small.RadiatedWatts, large.RadiatedWatts);
            pair.ConductionRatio = Ratio(small.ConductedWatts, large.ConductedWatts);

            // Pickup carries no block property at all: it is h times a cell face, so this is the
            // cell-size ratio squared and the same number for every pair. It is computed rather
            // than written down so that a change to PlateConductance moves it.
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

        /// <summary>
        /// The subtype with the prefix that names its cell size removed, or null where it carries
        /// none. `LargeBlockSmallGenerator` and `SmallBlockSmallGenerator` both give
        /// `BlockSmallGenerator` — note that only the *leading* word is the grid size, which is why
        /// this is a prefix strip rather than a search.
        /// </summary>
        public static string Family(string subtype, bool large)
        {
            if (string.IsNullOrEmpty(subtype)) return null;

            string prefix = large ? "Large" : "Small";
            if (!subtype.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (subtype.Length == prefix.Length) return null;

            return subtype.Substring(prefix.Length);
        }

        /// <summary>The median of a term's handicap over the pairs that make real heat.</summary>
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
