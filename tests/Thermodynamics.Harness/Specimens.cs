using System;
using System.Collections.Generic;

namespace Thermodynamics.Harness
{
    public static class Specimens
    {
        public class Scored
        {
            public ShipProfile Ship;

            public double Isolation;

            public string Reason;
        }


        public static List<Scored> Select(IList<ShipProfile> corpus, int count)
        {

            List<Scored> panel = new List<Scored>();
            if (corpus == null || corpus.Count == 0 || count <= 0) return panel;


            List<double[]> features = new List<double[]>(corpus.Count);
            for (int i = 0; i < corpus.Count; i++) features.Add(corpus[i].Features);


            double[] scale = Spread(features);
            bool[] taken = new bool[corpus.Count];

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
