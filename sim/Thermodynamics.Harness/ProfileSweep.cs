using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Every scenario in the library, run under every <see cref="BalanceProfile"/>.
    ///
    /// <see cref="ProfileLab"/> compares the profiles on one rig, which is enough to see what each
    /// buys and costs and nothing like enough to call a balance. This runs them across the whole
    /// scenario library and the worst-case hulls, because a profile that behaves on a heater and a
    /// panel can still fall apart on a sealed room, a reentry, a pumped ring or a burning ship —
    /// and those are the cases a player actually meets.
    ///
    /// Two things it is looking for.
    ///
    /// **Divergence.** How far each profile's answer sits from the physical one, scenario by
    /// scenario. A profile that tracks physics on a bare hull and loses 300 K once there is air in
    /// the compartment has not been shown to be cheap; it has been shown to be cheap on hulls.
    ///
    /// **Silence.** A scenario whose peak barely moves under a profile is not agreeing with it —
    /// it is failing to exercise it. The library's run lengths are all tuned to the shipped clock,
    /// so a profile 225 times slower reaches almost nothing in the same simulated seconds. That is
    /// a fact about the library rather than about the profile, and the report says so rather than
    /// reading a flat number as a match.
    /// </summary>
    public static class ProfileSweep
    {
        /// <summary>One scenario under one profile.</summary>
        public class Cell
        {
            public string Scenario;
            public string Profile;

            /// <summary>Hottest block at the end of the run, K.</summary>
            public float PeakKelvin;

            /// <summary>Mean of every tracked series at the end of the run, K.</summary>
            public float MeanKelvin;

            /// <summary>Substeps the last step asked for.</summary>
            public int Substeps;

            /// <summary>How far the run moved from where it started. Near zero means it did nothing.</summary>
            public float TravelledKelvin;

            /// <summary>
            /// True when the last two samples agreed. A scenario runs for a fixed number of
            /// simulated seconds, so a profile whose clock is 225 times slower is still climbing
            /// when the run ends — and its final reading is a point on a transient, not a
            /// settling point. Comparing one against another profile's equilibrium would report a
            /// disagreement that is entirely an artefact of the run length.
            /// </summary>
            public bool Converged;

            public bool Failed;
            public string Error;
        }

        /// <summary>
        /// Scenarios that build their own settings from a profile name or otherwise refuse to be
        /// driven from outside. Running them here would report the shipped answer under every
        /// profile, which is worse than not running them.
        /// </summary>
        private static readonly HashSet<string> NotDrivable = new HashSet<string>
        {
            "units",        // sweeps HeatTimeScale itself; a profile setting it is meaningless
            "perf",         // a throughput measurement, not a temperature one
            "solver",       // likewise
            "shadow-cost",  // likewise
            "capital",      // likewise
            "fleet",        // likewise
        };

        public static List<Cell> Run()
        {
            List<Cell> cells = new List<Cell>();

            foreach (BalanceProfile profile in BalanceProfile.All())
            {
                foreach (string scenario in Scenarios.Names)
                {
                    if (NotDrivable.Contains(scenario)) continue;
                    cells.Add(RunOne(profile, scenario));
                }
            }
            return cells;
        }

        private static Cell RunOne(BalanceProfile profile, string scenario)
        {
            Cell cell = new Cell { Scenario = scenario, Profile = profile.Name };

            ThermalSettings wanted = profile.ToSettings();
            GridBuilder.SettingsOverride = existing => Merge(existing, wanted);
            Catalog.MaterialOverride = profile.Material;

            try
            {
                ScenarioResult result = Scenarios.Run(scenario);
                ScenarioRunner runner = result.Runner;
                if (runner == null || runner.Samples.Count == 0)
                {
                    cell.Failed = true;
                    cell.Error = "no samples";
                    return cell;
                }

                Sample first = runner.Samples[0];
                Sample last = runner.Final;

                cell.PeakKelvin = last.HottestTemperature;
                cell.MeanKelvin = last.MeanTemperature;
                cell.Substeps = last.Substeps;
                cell.TravelledKelvin = Math.Abs(last.HottestTemperature - first.HottestTemperature);

                if (runner.Samples.Count >= 2)
                {
                    float previous = runner.Samples[runner.Samples.Count - 2].HottestTemperature;
                    float span = Math.Max(1f, Math.Abs(last.HottestTemperature));
                    cell.Converged = Math.Abs(previous - last.HottestTemperature) / span < 0.002f;
                }
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

        /// <summary>
        /// The profile's values, over whatever the scenario asked for.
        ///
        /// A scenario that deliberately switches a mechanism off — the feature-toggle ones, the
        /// isolated-conduction ones — must keep that, or the sweep is measuring a different
        /// experiment than the one named. So the profile writes the pace, the integration and the
        /// environment constants, and leaves every mechanism switch as the scenario set it.
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

        // ---- reporting ----------------------------------------------------------------------------

        private static string N(float value, int decimals = 1)
        {
            return value.ToString("n" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>The sweep as a matrix: scenario down, profile across.</summary>
        public static string Report()
        {
            List<Cell> cells = Run();

            List<string> profiles = new List<string>();
            foreach (BalanceProfile profile in BalanceProfile.All()) profiles.Add(profile.Name);

            Dictionary<string, Cell> byKey = new Dictionary<string, Cell>();
            List<string> scenarios = new List<string>();
            foreach (Cell cell in cells)
            {
                byKey[cell.Scenario + "/" + cell.Profile] = cell;
                if (!scenarios.Contains(cell.Scenario)) scenarios.Add(cell.Scenario);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PROFILE SWEEP — peak K at the end of each scenario's own run length");
            sb.AppendLine();

            StringBuilder header = new StringBuilder(string.Format("{0,-22}", "scenario"));
            foreach (string profile in profiles) header.Append(string.Format("{0,14}", profile));
            header.Append(string.Format("{0,12}", "vs physical"));
            sb.AppendLine(header.ToString());

            int quiet = 0;
            int running = 0;
            foreach (string scenario in scenarios)
            {
                StringBuilder line = new StringBuilder(string.Format("{0,-22}", scenario));
                float physical = 0f;
                float shipped = 0f;

                foreach (string profile in profiles)
                {
                    Cell cell;
                    if (!byKey.TryGetValue(scenario + "/" + profile, out cell))
                    {
                        line.Append(string.Format("{0,14}", "-"));
                        continue;
                    }

                    if (cell.Failed)
                    {
                        line.Append(string.Format("{0,14}", cell.Error));
                        continue;
                    }

                    if (profile == "physical" && cell.Converged) physical = cell.PeakKelvin;
                    if (profile == "shipped" && cell.Converged) shipped = cell.PeakKelvin;

                    // Two different kinds of "do not read this as an answer": a run that barely
                    // moved never exercised the profile, and a run still climbing never reached
                    // one. Both are properties of the run length, not of the profile.
                    string mark = "";
                    if (cell.TravelledKelvin < 1f) { mark = "*"; quiet++; }
                    else if (!cell.Converged) { mark = "~"; running++; }

                    line.Append(string.Format("{0,14}", N(cell.PeakKelvin, 1) + mark));
                }

                // Only meaningful when both ends actually settled.
                line.Append(string.Format("{0,12}",
                    physical > 0f && shipped > 0f ? N(shipped - physical, 1) : "-"));
                sb.AppendLine(line.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("* moved less than a kelvin — the scenario never exercised this profile ("
                + quiet + " cells)");
            sb.AppendLine("~ still climbing when the run ended — a transient, not a settling point ("
                + running + " cells)");
            sb.AppendLine("  Both are properties of the scenario's fixed run length, which is tuned to");
            sb.AppendLine("  the shipped clock. The vs-physical column is blank unless both ends settled.");
            sb.AppendLine();
            sb.AppendLine("skipped as not drivable from outside: " + string.Join(", ", Names(NotDrivable)));
            return sb.ToString();
        }

        private static string[] Names(HashSet<string> set)
        {
            string[] names = new string[set.Count];
            set.CopyTo(names);
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }
    }
}
