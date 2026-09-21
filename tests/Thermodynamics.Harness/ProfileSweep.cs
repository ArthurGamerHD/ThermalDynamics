using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ProfileSweep
    {
        public const float DivergenceKelvin = 10000f;

        public const int ConvergenceSamples = 5;

        public const float ConvergenceFraction = 0.002f;

        public class Cell
        {
            public string Rig;
            public string Profile;

            public int Nodes;
            public int Links;
            public int Loops;
            public int RoomsWithAir;

            public float PeakKelvin;
            public float MeanKelvin;
            public float ColdestKelvin;
            public bool Converged;

            public float EnergyDriftFraction;

            public float SubstepsMean;
            public int SubstepsMax;

            public float SubstepsWanted;

            public float LinkVisitsPerSecond;
            public double MillisecondsPerStep;
            public long Steps;

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


        public static readonly string[] ExtraRigs =
        {
            "x-shock", "x-stiff-lattice", "x-many-loops", "x-overloaded",
            "x-plumbed-ship", "x-pressurised-ship", "x-burning-ship",
        };

/// <summary>HullWithSource operation.</summary>
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

/// <summary>Builds the API method table.</summary>
        private static ScenarioRunner BuildExtra(string rig, BalanceProfile profile)
        {
            ThermalSettings settings = profile.ToSettings();

            switch (rig)
            {
                case "x-shock":
                {
                    BlockInstance source;
/// <summary>HullWithSource operation.</summary>
                    GridBuilder builder = HullWithSource(4000000f, out source);
                    ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);

/// <summary>ScenarioRunner operation.</summary>
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

/// <summary>ScenarioRunner operation.</summary>
                    ScenarioRunner runner = new ScenarioRunner(builder.BuildSimulation(settings, 293.15f));
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-many-loops":
                {
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

/// <summary>ScenarioRunner operation.</summary>
                    ScenarioRunner runner = new ScenarioRunner(builder.BuildSimulation(settings, 293.15f));
                    runner.Environment = t => Worlds.Shadow();
                    runner.Track("source", source);
                    runner.Run(3600f, 120f);
                    return runner;
                }

                case "x-overloaded":
                {
                    BlockInstance source;
/// <summary>HullWithSource operation.</summary>
                    GridBuilder builder = HullWithSource(40000000f, out source);

/// <summary>ScenarioRunner operation.</summary>
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

/// <summary>Sets the tled.</summary>
        public static bool Settled(IList<Sample> samples)
        {
            if (samples == null || samples.Count < 2) return false;

            int from = samples.Count - ConvergenceSamples;
            if (from < 1) from = 1;

            float last = samples[samples.Count - 1].HottestTemperature;
            float span = Math.Max(1f, Math.Abs(last));

            for (int i = from; i < samples.Count; i++)
            {
                float moved = Math.Abs(samples[i].HottestTemperature
                    - samples[i - 1].HottestTemperature);
                if (moved / span >= ConvergenceFraction) return false;
            }

            return true;
        }

/// <summary>FromBuilt operation.</summary>
        private static ScenarioRunner FromBuilt(WorstCases.Built built)
        {
/// <summary>ScenarioRunner operation.</summary>
            ScenarioRunner runner = new ScenarioRunner(built.Simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(1800f, 60f);
            return runner;
        }


/// <summary>Run operation.</summary>
        public static List<Cell> Run()
        {
/// <summary>List operation.</summary>
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

/// <summary>MeasureAtItsOwnClock operation.</summary>
        public static Cell MeasureAtItsOwnClock(BalanceProfile profile, string rig, bool extra)
        {
            float shipped = BalanceProfile.Shipped().HeatTimeScale;
            float scale = profile.HeatTimeScale <= 0f ? 1f : shipped / profile.HeatTimeScale;

            ScenarioRunner.DurationScale = scale;
            try
            {
/// <summary>Measure operation.</summary>
                return Measure(profile, rig, extra);
            }
            finally
            {
                ScenarioRunner.DurationScale = 1f;
            }
        }

/// <summary>Measure operation.</summary>
        private static Cell Measure(BalanceProfile profile, string rig, bool extra)
        {
            Cell cell = new Cell { Rig = rig, Profile = profile.Name };

            ThermalSettings wanted = profile.ToSettings();
/// <summary>Merge operation.</summary>
            GridBuilder.SettingsOverride = existing => Merge(existing, wanted);
            Catalog.MaterialOverride = profile.Material;

            try
            {
                Stopwatch clock = Stopwatch.StartNew();

                ScenarioRunner runner = extra
/// <summary>Builds the method table.</summary>
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
/// <summary>Coldest operation.</summary>
                cell.ColdestKelvin = Coldest(solver);
                cell.OverCritical = last.OverheatingBlocks;

/// <summary>Sets the tled.</summary>
                cell.Converged = Settled(runner.Samples);

                if (first.TotalEnergy > 0f)
                {
                    cell.EnergyDriftFraction = (last.TotalEnergy - first.TotalEnergy) / first.TotalEnergy;
                }

                cell.Diverged = !IsFinite(last.HottestTemperature)
                    || (last.HottestTemperature > DivergenceKelvin && !cell.Converged);
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

/// <summary>IsFinite operation.</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

/// <summary>Coldest operation.</summary>
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

/// <summary>Merge operation.</summary>
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


/// <summary>N operation.</summary>
        private static string N(float value, int decimals = 1)
        {
            return IsFinite(value) ? value.ToString("n" + decimals, CultureInfo.InvariantCulture) : "inf";
        }

/// <summary>Report operation.</summary>
        public static string Report()
        {
/// <summary>Run operation.</summary>
            List<Cell> cells = Run();
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();

/// <summary>List operation.</summary>
            List<string> profiles = new List<string>();
            foreach (BalanceProfile profile in BalanceProfile.All()) profiles.Add(profile.Name);

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
/// <summary>N operation.</summary>
                    cell.Profile, cell.Rig, N(cell.PeakKelvin, 0),
                    N(cell.StarvedShare * 100f, 0) + "%", cell.OverCritical, why));
            }
            sb.AppendLine();
            sb.AppendLine(bad + " of " + cells.Count + " cells are not trustworthy.");

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
/// <summary>N operation.</summary>
                    profile, N(substeps / count, 2), max, N(wantedMax, 0),
                    N(visits / count, 0), N((float)(ms / count), 3), diverged));
            }

            sb.AppendLine();
            sb.AppendLine("WORST-CASE RIGS — peak K, and what each grid was");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-20} {1,7} {2,7} {3,6} {4,5}",
/// <summary>Header operation.</summary>
                "rig", "nodes", "links", "loops", "air") + Header(profiles));

            foreach (string rig in ExtraRigs)
            {
/// <summary>Find operation.</summary>
                Cell shape = Find(cells, rig, "shipped");
                if (shape == null) continue;

/// <summary>StringBuilder operation.</summary>
                StringBuilder line = new StringBuilder(string.Format("{0,-20} {1,7} {2,7} {3,6} {4,5}",
                    rig, shape.Nodes, shape.Links, shape.Loops, shape.RoomsWithAir));

                foreach (string profile in profiles)
                {
/// <summary>Find operation.</summary>
                    Cell cell = Find(cells, rig, profile);
                    line.Append(string.Format("{0,15}", cell == null ? "-"
                        : cell.Failed ? cell.Error
/// <summary>N operation.</summary>
                        : N(cell.PeakKelvin, 0) + (cell.Diverged ? "!" : cell.Converged ? "" : "~")));
                }
                sb.AppendLine(line.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("! diverged past " + N(DivergenceKelvin, 0)
                + " K    ~ still climbing when the run ended");
            return sb.ToString();
        }

/// <summary>Find operation.</summary>
        private static Cell Find(List<Cell> cells, string rig, string profile)
        {
            foreach (Cell cell in cells)
            {
                if (cell.Rig == rig && cell.Profile == profile) return cell;
            }
            return null;
        }

/// <summary>Header operation.</summary>
        private static string Header(List<string> profiles)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder sb = new StringBuilder();
            foreach (string profile in profiles) sb.Append(string.Format("{0,15}", profile));
            return sb.ToString();
        }

/// <summary>Csv operation.</summary>
        public static string Csv()
        {
/// <summary>StringBuilder operation.</summary>
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
/// <summary>F operation.</summary>
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

/// <summary>F operation.</summary>
        private static string F(float value)
        {
            return IsFinite(value) ? value.ToString("r", CultureInfo.InvariantCulture) : "inf";
        }
    }
}
