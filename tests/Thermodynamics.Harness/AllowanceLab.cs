using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What <c>MaxElementVisitsPerStep</c> costs and what it buys, across grid size and world.
    ///
    /// <para>
    /// **The allowance is a per-frame budget wearing a per-step name.** A step is spread across the
    /// frames of its own window — <see cref="ThermalSimulation.Update"/> banks
    /// <c>StepWorkUnits * frameSeconds * StepsPerSecond</c> of credit a frame — so an allowance of
    /// <c>V</c> at <c>Frequency f</c> bounds a frame at <c>V * f / 60</c> element visits whatever
    /// the grid. That product is the only thing the setting controls, and it is what this lab
    /// measures against the two things it trades between: milliseconds of real time, and the share
    /// of simulated time a grid gets to keep.
    /// </para>
    ///
    /// <para>
    /// **Why it is a lab of its own** (`M8`). The ladder in <see cref="PerformanceReport"/> drives
    /// <see cref="ThermalSolver.Step(float, EnvironmentState)"/> directly, so the allowance is not
    /// in the path at all and every step it times is an unbounded one. <see cref="StepPathLab"/>
    /// puts the host's entry point back but deliberately holds the allowance out of reach, because
    /// a shortened step would stop being the same step as the one beside it. Nothing else runs a
    /// grid frame by frame through <see cref="ThermalSimulation.Update"/> and reads
    /// <see cref="ThermalSimulation.SimulationRate"/>, which is the number the setting exists to
    /// trade away.
    /// </para>
    ///
    /// <para>
    /// **What a lost rate costs is not a millisecond**: a grid at 73 % of real time runs a thermal
    /// clock 27 % slow, which is the mechanism `F23` priced at one point — 19.25 K standing under a
    /// moving load at a 10 % error, and 0.00 K under a steady one. The sweep here reaches deficits
    /// several times that, so <see cref="PriceRates"/> runs the same mechanism up a ladder rather
    /// than reading `F23`'s point as a slope, and <see cref="RateCost"/> reads a row off it.
    /// </para>
    /// </summary>
    public static class AllowanceLab
    {
        /// <summary>
        /// Sizes the sweep walks, chosen around where the shipped allowance stops covering a hull
        /// rather than to span the whole ladder. The rungs either side of the crossing are what a
        /// decision about the setting needs; a million-block station is bounded by every allowance
        /// on the list and says nothing about which to pick.
        /// </summary>
        public static readonly int[] DefaultSizes = { 8000, 16000, 32000, 64000, 125000 };

        /// <summary>
        /// Allowances the sweep walks, in element visits. Zero is the bound switched off and is the
        /// reference every other row is read against — the demand a grid would have taken if
        /// nothing shortened its step.
        ///
        /// <para>
        /// 2,000,000 is what ships. 3,200,000 is 1.6× it, which is the factor `C24` multiplied a
        /// conduction-limited demand by, so it is the value that would put a vacuum grid back where
        /// it was before the retune. 1,000,000 is what shipped at <c>Frequency</c> 8 and is here so
        /// the cost of the setting is bracketed on both sides rather than only above.
        /// </para>
        /// </summary>
        public static readonly int[] DefaultAllowances = { 0, 1000000, 2000000, 3200000, 4000000 };

        /// <summary>The worlds the sweep walks, cheapest first. Same three as the report's
        /// environment section, and the same samples, so the two are comparable (`P6`).</summary>
        public static readonly string[] DefaultWorlds = { "vacuum", "atmosphere", "flight" };

        private static EnvironmentSample World(string name)
        {
            if (name == "vacuum") return Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));
            if (name == "atmosphere") return Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 40f);
            return Worlds.Flight(1f, 300f);
        }

        /// <summary>
        /// Frames each case runs. At the shipped <c>Frequency</c> of 4 a step spans fifteen frames,
        /// so this is eight steps — enough that the rate is a ratio of whole steps rather than of
        /// whichever fraction the run stopped in the middle of.
        /// </summary>
        public const int DefaultFrames = 120;

        /// <summary>
        /// Times each case this many times and keeps the fastest, for the reason
        /// <see cref="PerformanceReport.Repeats"/> gives: timing noise is one-sided, so a mean
        /// measures the machine.
        /// </summary>
        public static int Repeats = 3;

        public class Row
        {
            public string World;
            public int TargetBlocks;
            public int Blocks;
            public int Links;

            /// <summary>The allowance this row ran under. 0 is the bound switched off.</summary>
            public int Allowance;

            /// <summary>Element visits one substep over this grid costs: links plus four times nodes.</summary>
            public long SubstepCost;

            /// <summary>Substeps a full step demanded, before the allowance shortened anything.</summary>
            public float Demand;

            /// <summary>Substeps the allowance leaves room for. -1 when the bound is off.</summary>
            public int Granted;

            /// <summary>Share of simulated time the grid kept: 1.0 is real time.</summary>
            public double Rate;

            /// <summary>Measured milliseconds of a rendered frame, averaged over the run.</summary>
            public double FrameMs;

            /// <summary>The worst single frame of the run, which is what a stutter is.</summary>
            public double WorstFrameMs;

            /// <summary>Element visits the grid actually made per rendered frame.</summary>
            public double VisitsPerFrame;

            /// <summary>Milliseconds of real time spent per second of real time.</summary>
            public double MsPerRealSecond
            {
                get { return FrameMs * 60d; }
            }

            /// <summary>
            /// Nanoseconds a visit cost, measured. The figure that says whether a change made the
            /// step smaller or made it slower.
            /// </summary>
            public double NsPerVisit
            {
                get
                {
                    if (VisitsPerFrame <= 0d) return 0d;
                    return FrameMs * 1e6 / VisitsPerFrame;
                }
            }

            /// <summary>
            /// The rate a grid gave up, as the clock error `F23` prices in kelvin. Zero when the
            /// allowance never bound.
            /// </summary>
            public double ClockError
            {
                get { return Rate >= 1d ? 0d : Rate - 1d; }
            }
        }

        public static List<Row> Run(string shape, IList<int> sizes, IList<int> allowances,
            IList<string> worlds, int frames, Action<string> log)
        {
            List<Row> rows = new List<Row>();

            for (int w = 0; w < worlds.Count; w++)
            {
                for (int s = 0; s < sizes.Count; s++)
                {
                    // One hull per (world, size), reused across every allowance on it. Rebuilding
                    // would put a differently-laid-out grid under each row and turn the comparison
                    // into one about hulls (`P6`); the run resets the spread and the counters
                    // instead.
                    ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, sizes[s]);
                    Census.DriveCensus(simulation);

                    for (int a = 0; a < allowances.Count; a++)
                    {
                        if (log != null)
                        {
                            log(worlds[w] + " " + sizes[s].ToString("n0") + ", allowance "
                                + (allowances[a] == 0 ? "off" : allowances[a].ToString("n0")));
                        }

                        rows.Add(Measure(simulation, worlds[w], sizes[s], allowances[a], frames));
                    }
                }
            }

            return rows;
        }

        private static Row Measure(ThermalSimulation simulation, string world, int target,
            int allowance, int frames)
        {
            Row row = new Row();
            row.World = world;
            row.TargetBlocks = target;
            row.Blocks = simulation.Solver.Nodes.Count;
            row.Links = simulation.Solver.Links.Count;
            row.Allowance = allowance;

            EnvironmentSample sample = World(world);

            simulation.Settings.MaxElementVisitsPerStep = allowance;
            simulation.Settings.Derive();

            row.SubstepCost = simulation.SubstepCost;
            row.Granted = allowance == 0 ? -1 : simulation.SubstepBudget;

            // Warm the caches and let the substep count settle before the clock starts. A grid
            // arriving from the row above is part way through a step against a different world and
            // holding work credit against it; StepExact is the entry point that abandons both, and
            // zero steps of it is that and nothing else.
            simulation.StepExact(0, sample);
            LoadBenchmarks.SeedSpread(simulation);
            for (int i = 0; i < 30; i++) simulation.Update(LoadBenchmarks.FrameSeconds, sample);

            double best = double.MaxValue;
            double worst = 0d;
            double rate = 1d;
            double visits = 0d;
            float demand = 0f;

            int repeats = Repeats < 1 ? 1 : Repeats;
            Stopwatch watch = new Stopwatch();
            Stopwatch frame = new Stopwatch();

            for (int r = 0; r < repeats; r++)
            {
                // Each repeat starts from the same spread, for the reason StepPathLab gives:
                // conduction skips a link whose ends agree, so a run inheriting the previous run's
                // flattened grid reads cheaper for no reason but its place in the order.
                simulation.StepExact(0, sample);
                LoadBenchmarks.SeedSpread(simulation);

                // The rate counters run for the life of the simulation and this is one row of
                // many on it, so the rate is read as a delta over the window rather than as the
                // grid's lifetime figure.
                double ranBefore = simulation.SimulatedSecondsRun;
                double skippedBefore = simulation.SimulatedSecondsSkipped;

                long substepsRun = 0;
                int steps = 0;
                double repeatWorst = 0d;

                watch.Restart();
                for (int i = 0; i < frames; i++)
                {
                    long before = simulation.StepsCompleted;

                    frame.Restart();
                    simulation.Update(LoadBenchmarks.FrameSeconds, sample);
                    frame.Stop();

                    double ms = frame.Elapsed.TotalMilliseconds;
                    if (ms > repeatWorst) repeatWorst = ms;

                    if (simulation.StepsCompleted > before)
                    {
                        steps++;
                        substepsRun += simulation.Solver.LastSubsteps;
                        demand = simulation.Solver.LastRequiredSubsteps;
                    }
                }
                watch.Stop();

                double ran = simulation.SimulatedSecondsRun - ranBefore;
                double skipped = simulation.SimulatedSecondsSkipped - skippedBefore;
                double owed = ran + skipped;
                double repeatRate = owed <= 0d ? 1d : ran / owed;

                double ms_per_frame = watch.Elapsed.TotalMilliseconds / frames;
                if (ms_per_frame < best)
                {
                    best = ms_per_frame;
                    worst = repeatWorst;
                    rate = repeatRate;
                    visits = steps <= 0
                        ? 0d
                        : substepsRun * (double)row.SubstepCost / frames;
                }
            }

            row.FrameMs = best;
            row.WorstFrameMs = worst;
            row.Rate = rate;
            row.VisitsPerFrame = visits;
            row.Demand = demand;
            return row;
        }

        // ----------------------------------------------------------------------------------
        // What a lost rate is worth
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Rate deficits the price ladder measures, as the share of simulated time a grid loses.
        ///
        /// <para>
        /// 0.10 is the one point `F23` took, and it is on the list so this ladder can be checked
        /// against a figure that was published before it existed (`P4`). The rest bracket what the
        /// allowance sweep actually produces, which reaches past half on the rungs where the
        /// tightest allowance binds — far enough out that reading the curve as a straight line
        /// through `F23`'s single point would be an extrapolation rather than a measurement.
        /// </para>
        /// </summary>
        public static readonly double[] DefaultDeficits = { 0.05d, 0.10d, 0.20d, 0.30d, 0.45d, 0.60d };

        /// <summary>One deficit, and what a hull under a moving load pays for it.</summary>
        public class PriceRow
        {
            /// <summary>Share of simulated time lost. 0.10 is a clock 10 % slow.</summary>
            public double Deficit;

            /// <summary>Worst disagreement at any sample against a grid at full rate, K.</summary>
            public float PeakKelvin;

            /// <summary>Mean disagreement over the final third, K — the standing error.</summary>
            public float StandingKelvin;

            /// <summary>Kelvin standing per unit of deficit. Flat if the curve is a line.</summary>
            public double KelvinPerUnit
            {
                get { return Deficit <= 0d ? 0d : StandingKelvin / Deficit; }
            }
        }

        /// <summary>
        /// Prices a rate deficit in kelvin, by running the mechanism rather than scaling `F23`'s
        /// one point.
        ///
        /// <para>
        /// A grid whose step the allowance shortens advances less simulated time per real second,
        /// which is the same quantity `ClientInputLab`'s <c>SimSpeedError</c> degrades — so this
        /// runs that lab's slow-clock case at each deficit and reads the standing error off it.
        /// Reusing it rather than building a second rig is what keeps the two comparable, and it is
        /// why the allowance sweep does not measure kelvin itself (`M8`).
        /// </para>
        ///
        /// <para>
        /// **The scope the figure carries** (`P1`): it is a hull under a *moving* load, which is
        /// where a clock error shows at all — `F23` measured 0.00 K under a steady one, because two
        /// hulls heading to the same equilibrium at different speeds agree once they arrive. So
        /// every kelvin figure here is an upper bound on a ship whose load is steadier, and says
        /// nothing about a settled grid.
        /// </para>
        /// </summary>
        public static List<PriceRow> PriceRates(IList<double> deficits, Action<string> log)
        {
            List<PriceRow> rows = new List<PriceRow>();

            for (int i = 0; i < deficits.Count; i++)
            {
                if (log != null) log("clock " + (100d * deficits[i]).ToString("n0") + "% slow");

                ClientInputLab.Degradation how = new ClientInputLab.Degradation
                {
                    Name = "clock " + (100d * deficits[i]).ToString("n0") + "% slow",
                    Because = "advances that much less simulated time a second, as a shortened step does",
                    SimSpeedError = (float)(-deficits[i]),
                };

                ClientInputLab.Result result = ClientInputLab.Measure(
                    how, ClientDriftLab.Correction.None);

                rows.Add(new PriceRow
                {
                    Deficit = deficits[i],
                    PeakKelvin = result.PeakKelvin,
                    StandingKelvin = result.StandingKelvin,
                });
            }

            return rows;
        }

        /// <summary>
        /// Kelvin a row's rate deficit stands to cost, read off a measured ladder.
        ///
        /// Linear between the two bracketing points and held flat above the last one rather than
        /// extrapolated — a curve that was measured to 0.60 says nothing about 0.90, and reporting
        /// a number there would be inventing one.
        /// </summary>
        public static double RateCost(Row row, IList<PriceRow> ladder)
        {
            double deficit = Math.Abs(row.ClockError);
            if (deficit <= 0d || ladder == null || ladder.Count == 0) return 0d;

            if (deficit <= ladder[0].Deficit)
            {
                return ladder[0].StandingKelvin * (deficit / ladder[0].Deficit);
            }

            for (int i = 1; i < ladder.Count; i++)
            {
                if (deficit > ladder[i].Deficit) continue;

                double span = ladder[i].Deficit - ladder[i - 1].Deficit;
                double at = span <= 0d ? 0d : (deficit - ladder[i - 1].Deficit) / span;
                return ladder[i - 1].StandingKelvin
                    + at * (ladder[i].StandingKelvin - ladder[i - 1].StandingKelvin);
            }

            return ladder[ladder.Count - 1].StandingKelvin;
        }

        /// <summary>True when a row's deficit is past the last point the ladder measured.</summary>
        public static bool PastTheLadder(Row row, IList<PriceRow> ladder)
        {
            if (ladder == null || ladder.Count == 0) return Math.Abs(row.ClockError) > 0d;
            return Math.Abs(row.ClockError) > ladder[ladder.Count - 1].Deficit;
        }

        public static string PriceTable(IList<PriceRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("   deficit     peak K   standing K   K per unit");

            for (int i = 0; i < rows.Count; i++)
            {
                PriceRow r = rows[i];
                sb.Append(((100d * r.Deficit).ToString("n0") + "%").PadLeft(10));
                sb.Append(r.PeakKelvin.ToString("n2").PadLeft(11));
                sb.Append(r.StandingKelvin.ToString("n2").PadLeft(13));
                sb.Append(r.KelvinPerUnit.ToString("n1").PadLeft(13));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string PriceCsv(IList<PriceRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("deficit,peak_kelvin,standing_kelvin,kelvin_per_unit");

            for (int i = 0; i < rows.Count; i++)
            {
                PriceRow r = rows[i];
                sb.Append(r.Deficit.ToString("r")).Append(',');
                sb.Append(r.PeakKelvin.ToString("r")).Append(',');
                sb.Append(r.StandingKelvin.ToString("r")).Append(',');
                sb.Append(r.KelvinPerUnit.ToString("r"));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string Table(IList<Row> rows, IList<PriceRow> ladder)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("world         blocks     links   substep cost   allowance"
                + "   granted   demand      rate   frame ms   worst ms"
                + "   visits/frame   ns/visit   K at load");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.World.PadRight(11));
                sb.Append(r.Blocks.ToString("n0").PadLeft(11));
                sb.Append(r.Links.ToString("n0").PadLeft(10));
                sb.Append(r.SubstepCost.ToString("n0").PadLeft(15));
                sb.Append((r.Allowance == 0 ? "off" : r.Allowance.ToString("n0")).PadLeft(12));
                sb.Append((r.Granted < 0 ? "-" : r.Granted.ToString("n0")).PadLeft(10));
                sb.Append(r.Demand.ToString("n2").PadLeft(9));
                sb.Append((100d * r.Rate).ToString("n1").PadLeft(9) + "%");
                sb.Append(r.FrameMs.ToString("n3").PadLeft(11));
                sb.Append(r.WorstFrameMs.ToString("n3").PadLeft(11));
                sb.Append(r.VisitsPerFrame.ToString("n0").PadLeft(15));
                sb.Append(r.NsPerVisit.ToString("n2").PadLeft(11));
                string kelvin = RateCost(r, ladder).ToString("n2");
                if (PastTheLadder(r, ladder)) kelvin = ">" + kelvin;
                sb.Append(kelvin.PadLeft(12));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string Csv(IList<Row> rows, IList<PriceRow> ladder)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("world,target_blocks,blocks,links,substep_cost,allowance,granted,"
                + "demand,rate,frame_ms,worst_frame_ms,visits_per_frame,ns_per_visit,"
                + "clock_error,kelvin_at_load,past_the_ladder");

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                sb.Append(r.World).Append(',');
                sb.Append(r.TargetBlocks).Append(',');
                sb.Append(r.Blocks).Append(',');
                sb.Append(r.Links).Append(',');
                sb.Append(r.SubstepCost).Append(',');
                sb.Append(r.Allowance).Append(',');
                sb.Append(r.Granted).Append(',');
                sb.Append(r.Demand.ToString("r")).Append(',');
                sb.Append(r.Rate.ToString("r")).Append(',');
                sb.Append(r.FrameMs.ToString("r")).Append(',');
                sb.Append(r.WorstFrameMs.ToString("r")).Append(',');
                sb.Append(r.VisitsPerFrame.ToString("r")).Append(',');
                sb.Append(r.NsPerVisit.ToString("r")).Append(',');
                sb.Append(r.ClockError.ToString("r")).Append(',');
                sb.Append(RateCost(r, ladder).ToString("r")).Append(',');
                sb.Append(PastTheLadder(r, ladder) ? 1 : 0);
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
