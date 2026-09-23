using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Thermodynamics.Harness
{
    public static class DumpAudit
    {
        private const double RelativeTolerance = 0.02d;
        private const double AbsoluteTolerance = 0.05d;

        public sealed class CheckResult
        {
            public string Name;

            public string Where;

            public int Rows;

            public int Hits;

            public string Worst = "";

            public string Missing = "";

            public bool Observation;

            public bool Skipped { get { return Missing.Length > 0; } }
            public bool Failed { get { return !Observation && !Skipped && Hits > 0; } }
        }

        public sealed class Result
        {
            public string Path = "";
            public int Rows;

            public int GridRows;
            public int BlockTypeRows;

            public readonly List<CheckResult> Checks = new List<CheckResult>();

            public readonly List<string> Summary = new List<string>();

            public bool Passed
            {
                get
                {
                    for (int i = 0; i < Checks.Count; i++) if (Checks[i].Failed) return false;
                    return true;
                }
            }
        }


        public static string DefaultPath()
        {
            string configured = Environment.GetEnvironmentVariable("THERMAL_DUMPS");
            if (!string.IsNullOrEmpty(configured)) return configured;

            string roaming = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(roaming))
            {
                string saves = Path.Combine(roaming, "SpaceEngineers", "Saves");
                if (Directory.Exists(saves)) return saves;
            }

            string content = GameBlocks.ContentPath();
            if (content == null) return null;


            DirectoryInfo directory = new DirectoryInfo(content);
            for (int i = 0; i < 4 && directory != null; i++) directory = directory.Parent;
            if (directory == null) return null;

            string prefix = Path.Combine(directory.FullName,
                "compatdata", "244850", "pfx", "drive_c", "users", "steamuser",
                "AppData", "Roaming", "SpaceEngineers", "Saves");
            return Directory.Exists(prefix) ? prefix : null;
        }


        public static string Newest(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) return null;

            string[] found = Directory.GetFiles(
                path, "Thermodynamics_Environment_*.csv", SearchOption.AllDirectories);
            if (found.Length == 0) return null;

            string newest = found[0];
            DateTime stamp = File.GetLastWriteTimeUtc(newest);
            for (int i = 1; i < found.Length; i++)
            {
                DateTime other = File.GetLastWriteTimeUtc(found[i]);
                if (other <= stamp) continue;
                newest = found[i];
                stamp = other;
            }

            return newest;
        }


        public static Result Run(string path)
        {
            Table table = Table.Read(path);
            Table grids = Table.Read(Sibling(path, "Grids"));
            Table types = Table.Read(Sibling(path, "BlockTypes"));


            Result result = new Result();
            result.Path = path;
            result.Rows = table.Rows.Count;
            result.GridRows = grids.Rows.Count;
            result.BlockTypeRows = types.Rows.Count;

            Describe(table, result);

            result.Checks.Add(WindDecomposes(table));
            result.Checks.Add(SlopeOnlyAdds(table));
            result.Checks.Add(WindUnderTheCeiling(table));
            result.Checks.Add(ConvectionNeedsAir(table));
            result.Checks.Add(DepthIsUndergroundOnly(table));
            result.Checks.Add(NoWindWhollyBuried(table));
            result.Checks.Add(WeatherScalesWithIntensity(table));
            result.Checks.Add(NothingIsNonsense(table, "climate", ClimatePositive));

            result.Checks.Add(StagesFitInsideTheUpdate(grids));
            result.Checks.Add(AWorstCallFitsItsParent(grids));
            result.Checks.Add(ClampedStepsAreSteps(grids));
            result.Checks.Add(GridRangesAreOrdered(grids));
            result.Checks.Add(LiveBlocksAreWhatWasPlaced(types));
            result.Checks.Add(BlockRangesAreOrdered(types));
            result.Checks.Add(EveryTypeHasUsableMaterialProperties(types));
            result.Checks.Add(AnIdlePumpLiftsNothing(grids));
            result.Checks.Add(NothingIsNonsense(grids, "grid", GridPositive));
            result.Checks.Add(NothingIsNonsense(types, "block type", BlockTypePositive));

            return result;
        }


        private static string Sibling(string path, string kind)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string name = Path.GetFileName(path);
            const string prefix = "Thermodynamics_Environment_";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;

            string directory = Path.GetDirectoryName(path);
            string sibling = "Thermodynamics_" + kind + "_" + name.Substring(prefix.Length);
            return string.IsNullOrEmpty(directory) ? sibling : Path.Combine(directory, sibling);
        }


        public static string Report(Result result)
        {

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("DUMP AUDIT");
            sb.AppendLine();
            sb.Append("  ").AppendLine(result.Path);
            sb.Append("  ").Append(result.Rows.ToString("n0")).Append(" environment rows, ")
              .Append(result.GridRows.ToString("n0")).Append(" grids, ")
              .Append(result.BlockTypeRows.ToString("n0")).AppendLine(" block types");
            sb.AppendLine();

            for (int i = 0; i < result.Summary.Count; i++)
            {
                sb.Append("  ").AppendLine(result.Summary[i]);
            }
            sb.AppendLine();

            sb.AppendLine("  claim                                        rows      hits  verdict");
            for (int i = 0; i < result.Checks.Count; i++)
            {
                CheckResult check = result.Checks[i];

                sb.Append("  ").Append(Trim(check.Name, 42).PadRight(43));
                sb.Append(check.Skipped ? "-".PadLeft(8) : check.Rows.ToString("n0").PadLeft(8));
                sb.Append(check.Skipped ? "-".PadLeft(10) : check.Hits.ToString("n0").PadLeft(10));
                sb.Append("  ").AppendLine(Verdict(check));

                if (check.Skipped)
                {
                    sb.Append("      not in this dump: ").AppendLine(check.Missing);
                }

                else if (check.Worst.Length > 0)
                {
                    sb.Append("      ").AppendLine(check.Worst);
                }

                sb.Append("      ").AppendLine(check.Where);
            }

            sb.AppendLine();
            sb.Append("  ").AppendLine(result.Passed ? "no defect check failed" : "A DEFECT CHECK FAILED");

            return sb.ToString();
        }


        private static string Verdict(CheckResult check)
        {
            if (check.Skipped) return "skipped";
            if (check.Observation) return check.Hits == 0 ? "none" : "observed";
            return check.Hits == 0 ? "held" : "FAILED";
        }



        private static CheckResult WindDecomposes(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "wind decomposes into the factors reported beside it";
            check.Where = "WindSolverContractTests.TheReportedFactorsMultiplyBackIntoTheReportedSpeed";

            string[] needs = { "wind_speed", "wind_ceiling", "wind_band_share", "wind_profile", "wind_speedup", "wind_shelter" };
            if (!table.Require(needs, check)) return check;

            bool motionKnown = table.Has("grid_speed");
            check.Observation = !motionKnown;

            double worst = 0d;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (motionKnown && table.Number(i, "grid_speed") > 0.5d) continue;

                double speed = table.Number(i, "wind_speed");
                double predicted = table.Number(i, "wind_ceiling")
                    * table.Number(i, "wind_band_share")
                    * table.Number(i, "wind_profile")
                    * table.Number(i, "wind_speedup")
                    * table.Number(i, "wind_shelter");

                if (table.Has("wind_burial")) predicted *= table.Number(i, "wind_burial");

                check.Rows++;

                double shortfall = predicted - speed;
                if (shortfall <= Tolerance(predicted)) continue;

                check.Hits++;
                if (shortfall <= worst) continue;

                worst = shortfall;

                check.Worst = "worst " + Fixed(speed) + " m/s reported against " + Fixed(predicted)
                    + " m/s composed, on " + table.Text(i, "grid");
            }

            return check;
        }


        private static CheckResult NoWindWhollyBuried(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a wholly buried grid is in no wind";
            check.Where = "docs/environment.md, Under the surface; backlog A15";

            string[] needs = { "wind_speed", "wind_burial" };
            if (!table.Require(needs, check)) return check;

            double worst = 0d;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.Number(i, "wind_burial") > 0d) continue;

                double speed = table.Number(i, "wind_speed");

                check.Rows++;
                if (speed <= AbsoluteTolerance) continue;

                check.Hits++;
                if (speed <= worst) continue;

                worst = speed;

                check.Worst = "worst " + Fixed(speed) + " m/s at depth "

                    + Fixed(table.Number(i, "depth_m")) + " m, on " + table.Text(i, "grid");
            }

            return check;
        }


        private static CheckResult SlopeOnlyAdds(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "slope wind is what adds to the composed speed";
            check.Where = "docs/environment.md, Slope winds; backlog B15";
            check.Observation = true;

            string[] needs = { "wind_speed", "wind_ceiling", "wind_band_share", "wind_profile", "wind_speedup", "wind_shelter" };
            if (!table.Require(needs, check)) return check;

            double worst = 0d;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double speed = table.Number(i, "wind_speed");
                double predicted = table.Number(i, "wind_ceiling")
                    * table.Number(i, "wind_band_share")
                    * table.Number(i, "wind_profile")
                    * table.Number(i, "wind_speedup")
                    * table.Number(i, "wind_shelter");

                if (table.Has("wind_burial")) predicted *= table.Number(i, "wind_burial");

                check.Rows++;

                double added = speed - predicted;
                if (added <= Tolerance(predicted)) continue;

                check.Hits++;
                if (added <= worst) continue;

                worst = added;

                check.Worst = "most added " + Fixed(added) + " m/s at "

                    + Fixed(table.Number(i, "wind_agl_m")) + " m above ground";
            }

            return check;
        }


        private static CheckResult WindUnderTheCeiling(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "wind stays under the engine's own figure";
            check.Where = "docs/backlog.md B18 — open";
            check.Observation = true;

            string[] needs = { "wind_speed", "wind_ceiling" };
            if (!table.Require(needs, check)) return check;

            double worst = 0d;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double speed = table.Number(i, "wind_speed");
                double ceiling = table.Number(i, "wind_ceiling");

                check.Rows++;
                if (speed <= ceiling + AbsoluteTolerance) continue;

                check.Hits++;
                double over = speed - ceiling;
                if (over <= worst) continue;

                worst = over;

                check.Worst = "worst " + Fixed(speed) + " m/s against a ceiling of " + Fixed(ceiling)

                    + " m/s, at " + Fixed(table.Number(i, "wind_agl_m")) + " m above ground";
            }

            return check;
        }


        private static CheckResult ConvectionNeedsAir(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "convection is reported only where there is air";
            check.Where = "docs/known-issues.md, A6";

            string[] needs = { "convection_coeff", "air_density" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double coefficient = table.Number(i, "convection_coeff");
                double density = table.Number(i, "air_density");

                check.Rows++;
                if (density > 0d == coefficient > 0d) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;


                check.Worst = "first at " + Fixed(coefficient) + " W/m2K against an air density of "
                    + density.ToString("0.0000", CultureInfo.InvariantCulture);
            }

            return check;
        }


        private static CheckResult DepthIsUndergroundOnly(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a positive depth means a buried grid";
            check.Where = "docs/environment.md, Underground";

            string[] needs = { "depth_m", "underground" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                bool deep = table.Number(i, "depth_m") > 0d;
                bool buried = table.Number(i, "underground") > 0d;

                check.Rows++;
                if (deep == buried) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;


                check.Worst = "first at depth " + Fixed(table.Number(i, "depth_m"))
                    + " m with underground " + table.Text(i, "underground");
            }

            return check;
        }


        private static CheckResult WeatherScalesWithIntensity(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a weather's offset scales with its intensity";
            check.Where = "docs/environment.md, Weather";

            string[] needs = { "weather", "weather_intensity", "weather_ambient_k" };
            if (!table.Require(needs, check)) return check;

            Dictionary<string, double> first = new Dictionary<string, double>();
            double worst = 0d;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double intensity = table.Number(i, "weather_intensity");

                if (intensity < 0.2d) continue;

                string kind = table.Text(i, "weather");
                if (kind.Length == 0) continue;

                double ratio = table.Number(i, "weather_ambient_k") / intensity;
                check.Rows++;

                double expected;
                if (!first.TryGetValue(kind, out expected))
                {
                    first[kind] = ratio;
                    continue;
                }

                double drift = Math.Abs(ratio - expected);
                if (drift <= Tolerance(expected)) continue;

                check.Hits++;
                if (drift <= worst) continue;

                worst = drift;

                check.Worst = kind + " offsets " + Fixed(ratio) + " K against " + Fixed(expected)
                    + " K per unit of intensity";
            }

            return check;
        }


        private static CheckResult NothingIsNonsense(Table table, string what, string[] positive)
        {

            CheckResult check = new CheckResult();
            check.Name = "no " + what + " reading is a NaN, an infinity or impossibly negative";
            check.Where = "docs/known-issues.md, A11";

            if (table.Columns.Count == 0)
            {
                check.Missing = what + " CSV";
                return check;
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                check.Rows++;

                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string column = table.Columns[c];
                    double value;
                    if (!table.TryNumber(i, c, out value)) continue;

                    bool nonsense = double.IsNaN(value) || double.IsInfinity(value);
                    if (!nonsense && value < 0d && Array.IndexOf(positive, column) >= 0) nonsense = true;
                    if (!nonsense) continue;

                    check.Hits++;
                    if (check.Worst.Length > 0) break;

                    check.Worst = "first " + column + " = " + table.Text(i, column)

                        + " on " + Identity(table, i);
                    break;
                }
            }

            return check;
        }



        private static CheckResult StagesFitInsideTheUpdate(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a grid's stages fit inside its update";
            check.Where = "docs/telemetry.md#what-the-stages-leave-over";

            string[] needs =
            {
                "sim_ms_total", "topology_ms_total", "mapping_ms_total",
                "exposure_ms_total", "solver_ms_total",
            };
            if (!table.Require(needs, check)) return check;

            double worst = 0d;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double update = table.Number(i, "sim_ms_total");
                double stages = table.Number(i, "topology_ms_total")
                    + table.Number(i, "mapping_ms_total")
                    + table.Number(i, "exposure_ms_total")
                    + table.Number(i, "solver_ms_total");

                check.Rows++;

                double over = stages - update;
                if (over <= Tolerance(update)) continue;

                check.Hits++;
                if (over <= worst) continue;

                worst = over;

                check.Worst = "worst " + Fixed(stages) + " ms of stages inside " + Fixed(update)
                    + " ms of update, on " + table.Text(i, "name");
            }

            return check;
        }


        private static CheckResult AWorstCallFitsItsParent(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a grid's worst solver call fits its worst update";
            check.Where = "docs/telemetry.md#frame-cost-and-hitching";

            string[] needs = { "sim_ms_max", "solver_ms_max" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double update = table.Number(i, "sim_ms_max");
                double solver = table.Number(i, "solver_ms_max");

                check.Rows++;
                if (solver <= update + Tolerance(update)) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;


                check.Worst = "first " + Fixed(solver) + " ms of solver inside " + Fixed(update)
                    + " ms of update, on " + table.Text(i, "name");
            }

            return check;
        }


        private static CheckResult ClampedStepsAreSteps(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "clamped steps are a subset of steps taken";
            check.Where = "docs/telemetry.md#substeps";

            string[] needs = { "simulation_steps", "clamped_steps" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double steps = table.Number(i, "simulation_steps");
                double clamped = table.Number(i, "clamped_steps");

                check.Rows++;
                if (clamped <= steps) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;

                check.Worst = "first " + clamped.ToString("n0", CultureInfo.InvariantCulture)
                    + " clamped of " + steps.ToString("n0", CultureInfo.InvariantCulture)
                    + " steps, on " + table.Text(i, "name");
            }

            return check;
        }


        private static CheckResult GridRangesAreOrdered(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a grid's minima, means and maxima are ordered";
            check.Where = "RunningStat, TelemetryStats.cs";

            string[] needs = { "ambient_min", "ambient_mean", "ambient_max", "mean_cells", "peak_cells" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                check.Rows++;


                string broke = Ordered(table, i,
                    "ambient_min", "ambient_mean", "ambient_max");
                if (broke == null && table.Number(i, "mean_cells") > table.Number(i, "peak_cells") + 0.5d)
                {
                    broke = "mean_cells above peak_cells";
                }

                if (broke == null) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;

                check.Worst = "first " + broke + " on " + table.Text(i, "name");
            }

            return check;
        }



        private static CheckResult LiveBlocksAreWhatWasPlaced(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "live blocks are what was placed less what was removed";
            check.Where = "docs/telemetry.md#what-is-collected";

            string[] needs = { "placed", "removed", "live" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double placed = table.Number(i, "placed");
                double removed = table.Number(i, "removed");
                double live = table.Number(i, "live");

                check.Rows++;
                if (Math.Abs((placed - removed) - live) < 0.5d) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;

                check.Worst = "first " + table.Text(i, "subtype") + ": placed "
                    + placed.ToString("n0", CultureInfo.InvariantCulture) + ", removed "
                    + removed.ToString("n0", CultureInfo.InvariantCulture) + ", live "
                    + live.ToString("n0", CultureInfo.InvariantCulture);
            }

            return check;
        }


        private static CheckResult BlockRangesAreOrdered(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a block type's temperatures and demands are ordered";
            check.Where = "RunningStat, TelemetryStats.cs";

            string[] needs =
            {
                "temp_min", "temp_mean", "temp_max", "peak_temp",
                "substep_demand_mean", "substep_demand_max",
            };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.Number(i, "temp_max") <= 0d) continue;

                check.Rows++;


                string broke = Ordered(table, i, "temp_min", "temp_mean", "temp_max", "peak_temp")

                    ?? Ordered(table, i, "substep_demand_mean", "substep_demand_max");
                if (broke == null) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;

                check.Worst = "first " + broke + " on " + table.Text(i, "subtype");
            }

            return check;
        }


        private static CheckResult EveryTypeHasUsableMaterialProperties(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "every block type has properties the solver can use";
            check.Where = "docs/definitions.md";

            string[] needs = { "specific_heat", "emissivity", "critical_temperature" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                check.Rows++;

                double heat = table.Number(i, "specific_heat");
                double emissivity = table.Number(i, "emissivity");
                double critical = table.Number(i, "critical_temperature");

                string broke = null;
                if (heat <= 0d) broke = "specific heat " + Fixed(heat);
                else if (emissivity < 0d || emissivity > 1d) broke = "emissivity " + Fixed(emissivity);
                else if (critical <= 0d) broke = "critical temperature " + Fixed(critical);

                if (broke == null) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;

                check.Worst = "first " + table.Text(i, "subtype") + ": " + broke;
            }

            return check;
        }


        private static string Identity(Table table, int row)
        {
            if (table.Has("grid")) return table.Text(row, "grid");
            if (table.Has("name")) return table.Text(row, "name");
            if (table.Has("subtype")) return table.Text(row, "subtype");
            return "row " + row;
        }


        private static string Ordered(Table table, int row, params string[] columns)
        {
            for (int i = 1; i < columns.Length; i++)
            {
                double lower = table.Number(row, columns[i - 1]);
                double upper = table.Number(row, columns[i]);
                if (lower <= upper + Tolerance(upper)) continue;

                return columns[i - 1] + " " + Fixed(lower) + " above " + columns[i] + " " + Fixed(upper);
            }

            return null;
        }

        private static readonly string[] ClimatePositive =
        {
            "air_density", "ambient_k", "solar_w", "wind_speed", "wind_ceiling",
            "convection_coeff", "weather_intensity", "grid_mean_k", "grid_peak_k",
        };

        private static readonly string[] GridPositive =
        {
            "lifetime_s", "peak_cells", "mean_cells", "peak_links", "peak_rooms",
            "simulation_steps", "node_updates", "substeps_mean", "clamped_steps",
            "peak_temperature", "ambient_min", "ambient_mean", "ambient_max",
            "air_density_mean", "wind_mean", "wind_max", "sim_ms_total", "sim_ms_max",
            "topology_ms_total", "mapping_ms_total", "exposure_ms_total",
            "solver_ms_total", "solver_ms_max", "pump_draw_w", "pump_cop",
        };

        private static readonly string[] BlockTypePositive =
        {
            "placed", "removed", "live", "peak_live", "updates", "sampled",
            "conductivity", "specific_heat", "emissivity", "critical_temperature",
            "mass_mean", "thermal_mass_mean", "exposed_area_mean",
            "temp_min", "temp_mean", "temp_max", "peak_temp",
            "substep_demand_mean", "substep_demand_max", "substep_demand_peak",
        };


        private static CheckResult AnIdlePumpLiftsNothing(Table table)
        {

            CheckResult check = new CheckResult();
            check.Name = "a pump that drew nothing lifted nothing";
            check.Where = "docs/telemetry.md#coolant-loops-and-heat-pumps";

            string[] needs = { "pump_lift_w", "pump_draw_w", "pump_cop" };
            if (!table.Require(needs, check)) return check;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.Number(i, "pump_draw_w") > 0d) continue;

                check.Rows++;
                if (table.Number(i, "pump_lift_w") <= 0d && table.Number(i, "pump_cop") <= 0d) continue;

                check.Hits++;
                if (check.Worst.Length > 0) continue;


                check.Worst = "first " + Fixed(table.Number(i, "pump_lift_w")) + " W lifted on no draw, on "
                    + table.Text(i, "name");
            }

            return check;
        }



        private static void Describe(Table table, Result result)
        {
            result.Summary.Add(Distinct(table, "planet") + " planets, " + Distinct(table, "grid")

                + " grids, " + Distinct(table, "surface_material") + " ground materials");

            Range(table, "air_density", "air density", "0.000", result);
            Range(table, "ambient_k", "ambient K", "0.0", result);
            Range(table, "wind_speed", "wind m/s", "0.0", result);
            Range(table, "convection_coeff", "convection W/m2K", "0.0", result);


            int buried = Count(table, "underground");

            int weather = Count(table, "weather_intensity");
            result.Summary.Add("buried rows " + buried.ToString("n0") + ", rows under weather "
                + weather.ToString("n0"));
        }


        private static void Range(Table table, string column, string label, string format, Result result)
        {
            if (!table.Has(column)) return;

            double low = double.MaxValue;
            double high = double.MinValue;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double value = table.Number(i, column);
                if (value < low) low = value;
                if (value > high) high = value;
            }

            if (low > high) return;

            result.Summary.Add(label.PadRight(20)
                + low.ToString(format, CultureInfo.InvariantCulture) + " .. "
                + high.ToString(format, CultureInfo.InvariantCulture));
        }


        private static int Distinct(Table table, string column)
        {
            if (!table.Has(column)) return 0;

            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                string value = table.Text(i, column);
                if (value.Length > 0) seen[value] = true;
            }

            return seen.Count;
        }


        private static int Count(Table table, string column)
        {
            if (!table.Has(column)) return 0;

            int count = 0;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (table.Number(i, column) > 0d) count++;
            }

            return count;
        }



        private static double Tolerance(double magnitude)
        {
            double scaled = Math.Abs(magnitude) * RelativeTolerance;
            return scaled > AbsoluteTolerance ? scaled : AbsoluteTolerance;
        }


        private static string Fixed(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }


        private static string Trim(string value, int width)
        {
            if (value == null) return "";
            return value.Length <= width ? value : value.Substring(0, width - 1) + "~";
        }

        private sealed class Table
        {

            public readonly List<string> Columns = new List<string>();

            public readonly List<string[]> Rows = new List<string[]>();

            private readonly Dictionary<string, int> index = new Dictionary<string, int>();


            public static Table Read(string path)
            {

                Table table = new Table();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return table;

                using (StreamReader reader = new StreamReader(path))
                {
                    string header = reader.ReadLine();
                    if (header == null) return table;

                    string[] names = header.Split(',');
                    for (int i = 0; i < names.Length; i++)
                    {
                        string name = names[i].Trim();
                        table.Columns.Add(name);
                        table.index[name] = i;
                    }

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        table.Rows.Add(line.Split(','));
                    }
                }

                return table;
            }


            public bool Has(string column) { return index.ContainsKey(column); }


            public bool Require(string[] columns, CheckResult check)
            {
                string missing = "";
                for (int i = 0; i < columns.Length; i++)
                {
                    if (Has(columns[i])) continue;
                    if (missing.Length > 0) missing += ", ";
                    missing += columns[i];
                }

                check.Missing = missing;
                return missing.Length == 0;
            }


            public string Text(int row, string column)
            {
                int at;
                if (!index.TryGetValue(column, out at)) return "";

                string[] fields = Rows[row];
                return at < fields.Length ? fields[at] : "";
            }


            public double Number(int row, string column)
            {
                double value;
                return double.TryParse(
                    Text(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    ? value : 0d;
            }


            public bool TryNumber(int row, int column, out double value)
            {
                value = 0d;

                string[] fields = Rows[row];
                if (column >= fields.Length) return false;

                return double.TryParse(
                    fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }
    }
}
