using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What raising <c>SolarOcclusionSamples</c> actually buys, in joules into the hull.
    ///
    /// <para>
    /// **The question [backlog.md](../../docs/backlog.md) `A9` asks is whether the shipped default is
    /// the cheap rung of a ladder, and it is** — one sample is a single ray from the grid's centre,
    /// so a ship is lit or dark all at once and flips the moment its middle crosses. What no page
    /// had was the size of that error, and without it *fidelity is the default* is an argument
    /// rather than a decision.
    /// </para>
    ///
    /// <para>
    /// **Two things bound the answer and only one of them is the sample count.** A grid is re-tested
    /// every <c>SolarOcclusionInterval</c> steps, so a ship that crosses the terminator entirely
    /// between two tests is fully lit at one and fully dark at the next whatever the sample count
    /// is — the extra samples have nowhere to land. The lab therefore sweeps grid length against
    /// sample count at the shipped cadence, and reports the length below which the ladder is inert.
    /// </para>
    ///
    /// <para>
    /// It calls <see cref="SolarOcclusionSampler.Points"/> and
    /// <see cref="OcclusionMath.IsOccludedBySphere"/> — the mod's own two functions — so it measures
    /// the code rather than a description of it.
    /// </para>
    /// </summary>
    public static class OcclusionLadderLab
    {
        /// <summary>An Earthlike's radius in Space Engineers, metres.</summary>
        public const double PlanetRadius = 60000d;

        /// <summary>
        /// Metres above the surface the crossing is flown at. Low orbit: high enough to be in
        /// sunlight above the terrain and low enough that the planet still subtends most of the sky,
        /// which is where the occlusion threshold is steepest and the ladder has most to do.
        /// </summary>
        public const double Altitude = 5000d;

        /// <summary>The grid's speed across the terminator, m/s. The game's own limit.</summary>
        public const double Speed = 100d;

        public class Rung
        {
            /// <summary>The grid's length along its travel, metres.</summary>
            public double LengthMetres;

            public int Samples;

            /// <summary>
            /// Seconds of sunlight the grid was told it got that it did not, over one crossing.
            ///
            /// **Stated in seconds rather than as a share, because a share depends on how much
            /// flight either side of the terminator you choose to integrate** — the same error over
            /// a longer flight is a smaller percentage of it, which would make the figure a
            /// property of the lab. Seconds of surplus sunlight is a property of the crossing.
            /// </summary>
            public double SurplusSeconds;

            /// <summary>
            /// How wrong the reported lit fraction gets at the worst moment of a crossing, averaged
            /// over where the test schedule falls.
            ///
            /// **This is what a player sees**: 1 means the ship was reported fully lit while it was
            /// fully dark, or the reverse. Averaged over phase for the same reason the energy is —
            /// a single phase is a coin toss.
            /// </summary>
            public double WorstError;

            /// <summary>
            /// Tests over one crossing that reported a partial shadow, averaged over phase. Zero
            /// means the ladder never had anywhere to land.
            /// </summary>
            public double PartialTests;
        }

        /// <summary>
        /// Flies a grid of each length through the terminator at each sample count and reads the
        /// error against the same crossing resolved finely in time.
        /// </summary>
        public static List<Rung> Sweep(double[] lengths, int[] samples,
            int occlusionInterval, float frequency)
        {
            List<Rung> rungs = new List<Rung>();

            double testSeconds = occlusionInterval / (double)frequency;

            foreach (double length in lengths)
            {
                double ignoredError;
                int ignoredPartial;

                // The reference: the same crossing, the same span, tested every tick. What a ladder
                // with no cost limit at all would tell the grid.
                double truth = Energy(length, SolarOcclusionSampler.MaxSamples, Tick, testSeconds,
                    0d, out ignoredError, out ignoredPartial);

                foreach (int count in samples)
                {
                    Rung rung = new Rung
                    {
                        LengthMetres = length,
                        Samples = count,
                    };

                    // **Averaged over where the test schedule happens to fall**, because a single
                    // phase is a lottery: the same ship crossing the same terminator reports a
                    // different total depending on whether a test landed just before the crossing
                    // or just after, and the swing is a whole cadence wide. What a player meets is
                    // the average over phases, not any one of them.
                    double total = 0d;

                    for (int p = 0; p < Phases; p++)
                    {
                        double phase = testSeconds * p / Phases;

                        double worst;
                        int partial;
                        total += Energy(length, count, testSeconds, testSeconds, phase,
                            out worst, out partial);

                        rung.WorstError += worst;
                        rung.PartialTests += partial;
                    }

                    rung.WorstError /= Phases;
                    rung.PartialTests /= Phases;

                    double reported = total / Phases;
                    rung.SurplusSeconds = reported - truth;
                    rungs.Add(rung);
                }
            }

            return rungs;
        }

        /// <summary>Seconds the integral advances by. Fine against a three-second test cadence.</summary>
        private const double Tick = 0.05d;

        /// <summary>
        /// Offsets of the test schedule the crossing is flown at, spread over one cadence.
        ///
        /// A ship does not choose when the occlusion test falls relative to its own crossing, so a
        /// figure taken at one phase is a coin toss rather than a measurement.
        /// </summary>
        private const int Phases = 24;

        /// <summary>
        /// Solar energy per unit area the grid is told it received while crossing the terminator,
        /// with the reported fraction held between tests exactly as the mod holds it.
        ///
        /// <paramref name="spanSeconds"/> sets how far either side of the crossing the flight runs,
        /// and is passed separately from the test cadence so every rung integrates the same journey.
        /// </summary>
        private static double Energy(double length, int samples, double testSeconds,
            double spanSeconds, double phase, out double worstError, out int partialTests)
        {
            worstError = 0d;
            partialTests = 0;

            double radius = PlanetRadius + Altitude;
            double omega = Speed / radius;                      // radians a second

            // The terminator this occluder actually has: the angle at which its own fitted
            // threshold flips, rather than the geometric ninety degrees.
            double terminator = Terminator(radius);

            // Far enough either side that both ends of the crossing are unambiguous: the grid's own
            // length plus six test cadences of travel.
            double halfSpan = ((length / radius) * 0.5d) + (omega * spanSeconds * 6d);

            double energy = 0d;
            double reported = double.NaN;

            // Where in the test cadence the flight starts. The first test still happens at once,
            // because a grid is tested on its first step whatever the phase.
            double sinceTest = double.PositiveInfinity;
            bool first = true;

            for (double angle = terminator - halfSpan; angle <= terminator + halfSpan; angle += omega * Tick)
            {
                if (first)
                {
                    first = false;
                    reported = 1d - OccludedShare(angle, length, samples);
                    sinceTest = phase;
                    if (reported > 0d && reported < 1d) partialTests++;
                }
                else if (sinceTest >= testSeconds)
                {
                    reported = 1d - OccludedShare(angle, length, samples);
                    sinceTest = 0d;
                    if (reported > 0d && reported < 1d) partialTests++;
                }

                double actual = 1d - OccludedShare(angle, length, SolarOcclusionSampler.MaxSamples);
                double error = Math.Abs(reported - actual);
                if (error > worstError) worstError = error;

                energy += reported * Tick;
                sinceTest += Tick;
            }

            return energy;
        }

        /// <summary>
        /// The angle from the sub-solar point at which this occluder says the sun has set, radians.
        ///
        /// **Not ninety degrees**, because `OcclusionThreshold` is a fitted curve by its own
        /// admission: at low orbit around an Earthlike it puts sunset well past the geometric
        /// terminator. Found rather than assumed, so the lab flies through the crossing the code
        /// actually has.
        /// </summary>
        public static double Terminator(double radius)
        {
            double lo = 0d;
            double hi = Math.PI;

            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5d * (lo + hi);
                if (IsOccluded(mid, radius)) hi = mid; else lo = mid;
            }

            return 0.5d * (lo + hi);
        }

        private static bool IsOccluded(double angle, double radius)
        {
            Vector3D point = new Vector3D(Math.Cos(angle) * radius, 0d, Math.Sin(angle) * radius);
            return OcclusionMath.IsOccludedBySphere(point, Vector3D.Zero, PlanetRadius,
                new Vector3(1f, 0f, 0f));
        }

        /// <summary>
        /// The share of a grid's sample points that cannot see the sun, with the grid's centre at
        /// <paramref name="angle"/> radians from the sub-solar point.
        ///
        /// The grid lies along its own track, so its two ends sit at slightly different angles —
        /// which is the whole reason more than one sample can say anything. The box handed to the
        /// sampler is the one containing both ends, which is what the mod reads off the grid.
        /// </summary>
        private static double OccludedShare(double angle, double length, int samples)
        {
            double radius = PlanetRadius + Altitude;
            double halfAngle = (length / radius) * 0.5d;

            Vector3D lead = Point(angle - halfAngle, radius);
            Vector3D tail = Point(angle + halfAngle, radius);

            // A thin ship: the cross-section is a tenth of the length, as the box sweep assumes.
            double girth = length * 0.05d;

            BoundingBoxD bounds = BoundingBoxD.CreateInvalid();
            bounds.Include(lead - new Vector3D(0d, girth, 0d));
            bounds.Include(lead + new Vector3D(0d, girth, 0d));
            bounds.Include(tail - new Vector3D(0d, girth, 0d));
            bounds.Include(tail + new Vector3D(0d, girth, 0d));

            List<Vector3D> points = new List<Vector3D>();
            SolarOcclusionSampler.Points(bounds, samples, points);

            Vector3 sun = new Vector3(1f, 0f, 0f);

            int occluded = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (OcclusionMath.IsOccludedBySphere(points[i], Vector3D.Zero, PlanetRadius, sun))
                {
                    occluded++;
                }
            }

            return points.Count == 0 ? 0d : occluded / (double)points.Count;
        }

        private static Vector3D Point(double angle, double radius)
        {
            return new Vector3D(Math.Cos(angle) * radius, 0d, Math.Sin(angle) * radius);
        }

        /// <summary>The lengths and sample counts the report sweeps.</summary>
        public static readonly double[] Lengths = { 25d, 75d, 150d, 300d, 600d, 1200d, 2500d };

        public static readonly int[] Samples = { 1, 3, 5, 9 };

        /// <summary>Intervals the second table sweeps, in solver steps.</summary>
        public static readonly int[] Intervals = { 1, 2, 4, 8, 12, 24, 48 };

        /// <summary>
        /// The dial that does move the energy: how often the test runs at all.
        ///
        /// Swept at nine samples, so the sample count is not what is being varied, and on a mid-size
        /// hull where the ladder had most to say.
        /// </summary>
        public static string IntervalReport(float frequency)
        {
            StringBuilder sb = new StringBuilder();

            const double Length = 600d;

            sb.AppendLine("  THE INTERVAL, WHICH IS THE DIAL THAT MOVES THE ENERGY");
            sb.AppendLine();
            sb.Append("  a ").Append(Length.ToString("n0"))
              .AppendLine(" m hull at nine samples, varying only how often it is tested");
            sb.AppendLine();
            sb.AppendLine("  interval    every        surplus sunlight   tests a crossing");

            foreach (int interval in Intervals)
            {
                List<Rung> one = Sweep(new[] { Length },
                    new[] { SolarOcclusionSampler.MaxSamples }, interval, frequency);

                double seconds = interval / (double)frequency;

                sb.Append("  ").Append((interval + " steps").PadRight(12))
                  .Append((seconds.ToString("n2") + " s").PadLeft(8)).Append("   ")
                  .Append(one[0].SurplusSeconds.ToString("n2").PadLeft(14)).Append(" s")
                  .Append(one[0].PartialTests.ToString("n2").PadLeft(19))
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  The surplus is the cadence's own lag: a test that ran before the crossing");
            sb.AppendLine("  keeps saying lit until the next one. Halving the interval halves it, which");
            sb.AppendLine("  no sample count does.");

            return sb.ToString();
        }

        /// <summary>
        /// What the whole-grid answer costs the blocks at the ends of a hull, which is the only
        /// thing the unbuilt top rung of `A9` would fix.
        ///
        /// <para>
        /// The grid-wide share is applied uniformly, so during a crossing every block is told the
        /// same thing while the leading end is already dark and the trailing end still lit. **The
        /// energy cancels over the hull and does not cancel over a block**: the two ends are wrong
        /// in opposite directions, which is why the surplus column above reads near zero while the
        /// blocks at the ends are each out by seconds of sunlight. Resolving the planet's shadow per
        /// face is what would remove it, and this is what removing it is worth.
        /// </para>
        /// </summary>
        public class Extremity
        {
            public double LengthMetres;

            /// <summary>Seconds of sunlight the worst-placed block was told it had and did not.</summary>
            public double SurplusSeconds;

            /// <summary>And the seconds it was denied, which is the other end of the same hull.</summary>
            public double DeficitSeconds;

            /// <summary>The surplus as a temperature on one sunward face of a standard armour block.</summary>
            public double SurplusKelvin;
            public double DeficitKelvin;
        }

        /// <summary>
        /// Kelvin one lit cell face gains per second of sunlight, on a standard light armour block
        /// at the shipped clock.
        ///
        /// 1,000 W/m² over a 2.5 m face at the block's own absorptivity, over 500 kg of steel whose
        /// capacity `HeatTimeScale` divides by 225. **Stated as a rate so the conversion is visible**:
        /// every figure below it is seconds of sunlight times this.
        /// </summary>
        public static double KelvinPerLitSecond(ThermalSettings settings)
        {
            const double LargeGridCell = 2.5d;
            const double ArmourKilograms = 500d;

            // The block is stated as a material rather than taken from a catalog: 500 kg of steel
            // plate at the figures `BlockMaterials` prices it with, so the conversion cannot drift
            // with a harness stand-in and cannot be read as a claim about one subtype.
            BlockMaterial steel = BlockMaterials.Steel;

            double face = LargeGridCell * LargeGridCell;
            double capacity = ArmourKilograms * steel.SpecificHeat / settings.HeatTimeScale;
            return capacity <= 0d
                ? 0d
                : settings.SolarEnergy * face * steel.Emissivity / capacity;
        }

        /// <summary>
        /// The worst-placed block on a hull of each length, averaged over test phases.
        ///
        /// Positions are walked along the hull rather than sampled from the box, because the
        /// question is what one block meets rather than what the grid reports.
        /// </summary>
        public static List<Extremity> Extremities(double[] lengths, int samples,
            int occlusionInterval, float frequency, double kelvinPerSecond)
        {
            List<Extremity> found = new List<Extremity>();
            double testSeconds = occlusionInterval / (double)frequency;

            foreach (double length in lengths)
            {
                double surplus = 0d;
                double deficit = 0d;

                for (int p = 0; p < Phases; p++)
                {
                    double phase = testSeconds * p / Phases;

                    double high, low;
                    BlockError(length, samples, testSeconds, phase, out high, out low);
                    surplus += high;
                    deficit += low;
                }

                Extremity row = new Extremity();
                row.LengthMetres = length;
                row.SurplusSeconds = surplus / Phases;
                row.DeficitSeconds = deficit / Phases;
                row.SurplusKelvin = row.SurplusSeconds * kelvinPerSecond;
                row.DeficitKelvin = row.DeficitSeconds * kelvinPerSecond;
                found.Add(row);
            }

            return found;
        }

        /// <summary>
        /// Seconds of sunlight the most over-told and most under-told point on the hull differ by,
        /// over one crossing at one phase.
        /// </summary>
        private static void BlockError(double length, int samples, double testSeconds, double phase,
            out double worstSurplus, out double worstDeficit)
        {
            const int Positions = 9;

            double radius = PlanetRadius + Altitude;
            double omega = Speed / radius;
            double terminator = Terminator(radius);
            double halfSpan = ((length / radius) * 0.5d) + (omega * testSeconds * 6d);

            double[] told = new double[Positions];
            double[] met = new double[Positions];

            double reported = double.NaN;
            double sinceTest = double.PositiveInfinity;
            bool first = true;

            for (double angle = terminator - halfSpan; angle <= terminator + halfSpan; angle += omega * Tick)
            {
                if (first)
                {
                    first = false;
                    reported = 1d - OccludedShare(angle, length, samples);
                    sinceTest = phase;
                }
                else if (sinceTest >= testSeconds)
                {
                    reported = 1d - OccludedShare(angle, length, samples);
                    sinceTest = 0d;
                }

                for (int i = 0; i < Positions; i++)
                {
                    // Along the hull from leading end to trailing end.
                    double offset = ((i / (double)(Positions - 1)) - 0.5d) * (length / radius);
                    double lit = IsOccluded(angle + offset, radius) ? 0d : 1d;

                    told[i] += reported * Tick;
                    met[i] += lit * Tick;
                }

                sinceTest += Tick;
            }

            worstSurplus = 0d;
            worstDeficit = 0d;

            for (int i = 0; i < Positions; i++)
            {
                double error = told[i] - met[i];
                if (error > worstSurplus) worstSurplus = error;
                if (error < worstDeficit) worstDeficit = error;
            }

            worstDeficit = -worstDeficit;
        }

        public static string ExtremityReport(int occlusionInterval, float frequency)
        {
            StringBuilder sb = new StringBuilder();

            ThermalSettings settings = new ThermalSettings();
            settings.Derive();

            double perSecond = KelvinPerLitSecond(settings);

            sb.AppendLine("  WHAT THE WHOLE-GRID ANSWER COSTS ONE BLOCK, WHICH IS THE TOP RUNG");
            sb.AppendLine();
            sb.Append("  nine samples at ").Append(occlusionInterval)
              .AppendLine(" steps: the shipped ladder, read per block rather than per hull");
            sb.Append("  one lit face of a 500 kg steel-plate block gains ")
              .Append(perSecond.ToString("n3")).AppendLine(" K a second of sunlight");
            sb.AppendLine();
            sb.AppendLine("  grid        told and did not get   told and was denied      worst block");

            List<Extremity> rows = Extremities(Lengths, SolarOcclusionSampler.MaxSamples,
                occlusionInterval, frequency, perSecond);

            foreach (Extremity row in rows)
            {
                double worst = Math.Max(row.SurplusKelvin, row.DeficitKelvin);

                sb.Append("  ").Append((row.LengthMetres.ToString("n0") + " m").PadRight(12))
                  .Append((row.SurplusSeconds.ToString("n2") + " s").PadLeft(14))
                  .Append((row.DeficitSeconds.ToString("n2") + " s").PadLeft(22))
                  .Append((worst.ToString("n2") + " K").PadLeft(17))
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  and the same hulls tested every step, which leaves only the spatial half");
            sb.AppendLine();
            sb.AppendLine("  grid        told and did not get   told and was denied      worst block");

            List<Extremity> tight = Extremities(Lengths, SolarOcclusionSampler.MaxSamples,
                1, frequency, perSecond);

            foreach (Extremity row in tight)
            {
                double worst = Math.Max(row.SurplusKelvin, row.DeficitKelvin);

                sb.Append("  ").Append((row.LengthMetres.ToString("n0") + " m").PadRight(12))
                  .Append((row.SurplusSeconds.ToString("n2") + " s").PadLeft(14))
                  .Append((row.DeficitSeconds.ToString("n2") + " s").PadLeft(22))
                  .Append((worst.ToString("n2") + " K").PadLeft(17))
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  The second table is what resolving the planet per face would remove and");
            sb.AppendLine("  the interval cannot: at one step it is all the error there is left, and it");
            sb.AppendLine("  is a property of the hull's length rather than of the cadence.");
            sb.AppendLine();
            sb.AppendLine("  The hull's own surplus cancels and a block's does not: the leading end is");
            sb.AppendLine("  told it is lit while it is dark and the trailing end the reverse, so the");
            sb.AppendLine("  error the ladder above reports as near zero is two errors of opposite sign");
            sb.AppendLine("  sitting at the two ends. Resolving the planet per face is what removes it.");

            return sb.ToString();
        }

        public static string Report(int occlusionInterval = 12, float frequency = 4f)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("SOLAR OCCLUSION LADDER");
            sb.AppendLine();
            sb.Append("  planet ").Append(PlanetRadius / 1000d).Append(" km, altitude ")
              .Append(Altitude / 1000d).Append(" km, speed ").Append(Speed).AppendLine(" m/s");
            sb.Append("  re-tested every ").Append(occlusionInterval).Append(" steps at Frequency ")
              .Append(frequency).Append(" = every ")
              .Append((occlusionInterval / frequency).ToString("n2")).AppendLine(" s of play");
            sb.Append("  which is ").Append((Speed * occlusionInterval / frequency).ToString("n0"))
              .AppendLine(" m of travel between one test and the next");
            sb.Append("  sunset lands at ")
              .Append((Terminator(PlanetRadius + Altitude) * 180d / Math.PI).ToString("n1"))
              .AppendLine(" degrees from the sub-solar point, not 90: OcclusionThreshold is a fitted curve");
            sb.AppendLine();

            List<Rung> rungs = Sweep(Lengths, Samples, occlusionInterval, frequency);

            sb.AppendLine("  grid        samples   surplus sunlight   worst error   partial tests");
            foreach (Rung rung in rungs)
            {
                sb.Append("  ").Append((rung.LengthMetres.ToString("n0") + " m").PadRight(12))
                  .Append(rung.Samples.ToString().PadLeft(4)).Append("   ")
                  .Append(rung.SurplusSeconds.ToString("n2").PadLeft(14)).Append(" s")
                  .Append(rung.WorstError.ToString("n3").PadLeft(14))
                  .Append(rung.PartialTests.ToString("n2").PadLeft(16))
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("  Surplus sunlight barely moves with the sample count, and the reason is that");
            sb.AppendLine("  one ray from the centre is unbiased: it reports light after the leading end");
            sb.AppendLine("  is dark and dark before the trailing end is, and the two cancel. What the");
            sb.AppendLine("  extra samples fix is the worst error column, which is what a player sees at");
            sb.AppendLine("  a moment rather than what the hull absorbs over a crossing.");
            sb.AppendLine();

            sb.AppendLine(IntervalReport(frequency));
            sb.AppendLine(ExtremityReport(occlusionInterval, frequency));

            return sb.ToString();
        }
    }
}
