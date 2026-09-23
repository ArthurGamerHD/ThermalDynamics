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


            List<ShipSet.Entry> panel = Panel();
            Assert.True(panel.Count > 0,
                "the corpus is opted in but no panel was read. Build one with "
                + "tools/corpus/panel.py, or point THERMAL_PANEL at it.");

            List<Blueprints.Ship> ships = ShipSet.Load(panel, Progress);
            Assert.True(ships.Count > 0, "the panel named ships but none of them could be read");

            Dictionary<string, Battery.Scenario> scenarios =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) scenarios[scenario.Name] = scenario;

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
