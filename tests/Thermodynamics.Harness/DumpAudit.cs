using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Checks a field telemetry dump against the claims the model makes about itself.
    ///
    /// <para>A dump is the only place the model meets a real world, and every past evaluation of one
    /// was a hand-written script that left nothing behind. The claims being checked are not new —
    /// they are the ones argued in the documents each check points at — but until now none of them
    /// was checked against a dump by anything that could be re-run when the next dump arrives.</para>
    ///
    /// <para>Reads the environment CSV, which is the climate and wind half of a dump: one row per
    /// profiled grid per sample. A column a dump does not carry skips its checks rather than failing
    /// them, so a dump taken before a column existed still audits for everything else.</para>
    ///
    /// <para>Checks are of two kinds. A <b>defect</b> check asserts something the model must never
    /// do, and a failure is a bug. An <b>observation</b> counts something open — a bound that is
    /// known to be exceeded, a term that is known to be present — so the count moves when the
    /// balance does, without turning an open question into a failing tool.</para>
    /// </summary>
    public static class DumpAudit
    {
        /// <summary>
        /// How far a recomputed figure may sit from the reported one: two per cent, or 0.05 of the
        /// unit, whichever is larger. The dump writes floats through a text format at fixed
        /// precision, so an exact comparison would fail on rounding alone.
        /// </summary>
        private const double RelativeTolerance = 0.02d;
        private const double AbsoluteTolerance = 0.05d;

        public sealed class CheckResult
        {
            /// <summary>What is being checked, as a claim rather than a procedure.</summary>
            public string Name;

            /// <summary>Where the claim is argued.</summary>
            public string Where;

            /// <summary>Rows the check could read. Zero where the dump lacks a column it needs.</summary>
            public int Rows;

            /// <summary>Rows that did not hold.</summary>
            public int Hits;

            /// <summary>The worst row, in the check's own terms. Empty where nothing was hit.</summary>
            public string Worst = "";

            /// <summary>The columns this check wanted and the dump did not carry.</summary>
            public string Missing = "";

            /// <summary>Counted rather than asserted: a hit is a measurement, not a defect.</summary>
            public bool Observation;

            public bool Skipped { get { return Missing.Length > 0; } }
            public bool Failed { get { return !Observation && !Skipped && Hits > 0; } }
        }

        public sealed class Result
        {
            public string Path = "";
            public int Rows;
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

        /// <summary>
        /// Where dumps are when a command was not told: `THERMAL_DUMPS`, else the installed game's
        /// saves folder.
        ///
        /// Dumps are written into world storage, which is inside the save, which is outside this
        /// repository — a mod folder is published to the workshop and a save is nobody's business
        /// but the player's. On Linux the game runs under a compatibility prefix, so its AppData
        /// sits beside the Steam library rather than in the user's home.
        /// </summary>
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

            // .../steamapps/common/SpaceEngineers/Content/Data -> .../steamapps
            DirectoryInfo directory = new DirectoryInfo(content);
            for (int i = 0; i < 4 && directory != null; i++) directory = directory.Parent;
            if (directory == null) return null;

            string prefix = Path.Combine(directory.FullName,
                "compatdata", "244850", "pfx", "drive_c", "users", "steamuser",
                "AppData", "Roaming", "SpaceEngineers", "Saves");
            return Directory.Exists(prefix) ? prefix : null;
        }

        /// <summary>
        /// The newest environment CSV at or under a path. A path to a file is taken as it stands, so
        /// one dump out of a folder of them can be named directly.
        /// </summary>
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

            Result result = new Result();
            result.Path = path;
            result.Rows = table.Rows.Count;

            Describe(table, result);

            result.Checks.Add(WindDecomposes(table));
            result.Checks.Add(SlopeOnlyAdds(table));
            result.Checks.Add(WindUnderTheCeiling(table));
            result.Checks.Add(ConvectionNeedsAir(table));
            result.Checks.Add(DepthIsUndergroundOnly(table));
            result.Checks.Add(WeatherScalesWithIntensity(table));
            result.Checks.Add(NothingIsNonsense(table));

            return result;
        }

        public static string Report(Result result)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("DUMP AUDIT");
            sb.AppendLine();
            sb.Append("  ").AppendLine(result.Path);
            sb.Append("  ").Append(result.Rows.ToString("n0")).AppendLine(" environment rows");
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

        // ---- the checks -------------------------------------------------------------------

        /// <summary>
        /// Every factor the wind report carries multiplies back into the wind speed it reports.
        ///
        /// The offline suite already pins this on invented inputs; this is the same claim against a
        /// planet. A speed *below* the product means a factor acted on the wind and was not
        /// reported, which is the fault that makes a dump unarguable.
        /// </summary>
        private static CheckResult WindDecomposes(Table table)
        {
            CheckResult check = new CheckResult();
            check.Name = "wind decomposes into the factors reported beside it";
            check.Where = "WindSolverContractTests.TheReportedFactorsMultiplyBackIntoTheReportedSpeed";

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

                check.Rows++;

                // Only a shortfall is a defect: the slope term is a velocity added after the
                // factors, so it can only carry the speed above their product.
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

        /// <summary>
        /// Where the reported speed exceeds the product of the factors, the slope wind is what did
        /// it — so it is a near-ground term on sloping ground, not something that happens anywhere.
        /// </summary>
        private static CheckResult SlopeOnlyAdds(Table table)
        {
            CheckResult check = new CheckResult();
            check.Name = "slope wind is what adds to the composed speed";
            check.Where = "docs/wind-model.md, B15";
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

        /// <summary>
        /// The engine's own figure was adopted because it bounds the wind. Once the vertical profile
        /// multiplies the band share above the reference height it no longer does, which is an open
        /// balance call rather than a defect — so this counts rather than fails.
        /// </summary>
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

        /// <summary>
        /// Convection is zero exactly where there is no air, and non-zero exactly where there is.
        /// A dump once reported 50 W/(m²·K) beside an air density of zero; that was a reporting
        /// fault, and this is what would catch it coming back.
        /// </summary>
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

        /// <summary>
        /// Depth below the surface is reported for a buried grid and for nothing else. The column is
        /// filled from the surface radius whatever the altitude, so a flying grid carries the
        /// negative of its height in it; what must hold is that the positive half of the column and
        /// the underground flag are the same set of rows.
        /// </summary>
        private static CheckResult DepthIsUndergroundOnly(Table table)
        {
            CheckResult check = new CheckResult();
            check.Name = "a positive depth means a buried grid";
            check.Where = "docs/planet-climate.md, Underground";

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

        /// <summary>
        /// A weather's temperature offset is its table figure faded in by its intensity, so within
        /// one kind the ratio between the two is a constant. A kind whose ratio wanders is either
        /// being blended twice or reading a table it does not belong to.
        /// </summary>
        private static CheckResult WeatherScalesWithIntensity(Table table)
        {
            CheckResult check = new CheckResult();
            check.Name = "a weather's offset scales with its intensity";
            check.Where = "docs/planet-climate.md, Weather";

            string[] needs = { "weather", "weather_intensity", "weather_ambient_k" };
            if (!table.Require(needs, check)) return check;

            Dictionary<string, double> first = new Dictionary<string, double>();
            double worst = 0d;

            for (int i = 0; i < table.Rows.Count; i++)
            {
                double intensity = table.Number(i, "weather_intensity");

                // Below a fifth of full, the offset is small enough that the dump's own rounding is
                // a large share of the ratio.
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

        /// <summary>
        /// No column carries a NaN or an infinity, and the quantities that cannot be negative are
        /// not. A dump is the last place a NaN can be seen before it is a black grid in someone's
        /// world.
        /// </summary>
        private static CheckResult NothingIsNonsense(Table table)
        {
            CheckResult check = new CheckResult();
            check.Name = "no reading is a NaN, an infinity or impossibly negative";
            check.Where = "docs/known-issues.md, A11";

            string[] positive =
            {
                "air_density", "ambient_k", "solar_w", "wind_speed", "wind_ceiling",
                "convection_coeff", "weather_intensity", "grid_mean_k", "grid_peak_k",
            };

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
                        + " on " + table.Text(i, "grid");
                    break;
                }
            }

            return check;
        }

        // ---- the summary ------------------------------------------------------------------

        /// <summary>
        /// What the dump is, before what it proves: the ranges every check is being read against, so
        /// a check that held against a dump with nothing in it says so on the same page.
        /// </summary>
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

        // ---- reading the file -------------------------------------------------------------

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

        /// <summary>
        /// A telemetry CSV, read by column name so a dump that gained or lost a column still parses.
        /// The mod writes its own CSV without quoting, replacing the separator in any field that
        /// could contain one, so splitting on commas is the whole of the format.
        /// </summary>
        private sealed class Table
        {
            public readonly List<string> Columns = new List<string>();
            public readonly List<string[]> Rows = new List<string[]>();

            private readonly Dictionary<string, int> index = new Dictionary<string, int>();

            public static Table Read(string path)
            {
                Table table = new Table();

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

            /// <summary>
            /// Records the columns a check wanted and the dump lacks, and answers whether it can run.
            /// </summary>
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

            /// <summary>By position, for a pass that walks every column of a row.</summary>
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
