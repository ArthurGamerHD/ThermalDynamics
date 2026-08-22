using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every balance dial, swept over the standing panel, so each one gets a measured response
    /// curve rather than an argument.
    ///
    /// <para>
    /// Opt-in on the same terms as the rest of the corpus work, and additionally on a panel file:
    /// <c>tools/corpus/panel.csv</c>, built by <c>tools/corpus/panel.py</c> from a census and a
    /// survey. The panel is 36 ships chosen for spread — both grid sizes, both failure modes, the
    /// hulls that are critical in exactly one scenario, and controls that must stay safe — and it
    /// is small enough that the whole grid of configurations is hours rather than weeks.
    /// </para>
    ///
    /// <code>
    ///     THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=out/knobs dotnet test --filter KnobSweep
    /// </code>
    ///
    /// <para>
    /// **The configurations run in sequence, and that is deliberate.** A material override is a
    /// process-wide static that empties the shared model cache, so two configurations in flight at
    /// once would build ships out of each other's world. The parallelism is inside a configuration,
    /// across the panel, which is where it was anyway.
    /// </para>
    /// </summary>
    public class KnobSweep
    {
        public const string Header =
            "knob,level,shipped,scenario,ship,workshop_id,large,blocks,"
            + "peak_k,mean_k,median_k,min_k,hotspot_k,over_critical,over_share,"
            + "seconds_to_settle,seconds_to_critical,made_w,vented_w,bulk_drift_w,"
            + "substeps_demanded,hottest_block";

        private static int rowsWritten;

        [Fact]
        public void EveryDialGetsAMeasuredCurve()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<PanelShip> panel = Panel();
            if (panel.Count == 0) return;

            List<Blueprints.Ship> ships = Load(panel);
            Assert.True(ships.Count > 0, "the panel named ships but none of them could be read");

            Dictionary<string, Battery.Scenario> scenarios =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) scenarios[scenario.Name] = scenario;

            // A control before anything is moved: the shipped world, in every environment the game
            // has. The dials are read against this, and a sweep whose control does not reproduce
            // the survey is measuring something other than the mod.
            Run("environment", 0f, true, KnobLab.Environments, new ThermalSettings().Derive(),
                null, ships, scenarios);

            foreach (KnobLab.Configuration configuration in KnobLab.All())
            {
                Run(configuration.Knob.Name, configuration.Level, configuration.IsShipped,
                    configuration.Scenarios, configuration.Settings(), configuration.Material(),
                    ships, scenarios);
            }

            Assert.True(rowsWritten > 0, "the sweep produced no rows");
        }

        /// <summary>
        /// One configuration: install its world, run the panel through its scenarios, take it down
        /// again.
        ///
        /// The override is cleared in a finally, because leaving one installed would silently
        /// contaminate every configuration after it — and the contamination reads as a smooth
        /// curve rather than as an error.
        /// </summary>
        private static void Run(string knob, float level, bool shipped, string[] wanted,
            ThermalSettings settings, Func<BlockThermalProperties, BlockThermalProperties> material,
            List<Blueprints.Ship> ships, Dictionary<string, Battery.Scenario> scenarios)
        {
            List<string> rows = new List<string>();
            object gate = new object();

            try
            {
                Blueprints.MaterialOverride = material;

                System.Threading.Tasks.ParallelOptions options =
                    new System.Threading.Tasks.ParallelOptions
                    { MaxDegreeOfParallelism = LabRun.Workers };

                System.Threading.Tasks.Parallel.ForEach(
                    System.Collections.Concurrent.Partitioner.Create(0, ships.Count, 1),
                    options,
                    range =>
                    {
                        List<string> mine = new List<string>();

                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            // Each worker owns its own copy, exactly as the survey does: block
                            // instances carry the applied load, so two scenarios sharing one
                            // instance would write over each other.
                            Blueprints.Ship ship = ships[i].Reload();

                            for (int s = 0; s < wanted.Length; s++)
                            {
                                Battery.Scenario scenario;
                                if (!scenarios.TryGetValue(wanted[s], out scenario)) continue;

                                try
                                {
                                    ScenarioOutcome outcome = Battery.Run(ship, scenario, settings);
                                    mine.Add(Row(knob, level, shipped, ship, outcome));
                                }
                                catch
                                {
                                    // A ship this configuration cannot run must not lose the sweep.
                                }
                            }
                        }

                        lock (gate) rows.AddRange(mine);
                    });
            }
            finally
            {
                Blueprints.MaterialOverride = null;
            }

            if (CorpusRecord.On && rows.Count > 0) CorpusRecord.Write("knobs", Header, rows);

            rowsWritten += rows.Count;
            Progress(knob + "=" + level.ToString("0.###", CultureInfo.InvariantCulture)
                + " wrote " + rows.Count + " rows");
        }

        private static string Row(string knob, float level, bool shipped, Blueprints.Ship ship,
            ScenarioOutcome o)
        {
            StringBuilder row = new StringBuilder();
            row.Append(CorpusRecord.Text(knob)).Append(',');
            row.Append(CorpusRecord.Num(level)).Append(',');
            row.Append(shipped ? 1 : 0).Append(',');
            row.Append(CorpusRecord.Text(o.Scenario)).Append(',');
            row.Append(CorpusRecord.Text(ship.Name)).Append(',');
            row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(ship.Large ? 1 : 0).Append(',');
            row.Append(ship.Blocks).Append(',');
            row.Append(CorpusRecord.Num(o.PeakKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MeanKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MedianKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MinKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.HotSpotKelvin)).Append(',');
            row.Append(o.BlocksOverCritical).Append(',');
            row.Append(CorpusRecord.Num(o.OverCriticalShare)).Append(',');
            row.Append(CorpusRecord.Num(o.SecondsToSettle)).Append(',');
            row.Append(CorpusRecord.Num(o.SecondsToCritical)).Append(',');
            row.Append(CorpusRecord.Num(o.MadeWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.VentedWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.BulkDriftWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.SubstepsDemanded)).Append(',');
            row.Append(CorpusRecord.Text(o.HottestBlock));
            return row.ToString();
        }

        // ---- the panel -----------------------------------------------------------------------

        private class PanelShip
        {
            public string Name;
            public string WorkshopId;

            /// <summary>The blueprint this ship was measured from, so the sweep need not go looking.</summary>
            public string Path;
        }

        /// <summary>Reads the panel, authored by tools/corpus/panel.py and committed.</summary>
        private static List<PanelShip> Panel()
        {
            List<PanelShip> panel = new List<PanelShip>();

            string path = Environment.GetEnvironmentVariable("THERMAL_PANEL");
            if (string.IsNullOrEmpty(path)) path = Find("tools/corpus/panel.csv");
            if (path == null || !File.Exists(path)) return panel;

            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                List<string> fields = Split(lines[i]);
                if (fields.Count < 3) continue;
                panel.Add(new PanelShip
                {
                    Name = fields[0], WorkshopId = fields[1], Path = fields[2],
                });
            }

            return panel;
        }

        /// <summary>Splits one CSV line, honouring the quoting CorpusRecord.Text writes.</summary>
        private static List<string> Split(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { current.Append(c); continue; }

                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Length = 0; }
                else current.Append(c);
            }

            fields.Add(current.ToString());
            return fields;
        }

        /// <summary>Walks up for a repository-relative path, as the rest of the harness does.</summary>
        private static string Find(string relative)
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10 && directory != null; i++)
            {
                string candidate = Path.Combine(directory, relative);
                if (File.Exists(candidate)) return candidate;
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }

        /// <summary>
        /// Reads the panel's ships, one blueprint each.
        ///
        /// <para>
        /// **The panel carries its own paths, and that is what makes this cheap.** The first
        /// version matched on name and workshop id by walking the corpus, which meant fully parsing
        /// all 9,981 blueprints — with every worker free to open a quarter-gigabyte file at the same
        /// moment — to find 36 ships. That is precisely the shape <see cref="CorpusFixture"/>
        /// documents as how earlier runs died on memory, and it wedged the first smoke test of this
        /// sweep. Reading the paths the panel was built from opens 36 files instead.
        /// </para>
        ///
        /// <para>
        /// Matched on name and workshop id together within the file, because one blueprint can hold
        /// several ships and the corpus holds ships whose names collide — the census and survey are
        /// joined on the same pair for the same reason.
        /// </para>
        /// </summary>
        private static List<Blueprints.Ship> Load(List<PanelShip> panel)
        {
            List<Blueprints.Ship> found = new List<Blueprints.Ship>();
            object gate = new object();
            int unresolved = 0;

            GameBlocks.BySubtype();

            System.Threading.Tasks.ParallelOptions options =
                new System.Threading.Tasks.ParallelOptions
                { MaxDegreeOfParallelism = LabRun.Workers };

            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, panel.Count, 1),
                options,
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        PanelShip wanted = panel[i];

                        if (string.IsNullOrEmpty(wanted.Path) || !File.Exists(wanted.Path))
                        {
                            lock (gate) unresolved++;
                            continue;
                        }

                        List<Blueprints.Ship> read;
                        try { read = Blueprints.Read(wanted.Path); }
                        catch { lock (gate) unresolved++; continue; }

                        bool matched = false;
                        foreach (Blueprints.Ship ship in read)
                        {
                            if (ship.Name != wanted.Name) continue;
                            if (ship.WorkshopId.ToString(CultureInfo.InvariantCulture)
                                != wanted.WorkshopId) continue;

                            lock (gate) found.Add(ship);
                            matched = true;
                            break;
                        }

                        if (!matched) lock (gate) unresolved++;
                    }
                });

            Progress("panel resolved " + found.Count + " of " + panel.Count + " ships, "
                + unresolved + " unresolved");
            return found;
        }

        private static void Progress(string line)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_PROGRESS");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss") + " knobs " + line + Environment.NewLine);
            }
            catch
            {
                // Progress reporting must never be the reason a run fails.
            }
        }
    }
}
