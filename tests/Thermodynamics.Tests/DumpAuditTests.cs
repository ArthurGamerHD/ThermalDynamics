using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The auditor that reads a field dump.
    ///
    /// <para>Every check in it exists because a document claims something about a real world, and
    /// the auditor is what turns that claim into a count. So each test here builds the smallest CSV
    /// that could break one check, and asserts that the check breaks — a check that cannot fail is
    /// worse than no check, because it reads as evidence.</para>
    ///
    /// <para>A dump this repository can no longer produce is still audited, since the checks skip
    /// the columns a dump does not carry. That is tested too: a skip must never read as a pass.</para>
    /// </summary>
    public class DumpAuditTests : IDisposable
    {
        private readonly List<string> written = new List<string>();

        public void Dispose()
        {
            foreach (string path in written)
            {
                try { File.Delete(path); }
                catch (IOException) { }
            }
        }

        /// <summary>Every column a full dump carries, in the order the mod writes them.</summary>
        private const string Header =
            "time_s,grid,grid_id,planet,altitude_surface_m,altitude_sealevel_m,latitude_deg," +
            "sun_elevation_deg,air_density,atmosphere_factor,ambient_k,ambient_c,underground," +
            "depth_m,solar_w,solar_occlusion,convection_coeff,wind_speed,wind_bearing_deg," +
            "wind_ceiling,wind_agl_m,wind_band_share,wind_profile,wind_heating,wind_speedup," +
            "wind_shelter,wind_channel_deg,weather,weather_intensity,weather_ambient_k," +
            "game_temperature,surface_material,grid_mean_k,grid_peak_k";

        /// <summary>
        /// One plausible row, by column name. A test names only what it is about and inherits a
        /// consistent world for everything else, so a check firing is attributable to the one field
        /// the test moved.
        /// </summary>
        private static Dictionary<string, string> Row()
        {
            Dictionary<string, string> row = new Dictionary<string, string>();

            foreach (string column in Header.Split(',')) row[column] = "0";

            row["time_s"] = "10";
            row["grid"] = "Test Grid";
            row["planet"] = "EarthLike";
            row["altitude_surface_m"] = "120";
            row["air_density"] = "0.9";
            row["ambient_k"] = "288";
            row["convection_coeff"] = "60";
            row["solar_w"] = "800";
            row["wind_ceiling"] = "70";
            row["wind_band_share"] = "0.2";
            row["wind_profile"] = "1.5";
            row["wind_speedup"] = "1";
            row["wind_shelter"] = "1";
            row["wind_speed"] = "21";
            row["wind_agl_m"] = "120";
            row["weather"] = "Clear";
            row["surface_material"] = "Grass";
            row["grid_mean_k"] = "290";
            row["grid_peak_k"] = "300";

            return row;
        }

        private string Write(params Dictionary<string, string>[] rows)
        {
            return Write(Header, rows);
        }

        private string Write(string header, params Dictionary<string, string>[] rows)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "thermal-dump-" + Guid.NewGuid().ToString("n") + ".csv");
            written.Add(path);

            string[] columns = header.Split(',');

            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine(header);
                foreach (Dictionary<string, string> row in rows)
                {
                    string[] fields = new string[columns.Length];
                    for (int i = 0; i < columns.Length; i++)
                    {
                        string value;
                        fields[i] = row.TryGetValue(columns[i], out value) ? value : "0";
                    }
                    writer.WriteLine(string.Join(",", fields));
                }
            }

            return path;
        }

        private static DumpAudit.CheckResult Check(DumpAudit.Result result, string name)
        {
            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                if (check.Name.StartsWith(name, StringComparison.Ordinal)) return check;
            }

            throw new Xunit.Sdk.XunitException("no check named " + name);
        }

        [Fact]
        public void APlausibleDumpPassesEveryCheck()
        {
            DumpAudit.Result result = DumpAudit.Run(Write(Row(), Row()));

            Assert.True(result.Passed);
            Assert.Equal(2, result.Rows);

            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                Assert.False(check.Skipped, check.Name + " skipped on a full dump");
                Assert.Equal(0, check.Hits);
            }
        }

        /// <summary>
        /// A wind slower than its own factors means a factor acted and was not reported, which is
        /// the fault that makes a dump unarguable rather than merely wrong.
        /// </summary>
        [Fact]
        public void AWindSlowerThanItsFactorsIsADefect()
        {
            Dictionary<string, string> row = Row();
            row["wind_speed"] = "10";

            DumpAudit.Result result = DumpAudit.Run(Write(row));
            DumpAudit.CheckResult check = Check(result, "wind decomposes");

            Assert.Equal(1, check.Hits);
            Assert.True(check.Failed);
            Assert.False(result.Passed);
            Assert.Contains("10.00", check.Worst);
        }

        /// <summary>
        /// Faster is the slope term, which is added as a velocity after the factors are applied. It
        /// is counted rather than failed, and it does not also count as a shortfall.
        /// </summary>
        [Fact]
        public void AWindFasterThanItsFactorsIsCountedAsSlopeWind()
        {
            Dictionary<string, string> row = Row();
            row["wind_speed"] = "24";

            DumpAudit.Result result = DumpAudit.Run(Write(row));

            Assert.Equal(0, Check(result, "wind decomposes").Hits);
            Assert.Equal(1, Check(result, "slope wind").Hits);
            Assert.True(result.Passed);
        }

        /// <summary>
        /// The dump writes floats at fixed precision, so a check that demanded equality would fail
        /// on rounding alone.
        /// </summary>
        [Fact]
        public void RoundingInTheDumpIsNotADefect()
        {
            Dictionary<string, string> row = Row();
            row["wind_speed"] = "20.9994";

            Assert.True(DumpAudit.Run(Write(row)).Passed);
        }

        [Fact]
        public void AWindOverTheEnginesCeilingIsObservedRatherThanFailed()
        {
            Dictionary<string, string> row = Row();
            row["wind_profile"] = "6";
            row["wind_speed"] = "84";

            DumpAudit.Result result = DumpAudit.Run(Write(row));
            DumpAudit.CheckResult check = Check(result, "wind stays under");

            Assert.Equal(1, check.Hits);
            Assert.True(check.Observation);
            Assert.False(check.Failed);
            Assert.True(result.Passed);
        }

        /// <summary>Both halves: air with no convection, and convection with no air.</summary>
        [Fact]
        public void ConvectionAndAirMustAgreeInBothDirections()
        {
            Dictionary<string, string> vacuum = Row();
            vacuum["air_density"] = "0";
            vacuum["wind_ceiling"] = "0";
            vacuum["wind_speed"] = "0";

            Assert.Equal(1, Check(DumpAudit.Run(Write(vacuum)), "convection is reported").Hits);

            Dictionary<string, string> still = Row();
            still["convection_coeff"] = "0";

            Assert.Equal(1, Check(DumpAudit.Run(Write(still)), "convection is reported").Hits);
        }

        /// <summary>
        /// A flying grid carries the negative of its height in the depth column, which is not a
        /// defect. Only a positive depth on a grid the game does not call buried is.
        /// </summary>
        [Fact]
        public void OnlyAPositiveDepthOnAnUnburiedGridIsADefect()
        {
            Dictionary<string, string> flying = Row();
            flying["depth_m"] = "-120";

            Assert.True(DumpAudit.Run(Write(flying)).Passed);

            Dictionary<string, string> buried = Row();
            buried["depth_m"] = "46";
            buried["underground"] = "1";

            Assert.True(DumpAudit.Run(Write(buried)).Passed);

            Dictionary<string, string> confused = Row();
            confused["depth_m"] = "46";

            Assert.Equal(1, Check(DumpAudit.Run(Write(confused)), "a positive depth").Hits);
        }

        [Fact]
        public void AWeatherOffsetThatDoesNotScaleWithItsIntensityIsADefect()
        {
            Dictionary<string, string> half = Row();
            half["weather"] = "RainLight";
            half["weather_intensity"] = "0.5";
            half["weather_ambient_k"] = "-0.9";

            Dictionary<string, string> full = Row();
            full["weather"] = "RainLight";
            full["weather_intensity"] = "1";
            full["weather_ambient_k"] = "-1.8";

            Assert.True(DumpAudit.Run(Write(half, full)).Passed);

            Dictionary<string, string> stuck = Row();
            stuck["weather"] = "RainLight";
            stuck["weather_intensity"] = "1";
            stuck["weather_ambient_k"] = "-0.9";

            Assert.Equal(1, Check(DumpAudit.Run(Write(half, stuck)), "a weather's offset").Hits);
        }

        /// <summary>
        /// Two kinds of weather in one dump are two tables, so the ratios are compared within a kind
        /// rather than across the dump.
        /// </summary>
        [Fact]
        public void TwoWeathersDoNotHaveToAgreeWithEachOther()
        {
            Dictionary<string, string> rain = Row();
            rain["weather"] = "RainLight";
            rain["weather_intensity"] = "1";
            rain["weather_ambient_k"] = "-1.8";

            Dictionary<string, string> storm = Row();
            storm["weather"] = "ThunderstormHeavy";
            storm["weather_intensity"] = "1";
            storm["weather_ambient_k"] = "-6";

            Assert.True(DumpAudit.Run(Write(rain, storm)).Passed);
        }

        [Fact]
        public void ANaNAndAnImpossibleNegativeAreBothCaught()
        {
            Dictionary<string, string> nan = Row();
            nan["grid_peak_k"] = "NaN";

            DumpAudit.CheckResult check = Check(DumpAudit.Run(Write(nan)), "no reading is a NaN");
            Assert.Equal(1, check.Hits);
            Assert.Contains("grid_peak_k", check.Worst);

            Dictionary<string, string> negative = Row();
            negative["air_density"] = "-0.5";

            Assert.Equal(1, Check(DumpAudit.Run(Write(negative)), "no reading is a NaN").Hits);
        }

        /// <summary>
        /// Latitude and bearing are signed by construction, so the negative half of the check has to
        /// know which quantities cannot be.
        /// </summary>
        [Fact]
        public void ASignedColumnIsNotAnImpossibleNegative()
        {
            Dictionary<string, string> row = Row();
            row["latitude_deg"] = "-41";
            row["wind_bearing_deg"] = "-160";
            row["ambient_c"] = "-52";
            row["altitude_surface_m"] = "-169";

            Assert.True(DumpAudit.Run(Write(row)).Passed);
        }

        /// <summary>
        /// A dump taken before the wind was written out decomposed cannot answer the decomposition
        /// checks. It must say so rather than report a clean bill of health for them.
        /// </summary>
        [Fact]
        public void AnOlderDumpSkipsWhatItCannotAnswerAndAuditsTheRest()
        {
            string header = "time_s,grid,planet,air_density,ambient_k,convection_coeff,"
                + "wind_speed,wind_ceiling,underground,depth_m,weather,weather_intensity,"
                + "weather_ambient_k,grid_mean_k,grid_peak_k";

            Dictionary<string, string> row = Row();
            DumpAudit.Result result = DumpAudit.Run(Write(header, row));

            DumpAudit.CheckResult decomposition = Check(result, "wind decomposes");
            Assert.True(decomposition.Skipped);
            Assert.False(decomposition.Failed);
            Assert.Equal(0, decomposition.Rows);
            Assert.Contains("wind_band_share", decomposition.Missing);

            Assert.False(Check(result, "convection is reported").Skipped);
            Assert.True(result.Passed);
        }

        [Fact]
        public void AnEmptyDumpIsReadWithoutCrashingAndClaimsNothing()
        {
            DumpAudit.Result result = DumpAudit.Run(Write(Header));

            Assert.Equal(0, result.Rows);
            Assert.True(result.Passed);

            foreach (DumpAudit.CheckResult check in result.Checks) Assert.Equal(0, check.Rows);
        }

        /// <summary>The report names the file, the verdict, and every check either way.</summary>
        [Fact]
        public void TheReportNamesWhatItRead()
        {
            Dictionary<string, string> row = Row();
            row["wind_speed"] = "10";

            string path = Write(row);
            string report = DumpAudit.Report(DumpAudit.Run(path));

            Assert.Contains(Path.GetFileName(path), report);
            Assert.Contains("A DEFECT CHECK FAILED", report);
            Assert.Contains("wind decomposes", report);
        }

        [Fact]
        public void TheNewestDumpUnderAFolderIsTheOneAudited()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "thermal-dumps-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(folder);

            try
            {
                string older = Path.Combine(folder, "Thermodynamics_Environment_20260101_000000.csv");
                string newer = Path.Combine(folder, "nested", "Thermodynamics_Environment_20260820_175059.csv");
                Directory.CreateDirectory(Path.GetDirectoryName(newer));

                File.WriteAllText(older, Header);
                File.WriteAllText(newer, Header);
                File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(newer, new DateTime(2026, 8, 20, 17, 50, 59, DateTimeKind.Utc));

                Assert.Equal(newer, DumpAudit.Newest(folder));

                // A file named directly is taken as it stands, so one dump out of a folder of them
                // can still be audited.
                Assert.Equal(older, DumpAudit.Newest(older));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>
        /// The auditor against real field data rather than invented rows.
        ///
        /// <para>Synthetic rows prove a check can fire. They cannot prove it holds against numbers
        /// a planet produced, which is the only place the model and the world meet — the same reason
        /// `Census` keeps a real ship's block population rather than a plausible one.</para>
        ///
        /// <para>The fixture is a 264-row sample of the 2026-08-20 fleet dump, thinned one row in
        /// twenty and then completed with every buried row, every row under weather, every airless
        /// row, and every row that exceeded the engine's wind ceiling or carried slope wind — the
        /// minorities a uniform sample would drop. Refresh it when a dump arrives, from
        /// `-- dump`.</para>
        /// </summary>
        [Fact]
        public void TheFieldDumpFixtureHoldsEveryDefectCheck()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            foreach (DumpAudit.CheckResult check in result.Checks)
            {
                Assert.False(check.Skipped, check.Name + " skipped: the fixture lost a column");
                Assert.False(check.Failed, check.Name + " failed: " + check.Worst);
            }

            Assert.Equal(264, result.Rows);
        }

        /// <summary>
        /// The two open questions the fixture is evidence for. Both are counted rather than failed,
        /// and both are here so that a change which quietly resolves — or worsens — one of them
        /// cannot pass unnoticed.
        /// </summary>
        [Fact]
        public void TheFieldDumpFixtureCarriesTheTwoOpenObservations()
        {
            DumpAudit.Result result = DumpAudit.Run(Fixture());

            // B18: the engine's figure scales the wind rather than bounding it, once the vertical
            // profile multiplies the band share above the reference height.
            DumpAudit.CheckResult ceiling = Check(result, "wind stays under");
            Assert.Equal(3, ceiling.Hits);
            Assert.Contains("80.29", ceiling.Worst);

            // B15: slope wind is a near-ground term and behaves like one.
            DumpAudit.CheckResult slope = Check(result, "slope wind");
            Assert.Equal(2, slope.Hits);
        }

        private static string Fixture()
        {
            // The build output no longer sits inside the repository, so the fixture is located the
            // way the benchmark baseline is: from the compiled-in source path.
            string path = Path.Combine(
                Harness.ShippedBlocks.RepoRoot(), "tests", "benchmarks", "field-environment.csv");

            Assert.True(File.Exists(path), "field dump fixture missing at " + path);
            return path;
        }

        [Fact]
        public void APathWithNoDumpInItAnswersNothingRatherThanThrowing()
        {
            Assert.Null(DumpAudit.Newest(null));
            Assert.Null(DumpAudit.Newest(""));
            Assert.Null(DumpAudit.Newest(Path.Combine(Path.GetTempPath(), "thermal-absent-" + Guid.NewGuid().ToString("n"))));
        }
    }
}
