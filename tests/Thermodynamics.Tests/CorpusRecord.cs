using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a corpus pass **learned**, written down: the invariants are pass or fail, and every figure
    /// the run measured to answer them used to die with the assertion. Written down once, the same
    /// pass names the specimens that answer the question in minutes afterwards.
    ///
    /// <para>
    /// Off unless <c>THERMAL_CORPUS_DATA</c> names a directory. One file per kind of row, appended a
    /// batch at a time, because thirty workers opening a file per ship is its own bottleneck.
    /// See balance.md, The datasets.
    /// </para>
    /// </summary>
    internal static class CorpusRecord
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Started = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The directory to write into, or null when recording is off.</summary>
        public static string Directory()
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");
            return string.IsNullOrEmpty(path) ? null : path;
        }

        public static bool On
        {
            get { return Directory() != null; }
        }

        /// <summary>
        /// Appends rows to <paramref name="name"/>.csv, writing the header the first time that file
        /// is touched in this run.
        /// </summary>
        public static void Write(string name, string header, List<string> rows)
        {
            string directory = Directory();
            if (directory == null || rows.Count == 0) return;

            try
            {
                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, name + ".csv");

                    StringBuilder text = new StringBuilder();
                    if (Started.Add(name) && !File.Exists(path)) text.AppendLine(header);
                    foreach (string row in rows) text.AppendLine(row);

                    File.AppendAllText(path, text.ToString());
                }
            }
            catch
            {
                // Recording must never be the reason a run fails.
            }
        }

        /// <summary>A CSV field: quoted, with any quotes doubled.</summary>
        public static string Text(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>A number, invariant and with no thousands separators.</summary>
        public static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "";
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ---- the scenario matrix ------------------------------------------------------------

        public const string OutcomeHeader =
            "walk,ship,workshop_id,scenario,blocks,grids,joints,peak_k,mean_k,median_k,p95_k,min_k,"
            + "gradient_k,hotspot_k,hotspot_blocks,over_critical,over_share,margin_k,"
            + "seconds_to_settle,seconds_to_critical,peak_rate_k_per_s,thermal_mass_j_per_k,"
            + "bulk_drift_w,made_w,vented_w,radiation_w,convection_w,solar_w,friction_w,"
            + "generation_w,substeps_demanded,substeps_granted,hottest_block";

        /// <summary>Everything one run of one ship measured, as a row.</summary>
        public static string Row(string walk, ScenarioOutcome o)
        {
            StringBuilder row = new StringBuilder();
            row.Append(Text(walk)).Append(',');
            row.Append(Text(o.Ship)).Append(',');
            row.Append(o.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(Text(o.Scenario)).Append(',');
            row.Append(o.Blocks).Append(',');
            row.Append(o.Grids).Append(',');
            row.Append(o.Joints).Append(',');
            row.Append(Num(o.PeakKelvin)).Append(',');
            row.Append(Num(o.MeanKelvin)).Append(',');
            row.Append(Num(o.MedianKelvin)).Append(',');
            row.Append(Num(o.P95Kelvin)).Append(',');
            row.Append(Num(o.MinKelvin)).Append(',');
            row.Append(Num(o.GradientKelvin)).Append(',');
            row.Append(Num(o.HotSpotKelvin)).Append(',');
            row.Append(o.HotSpotBlocks).Append(',');
            row.Append(o.BlocksOverCritical).Append(',');
            row.Append(Num(o.OverCriticalShare)).Append(',');
            row.Append(Num(o.MarginKelvin)).Append(',');
            row.Append(Num(o.SecondsToSettle)).Append(',');
            row.Append(Num(o.SecondsToCritical)).Append(',');
            row.Append(Num(o.PeakRateKelvinPerSecond)).Append(',');
            row.Append(Num(o.ThermalMass)).Append(',');
            row.Append(Num(o.BulkDriftWatts)).Append(',');
            row.Append(Num(o.MadeWatts)).Append(',');
            row.Append(Num(o.VentedWatts)).Append(',');
            row.Append(Num(o.RadiationWatts)).Append(',');
            row.Append(Num(o.ConvectionWatts)).Append(',');
            row.Append(Num(o.SolarWatts)).Append(',');
            row.Append(Num(o.FrictionWatts)).Append(',');
            row.Append(Num(o.GenerationWatts)).Append(',');
            row.Append(Num(o.SubstepsDemanded)).Append(',');
            row.Append(o.SubstepsGranted).Append(',');
            row.Append(Text(o.HottestBlock));
            return row.ToString();
        }

        /// <summary>Records a whole matrix of outcomes under one walk's name.</summary>
        public static void Outcomes(string walk, List<ScenarioOutcome> outcomes)
        {
            if (!On) return;

            List<string> rows = new List<string>(outcomes.Count);
            foreach (ScenarioOutcome outcome in outcomes) rows.Add(Row(walk, outcome));

            Write("outcomes", OutcomeHeader, rows);
        }

        // ---- what a ship is, before anything is stepped --------------------------------------

        public const string ShipHeader =
            "ship,workshop_id,path,large,blocks,nodes,grids,joints,rooms,sealed_blocks,"
            + "stepped,accounted";

        public static string ShipRow(Blueprints.Ship ship, int nodes, int joints, int rooms,
            long sealedBlocks, bool stepped, bool accounted)
        {
            StringBuilder row = new StringBuilder();
            row.Append(Text(ship.Name)).Append(',');
            row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(Text(ship.Path)).Append(',');
            row.Append(ship.Large ? 1 : 0).Append(',');
            row.Append(ship.Blocks).Append(',');
            row.Append(nodes).Append(',');
            row.Append(ship.Grids.Count).Append(',');
            row.Append(joints).Append(',');
            row.Append(rooms).Append(',');
            row.Append(sealedBlocks).Append(',');
            row.Append(stepped ? 1 : 0).Append(',');
            row.Append(accounted ? 1 : 0);
            return row.ToString();
        }
    }
}
