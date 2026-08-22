using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Which ships are worth keeping, and which are saying what another ship already said.
    /// **Selection is by coverage, not by frequency** (`M10`): the extremes of every axis first, then
    /// greedy maximin, because the interior of a cluster is predictable from its edges and a balance
    /// figure fails at the edges first.
    ///
    /// <para>
    /// **That two close ships behave alike is a hypothesis, not a result**, and until the battery has
    /// run over a whole corpus once a reduced panel is a guess about what matters. See
    /// <see cref="Fidelity"/>, and balance-lab.md, Specimens.
    /// </para>
    /// </summary>
    public static class Specimens
    {
        public class Scored
        {
            public ShipProfile Ship;

            /// <summary>
            /// Distance in feature space to the nearest ship already chosen. High means this ship
            /// occupies a part of the space nothing else does.
            /// </summary>
            public double Isolation;

            /// <summary>Why it was taken: the axis it is extreme on, or "coverage".</summary>
            public string Reason;
        }

        /// <summary>
        /// A panel of at most <paramref name="count"/> ships covering the corpus.
        ///
        /// Extremes first — the largest, the smallest, the most and least stressed, the stiffest —
        /// because an axis with no example at its end is an axis the panel cannot speak about at
        /// all. Then greedy maximin: repeatedly take the ship furthest from everything already
        /// taken, which is the standard construction for spreading a small sample through a space
        /// whose density you do not want to reproduce.
        /// </summary>
        public static List<Scored> Select(IList<ShipProfile> corpus, int count)
        {
            List<Scored> panel = new List<Scored>();
            if (corpus == null || corpus.Count == 0 || count <= 0) return panel;

            List<double[]> features = new List<double[]>(corpus.Count);
            for (int i = 0; i < corpus.Count; i++) features.Add(corpus[i].Features);

            double[] scale = Spread(features);
            bool[] taken = new bool[corpus.Count];

            // Extremes, both ends of every axis. A panel missing the stiffest ship in the corpus
            // cannot answer the one question stiffness was measured for.
            int axes = ShipProfile.FeatureNames.Length;
            for (int axis = 0; axis < axes && panel.Count < count; axis++)
            {
                Take(corpus, features, taken, panel, Extreme(features, axis, true),
                    "max " + ShipProfile.FeatureNames[axis], scale);

                if (panel.Count < count)
                {
                    Take(corpus, features, taken, panel, Extreme(features, axis, false),
                        "min " + ShipProfile.FeatureNames[axis], scale);
                }
            }

            while (panel.Count < count)
            {
                int best = -1;
                double furthest = -1d;

                for (int i = 0; i < corpus.Count; i++)
                {
                    if (taken[i]) continue;

                    double nearest = Nearest(features[i], panel, scale);
                    if (nearest > furthest)
                    {
                        furthest = nearest;
                        best = i;
                    }
                }

                if (best < 0) break;
                Take(corpus, features, taken, panel, best, "coverage", scale);
            }

            return panel;
        }

        private static void Take(IList<ShipProfile> corpus, List<double[]> features, bool[] taken,
            List<Scored> panel, int index, string reason, double[] scale)
        {
            if (index < 0 || taken[index]) return;

            taken[index] = true;
            panel.Add(new Scored
            {
                Ship = corpus[index],
                Isolation = panel.Count == 0 ? double.PositiveInfinity : Nearest(features[index], panel, scale),
                Reason = reason,
            });
        }

        private static int Extreme(List<double[]> features, int axis, bool highest)
        {
            int best = -1;
            double bestValue = highest ? double.NegativeInfinity : double.PositiveInfinity;

            for (int i = 0; i < features.Count; i++)
            {
                double value = features[i][axis];
                if (highest ? value > bestValue : value < bestValue)
                {
                    bestValue = value;
                    best = i;
                }
            }

            return best;
        }

        private static double Nearest(double[] candidate, List<Scored> panel, double[] scale)
        {
            double nearest = double.PositiveInfinity;

            for (int i = 0; i < panel.Count; i++)
            {
                double distance = Distance(candidate, panel[i].Ship.Features, scale);
                if (distance < nearest) nearest = distance;
            }

            return nearest;
        }

        /// <summary>
        /// Euclidean distance with every axis divided by its spread across the corpus, so an axis
        /// that happens to be measured in larger numbers does not dominate one that is not.
        /// </summary>
        public static double Distance(double[] a, double[] b, double[] scale)
        {
            double total = 0d;

            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                double difference = (a[i] - b[i]) / scale[i];
                total += difference * difference;
            }

            return Math.Sqrt(total);
        }

        /// <summary>Per-axis range across the corpus, floored so a constant axis cannot divide by zero.</summary>
        public static double[] Spread(List<double[]> features)
        {
            int axes = ShipProfile.FeatureNames.Length;
            double[] scale = new double[axes];

            for (int axis = 0; axis < axes; axis++)
            {
                double low = double.PositiveInfinity;
                double high = double.NegativeInfinity;

                for (int i = 0; i < features.Count; i++)
                {
                    double value = features[i][axis];
                    if (value < low) low = value;
                    if (value > high) high = value;
                }

                scale[axis] = high - low;
                if (scale[axis] <= 1e-9d) scale[axis] = 1d;
            }

            return scale;
        }

        /// <summary>
        /// How well a panel represents the corpus it came from, as the distance from the worst-served
        /// ship to its nearest panel member.
        ///
        /// This is the number that decides whether a panel of a few hundred can stand in for ten
        /// thousand. It is a statement about the *feature space* rather than about behaviour, so it
        /// is necessary and not sufficient: a panel that covers the space badly certainly cannot
        /// substitute, and one that covers it well still has to be shown to reproduce the corpus's
        /// verdicts before anyone trusts it to.
        /// </summary>
        public static double Fidelity(IList<ShipProfile> corpus, List<Scored> panel)
        {
            if (corpus == null || corpus.Count == 0 || panel == null || panel.Count == 0) return 0d;

            List<double[]> features = new List<double[]>(corpus.Count);
            for (int i = 0; i < corpus.Count; i++) features.Add(corpus[i].Features);

            double[] scale = Spread(features);
            double worst = 0d;

            for (int i = 0; i < corpus.Count; i++)
            {
                double nearest = Nearest(features[i], panel, scale);
                if (nearest > worst) worst = nearest;
            }

            return worst;
        }

        /// <summary>
        /// Ships that say nothing another ship has not already said, nearest-duplicate first.
        ///
        /// The inverse of <see cref="Select"/> and the more interesting half while a corpus is
        /// being assembled: it says what the next thousand downloads are *not* buying. A corpus
        /// whose redundant share is climbing has stopped being worth growing.
        /// </summary>
        public static List<KeyValuePair<ShipProfile, double>> Redundant(IList<ShipProfile> corpus,
            double within)
        {
            List<KeyValuePair<ShipProfile, double>> redundant =
                new List<KeyValuePair<ShipProfile, double>>();

            if (corpus == null || corpus.Count < 2) return redundant;

            List<double[]> features = new List<double[]>(corpus.Count);
            for (int i = 0; i < corpus.Count; i++) features.Add(corpus[i].Features);

            double[] scale = Spread(features);

            for (int i = 0; i < corpus.Count; i++)
            {
                double nearest = double.PositiveInfinity;

                for (int j = 0; j < corpus.Count; j++)
                {
                    if (i == j) continue;

                    double distance = Distance(features[i], features[j], scale);
                    if (distance < nearest) nearest = distance;
                }

                if (nearest <= within)
                {
                    redundant.Add(new KeyValuePair<ShipProfile, double>(corpus[i], nearest));
                }
            }

            redundant.Sort(delegate (KeyValuePair<ShipProfile, double> a, KeyValuePair<ShipProfile, double> b)
            {
                return a.Value.CompareTo(b.Value);
            });

            return redundant;
        }
    }
}
