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
    /// survey. The panel is chosen for spread — both grid sizes, both failure modes, the hulls that
    /// are critical in exactly one scenario, and controls that must stay safe — and it is small
    /// enough that the whole grid of configurations is hours rather than weeks. **Its size is not
    /// quoted here**: it was 36 for as long as this comment said so and the file had 50 (`E5`), and
    /// `panel.csv` is the only place that answers it.
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
    [Collection("alone")]
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

            // **Past the corpus opt-in, a missing panel is a fault rather than a reason to stand
            // down.** The first full run of this sweep returned green in 355 ms because the panel
            // was not found, which is the same silent-no-op failure the material override guards
            // against: a sweep that measures nothing must not report success.
            List<ShipSet.Entry> panel = Panel();
            Assert.True(panel.Count > 0,
                "the corpus is opted in but no panel was read. Build one with "
                + "tools/corpus/panel.py, or point THERMAL_PANEL at it.");

            List<Blueprints.Ship> ships = ShipSet.Load(panel, Progress);
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
            ThermalSettings settings,
            Func<string, string, BlockThermalProperties, BlockThermalProperties> material,
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

        // ---- the panel ---------------------------------------------------------------------

        /// <summary>
        /// Reads the standing panel, authored by <c>tools/corpus/panel.py</c> and committed. The
        /// mechanics live in <see cref="ShipSet"/>, which three sweeps share — this one, the
        /// conductance retest and the paired grid — because three copies of a CSV reader drift and
        /// the drift is silent in all three directions (`D3`).
        /// </summary>
        private static List<ShipSet.Entry> Panel()
        {
            return ShipSet.Read("THERMAL_PANEL", "tools/corpus/panel.csv");
        }

        private static void Progress(string line)
        {
            ShipSet.Progress("knobs", line);
        }
    }
}
