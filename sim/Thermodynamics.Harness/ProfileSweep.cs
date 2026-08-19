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
    /// Every scenario and every worst-case rig, run under every <see cref="BalanceProfile"/>, with
    /// every metric worth having.
    ///
    /// <see cref="ProfileLab"/> compares the profiles on one rig; this asks whether those answers
    /// survive contact with the whole library and with cases built specifically to break them.
    ///
    /// Four families of metric, because a profile can fail in four unrelated ways:
    ///
    /// * **Performance** — substeps asked for and granted, link visits per simulated second, real
    ///   milliseconds per step, and the size of what was built.
    /// * **Accuracy** — where it settled, whether it settled at all, and how far the total energy
    ///   in the grid moved over the run.
    /// * **Failure** — steps refused the substeps they asked for, blocks past critical, and
    ///   temperatures that left the physically possible.
    /// * **Shape** — hottest and coldest block, and how many nodes, links, loops and rooms carried
    ///   the answer.
    ///
    /// A profile is only as good as its worst row. The temperature columns say what it got right;
    /// the starvation and divergence columns say where it stopped being a simulation.
    /// </summary>
    public static class ProfileSweep
    {
        /// <summary>
        /// Above this, a reading is not a hot ship — it is a diverged integration. Tungsten boils
        /// at about 6,200 K, so nothing on a grid has any business here, and calling it a failure
        /// rather than a big number is the whole point of the column.
        /// </summary>
        public const float DivergenceKelvin = 10000f;

        /// <summary>One rig under one profile, fully measured.</summary>
        public class Cell
        {
            public string Rig;
            public string Profile;

            // ---- shape of what was built ----
            public int Nodes;
            public int Links;
            public int Loops;
            public int RoomsWithAir;

            // ---- accuracy ----
            public float PeakKelvin;
            public float MeanKelvin;
            public float ColdestKelvin;
            public bool Converged;

            /// <summary>Fractional change in total grid energy over the run.</summary>
            public float EnergyDriftFraction;

            // ---- performance ----
            public float SubstepsMean;
            public int SubstepsMax;

            /// <summary>Substeps the stability estimate last asked for — may exceed what was granted.</summary>
            public float SubstepsWanted;

            public float LinkVisitsPerSecond;
            public double MillisecondsPerStep;
            public long Steps;

            // ---- failure ----
            /// <summary>How much of the last estimate's demand was refused, 0..1.</summary>
            public float StarvedShare;

            public int OverCritical;
            public bool Diverged;
            public bool Failed;
            public string Error;

            public bool Trustworthy
            {
                get { return !Failed && !Diverged && Converged; }
            }
        }

        private static readonly HashSet<string> NotDrivable = new HashSet<string>
        {
            "units", "perf", "solver", "shadow-cost", "capital", "fleet",
        };

        // ---- the extra rigs ----------------------------------------------------------------------

        /// <summary>
        /// Cases built to break a profile rather than to describe the model.
        ///
        /// The scenario library was written to answer questions about the simulation, and it asks
        /// them on grids that behave. None of it sustains a load past what the hull can shed, none
        /// of it switches a load off to see what happens on the way down, and none of it puts
        /// enough plumbing on a grid for the loop integration to be the stiffest thing there. Those
        /// are the three shapes that broke the arcade profile, so they belong in the sweep.
        /// </summary>
        public static readonly string[] ExtraRigs =
        {
            "x-shock", "x-stiff-lattice", "x-many-loops", "x-overloaded",
            "x-plumbed-ship", "x-pressurised-ship", "x-burning-ship",
        };

        private static GridBuilder HullWithSource(float watts, out BlockInstance source)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));

            BlockInstance centre = builder.Grid.GetAtCell(new Vector3I(1, 1, 1));
            builder.Grid.Remove(centre);
            builder.Placed.Remove(centre);
            builder.Place(Catalog.Reactor(), new Vector3I(1, 1, 1));

            source = builder.Last;
            source.PowerConsumedWatts = watts;
            return builder;
        }

        private static ScenarioRunner BuildExtra(string rig, BalanceProfile profile)
        {
            ThermalSettings settings = profile.ToSettings();

            switch (rig)
            {
                case "x-shock":
                {
                    // Full load, then nothing. A profile that only ever climbs is never asked
                    // whether it can come back down, and a clamped one overshoots on the way.
                    BlockInstance source;
                    GridBuilder builder = HullWithSource(4000000f, out source);
                    ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

                    ScenarioRunner runner = new ScenarioRunner(simulation);
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.AfterStep = s =>
                    {
                        float wanted = runner.ElapsedSeconds > 1800f ? 0f : 4000000f;
                        if (source.PowerConsumedWatts != wanted)
                        {
                            source.PowerConsumedWatts = wanted;
                            s.RefreshBlock(source);
                        }
                    };
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-stiff-lattice":
                {
                    // Featherweight blocks bolted to heavy ones, over and over. Stiffness is
                    // conductance over capacity, so this is the cheapest way a player can build the
                    // worst case the substep estimator exists for.
                    GridBuilder builder = GridBuilder.Large();
                    for (int x = 0; x < 6; x++)
                    {
                        for (int z = 0; z < 6; z++)
                        {
                            builder.Place(Catalog.HeavyArmor(), new Vector3I(x, 0, z));
                            builder.Place(BlockModel.Solid("Foil", Vector3I.One, 8f,
                                Catalog.DefaultThermal()), new Vector3I(x, 1, z));
                        }
                    }
                    builder.Place(Catalog.Reactor(), new Vector3I(0, 2, 0));
                    BlockInstance source = builder.Last;
                    source.PowerConsumedWatts = 2000000f;

                    ScenarioRunner runner = new ScenarioRunner(builder.BuildSimulation(settings, 293.15f));
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-many-loops":
                {
                    // Eight rings on one grid. The loop integration is a separate path from block
                    // conduction and is where arcade diverged worst, so it gets a rig of its own.
                    GridBuilder builder = GridBuilder.Large();
                    builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(10, 1, 10));

                    BlockInstance hot = builder.Grid.GetAtCell(Vector3I.Zero);
                    builder.Grid.Remove(hot);
                    builder.Placed.Remove(hot);
                    builder.Place(Catalog.Reactor(), Vector3I.Zero);
                    BlockInstance source = builder.Last;
                    source.PowerConsumedWatts = 2000000f;

                    for (int ring = 0; ring < 8; ring++)
                    {
                        PipeFitter.BuildRing(builder,
                            PipeFitter.RectangleXZ(new Vector3I(0, 1 + ring, 0), 5, 4));
                    }

                    ScenarioRunner runner = new ScenarioRunner(builder.BuildSimulation(settings, 293.15f));
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-overloaded":
                {
                    // Far more heat than the hull can shed, held there. The steady state is damage,
                    // and a profile has to reach it without leaving the number line.
                    BlockInstance source;
                    GridBuilder builder = HullWithSource(40000000f, out source);

                    ScenarioRunner runner = new ScenarioRunner(builder.BuildSimulation(settings, 293.15f));
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-plumbed-ship":
                    return FromBuilt(WorstCases.Plumbed("ship", 4000, 8, settings));

                case "x-pressurised-ship":
                    return FromBuilt(WorstCases.Pressurised("ship", 4000, settings));

                case "x-burning-ship":
                    return FromBuilt(WorstCases.Burning("ship", 4000, settings));

                default:
                    return null;
            }
        }

        private static ScenarioRunner FromBuilt(WorstCases.Built built)
        {
            ScenarioRunner runner = new ScenarioRunner(built.Simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(1800f, 60f);
            return runner;
        }

        // ---- running -------------------------------------------------------------------------------

        public static List<Cell> Run()
        {
            List<Cell> cells = new List<Cell>();

            foreach (BalanceProfile profile in BalanceProfile.All())
            {
                foreach (string scenario in Scenarios.Names)
                {
                    if (NotDrivable.Contains(scenario)) continue;
                    cells.Add(Measure(profile, scenario, false));
                }
                foreach (string rig in ExtraRigs)
                {
                    cells.Add(Measure(profile, rig, true));
                }
            }
            return cells;
        }

        private static Cell Measure(BalanceProfile profile, string rig, bool extra)
        {
            Cell cell = new Cell { Rig = rig, Profile = profile.Name };

            ThermalSettings wanted = profile.ToSettings();
            GridBuilder.SettingsOverride = existing => Merge(existing, wanted);
            Catalog.MaterialOverride = profile.Material;

            try
            {
                Stopwatch clock = Stopwatch.StartNew();

                ScenarioRunner runner = extra
                    ? BuildExtra(rig, profile)
                    : Scenarios.Run(rig).Runner;

                clock.Stop();

                if (runner == null || runner.Samples.Count == 0)
                {
                    cell.Failed = true;
                    cell.Error = "no-samples";
                    return cell;
                }

                ThermalSimulation simulation = runner.Simulation;
                ThermalSolver solver = simulation.Solver;
                SimulationWork work = simulation.Work;

                long steps = Math.Max(1, work.SolverSteps);
                cell.Steps = steps;
                cell.SubstepsMean = work.SolverSubsteps / (float)steps;
                cell.MillisecondsPerStep = clock.Elapsed.TotalMilliseconds / steps;

                foreach (Sample sample in runner.Samples)
                {
                    if (sample.Substeps > cell.SubstepsMax) cell.SubstepsMax = sample.Substeps;
                }

                cell.SubstepsWanted = solver.LastRequiredSubsteps;
                cell.StarvedShare = cell.SubstepsWanted > wanted.MaxSubsteps
                    ? (cell.SubstepsWanted - wanted.MaxSubsteps) / cell.SubstepsWanted
                    : 0f;

                cell.Nodes = solver.Nodes.Count;
                cell.Links = solver.LinkCount;
                cell.Loops = solver.Loops == null ? 0 : solver.Loops.Count;
                cell.RoomsWithAir = simulation.RoomAir == null ? 0 : simulation.RoomAir.Count;
                cell.LinkVisitsPerSecond = cell.Links * cell.SubstepsMean * wanted.StepsPerSecond;

                Sample first = runner.Samples[0];
                Sample last = runner.Final;

                cell.PeakKelvin = last.HottestTemperature;
                cell.MeanKelvin = last.MeanTemperature;
                cell.ColdestKelvin = Coldest(solver);
                cell.OverCritical = last.OverheatingBlocks;

                if (runner.Samples.Count >= 2)
                {
                    float previous = runner.Samples[runner.Samples.Count - 2].HottestTemperature;
                    float span = Math.Max(1f, Math.Abs(last.HottestTemperature));
                    cell.Converged = Math.Abs(previous - last.HottestTemperature) / span < 0.002f;
                }

                if (first.TotalEnergy > 0f)
                {
                    cell.EnergyDriftFraction = (last.TotalEnergy - first.TotalEnergy) / first.TotalEnergy;
                }

                cell.Diverged = !IsFinite(last.HottestTemperature)
                    || last.HottestTemperature > DivergenceKelvin;
            }
            catch (Exception error)
            {
                cell.Failed = true;
                cell.Error = error.GetType().Name;
            }
            finally
            {
                GridBuilder.SettingsOverride = null;
                Catalog.MaterialOverride = null;
            }

            return cell;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Coldest(ThermalSolver solver)
        {
            float coldest = float.MaxValue;
            IList<ThermalNode> nodes = solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature < coldest) coldest = nodes[i].Temperature;
            }
            return coldest == float.MaxValue ? 0f : coldest;
        }

        /// <summary>
        /// The profile's values, over whatever the scenario asked for.
        ///
        /// Mechanism switches are left as the scenario set them: a scenario that deliberately turns
        /// one off is asking a specific question, and a profile overriding that would measure a
        /// different experiment than the one named.
        /// </summary>
        private static ThermalSettings Merge(ThermalSettings scenario, ThermalSettings profile)
        {
            scenario.HeatTimeScale = profile.HeatTimeScale;
            scenario.Frequency = profile.Frequency;
            scenario.MaxSubsteps = profile.MaxSubsteps;
            scenario.ClampConductionOvershoot = profile.ClampConductionOvershoot;
            scenario.ClampEnvironmentOvershoot = profile.ClampEnvironmentOvershoot;
            scenario.SolarEnergy = profile.SolarEnergy;
            scenario.VacuumTemperature = profile.VacuumTemperature;
            scenario.RoomConvectionCoefficient = profile.RoomConvectionCoefficient;
            scenario.WellMixedCoolant = profile.WellMixedCoolant;
            return scenario.Derive();
        }

        // ---- reporting ------------------------------------------------------------------------------

        private static string N(float value, int decimals = 1)
        {
            return IsFinite(value) ? value.ToString("n" + decimals, CultureInfo.InvariantCulture) : "inf";
        }

        public static string Report()
        {
            List<Cell> cells = Run();
            StringBuilder sb = new StringBuilder();

            List<string> profiles = new List<string>();
            foreach (BalanceProfile profile in BalanceProfile.All()) profiles.Add(profile.Name);

            // ---- failures first: the only section that can condemn a profile -------------------
            sb.AppendLine("FAILURE POINTS");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-11} {1,-22} {2,14} {3,9} {4,10} {5,12}",
                "profile", "rig", "peak K", "starved", "over crit", "why"));

            int bad = 0;
            foreach (Cell cell in cells)
            {
                if (cell.Trustworthy) continue;
                bad++;
                string why = cell.Failed ? cell.Error : cell.Diverged ? "DIVERGED" : "not settled";
                sb.AppendLine(string.Format("{0,-11} {1,-22} {2,14} {3,9} {4,10} {5,12}",
                    cell.Profile, cell.Rig, N(cell.PeakKelvin, 0),
                    N(cell.StarvedShare * 100f, 0) + "%", cell.OverCritical, why));
            }
            sb.AppendLine();
            sb.AppendLine(bad + " of " + cells.Count + " cells are not trustworthy.");

            // ---- cost --------------------------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine("COST — averaged over every rig that ran");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-11} {1,10} {2,7} {3,10} {4,14} {5,11} {6,10}",
                "profile", "substeps", "max", "wanted", "visits/s", "ms/step", "diverged"));

            foreach (string profile in profiles)
            {
                float substeps = 0f;
                float visits = 0f;
                double ms = 0d;
                int max = 0;
                float wantedMax = 0f;
                int count = 0;
                int diverged = 0;

                foreach (Cell cell in cells)
                {
                    if (cell.Profile != profile || cell.Failed) continue;
                    substeps += cell.SubstepsMean;
                    visits += cell.LinkVisitsPerSecond;
                    ms += cell.MillisecondsPerStep;
                    if (cell.SubstepsMax > max) max = cell.SubstepsMax;
                    if (cell.SubstepsWanted > wantedMax) wantedMax = cell.SubstepsWanted;
                    if (cell.Diverged) diverged++;
                    count++;
                }
                if (count == 0) continue;

                sb.AppendLine(string.Format("{0,-11} {1,10} {2,7} {3,10} {4,14} {5,11} {6,10}",
                    profile, N(substeps / count, 2), max, N(wantedMax, 0),
                    N(visits / count, 0), N((float)(ms / count), 3), diverged));
            }

            // ---- the new rigs ------------------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine("WORST-CASE RIGS — peak K, and what each grid was");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-20} {1,7} {2,7} {3,6} {4,5}",
                "rig", "nodes", "links", "loops", "air") + Header(profiles));

            foreach (string rig in ExtraRigs)
            {
                Cell shape = Find(cells, rig, "shipped");
                if (shape == null) continue;

                StringBuilder line = new StringBuilder(string.Format("{0,-20} {1,7} {2,7} {3,6} {4,5}",
                    rig, shape.Nodes, shape.Links, shape.Loops, shape.RoomsWithAir));

                foreach (string profile in profiles)
                {
                    Cell cell = Find(cells, rig, profile);
                    line.Append(string.Format("{0,15}", cell == null ? "-"
                        : cell.Failed ? cell.Error
                        : N(cell.PeakKelvin, 0) + (cell.Diverged ? "!" : cell.Converged ? "" : "~")));
                }
                sb.AppendLine(line.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("! diverged past " + N(DivergenceKelvin, 0)
                + " K    ~ still climbing when the run ended");
            return sb.ToString();
        }

        private static Cell Find(List<Cell> cells, string rig, string profile)
        {
            foreach (Cell cell in cells)
            {
                if (cell.Rig == rig && cell.Profile == profile) return cell;
            }
            return null;
        }

        private static string Header(List<string> profiles)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string profile in profiles) sb.Append(string.Format("{0,15}", profile));
            return sb.ToString();
        }

        /// <summary>Every cell, every metric, for diffing one tuning pass against the next.</summary>
        public static string Csv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("rig,profile,nodes,links,loops,rooms_with_air,peak_k,mean_k,coldest_k,"
                + "converged,diverged,failed,error,energy_drift,substeps_mean,substeps_max,"
                + "substeps_wanted,starved_share,link_visits_per_s,ms_per_step,steps,over_critical");

            foreach (Cell cell in Run())
            {
                sb.AppendLine(string.Join(",", new string[]
                {
                    cell.Rig, cell.Profile,
                    cell.Nodes.ToString(CultureInfo.InvariantCulture),
                    cell.Links.ToString(CultureInfo.InvariantCulture),
                    cell.Loops.ToString(CultureInfo.InvariantCulture),
                    cell.RoomsWithAir.ToString(CultureInfo.InvariantCulture),
                    F(cell.PeakKelvin), F(cell.MeanKelvin), F(cell.ColdestKelvin),
                    cell.Converged ? "1" : "0", cell.Diverged ? "1" : "0", cell.Failed ? "1" : "0",
                    cell.Error ?? "", F(cell.EnergyDriftFraction),
                    F(cell.SubstepsMean), cell.SubstepsMax.ToString(CultureInfo.InvariantCulture),
                    F(cell.SubstepsWanted), F(cell.StarvedShare), F(cell.LinkVisitsPerSecond),
                    cell.MillisecondsPerStep.ToString("r", CultureInfo.InvariantCulture),
                    cell.Steps.ToString(CultureInfo.InvariantCulture),
                    cell.OverCritical.ToString(CultureInfo.InvariantCulture),
                }));
            }
            return sb.ToString();
        }

        private static string F(float value)
        {
            return IsFinite(value) ? value.ToString("r", CultureInfo.InvariantCulture) : "inf";
        }
    }
}
