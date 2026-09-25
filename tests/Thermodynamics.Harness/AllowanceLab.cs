using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class AllowanceLab
    {
        public static readonly int[] DefaultSizes = { 8000, 16000, 32000, 64000, 125000 };

        public static readonly int[] DefaultAllowances = { 0, 1000000, 2000000, 3200000, 4000000 };

        public static readonly string[] DefaultWorlds = { "vacuum", "atmosphere", "flight" };


        private static EnvironmentSample World(string name)
        {
            if (name == "vacuum") return Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));
            if (name == "atmosphere") return Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 40f);
            return Worlds.Flight(1f, 300f);
        }

        public const int DefaultFrames = 120;

        public static int Repeats = 3;

        public class Row
        {
            public string World;
            public int TargetBlocks;
            public int Blocks;
            public int Links;

            public int Allowance;

            public int Cap;

            public long SubstepCost;

            public float Demand;

            public int Granted;

            public double Rate;

            public double FrameMs;

            public double WorstFrameMs;

            public double VisitsPerFrame;

            public double MsPerRealSecond
            {
                get { return FrameMs * 60d; }
            }

            public double NsPerVisit
            {
                get
                {
                    if (VisitsPerFrame <= 0d) return 0d;
                    return FrameMs * 1e6 / VisitsPerFrame;
                }
            }

            public double ClockError
            {
                get { return Rate >= 1d ? 0d : Rate - 1d; }
            }
        }


        public static List<Row> Run(string shape, IList<int> sizes, IList<int> allowances,
            IList<string> worlds, int frames, Action<string> log)
        {

            return Run(shape, sizes, allowances, worlds, new[] { 0 }, frames, log);
        }


        public static List<Row> RunFloored(string shape, IList<int> sizes, IList<int> allowances,
            IList<string> worlds, bool floorWhenOverBudget, int frames, Action<string> log)
        {

            List<Row> rows = new List<Row>();

            for (int w = 0; w < worlds.Count; w++)
            {
                for (int s = 0; s < sizes.Count; s++)
                {
                    ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, sizes[s]);
                    Census.DriveCensus(simulation);
                    simulation.Settings.FloorBlocksWhenOverBudget = floorWhenOverBudget;

                    for (int a = 0; a < allowances.Count; a++)
                    {
                        if (log != null)
                        {
                            log(worlds[w] + " " + sizes[s].ToString("n0") + ", allowance "
                                + (allowances[a] == 0 ? "off" : allowances[a].ToString("n0"))
                                + ", floor " + (floorWhenOverBudget ? "on" : "off"));
                        }

                        rows.Add(Measure(simulation, worlds[w], sizes[s], allowances[a], 0, frames));
                    }
                }
            }

            return rows;
        }


        public static List<Row> Run(string shape, IList<int> sizes, IList<int> allowances,
            IList<string> worlds, IList<int> caps, int frames, Action<string> log)
        {

            List<Row> rows = new List<Row>();

            for (int w = 0; w < worlds.Count; w++)
            {
                for (int s = 0; s < sizes.Count; s++)
                {
                    ThermalSimulation simulation = LoadBenchmarks.BuildSettled(shape, sizes[s]);
                    Census.DriveCensus(simulation);

                    for (int a = 0; a < allowances.Count; a++)
                    {
                        for (int c = 0; c < caps.Count; c++)
                        {
                            if (log != null)
                            {
                                log(worlds[w] + " " + sizes[s].ToString("n0") + ", allowance "
                                    + (allowances[a] == 0 ? "off" : allowances[a].ToString("n0"))
                                    + ", cap " + (caps[c] == 0 ? "off" : caps[c].ToString()));
                            }

                            rows.Add(Measure(simulation, worlds[w], sizes[s], allowances[a],
                                caps[c], frames));
                        }
                    }
                }
            }

            return rows;
        }


        private static Row Measure(ThermalSimulation simulation, string world, int target,
            int allowance, int cap, int frames)
        {

            Row row = new Row();
            row.Cap = cap;
            row.World = world;
            row.TargetBlocks = target;
            row.Blocks = simulation.Solver.Nodes.Count;
            row.Links = simulation.Solver.Links.Count;
            row.Allowance = allowance;


            EnvironmentSample sample = World(world);

            simulation.Settings.MaxElementVisitsPerStep = allowance;
            simulation.Settings.MaxSubstepsPerBlock = cap;
            simulation.Settings.Derive();

            row.SubstepCost = simulation.SubstepCost;
            row.Granted = allowance == 0 ? -1 : simulation.SubstepBudget;

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
                simulation.StepExact(0, sample);
                LoadBenchmarks.SeedSpread(simulation);

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

                double msPerFrame = watch.Elapsed.TotalMilliseconds / frames;
                if (msPerFrame < best)
                {
                    best = msPerFrame;
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


        public static readonly double[] DefaultDeficits = { 0.05d, 0.10d, 0.20d, 0.30d, 0.45d, 0.60d };

        public class PriceRow
        {
            public double Deficit;

            public float PeakKelvin;

            public float StandingKelvin;

            public double KelvinPerUnit
            {
                get { return Deficit <= 0d ? 0d : StandingKelvin / Deficit; }
            }
        }


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
