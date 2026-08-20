using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Sweeps <see cref="ThermalSettings.Frequency"/> to find where the substep cost bottoms out.
    ///
    /// The arithmetic that governs it. A step is <c>1 / Frequency</c> seconds long, and the number
    /// of substeps it needs is proportional to its length times the grid's stiffness. So substeps
    /// per step fall as Frequency rises — but there are proportionally more steps, and the product
    /// is the same:
    ///
    /// <code>
    ///   substeps per second = Frequency x (stiffness / Frequency) = stiffness
    /// </code>
    ///
    /// **Total substep work is invariant in Frequency**, right up until substeps per step reaches
    /// its floor of one. Past that point the grid is being stepped more often than it needs and
    /// every extra step is pure overhead.
    ///
    /// That is the theory, and it predicts a knee rather than a slope. What it does not predict is
    /// which side of the knee is actually cheaper in wall-clock, because a substep and a step are
    /// not the same price: a field run showed a single substep pass getting 32 % cheaper when there
    /// were more of them per step, which is locality rather than arithmetic. If that holds, fewer
    /// longer steps beat more shorter ones and the sweet spot is *below* the knee, not at it.
    ///
    /// This measures both, on the same grid, so the question is settled by the clock rather than by
    /// the model.
    /// </summary>
    public static class FrequencyLab
    {
        public class Row
        {
            public int Frequency;

            /// <summary>Substeps the last step actually took.</summary>
            public float SubstepsPerStep;

            /// <summary>The product — what the theory says is invariant.</summary>
            public float SubstepsPerSecond;

            /// <summary>What the stability estimate asked for, before any cap.</summary>
            public float Demanded;

            /// <summary>True when the estimate was refused; the answer is then approximate.</summary>
            public bool Starved;

            public double MillisecondsPerStep;

            /// <summary>The figure that decides it: real milliseconds per simulated second.</summary>
            public double MillisecondsPerSimulatedSecond;

            public float SettledKelvin;

            /// <summary>
            /// Simulated seconds to reach 90 % of the total rise.
            ///
            /// The column that answers whether Frequency is a propagation dial. Substepping is an
            /// accuracy device, not a rate one: the integrated transfer over a second is the same
            /// however the second is chopped up, so if this is flat then Frequency moves no heat
            /// and is purely latency and cost.
            /// </summary>
            public float SecondsTo90Percent;
        }

        /// <summary>
        /// A grid with a real stiffness spread: heavy armour, light panels and a driven source, so
        /// the substep estimate has something to say at every frequency. Deliberately not a census
        /// hull — this measures the shape of a curve, and a smaller grid measures it faster without
        /// changing the shape.
        /// </summary>
        private static ThermalSimulation Rig(ThermalSettings settings, out BlockInstance source)
        {
            GridBuilder builder = GridBuilder.Large();

            for (int x = 0; x < 12; x++)
            {
                for (int z = 0; z < 12; z++)
                {
                    builder.Place(Catalog.HeavyArmor(), new Vector3I(x, 0, z));
                    builder.Place(Catalog.LightArmor(), new Vector3I(x, 1, z));
                }
            }

            // The light block that sets the pace, as a real ship's fittings do.
            builder.Place(BlockModel.Solid("Fitting", Vector3I.One, 24f, Catalog.DefaultThermal()),
                new Vector3I(0, 2, 0));

            builder.Place(Catalog.Reactor(), new Vector3I(5, 2, 5));
            source = builder.Last;
            source.PowerConsumedWatts = 2000000f;

            return builder.BuildSimulation(settings, 293.15f);
        }

        /// <summary>
        /// The sweep. Simulated seconds are held constant so the rows are comparable.
        ///
        /// Warmed up and taken best-of-three. The first cut of this reported a five-fold drop in
        /// cost between Frequency 6 and 8, which was the JIT finishing rather than anything about
        /// frequency: the early rows paid for compiling the solver and the later ones did not.
        /// Best-of-three because a mean measures the machine — the same mistake the coolant
        /// benchmark made once and documents.
        /// </summary>
        public static List<Row> Run(float simulatedSeconds = 600f)
        {
            Measure(4, 60f);
            Measure(4, 60f);

            List<Row> rows = new List<Row>();
            foreach (int frequency in new int[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 60 })
            {
                Row best = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Row row = Measure(frequency, simulatedSeconds);
                    if (best == null || row.MillisecondsPerSimulatedSecond < best.MillisecondsPerSimulatedSecond)
                    {
                        best = row;
                    }
                }
                rows.Add(best);
            }
            return rows;
        }

        private static Row Measure(int frequency, float simulatedSeconds)
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = frequency,
                SimulationSpeed = 1f,
                HeatTimeScale = 225f,

                // High enough that the estimate is granted at every frequency, so the sweep
                // measures the cost of a choice rather than the cost of being refused. A capped
                // run would flatten the curve and hide the knee it exists to find.
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = 0,
            };
            settings.Derive();

            BlockInstance source;
            ThermalSimulation simulation = Rig(settings, out source);

            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("source", source);

            Stopwatch clock = Stopwatch.StartNew();
            runner.Run(simulatedSeconds, simulatedSeconds / 200f);
            clock.Stop();

            long steps = Math.Max(1, simulation.Work.SolverSteps);
            float substeps = simulation.Work.SolverSubsteps / (float)steps;

            float settled;
            runner.Final.Tracked.TryGetValue("source", out settled);

            // Where the transient crossed nine tenths of its rise, from the samples.
            float start = 293.15f;
            float target = start + ((settled - start) * 0.9f);
            float crossed = simulatedSeconds;
            foreach (Sample sample in runner.Samples)
            {
                float value;
                if (!sample.Tracked.TryGetValue("source", out value)) continue;
                if (value >= target) { crossed = sample.TimeSeconds; break; }
            }

            return new Row
            {
                Frequency = frequency,
                SubstepsPerStep = substeps,
                SubstepsPerSecond = substeps * settings.StepsPerSecond,
                Demanded = simulation.Solver.LastRequiredSubsteps,
                Starved = simulation.Solver.LastRequiredSubsteps > settings.MaxSubsteps,
                MillisecondsPerStep = clock.Elapsed.TotalMilliseconds / steps,
                MillisecondsPerSimulatedSecond = clock.Elapsed.TotalMilliseconds / simulatedSeconds,
                SettledKelvin = settled,
                SecondsTo90Percent = crossed,
            };
        }

        private static string N(double value, int decimals = 2)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Report()
        {
            List<Row> rows = Run();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("FREQUENCY SWEEP — where does substep cost bottom out?");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,6} {1,12} {2,14} {3,11} {4,12} {5,14} {6,11} {7,10}",
                "freq", "substeps", "substeps/s", "demanded", "ms/step", "ms/sim second", "settled K", "90% at s"));

            double best = double.MaxValue;
            int bestFrequency = 0;

            foreach (Row row in rows)
            {
                if (row.MillisecondsPerSimulatedSecond < best)
                {
                    best = row.MillisecondsPerSimulatedSecond;
                    bestFrequency = row.Frequency;
                }

                sb.AppendLine(string.Format("{0,6} {1,12} {2,14} {3,11} {4,12} {5,14} {6,11} {7,10}",
                    row.Frequency, N(row.SubstepsPerStep), N(row.SubstepsPerSecond, 1),
                    N(row.Demanded, 1) + (row.Starved ? "!" : ""), N(row.MillisecondsPerStep, 4),
                    N(row.MillisecondsPerSimulatedSecond, 3), N(row.SettledKelvin, 1),
                    N(row.SecondsTo90Percent, 0)));
            }

            sb.AppendLine();
            sb.AppendLine("cheapest by wall clock: Frequency " + bestFrequency
                + " at " + N(best, 3) + " ms per simulated second");
            sb.AppendLine();
            sb.AppendLine("substeps/s is the column the theory says should be flat: a shorter step");
            sb.AppendLine("needs proportionally fewer substeps, so the product is the grid's stiffness");
            sb.AppendLine("and not a setting. Where it starts rising, the substep count has hit its");
            sb.AppendLine("floor of one and the extra steps are buying nothing.");
            return sb.ToString();
        }

        public static string Csv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("frequency,substeps_per_step,substeps_per_second,demanded,starved,"
                + "ms_per_step,ms_per_simulated_second,settled_k");
            foreach (Row row in Run())
            {
                sb.AppendLine(string.Join(",", new string[]
                {
                    row.Frequency.ToString(CultureInfo.InvariantCulture),
                    row.SubstepsPerStep.ToString("r", CultureInfo.InvariantCulture),
                    row.SubstepsPerSecond.ToString("r", CultureInfo.InvariantCulture),
                    row.Demanded.ToString("r", CultureInfo.InvariantCulture),
                    row.Starved ? "1" : "0",
                    row.MillisecondsPerStep.ToString("r", CultureInfo.InvariantCulture),
                    row.MillisecondsPerSimulatedSecond.ToString("r", CultureInfo.InvariantCulture),
                    row.SettledKelvin.ToString("r", CultureInfo.InvariantCulture),
                }));
            }
            return sb.ToString();
        }
    }
}
