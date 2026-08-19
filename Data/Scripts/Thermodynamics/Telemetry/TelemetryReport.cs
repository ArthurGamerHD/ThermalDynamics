using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Turns the collected telemetry into files in world storage, plus a condensed summary in
    /// the game log.
    ///
    /// Three artefacts are produced:
    ///   Thermodynamics_Telemetry_&lt;stamp&gt;.log   the full human-readable report
    ///   Thermodynamics_BlockTypes_&lt;stamp&gt;.csv  one row per block definition
    ///   Thermodynamics_Grids_&lt;stamp&gt;.csv       one row per grid
    ///
    /// The CSVs exist so a session can be diffed against another one, or against the rewritten
    /// model in sim/, without re-reading prose.
    /// </summary>
    public static class TelemetryReport
    {
        private const int DetailedGridLimit = 25;

        public static void Write(string reason)
        {
            string stamp = Telemetry.StartedUtc.ToString("yyyyMMdd_HHmmss");

            string report = BuildReport(reason);

            // The game log always gets the headline numbers, because world-storage writes are the
            // part most likely to fail during shutdown.
            MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] " + BuildLogSummary(reason));

            bool wrote = TryWrite("Thermodynamics_Telemetry_" + stamp + ".log", report);
            TryWrite("Thermodynamics_BlockTypes_" + stamp + ".csv", BuildBlockTypeCsv());
            TryWrite("Thermodynamics_Grids_" + stamp + ".csv", BuildGridCsv());
            TryWrite("Thermodynamics_Surfaces_" + stamp + ".csv", BuildSurfaceCsv());
            TryWrite("Thermodynamics_Rooms_" + stamp + ".csv", BuildRoomCsv());
            TryWrite("Thermodynamics_Environment_" + stamp + ".csv", BuildEnvironmentCsv());

            if (!wrote)
            {
                // Nowhere else for it to go. The log takes the whole thing rather than lose it.
                MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] world storage unavailable, full report follows\n" + report);
            }
        }

        private static bool TryWrite(string filename, string content)
        {
            try
            {
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(TelemetryReport));
                writer.Write(content);
                writer.Flush();
                writer.Close();

                MyLog.Default.Info("[" + Settings.Name + "] [Telemetry] wrote " + filename
                    + " (" + content.Length.ToString("n0") + " chars)");
                return true;
            }
            catch (Exception e)
            {
                MyLog.Default.Warning("[" + Settings.Name + "] [Telemetry] could not write " + filename + ": " + e.Message);
                return false;
            }
        }

        private static string BuildLogSummary(string reason)
        {
            long liveGrids = 0;
            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                if (!Telemetry.Grids[i].IsClosed) liveGrids++;
            }

            return "session ended (" + reason + ") after " + Telemetry.SessionSeconds.ToString("n1")
                + "s: " + Telemetry.GridsSeen + " grids (" + liveGrids + " alive at close), "
                + Telemetry.BlockTypes.Count + " block types, "
                + Telemetry.CellUpdatesObserved.ToString("n0") + " cell updates, "
                + Telemetry.Anomalies.Count + " anomaly kinds";
        }

        // ------------------------------------------------------------------------------------
        // Text report
        // ------------------------------------------------------------------------------------

        private static string BuildReport(string reason)
        {
            StringBuilder sb = new StringBuilder(64 * 1024);

            WriteHeader(sb, reason);
            WriteSettings(sb);
            WriteSessionTotals(sb);
            WriteAnomalies(sb);
            AppendClimate(sb);
            WritePerformance(sb);
            WriteSubsteps(sb);
            WriteGridTable(sb);
            WriteGridDetails(sb);
            WriteBlockTypes(sb);
            WriteConsistency(sb);

            sb.Append("\n--- end of report ---\n");
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title)
        {
            sb.Append('\n').Append(title).Append('\n');
            sb.Append(new string('=', title.Length)).Append('\n');
        }

        /// <summary>
        /// One "name value" line. The name is padded to a column and always followed by at least
        /// one space: a name that exactly fills the column used to run straight into its value and
        /// produce lines like "DebugSolarRadiationBlockColorsFalse".
        /// </summary>
        private static void Field(StringBuilder sb, string name, object value)
        {
            sb.Append("  ").Append(name.PadRight(30)).Append(name.Length >= 30 ? " " : "").Append(value).Append('\n');
        }

        /// <summary>
        /// What the room mapper made of the grid, and whether it agrees with the grid.
        ///
        /// The counts on their own answer "why is my sealed room not a room": every cell of the
        /// padded search box is external, structure or room, so a total that leaves block cells on
        /// the external side names the failure without anyone having to reason about flood fills.
        /// </summary>
        private static void WriteRoomMapping(StringBuilder sb, GridTelemetry g)
        {
            sb.Append("\n    room mapping (min / mean / max)\n");

            if (g.MapperCompletions == 0)
            {
                // Not a measurement of anything: the grid closed before its first pass.
                Field(sb, "  mapped", "never (no pass completed)");
                return;
            }

            Field(sb, "  sealed rooms", g.RoomCount.Format("n0"));
            Field(sb, "  exterior cells", g.ExternalCells.Format("n0"));
            Field(sb, "  structure cells", g.SolidCells.Format("n0"));
            Field(sb, "  room cells", g.RoomCells.Format("n0"));
            Field(sb, "  cells outdoors w/ block", g.OpenBlockCells.Format("n0"));
            Field(sb, "  sealed struct. read open", g.LeakedCells.Format("n0"));

            if (!g.HasAudit) return;

            Core.RoomAudit audit = g.LastAudit;
            Field(sb, "  last pass: rooms", DescribeRooms(audit));
            Field(sb, "  last pass: search box", audit.SearchVolume.ToString("n0") + " cells");
            Field(sb, "  last pass: block cells", audit.BlockCells.ToString("n0"));
            Field(sb, "  last pass: unsealed faces", audit.UnsealedBlockFaces.ToString("n0")
                + " of " + (audit.BlockCells * Core.Face.Count).ToString("n0"));
            Field(sb, "  blocks sealing nothing", audit.BlocksSealingNothing.ToString("n0"));

            if (audit.Examples != null && audit.Examples.Count > 0)
            {
                sb.Append("\n    block cells the map left outdoors\n");
                for (int i = 0; i < audit.Examples.Count; i++)
                {
                    sb.Append("      ").Append(audit.Examples[i]).Append('\n');
                }
            }

            WriteRooms(sb, g);
        }

        /// <summary>
        /// Every compartment on the grid: the ones this model found, and the ones only the game
        /// has.
        ///
        /// The second list is the one worth reading. A room in it is sealed as far as the game is
        /// concerned — its vent will say pressurised and a player will be standing in air — while
        /// this model believes the cells are outdoors, runs no air in them, and draws nothing in
        /// the room overlay. Each is named by the vent standing in it, because that is the only
        /// identity a player can read off a terminal, and by the block subtypes across the faces
        /// this model leaves open, because those are what has to be fixed.
        /// </summary>
        private static void WriteRooms(StringBuilder sb, GridTelemetry g)
        {
            if (!g.RoomScanRan && g.Rooms.Count == 0) return;

            int mapped = 0;
            int lost = 0;
            int lostWithVent = 0;
            int dry = 0;

            for (int i = 0; i < g.Rooms.Count; i++)
            {
                if (g.Rooms[i].Kind == "lost")
                {
                    lost++;
                    if (g.Rooms[i].VentPressurised) lostWithVent++;
                }
                else
                {
                    mapped++;
                    if (g.Rooms[i].Disagreement) dry++;
                }
            }

            sb.Append("\n    compartments\n");
            Field(sb, "  found by this model", mapped.ToString("n0"));
            Field(sb, "  held only by the game", lost.ToString("n0")
                + (lostWithVent > 0 ? "  (" + lostWithVent + " with a vent reporting pressurised)" : ""));

            // The row that matters when the map is right and the air is missing anyway: a
            // compartment found, left dry, and sealed as far as the game is concerned.
            Field(sb, "  found, dry, air in game", dry.ToString("n0")
                + (dry > 0 ? "  <-- the game has air in these and this model runs none" : ""));

            if (g.RoomScanTruncated)
            {
                Field(sb, "  scan", "TRUNCATED at the cell limit - the list below is partial");
            }

            if (g.Rooms.Count == 0)
            {
                Field(sb, "  rooms", "none");
                return;
            }

            sb.Append("\n      kind   idx  cells      volume  press   air kg      K links  seal  gameO2  vent  O2   leaks\n");

            for (int i = 0; i < g.Rooms.Count && i < MaxRoomsReported; i++)
            {
                RoomRow row = g.Rooms[i];

                sb.Append("      ");
                sb.Append(row.Kind.PadRight(7));
                sb.Append(row.Index.ToString().PadLeft(3));
                sb.Append(row.CellCount.ToString("n0").PadLeft(7));
                sb.Append(row.Volume.ToString("n1").PadLeft(12));
                sb.Append(row.Pressure.ToString("n2").PadLeft(7));
                sb.Append(row.AirMass.ToString("n1").PadLeft(9));
                sb.Append(row.TemperatureKelvin.ToString("n1").PadLeft(7));
                sb.Append(row.LinkCount.ToString("n0").PadLeft(6));
                sb.Append((row.GameAirtight ? "  yes" : "   no").PadLeft(6));
                sb.Append((row.GameOxygen < 0f ? "   -" : row.GameOxygen.ToString("n2")).PadLeft(8));
                sb.Append((row.VentPressurised ? "  yes" : (string.IsNullOrEmpty(row.Vents) ? "    -" : "   no")).PadLeft(6));
                sb.Append((row.OxygenLevel < 0f ? "   -" : row.OxygenLevel.ToString("n2")).PadLeft(6));
                sb.Append(row.LeakCount.ToString("n0").PadLeft(7));
                if (row.Disagreement) sb.Append("  <-- the game has air here and this model does not");
                sb.Append('\n');

                if (!string.IsNullOrEmpty(row.Vents))
                {
                    sb.Append("               vents: ").Append(row.Vents).Append('\n');
                }

                if (!string.IsNullOrEmpty(row.LeakingBlocks))
                {
                    sb.Append("               leaking through: ").Append(row.LeakingBlocks).Append('\n');
                }
            }

            if (g.Rooms.Count > MaxRoomsReported)
            {
                Field(sb, "  not listed",
                    (g.Rooms.Count - MaxRoomsReported).ToString("n0") + " more, see the rooms CSV");
            }
        }

        /// <summary>Rooms printed per grid before the report defers to the CSV.</summary>
        private const int MaxRoomsReported = 60;

        /// <summary>
        /// The rooms themselves, as sizes: "1 room, 2 cells". A count with nothing behind it
        /// leaves a reader unable to tell one enclosed cupboard from a whole sealed deck.
        /// </summary>
        private static string DescribeRooms(Core.RoomAudit audit)
        {
            if (audit.RoomCount == 0) return "none";
            if (audit.RoomSizes == null || audit.RoomSizes.Count == 0)
            {
                return audit.RoomCount + " (" + audit.RoomCells + " cells)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(audit.RoomCount).Append(audit.RoomCount == 1 ? " room, cells: " : " rooms, cells: ");
            for (int i = 0; i < audit.RoomSizes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(audit.RoomSizes[i]);
            }
            if (audit.RoomSizes.Count < audit.RoomCount) sb.Append(", ...");
            return sb.ToString();
        }

        /// <summary>Per-face fractions in canonical face order, as "F:1.0 L:1.0 U:0.0 ...".</summary>
        private static string FaceFractions(float[] byFace)
        {
            if (byFace == null) return "-";

            StringBuilder sb = new StringBuilder();
            for (int face = 0; face < Core.Face.Count && face < byFace.Length; face++)
            {
                if (face > 0) sb.Append(' ');
                sb.Append(Core.Face.Name(face).Substring(0, 1)).Append(':').Append(byFace[face].ToString("n2"));
            }
            return sb.ToString();
        }

        private static void WriteHeader(StringBuilder sb, string reason)
        {
            sb.Append("Thermodynamics telemetry report\n");
            sb.Append("===============================\n");

            Field(sb, "reason", reason);
            Field(sb, "started (utc)", Telemetry.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            Field(sb, "ended (utc)", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            Field(sb, "real duration", Telemetry.SessionSeconds.ToString("n1") + " s ("
                + (Telemetry.SessionSeconds / 60.0).ToString("n1") + " min)");
            Field(sb, "in-game start", Telemetry.GameStartDate.ToString("yyyy-MM-dd HH:mm:ss"));
            Field(sb, "in-game end", Telemetry.GameEndDate.ToString("yyyy-MM-dd HH:mm:ss"));
            Field(sb, "in-game elapsed", (Telemetry.GameEndDate - Telemetry.GameStartDate).ToString());
            Field(sb, "world", Telemetry.WorldName);
            Field(sb, "world path", Telemetry.SessionPath);
            Field(sb, "online mode", Telemetry.OnlineMode);
            Field(sb, "max players", Telemetry.MaxPlayers);
            Field(sb, "server", Telemetry.IsServer);
            Field(sb, "dedicated", Telemetry.IsDedicated);
            Field(sb, "multiplayer", Telemetry.IsMultiplayer);
            Field(sb, "sample stride", "1 in " + Telemetry.SampleStride + " cell updates");
        }

        private static void WriteSettings(StringBuilder sb)
        {
            Section(sb, "Settings in force");

            Settings s = Telemetry.SettingsSnapshot;
            if (s == null)
            {
                sb.Append("  (settings were never captured)\n");
                return;
            }

            // Driven off the name table rather than a hand-written list, so a setting added to
            // the config cannot go missing from the report that is supposed to explain a run.
            Field(sb, "Version", s.Version);
            Field(sb, "StepsPerSecond", s.StepsPerSecond);

            List<string> names = Settings.Names();
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                float value = s.GetValue(name);

                if (Settings.IsFlag(name)) Field(sb, name, value != 0f);
                else Field(sb, name, value);
            }
        }

        private static void WriteSessionTotals(StringBuilder sb)
        {
            Section(sb, "Session totals");

            long added = 0, removed = 0, splits = 0, merges = 0, doorChanges = 0;
            long mapperPasses = 0, surfaceRecalcs = 0, saves = 0, loads = 0;
            long saveBytes = 0, loadBytes = 0, damageEvents = 0, simFrames = 0;
            long loopsCreated = 0, clampedSteps = 0, nodeUpdates = 0;
            double totalDamage = 0;
            long liveGrids = 0;
            int peakCells = 0;

            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                GridTelemetry g = Telemetry.Grids[i];
                added += g.BlocksAdded;
                removed += g.BlocksRemoved;
                splits += g.Splits;
                merges += g.Merges;
                doorChanges += g.DoorStateChanges;
                mapperPasses += g.MapperCompletions;
                surfaceRecalcs += g.SurfaceRecalcs;
                saves += g.Saves;
                loads += g.Loads;
                saveBytes += g.SaveBytes;
                loadBytes += g.LoadBytes;
                damageEvents += g.DamageEvents;
                totalDamage += g.TotalDamage;
                simFrames += g.SimulationSteps;
                loopsCreated += g.CoolantLoopsCreated;
                clampedSteps += g.ClampedSteps;
                nodeUpdates += g.NodeUpdates;
                if (!g.IsClosed) liveGrids++;
                if (g.PeakCellCount > peakCells) peakCells = g.PeakCellCount;
            }

            Field(sb, "frames observed", Telemetry.FramesObserved.ToString("n0"));
            Field(sb, "grid simulation steps", simFrames.ToString("n0"));
            Field(sb, "block updates", nodeUpdates.ToString("n0"));
            Field(sb, "block updates / real second", Telemetry.SessionSeconds <= 0
                ? "-"
                : (nodeUpdates / Telemetry.SessionSeconds).ToString("n0"));
            Field(sb, "blocks observed (sampled)", Telemetry.CellUpdatesObserved.ToString("n0"));
            Field(sb, "steps clamped by substep cap", clampedSteps.ToString("n0"));
            Field(sb, "grids seen", Telemetry.GridsSeen);
            Field(sb, "grids alive at close", liveGrids);
            Field(sb, "largest grid (cells)", peakCells.ToString("n0"));
            Field(sb, "block types seen", Telemetry.BlockTypes.Count);
            Field(sb, "block models built", ThermalBlockCatalog.ModelCount
                + (ThermalBlockCatalog.MountFallbacks > 0
                    ? " (" + ThermalBlockCatalog.MountFallbacks + " with no usable mount points)"
                    : ""));
            Field(sb, "cross-grid bridges", ThermalBridges.Count);
            Field(sb, "blocks added", added.ToString("n0"));
            Field(sb, "blocks removed", removed.ToString("n0"));
            Field(sb, "grid splits", splits);
            Field(sb, "grid merges", merges);
            Field(sb, "door state changes", doorChanges.ToString("n0"));
            Field(sb, "surface refreshes", surfaceRecalcs.ToString("n0"));
            Field(sb, "room mapper passes", mapperPasses.ToString("n0"));
            Field(sb, "coolant loops created", loopsCreated);
            Field(sb, "critical damage events", damageEvents.ToString("n0"));
            Field(sb, "total heat damage", totalDamage.ToString("n1"));
            Field(sb, "saves / bytes", saves + " / " + saveBytes.ToString("n0"));
            Field(sb, "loads / bytes", loads + " / " + loadBytes.ToString("n0"));

            if (Telemetry.GridRecordsDropped > 0)
                Field(sb, "grid records dropped", Telemetry.GridRecordsDropped + " (cap " + Telemetry.MaxGridRecords + ")");
            if (Telemetry.BlockTypeRecordsDropped > 0)
                Field(sb, "block type lookups dropped", Telemetry.BlockTypeRecordsDropped);
            if (Telemetry.AnomalyKindsDropped > 0)
                Field(sb, "anomaly kinds dropped", Telemetry.AnomalyKindsDropped);
        }

        private static void WriteAnomalies(StringBuilder sb)
        {
            Section(sb, "Anomalies");

            if (Telemetry.Anomalies.Count == 0)
            {
                sb.Append("  none recorded\n");
                return;
            }

            List<AnomalyRecord> records = new List<AnomalyRecord>(Telemetry.Anomalies.Values);
            records.Sort(delegate (AnomalyRecord a, AnomalyRecord b) { return b.Count.CompareTo(a.Count); });

            for (int i = 0; i < records.Count; i++)
            {
                AnomalyRecord r = records[i];
                sb.Append("  ").Append(r.Kind).Append(" x").Append(r.Count.ToString("n0")).Append('\n');
                sb.Append("      first at ").Append(r.FirstSeconds.ToString("n1")).Append("s: ").Append(r.FirstExample).Append('\n');
                if (r.Count > 1)
                {
                    sb.Append("      last  at ").Append(r.LastSeconds.ToString("n1")).Append("s: ").Append(r.LastExample).Append('\n');
                }
            }
        }

        /// <summary>
        /// What the Cost table saw, kept so the consistency section can re-take the same figures
        /// after every other section has been written and say whether they moved.
        ///
        /// A report is built from one list in one call, so these must not move. In a field dump
        /// of 18 August they did: the merged total came to 62 % of the sum of the per-grid rows
        /// printed further down the same file, and the per-grid rows agreed to the last decimal
        /// with the CSV written afterwards. Either the list grew between the two sections — grids
        /// register from worker threads — or the aggregates ran over a shorter one. Recording
        /// both ends of the report is what tells the two apart.
        /// </summary>
        private static int costRecords;
        private static long costCalls;
        private static double costMilliseconds;
        private static long costSteps;

        private static void WritePerformance(StringBuilder sb)
        {
            Section(sb, "Cost");

            TimingStat simulation = new TimingStat("grid simulation");
            TimingStat topology = new TimingStat("  of which topology rebuild");
            TimingStat mapping = new TimingStat("  of which room mapping");
            TimingStat exposure = new TimingStat("  of which exposure refresh");
            TimingStat solver = new TimingStat("  of which solver");
            TimingStat solar = new TimingStat("  of which solar occlusion");
            TimingStat save = new TimingStat("save");
            TimingStat load = new TimingStat("load");

            costRecords = 0;
            costCalls = 0;
            costMilliseconds = 0;
            costSteps = 0;

            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                GridTelemetry g = Telemetry.Grids[i];

                costRecords++;
                costCalls += g.SimulationTime.Calls;
                costMilliseconds += g.SimulationTime.TotalMilliseconds;
                costSteps += g.SimulationSteps;

                simulation.Merge(g.SimulationTime);
                topology.Merge(g.Profiler.Topology);
                mapping.Merge(g.Profiler.RoomMapping);
                exposure.Merge(g.Profiler.Exposure);
                solver.Merge(g.Profiler.Solver);
                solar.Merge(g.SolarTime);
                save.Merge(g.SaveTime);
                load.Merge(g.LoadTime);
            }

            sb.Append("  Rows marked \"of which\" are nested inside grid simulation and are not\n");
            sb.Append("  added into the total below.\n\n");

            TimingStat.WriteHeader(sb, "path (all grids)");
            simulation.WriteRow(sb);
            topology.WriteRow(sb);
            mapping.WriteRow(sb);
            exposure.WriteRow(sb);
            solver.WriteRow(sb);
            solar.WriteRow(sb);
            save.WriteRow(sb);
            load.WriteRow(sb);
            Telemetry.SessionFrameTime.WriteRow(sb);

            // Solar occlusion runs inside the grid simulation call, so only the outer
            // measurement and the paths driven by events outside it are summed.
            double total = simulation.TotalMilliseconds + save.TotalMilliseconds
                + load.TotalMilliseconds + Telemetry.SessionFrameTime.TotalMilliseconds;

            sb.Append('\n');
            Field(sb, "total measured", total.ToString("n1") + " ms");
            Field(sb, "share of real time", Telemetry.SessionSeconds <= 0
                ? "-"
                : (100.0 * total / (Telemetry.SessionSeconds * 1000.0)).ToString("n3") + " %");

            WriteFrames(sb);

            sb.Append("\n  grid simulation, per call:\n");
            simulation.WriteDistribution(sb, "    ");
            sb.Append("\n  solver, per call:\n");
            solver.WriteDistribution(sb, "    ");
            sb.Append("\n  room mapping, per call:\n");
            mapping.WriteDistribution(sb, "    ");
            sb.Append("\n  topology rebuild, per call:\n");
            topology.WriteDistribution(sb, "    ");
            sb.Append("\n  solar occlusion, per call:\n");
            solar.WriteDistribution(sb, "    ");
        }

        /// <summary>
        /// What the mod cost per frame across every grid, and the worst frames of the session.
        ///
        /// This is the section to read first when someone reports stuttering. Every other cost
        /// figure is per grid and a stutter is not per grid: twenty ships each taking two
        /// tolerable milliseconds on the same frame is a forty-millisecond frame, and the
        /// per-grid rows all look fine. The worst frames are printed in full because a stutter
        /// is a particular event with a cause, and the counts of what its stages touched say
        /// which cause it was.
        /// </summary>
        private static void WriteFrames(StringBuilder sb)
        {
            // Grids running below real time, which is the work budget doing its job rather than
            // a fault — but it is also why heat might be moving slowly, so it is said out loud.
            int throttled = 0;
            double worstRate = 1d;
            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                GridTelemetry g = Telemetry.Grids[i];
                if (g.SimulationRate.Count == 0) continue;

                double rate = g.SimulationRate.Mean;
                if (rate >= 0.999d) continue;

                throttled++;
                if (rate < worstRate) worstRate = rate;
            }

            if (throttled > 0)
            {
                sb.Append('\n');
                Field(sb, "grids below real time", throttled.ToString("n0")
                    + ", slowest at " + (100.0 * worstRate).ToString("n1") + " %");
                sb.Append("  A grid below real time is too large to simulate at full rate and is\n");
                sb.Append("  taking shorter steps rather than coarser ones — MaxLinkVisitsPerStep.\n");
                sb.Append("  Heat moves more slowly on it; nothing else about it is different.\n");
            }

            FrameCostTracker frames = Telemetry.FrameCost;

            sb.Append("\n  per frame, all grids together:\n");

            if (frames.Frame.Calls == 0)
            {
                sb.Append("    (no frame did any work)\n");
                return;
            }

            Field(sb, "frames with work", frames.FramesWithWork.ToString("n0"));
            Field(sb, "mean", frames.Frame.MeanMilliseconds.ToString("n3") + " ms");
            Field(sb, "worst", frames.Frame.MaxMilliseconds.ToString("n3") + " ms");
            Field(sb, "over a 60 fps frame", frames.FramesOverBudget.ToString("n0") + " frames ("
                + (frames.FramesWithWork == 0
                    ? "-"
                    : (100.0 * frames.FramesOverBudget / frames.FramesWithWork).ToString("n2") + " %")
                + ")");

            // The one number worth quoting about smoothness. Near one is a mod that is uniformly
            // expensive, which costs frame rate; in the hundreds is one that is cheap on average
            // and occasionally enormous, which costs a stutter. They want different fixes.
            Field(sb, "worst over mean", frames.SpikeRatio.ToString("n1") + "x");

            sb.Append("\n    distribution:\n");
            frames.Frame.WriteDistribution(sb, "      ");

            IList<FrameSample> worst = frames.Worst;
            if (worst.Count == 0) return;

            sb.Append("\n    worst frames:\n");
            for (int i = 0; i < worst.Count; i++)
            {
                sb.Append("      ").Append(worst[i].Describe()).Append('\n');
            }
        }

        private static List<GridTelemetry> SortedGrids()
        {
            List<GridTelemetry> grids = new List<GridTelemetry>(Telemetry.Grids);
            grids.Sort(delegate (GridTelemetry a, GridTelemetry b) { return b.PeakCellCount.CompareTo(a.PeakCellCount); });
            return grids;
        }

        private static void WriteGridTable(StringBuilder sb)
        {
            Section(sb, "Grids");

            if (Telemetry.Grids.Count == 0)
            {
                sb.Append("  none\n");
                return;
            }

            sb.Append("  ")
              .Append("name".PadRight(28))
              .Append("size".PadRight(7))
              .Append("cells".PadLeft(8))
              .Append("links".PadLeft(9))
              .Append("rooms".PadLeft(7))
              .Append("peak T".PadLeft(10))
              .Append("crit".PadLeft(8))
              .Append("life s".PadLeft(9))
              .Append("  state\n");

            List<GridTelemetry> grids = SortedGrids();
            for (int i = 0; i < grids.Count; i++)
            {
                GridTelemetry g = grids[i];
                sb.Append("  ")
                  .Append(Truncate(g.Name, 27).PadRight(28))
                  .Append((g.GridSize ?? "-").PadRight(7))
                  .Append(g.PeakCellCount.ToString("n0").PadLeft(8))
                  .Append(g.NeighborLinks.SafeMax.ToString("n0").PadLeft(9))
                  .Append(g.RoomCount.SafeMax.ToString("n0").PadLeft(7))
                  .Append(FormatPeak(g.PeakTemperature).PadLeft(10))
                  .Append(g.CriticalBlocks.SafeMax.ToString("n0").PadLeft(8))
                  .Append(g.LifetimeSeconds.ToString("n0").PadLeft(9))
                  .Append("  ")
                  .Append(g.IsClosed ? "closed" : "alive")
                  .Append('\n');
            }
        }

        /// <summary>
        /// What the substep count is, what sets it, and what capping it would buy.
        ///
        /// <para>
        /// A solver step is divided into as many substeps as the <em>stiffest</em> element on the
        /// grid needs to stay numerically stable, and every other element pays for all of them. So
        /// this is the section that explains the solver's bill: the cost of a grid is its elements
        /// times its substeps, and the substeps are decided by one block.
        /// </para>
        ///
        /// <para>
        /// It reports what was asked for beside what was granted, because they diverge in two
        /// different ways — <c>MaxSubsteps</c> refuses, and <c>MaxLinkVisitsPerStep</c> shortens
        /// the step instead — and the demand keeps moving after both have bound.
        /// </para>
        /// </summary>
        private static void WriteSubsteps(StringBuilder sb)
        {
            Section(sb, "Substeps");

            sb.Append("  A step is cut into as many substeps as the stiffest element needs, and every\n");
            sb.Append("  other element pays for all of them. Demand figures are computed from real heat\n");
            sb.Append("  capacities, so they describe the grids rather than the settings in force.\n");

            List<GridTelemetry> grids = SortedGrids();

            // What a substep actually costs, measured rather than assumed: the solver's own
            // milliseconds divided by the passes it made, and again per element visited.
            double passes = 0;
            double solverMs = 0;
            double elementPasses = 0;
            long stepping = 0;

            Histogram granted = new Histogram(new float[] { 1.5f, 2.5f, 4.5f, 6.5f, 8.5f, 12.5f, 16.5f, 32.5f, 64.5f });
            long[] grantedCells = new long[granted.Counts.Length];

            for (int i = 0; i < grids.Count; i++)
            {
                GridTelemetry g = grids[i];
                if (g.SimulationSteps <= 0 || g.Substeps.Count == 0) continue;

                stepping++;

                double substeps = g.Substeps.Mean;
                double gridPasses = g.SimulationSteps * substeps;

                passes += gridPasses;
                solverMs += g.Profiler.Solver.TotalMilliseconds;
                elementPasses += gridPasses * (g.PeakCellCount + g.NeighborLinks.SafeMax);

                granted.Add((float)substeps);

                // The same buckets, weighted by how much ship is in them, because thirty grids of
                // debris and three capital ships are not the same finding.
                float value = (float)substeps;
                int bucket = granted.Edges.Length;
                for (int e = 0; e < granted.Edges.Length; e++)
                {
                    if (value < granted.Edges[e]) { bucket = e; break; }
                }
                grantedCells[bucket] += g.PeakCellCount;
            }

            sb.Append('\n');
            Field(sb, "grids stepping", stepping.ToString("n0"));
            Field(sb, "substep passes", passes.ToString("n0"));
            Field(sb, "solver ms", solverMs.ToString("n1"));
            Field(sb, "ms per substep pass", passes <= 0 ? "-" : (solverMs / passes).ToString("n4"));
            Field(sb, "ns per element visit", elementPasses <= 0
                ? "-"
                : (solverMs * 1e6 / elementPasses).ToString("n1"));

            sb.Append("\n  substeps granted, by grid and by how many cells are in them:\n");
            for (int i = 0; i < granted.Counts.Length; i++)
            {
                if (granted.Counts[i] == 0) continue;

                string label = TelemetryFormat.BucketLabel(granted.Edges, i, "");
                sb.Append("      ").Append(label.PadRight(20))
                  .Append(granted.Counts[i].ToString("n0").PadLeft(8)).Append(" grids")
                  .Append(grantedCells[i].ToString("n0").PadLeft(12)).Append(" cells\n");
            }

            WriteSubstepDrivers(sb, grids);
            WriteStiffestBlockTypes(sb);
            WriteSubstepProjection(sb, grids);
        }

        /// <summary>
        /// The block definitions that ask the most of a step, ranked.
        ///
        /// The per-grid view names one block; this names the *kinds* of block, across every grid
        /// in the world, which is what a definition author or a server operator can act on. A
        /// block's demand is not a property of its definition alone — it depends on what it is
        /// bolted to and whether it is exposed — so the spread matters as much as the peak.
        /// </summary>
        private static void WriteStiffestBlockTypes(StringBuilder sb)
        {
            List<BlockTypeTelemetry> types = new List<BlockTypeTelemetry>();
            foreach (BlockTypeTelemetry type in Telemetry.BlockTypes.Values)
            {
                if (type.SubstepDemand.Count > 0) types.Add(type);
            }

            if (types.Count == 0) return;

            types.Sort(delegate (BlockTypeTelemetry a, BlockTypeTelemetry b)
            {
                return b.PeakSubstepDemand.CompareTo(a.PeakSubstepDemand);
            });

            sb.Append("\n  block types by the substeps they demand (20 stiffest of ")
              .Append(types.Count).Append("):\n");

            sb.Append("    ").Append("subtype".PadRight(34))
              .Append("live".PadLeft(9)).Append("J/K".PadLeft(11))
              .Append("worst".PadLeft(9)).Append("mean".PadLeft(9)).Append("  where\n");

            int limit = Math.Min(types.Count, 20);
            for (int i = 0; i < limit; i++)
            {
                BlockTypeTelemetry t = types[i];

                sb.Append("    ").Append(Truncate(t.Name, 33).PadRight(34))
                  .Append(t.Live.ToString("n0").PadLeft(9))
                  .Append(t.ThermalMass.Mean.ToString("n0").PadLeft(11))
                  .Append(t.PeakSubstepDemand.ToString("n1").PadLeft(9))
                  .Append(t.SubstepDemand.Mean.ToString("n1").PadLeft(9))
                  .Append("  ").Append(t.PeakSubstepDemandPosition).Append('\n');
            }
        }

        /// <summary>The block on each grid that sets its substep count, worst grids first.</summary>
        private static void WriteSubstepDrivers(StringBuilder sb, IList<GridTelemetry> grids)
        {
            List<GridTelemetry> profiled = new List<GridTelemetry>();
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i].Profile != null && grids[i].Profile.WorstNodeIndex >= 0) profiled.Add(grids[i]);
            }

            profiled.Sort(delegate (GridTelemetry a, GridTelemetry b)
            {
                return b.Profile.RequiredSubsteps.CompareTo(a.Profile.RequiredSubsteps);
            });

            if (profiled.Count == 0) return;

            sb.Append("\n  what sets each grid's substep count (");
            sb.Append(Math.Min(profiled.Count, DetailedGridLimit)).Append(" stiffest of ")
              .Append(profiled.Count).Append("):\n");

            sb.Append("    ").Append("grid".PadRight(26))
              .Append("needs".PadLeft(8)).Append("got".PadLeft(6))
              .Append("cond%".PadLeft(7)).Append("air".PadLeft(8)).Append("loop".PadLeft(8))
              .Append("  set by\n");

            int limit = Math.Min(profiled.Count, DetailedGridLimit);
            for (int i = 0; i < limit; i++)
            {
                GridTelemetry g = profiled[i];
                ThermalSolver.SubstepProfile p = g.Profile;

                sb.Append("    ").Append(Truncate(g.Name, 25).PadRight(26))
                  .Append(p.RequiredSubsteps.ToString("n1").PadLeft(8))
                  .Append(g.Substeps.SafeMax.ToString("n0").PadLeft(6))
                  .Append((100f * p.WorstNodeConductionShare).ToString("n0").PadLeft(7))
                  .Append(p.WorstRoomAirDemand.ToString("n1").PadLeft(8))
                  .Append(p.WorstLoopDemand.ToString("n1").PadLeft(8))
                  .Append("  ").Append(g.WorstSubstepBlock).Append('\n');
            }
        }

        /// <summary>
        /// What each candidate <c>MaxSubstepsPerBlock</c> would do, summed over the world.
        ///
        /// This is the setting's own evidence: how many blocks it would raise, what the substep
        /// count would fall to, and therefore how much of the solver's bill it would remove. A
        /// cap that reaches only a handful of blocks and halves the arithmetic is a different
        /// proposition from one that reaches a tenth of the ship.
        /// </summary>
        private static void WriteSubstepProjection(StringBuilder sb, IList<GridTelemetry> grids)
        {
            int[] caps = ThermalSolver.SubstepProfile.ProjectedCaps;

            double[] visitsAfter = new double[caps.Length];
            long[] flooredNodes = new long[caps.Length];
            double visitsNow = 0;
            long nodes = 0;
            long profiled = 0;
            long environmentDominated = 0;

            for (int i = 0; i < grids.Count; i++)
            {
                GridTelemetry g = grids[i];
                ThermalSolver.SubstepProfile p = g.Profile;
                if (p == null || g.SimulationSteps <= 0 || g.Substeps.Count == 0) continue;

                profiled++;
                nodes += p.Nodes;
                environmentDominated += p.EnvironmentDominatedNodes;

                // Per simulated second, not per step, and the distinction is the whole reading.
                //
                // MaxLinkVisitsPerStep answers a step it cannot afford by making it shorter, so a
                // throttled grid already takes few substeps — measuring visits per *step* there
                // shows a cap saving nothing, because what the cap actually buys is returned as
                // simulated time rather than as arithmetic. Per simulated second the budget
                // cancels out entirely: substeps are proportional to step length, so the cost of
                // a second of heat is elements x demand / StepSeconds however the step is cut.
                // A field dump reported 0.1 % where the truth was an eightfold saving, because
                // this line divided by the wrong thing.
                double elements = p.Nodes + p.Links;
                double perStepSecond = p.StepSeconds <= 0 ? 1 : elements / p.StepSeconds;

                visitsNow += perStepSecond * Math.Max(1.0, p.RequiredSubsteps);

                for (int c = 0; c < caps.Length; c++)
                {
                    visitsAfter[c] += perStepSecond * Math.Max(1.0, p.CapRequiredSubsteps[c]);
                    flooredNodes[c] += p.CapNodesFloored[c];
                }
            }

            if (profiled == 0) return;

            sb.Append("\n  what MaxSubstepsPerBlock would do, over ").Append(profiled.ToString("n0"))
              .Append(" profiled grids and ").Append(nodes.ToString("n0")).Append(" blocks:\n");

            sb.Append("  Cost is element visits per simulated second, which is what a cap changes:\n");
            sb.Append("  a shortened step trades the same arithmetic for less heat, so per step it\n");
            sb.Append("  would look free.\n\n");

            sb.Append("    ").Append("cap".PadLeft(5))
              .Append("blocks raised".PadLeft(16)).Append("share".PadLeft(9))
              .Append("visits/sim second".PadLeft(20)).Append("saving".PadLeft(9))
              .Append("speedup".PadLeft(10)).Append('\n');

            sb.Append("    ").Append("off".PadLeft(5))
              .Append("0".PadLeft(16)).Append("-".PadLeft(9))
              .Append(visitsNow.ToString("n0").PadLeft(20)).Append("-".PadLeft(9))
              .Append("-".PadLeft(10)).Append('\n');

            for (int c = 0; c < caps.Length; c++)
            {
                double saving = visitsNow <= 0 ? 0 : 1.0 - (visitsAfter[c] / visitsNow);
                double speedup = visitsAfter[c] <= 0 ? 0 : visitsNow / visitsAfter[c];

                sb.Append("    ").Append(caps[c].ToString().PadLeft(5))
                  .Append(flooredNodes[c].ToString("n0").PadLeft(16))
                  .Append((nodes <= 0 ? 0 : 100.0 * flooredNodes[c] / nodes).ToString("n2").PadLeft(8)).Append('%')
                  .Append(visitsAfter[c].ToString("n0").PadLeft(20))
                  .Append((100.0 * saving).ToString("n1").PadLeft(8)).Append('%')
                  .Append(speedup.ToString("n2").PadLeft(9)).Append('x').Append('\n');
            }

            if (environmentDominated > 0)
            {
                sb.Append("\n  ").Append(environmentDominated.ToString("n0"))
                  .Append(" blocks are stiff mostly through radiation and convection rather than\n");
                sb.Append("  conduction. The cap covers those too, which changes how they exchange with\n");
                sb.Append("  the sky rather than with what they are bolted to — a more visible trade.\n");
            }
        }

        /// <summary>
        /// Whether the report agrees with itself.
        ///
        /// Every aggregate in this file is a sum over the same list of grid records, taken at
        /// different points while the report is built, and the CSVs are a sixth pass over it
        /// after the text is finished. Nothing about that is supposed to be able to disagree, and
        /// in a field dump it did — by a factor of 1.6, across every counter, in the direction
        /// that made the mod look cheaper than it was. A total that cannot be checked is a total
        /// that gets believed.
        ///
        /// So this section re-takes the Cost table's own figures after everything else has been
        /// written, and states the two invariants that must hold:
        ///
        /// <list type="bullet">
        /// <item>the record list must not change while the report is built;</item>
        /// <item>a grid cannot tick more often than the session is framed, because both happen
        /// once each in the same <c>Simulate</c> call.</item>
        /// </list>
        ///
        /// A line beginning <c>!!</c> is a defect in the telemetry, not in the simulation.
        /// </summary>
        private static void WriteConsistency(StringBuilder sb)
        {
            Section(sb, "Consistency");

            int records = 0;
            long calls = 0;
            long steps = 0;
            double milliseconds = 0;
            long mostTicks = 0;
            long firstFrame = long.MaxValue;
            long lastFrame = -1;
            int overFramed = 0;

            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                GridTelemetry g = Telemetry.Grids[i];

                records++;
                calls += g.SimulationTime.Calls;
                steps += g.SimulationSteps;
                milliseconds += g.SimulationTime.TotalMilliseconds;

                if (g.SimulationTime.Calls > mostTicks) mostTicks = g.SimulationTime.Calls;
                if (g.SimulationTime.Calls > Telemetry.FramesObserved) overFramed++;
                if (g.FirstTickFrame >= 0 && g.FirstTickFrame < firstFrame) firstFrame = g.FirstTickFrame;
                if (g.LastTickFrame > lastFrame) lastFrame = g.LastTickFrame;
            }

            if (firstFrame == long.MaxValue) firstFrame = -1;

            sb.Append("  The same list, summed at the Cost table and again here. These must agree;\n");
            sb.Append("  a line marked !! is a defect in the telemetry rather than in the mod.\n\n");

            Field(sb, "grid records", costRecords + " -> " + records);
            Field(sb, "grid ticks", costCalls.ToString("n0") + " -> " + calls.ToString("n0"));
            Field(sb, "grid simulation ms", costMilliseconds.ToString("n2") + " -> " + milliseconds.ToString("n2"));
            Field(sb, "grid simulation steps", costSteps.ToString("n0") + " -> " + steps.ToString("n0"));

            if (records != costRecords || calls != costCalls || steps != costSteps)
            {
                sb.Append("  !! the record list changed while the report was being written;\n");
                sb.Append("  !! every aggregate above the grid detail is short by the difference.\n");
            }

            sb.Append('\n');
            Field(sb, "frames observed", Telemetry.FramesObserved.ToString("n0"));
            Field(sb, "most ticks on one grid", mostTicks.ToString("n0"));
            Field(sb, "tick frames spanned", firstFrame < 0
                ? "-"
                : firstFrame.ToString("n0") + " .. " + lastFrame.ToString("n0"));

            if (overFramed > 0)
            {
                sb.Append("  !! ").Append(overFramed).Append(" grid(s) ticked more often than the session was framed.\n");
                sb.Append("  !! A grid ticks once per Simulate call and so does the frame counter, so this\n");
                sb.Append("  !! means the session counters were reset under records that survived it —\n");
                sb.Append("  !! see Telemetry.Reset and Telemetry.Start. Every per-session figure in this\n");
                sb.Append("  !! report covers a shorter window than the per-grid rows do.\n");
            }

            double longestLife = 0;
            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                double life = Telemetry.Grids[i].LifetimeSeconds;
                if (life > longestLife) longestLife = life;
            }

            sb.Append('\n');
            Field(sb, "session seconds", Telemetry.SessionSeconds.ToString("n1"));
            Field(sb, "longest grid lifetime", longestLife.ToString("n1") + " s");

            // Both are read off the same stopwatch, so a grid cannot have lived longer than the
            // session it lived in. Where it has, the clock was restarted under the record.
            if (longestLife > Telemetry.SessionSeconds + 1.0)
            {
                sb.Append("  !! a grid outlived the session clock, which is only possible if the\n");
                sb.Append("  !! clock was reset while the record survived.\n");
            }
        }

        /// <summary>
        /// Why this grid takes the substeps it does: what sets the count, the distribution of
        /// demand behind it, and what each cap would leave.
        /// </summary>
        private static void WriteGridSubsteps(StringBuilder sb, GridTelemetry g)
        {
            ThermalSolver.SubstepProfile p = g.Profile;
            if (p == null) return;

            sb.Append("\n    substeps\n");
            Field(sb, "  demanded (uncapped)", p.RequiredSubsteps.ToString("n2"));
            Field(sb, "  demanded as configured", p.RequiredSubstepsInForce.ToString("n2"));
            Field(sb, "  set by", g.WorstSubstepBlock);
            Field(sb, "  of which conduction", (100f * p.WorstNodeConductionShare).ToString("n0") + " %");

            if (p.WorstRoomAirDemand > 0f)
            {
                Field(sb, "  stiffest room air", p.WorstRoomAirDemand.ToString("n2")
                    + " substeps (room " + p.WorstRoomAirIndex + ")");
            }

            if (p.WorstLoopDemand > 0f)
            {
                Field(sb, "  stiffest coolant loop", p.WorstLoopDemand.ToString("n2")
                    + " substeps (loop " + p.WorstLoopIndex + ")");
            }

            if (p.EnvironmentDominatedNodes > 0)
            {
                Field(sb, "  stiff through the sky", p.EnvironmentDominatedNodes.ToString("n0") + " blocks");
            }

            sb.Append("\n      blocks by the substeps they demand\n");
            float[] edges = ThermalSolver.SubstepProfile.DemandEdges;
            for (int i = 0; i < p.Buckets.Length; i++)
            {
                if (p.Buckets[i] == 0) continue;

                string label = TelemetryFormat.BucketLabel(edges, i, "");
                double share = p.Nodes <= 0 ? 0 : 100.0 * p.Buckets[i] / p.Nodes;

                sb.Append("        ").Append(label.PadRight(22))
                  .Append(p.Buckets[i].ToString("n0").PadLeft(10))
                  .Append("  ").Append(share.ToString("n2")).Append("%\n");
            }

            sb.Append("\n      what a cap would leave\n");
            sb.Append("        ").Append("cap".PadLeft(5))
              .Append("blocks raised".PadLeft(16)).Append("substeps".PadLeft(11))
              .Append("cost per sim s".PadLeft(16)).Append('\n');

            int[] caps = ThermalSolver.SubstepProfile.ProjectedCaps;
            double now = Math.Ceiling(Math.Max(1.0, p.RequiredSubsteps));

            for (int c = 0; c < caps.Length; c++)
            {
                double after = Math.Ceiling(Math.Max(1.0, p.CapRequiredSubsteps[c]));
                double share = now <= 0 ? 1 : after / now;

                sb.Append("        ").Append(caps[c].ToString().PadLeft(5))
                  .Append(p.CapNodesFloored[c].ToString("n0").PadLeft(16))
                  .Append(after.ToString("n0").PadLeft(11))
                  .Append((100.0 * share).ToString("n0").PadLeft(12)).Append("%").Append('\n');
            }
        }

        private static void WriteGridDetails(StringBuilder sb)
        {
            List<GridTelemetry> grids = SortedGrids();
            int limit = Math.Min(grids.Count, DetailedGridLimit);

            if (limit == 0) return;

            Section(sb, "Grid detail (" + limit + " largest of " + grids.Count + ")");

            for (int i = 0; i < limit; i++)
            {
                GridTelemetry g = grids[i];

                sb.Append("\n  ").Append(g.Name).Append("  [").Append(g.EntityId).Append("]\n");
                sb.Append("  ").Append(new string('-', 60)).Append('\n');

                Field(sb, "grid size", g.GridSize + " (" + g.GridSizeMeters.ToString("n2") + " m)");
                Field(sb, "static", g.IsStatic);
                Field(sb, "lifetime", g.LifetimeSeconds.ToString("n1") + " s"
                    + (g.IsClosed ? " (closed at " + g.ClosedAtSeconds.ToString("n1") + "s)" : " (still alive)"));

                sb.Append("\n    structure (min / mean / max)\n");
                Field(sb, "  thermal cells", g.CellCount.Format("n0"));
                Field(sb, "  grid blocks", g.BlockCount.Format("n0"));
                Field(sb, "  conduction links", g.NeighborLinks.Format("n0"));
                Field(sb, "  sealed rooms", g.RoomCount.Format("n0"));
                Field(sb, "  surface entries", g.SurfaceEntries.Format("n0"));
                Field(sb, "  coolant loops", g.CoolantLoops.Format("n0"));
                Field(sb, "  RecentlyRemoved size", g.RecentlyRemovedSize.Format("n0"));
                Field(sb, "  mapper queue", g.MapperQueueDepth.Format("n0"));

                WriteRoomMapping(sb, g);

                sb.Append("\n    simulation\n");
                Field(sb, "  simulation steps", g.SimulationSteps.ToString("n0"));
                Field(sb, "  node updates", g.NodeUpdates.ToString("n0"));
                Field(sb, "  nodes sampled", g.SampledNodes.ToString("n0"));
                Field(sb, "  nodes per step", g.NodesPerStep.Format("n0"));
                Field(sb, "  solver substeps", g.Substeps.Format("n2"));
                Field(sb, "  substeps required", g.RequiredSubsteps.Format("n2"));
                Field(sb, "  steps clamped", g.ClampedSteps.ToString("n0"));
                Field(sb, "  blocks raised by cap", g.FlooredNodes.Count == 0
                    ? "-"
                    : g.FlooredNodes.Format("n0"));
                Field(sb, "  simulation rate", g.SimulationRate.Count == 0
                    ? "-"
                    : (100.0 * g.SimulationRate.Mean).ToString("n1") + " % ("
                        + g.SimulatedSecondsSkipped.ToString("n1") + " s not advanced)");
                Field(sb, "  hottest block T", g.HottestBlockTemperature.Format("n1"));
                Field(sb, "  peak temperature", FormatPeak(g.PeakTemperature) + "  " + g.PeakTemperatureBlock);
                Field(sb, "  critical blocks", g.CriticalBlocks.Format("n0"));
                Field(sb, "  damage events", g.DamageEvents.ToString("n0"));
                Field(sb, "  total damage", g.TotalDamage.ToString("n1"));

                WriteGridSubsteps(sb, g);

                sb.Append("\n    environment\n");
                Field(sb, "  planets visited", g.PlanetList);
                Field(sb, "  ambient K", g.AmbientTemperature.Format("n1"));
                Field(sb, "  air density", g.AirDensity.Format("n4"));
                Field(sb, "  atmosphere factor", g.AtmosphereFactor.Format("n4"));
                Field(sb, "  wind speed m/s", g.WindSpeed.Format("n2"));
                Field(sb, "  convection coeff", g.ConvectionCoefficient.Format("n3"));
                Field(sb, "  effective solar W", g.EffectiveSolarEnergy.Format("n1"));
                Field(sb, "  occluded share", g.OccludedShare.Format("n3"));
                Field(sb, "  grid speed m/s", g.Speed.Format("n2"));
                Field(sb, "  sun occluded", (100.0 * g.OccludedFraction).ToString("n1") + " % of "
                    + g.EnvironmentSamples.ToString("n0") + " samples");
                Field(sb, "  in atmosphere", (100.0 * g.AtmosphereFraction).ToString("n1") + " %");

                sb.Append("\n    events\n");
                Field(sb, "  blocks added / removed", g.BlocksAdded.ToString("n0") + " / " + g.BlocksRemoved.ToString("n0"));
                Field(sb, "  blocks ignored", g.BlocksIgnored.ToString("n0"));
                Field(sb, "  foreign block events", g.ForeignBlockEvents.ToString("n0"));
                Field(sb, "  splits / merges", g.Splits + " / " + g.Merges);
                Field(sb, "  door state changes", g.DoorStateChanges.ToString("n0"));
                Field(sb, "  surface refreshes", g.SurfaceRecalcs.ToString("n0"));
                Field(sb, "  mapper passes completed", g.MapperCompletions.ToString("n0"));
                Field(sb, "  blocks restored on load", g.BlocksRestored.ToString("n0"));
                Field(sb, "  rooms restored on load", g.RoomsRestored.ToString("n0"));
                Field(sb, "  saves / loads", g.Saves + " / " + g.Loads);
                Field(sb, "  save / load bytes", g.SaveBytes.ToString("n0") + " / " + g.LoadBytes.ToString("n0"));

                sb.Append("\n    cost\n");
                TimingStat.WriteHeader(sb, "  path");
                g.SimulationTime.WriteRow(sb);
                g.Profiler.Topology.WriteRow(sb);
                g.Profiler.RoomMapping.WriteRow(sb);
                g.Profiler.Exposure.WriteRow(sb);
                g.Profiler.Solver.WriteRow(sb);
                g.SolarTime.WriteRow(sb);
                g.SaveTime.WriteRow(sb);
                g.LoadTime.WriteRow(sb);

                Field(sb, "  ticks", g.SimulationTime.Calls.ToString("n0")
                    + (g.FirstTickFrame < 0
                        ? ""
                        : " over frames " + g.FirstTickFrame.ToString("n0")
                            + " .. " + g.LastTickFrame.ToString("n0")));

                sb.Append("\n    final temperature distribution\n");
                g.FinalTemperatures.Write(sb, "      ", "K");
            }
        }

        private static void WriteBlockTypes(StringBuilder sb)
        {
            Section(sb, "Block types (" + Telemetry.BlockTypes.Count + ")");

            if (Telemetry.BlockTypes.Count == 0)
            {
                sb.Append("  none\n");
                return;
            }

            List<BlockTypeTelemetry> types = SortedBlockTypes();

            sb.Append("  ")
              .Append("subtype".PadRight(40))
              .Append("placed".PadLeft(8))
              .Append("live".PadLeft(7))
              .Append("updates".PadLeft(12))
              .Append("mean T".PadLeft(10))
              .Append("max T".PadLeft(10))
              .Append("peak T".PadLeft(10))
              .Append("crit".PadLeft(9))
              .Append("damage".PadLeft(11))
              .Append('\n');

            for (int i = 0; i < types.Count; i++)
            {
                BlockTypeTelemetry t = types[i];
                sb.Append("  ")
                  .Append(Truncate(t.Name, 39).PadRight(40))
                  .Append(t.Placed.ToString("n0").PadLeft(8))
                  .Append(t.Live.ToString("n0").PadLeft(7))
                  .Append(t.TotalUpdates.ToString("n0").PadLeft(12))
                  .Append(t.Temperature.Mean.ToString("n1").PadLeft(10))
                  .Append(t.Temperature.SafeMax.ToString("n1").PadLeft(10))
                  .Append(FormatPeak(t.PeakTemperature).PadLeft(10))
                  .Append(t.CriticalUpdates.ToString("n0").PadLeft(9))
                  .Append(t.TotalDamage.ToString("n1").PadLeft(11))
                  .Append('\n');
            }

            Section(sb, "Block type detail");

            for (int i = 0; i < types.Count; i++)
            {
                BlockTypeTelemetry t = types[i];

                sb.Append("\n  ").Append(t.Name).Append("   (").Append(t.DefinitionId.TypeId).Append(")\n");
                sb.Append("  ").Append(new string('-', 60)).Append('\n');

                if (t.Definition != null)
                {
                    Core.BlockThermalProperties d = t.Definition;
                    Field(sb, "  size", t.Size.ToString());
                    Field(sb, "  Conductivity", d.Conductivity);
                    Field(sb, "  SpecificHeat", d.SpecificHeat);
                    Field(sb, "  Emissivity", d.Emissivity);
                    Field(sb, "  SurfaceAreaScaler", d.SurfaceAreaScaler);
                    Field(sb, "  ProducerWasteEnergy", d.ProducerWasteEnergy);
                    Field(sb, "  ConsumerWasteEnergy", d.ConsumerWasteEnergy);
                    Field(sb, "  CriticalTemperature", d.CriticalTemperature);
                    Field(sb, "  CriticalTemperatureScaler", d.CriticalTemperatureScaler);
                }

                if (t.HasSurfaceProfile)
                {
                    Field(sb, "  seals faces", t.FullySealingFaces + " of 6   " + FaceFractions(t.SealFractionByFace));
                    Field(sb, "  mounts faces", t.MountingFaces + " of 6   " + FaceFractions(t.MountFractionByFace));
                    Field(sb, "  observed with seal off", t.UnsealedByDoorState.ToString("n0") + " (open door)");
                }

                Field(sb, "  placed / removed / live", t.Placed + " / " + t.Removed + " / " + t.Live
                    + " (peak " + t.PeakLive + ")");
                Field(sb, "  updates (all / sampled)", t.TotalUpdates.ToString("n0") + " / " + t.SampledUpdates.ToString("n0"));
                Field(sb, "  mass kg", t.Mass.Format("n0"));
                Field(sb, "  thermal mass J/K", t.ThermalMass.Format("n0"));
                Field(sb, "  exposed surfaces", t.ExposedSurfaces.Format("n2"));
                Field(sb, "  exposed area m2", t.ExposedSurfaceArea.Format("n2"));
                Field(sb, "  temperature K", t.Temperature.Format("n1"));
                Field(sb, "  peak temperature K", FormatPeak(t.PeakTemperature) + " on grid " + t.PeakTemperatureGrid);
                Field(sb, "  dT per step", t.DeltaTemperature.Format("n5"));
                Field(sb, "  conduction W", t.ConductionWatts.Format("n2"));
                Field(sb, "  radiation W", t.RadiationWatts.Format("n2"));
                Field(sb, "  convection W", t.ConvectionWatts.Format("n2"));
                Field(sb, "  solar W", t.SolarWatts.Format("n2"));
                Field(sb, "  friction W", t.FrictionWatts.Format("n2"));
                Field(sb, "  heat generation W", t.HeatGeneration.Format("n2"));
                Field(sb, "  power produced W", t.EnergyProduction.Format("n0"));
                Field(sb, "  power consumed W", t.EnergyConsumption.Format("n0"));
                Field(sb, "  thrust consumed W", t.ThrustConsumption.Format("n0"));
                Field(sb, "  substeps demanded", t.SubstepDemand.Count == 0
                    ? "-"
                    : t.SubstepDemand.Format("n2"));
                if (t.PeakSubstepDemand > 0f)
                {
                    Field(sb, "  worst demand", t.PeakSubstepDemand.ToString("n2")
                        + " substeps, " + t.PeakSubstepDemandPosition
                        + " on grid " + t.PeakSubstepDemandGrid);
                }
                Field(sb, "  critical updates", t.CriticalUpdates.ToString("n0"));
                Field(sb, "  heat damage dealt", t.TotalDamage.ToString("n1"));

                sb.Append("\n    sampled temperature distribution\n");
                t.SampledTemperatures.Write(sb, "      ", "K");
                sb.Append("    final temperature distribution\n");
                t.FinalTemperatures.Write(sb, "      ", "K");
            }
        }

        private static List<BlockTypeTelemetry> SortedBlockTypes()
        {
            List<BlockTypeTelemetry> types = new List<BlockTypeTelemetry>(Telemetry.BlockTypes.Values);
            types.Sort(delegate (BlockTypeTelemetry a, BlockTypeTelemetry b)
            {
                int byUpdates = b.TotalUpdates.CompareTo(a.TotalUpdates);
                return byUpdates != 0 ? byUpdates : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            return types;
        }

        // ------------------------------------------------------------------------------------
        // CSV
        // ------------------------------------------------------------------------------------

        private static string BuildBlockTypeCsv()
        {
            StringBuilder sb = new StringBuilder(16 * 1024);
            sb.Append("subtype,type,placed,removed,live,peak_live,updates,sampled,");
            sb.Append("conductivity,specific_heat,emissivity,surface_area_scaler,");
            sb.Append("producer_waste,consumer_waste,critical_temperature,critical_scaler,");
            sb.Append("size_x,size_y,size_z,mass_mean,thermal_mass_mean,exposed_surfaces_mean,exposed_area_mean,");
            sb.Append("temp_min,temp_mean,temp_max,temp_sd,peak_temp,");
            sb.Append("dt_mean,conduction_w_mean,radiation_w_mean,convection_w_mean,solar_w_mean,friction_w_mean,heat_generation_w_mean,");
            sb.Append("power_produced_mean,power_consumed_mean,thrust_mean,");
            sb.Append("critical_updates,total_damage,");
            sb.Append("substep_demand_mean,substep_demand_max,substep_demand_peak,substep_demand_where\n");

            List<BlockTypeTelemetry> types = SortedBlockTypes();
            for (int i = 0; i < types.Count; i++)
            {
                BlockTypeTelemetry t = types[i];
                Core.BlockThermalProperties d = t.Definition;

                Csv(sb, t.Name);
                Csv(sb, t.DefinitionId.TypeId.ToString());
                Csv(sb, t.Placed);
                Csv(sb, t.Removed);
                Csv(sb, t.Live);
                Csv(sb, t.PeakLive);
                Csv(sb, t.TotalUpdates);
                Csv(sb, t.SampledUpdates);

                Csv(sb, d == null ? 0 : d.Conductivity);
                Csv(sb, d == null ? 0 : d.SpecificHeat);
                Csv(sb, d == null ? 0 : d.Emissivity);
                Csv(sb, d == null ? 0 : d.SurfaceAreaScaler);
                Csv(sb, d == null ? 0 : d.ProducerWasteEnergy);
                Csv(sb, d == null ? 0 : d.ConsumerWasteEnergy);
                Csv(sb, d == null ? 0 : d.CriticalTemperature);
                Csv(sb, d == null ? 0 : d.CriticalTemperatureScaler);

                Csv(sb, t.Size.X);
                Csv(sb, t.Size.Y);
                Csv(sb, t.Size.Z);
                Csv(sb, t.Mass.Mean);
                Csv(sb, t.ThermalMass.Mean);
                Csv(sb, t.ExposedSurfaces.Mean);
                Csv(sb, t.ExposedSurfaceArea.Mean);

                Csv(sb, t.Temperature.SafeMin);
                Csv(sb, t.Temperature.Mean);
                Csv(sb, t.Temperature.SafeMax);
                Csv(sb, t.Temperature.StdDev);
                Csv(sb, t.PeakTemperature == float.MinValue ? 0 : t.PeakTemperature);

                Csv(sb, t.DeltaTemperature.Mean);
                Csv(sb, t.ConductionWatts.Mean);
                Csv(sb, t.RadiationWatts.Mean);
                Csv(sb, t.ConvectionWatts.Mean);
                Csv(sb, t.SolarWatts.Mean);
                Csv(sb, t.FrictionWatts.Mean);
                Csv(sb, t.HeatGeneration.Mean);
                Csv(sb, t.EnergyProduction.Mean);
                Csv(sb, t.EnergyConsumption.Mean);
                Csv(sb, t.ThrustConsumption.Mean);

                Csv(sb, t.CriticalUpdates);
                Csv(sb, t.TotalDamage);

                Csv(sb, t.SubstepDemand.Mean);
                Csv(sb, t.SubstepDemand.SafeMax);
                Csv(sb, t.PeakSubstepDemand);
                CsvLast(sb, t.PeakSubstepDemandPosition);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Every face of every block the session saw, and why the model calls it exposed or not.
        ///
        /// One row per block face — six per block — because the question this file exists to answer
        /// is about a single face on a single block: it looks open to the sky and the model says it
        /// is not, so which of the three rejection rules fired. Aggregates cannot answer that.
        ///
        /// The rows are read from the telemetry records rather than from the live grids, because
        /// the report is usually written while the world is closing and by then there are no live
        /// grids left. Each record captured its own faces at its final snapshot.
        /// </summary>
        /// <summary>
        /// One row per compartment per grid, found or lost.
        ///
        /// The columns that matter are the last four: what the game says about the same cells,
        /// what a vent in the room says, and — for a room only the game has — which block subtypes
        /// stand across the faces this model leaves open. That last column is the fix list.
        /// </summary>
        private static string BuildRoomCsv()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("grid,grid_id,kind,index,anchor_x,anchor_y,anchor_z,cells,volume_m3,");
            sb.Append("vented,pressure,air_kg,temperature_k,links,game_airtight,game_oxygen,disagreement,");
            sb.Append("vents,vent_pressurised,oxygen_level,leak_faces,leaking_blocks\n");

            IList<GridTelemetry> grids = Telemetry.Grids;

            for (int g = 0; g < grids.Count; g++)
            {
                GridTelemetry record = grids[g];
                string name = Truncate(record.Name, 40);

                for (int i = 0; i < record.Rooms.Count; i++)
                {
                    RoomRow row = record.Rooms[i];

                    Csv(sb, name);
                    Csv(sb, record.EntityId);
                    Csv(sb, row.Kind);
                    Csv(sb, row.Index);

                    Csv(sb, row.AnchorX);
                    Csv(sb, row.AnchorY);
                    Csv(sb, row.AnchorZ);

                    Csv(sb, row.CellCount);
                    Csv(sb, row.Volume);

                    Csv(sb, row.Vented ? 1 : 0);
                    Csv(sb, row.Pressure);
                    Csv(sb, row.AirMass);
                    Csv(sb, row.TemperatureKelvin);
                    Csv(sb, row.LinkCount);
                    Csv(sb, row.GameAirtight ? 1 : 0);
                    Csv(sb, row.GameOxygen);
                    Csv(sb, row.Disagreement ? 1 : 0);

                    Csv(sb, Truncate(row.Vents ?? "", 120));
                    Csv(sb, row.VentPressurised ? 1 : 0);
                    Csv(sb, row.OxygenLevel);
                    Csv(sb, row.LeakCount);
                    CsvLast(sb, Truncate(row.LeakingBlocks ?? "", 200));
                }
            }

            return sb.ToString();
        }

        private static string BuildSurfaceCsv()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("grid,grid_id,block,subtype,cell_x,cell_y,cell_z,size_x,size_y,size_z,");
            sb.Append("face,face_cells,exposed,sealed,mounted,interior,");
            sb.Append("sun_dot,sun_lit_fraction,solar_w,temperature_k,exposed_area_m2\n");

            IList<GridTelemetry> grids = Telemetry.Grids;

            for (int g = 0; g < grids.Count; g++)
            {
                GridTelemetry record = grids[g];
                string gridName = Truncate(record.Name, 40);

                for (int i = 0; i < record.Surfaces.Count; i++)
                {
                    SurfaceRow row = record.Surfaces[i];

                    Csv(sb, gridName);
                    Csv(sb, record.EntityId);
                    Csv(sb, Truncate(row.Block, 40));
                    Csv(sb, Truncate(row.Subtype, 40));

                    Csv(sb, row.Cell.X);
                    Csv(sb, row.Cell.Y);
                    Csv(sb, row.Cell.Z);
                    Csv(sb, row.Size.X);
                    Csv(sb, row.Size.Y);
                    Csv(sb, row.Size.Z);

                    Csv(sb, Face.Name(row.Face));
                    Csv(sb, row.Cells);
                    Csv(sb, row.Exposed);
                    Csv(sb, row.Sealed);
                    Csv(sb, row.Mounted);
                    Csv(sb, row.Interior);

                    Csv(sb, row.SunDot);
                    Csv(sb, row.SunLitFraction);
                    Csv(sb, row.SolarWatts);
                    Csv(sb, row.Temperature);
                    CsvLast(sb, row.ExposedArea);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// What the world was doing at each grid, over time — the file for balancing a planet.
        ///
        /// One row per grid per sampling interval, with position on the globe, the air, the sun,
        /// what this mod made of them and what the game's own weather thinks. Averages cannot
        /// answer a climate question: the whole point is the shape of ambient against altitude,
        /// against latitude, and around a day, and only the raw readings have that in them.
        /// </summary>
        /// <summary>
        /// A climate summary per planet, so the headline is readable without opening the CSV: how
        /// warm it got, how cold, how thin the air was, and what the ground was made of.
        /// </summary>
        private static void AppendClimate(StringBuilder sb)
        {
            Dictionary<string, ClimateSummary> planets = new Dictionary<string, ClimateSummary>();

            IList<GridTelemetry> grids = Telemetry.Grids;
            for (int g = 0; g < grids.Count; g++)
            {
                List<EnvironmentRow> rows = grids[g].Environment;

                for (int i = 0; i < rows.Count; i++)
                {
                    EnvironmentRow row = rows[i];
                    string planet = string.IsNullOrEmpty(row.Planet) ? "space" : row.Planet;

                    ClimateSummary summary;
                    if (!planets.TryGetValue(planet, out summary))
                    {
                        summary = new ClimateSummary();
                        planets[planet] = summary;
                    }

                    summary.Add(ref row);
                }
            }

            if (planets.Count == 0) return;

            Section(sb, "Climate");

            foreach (KeyValuePair<string, ClimateSummary> pair in planets)
            {
                sb.Append("  ").Append(pair.Key).Append('\n');
                pair.Value.Write(sb);
            }
        }

        /// <summary>Ranges seen on one planet, over every grid that was on it.</summary>
        private class ClimateSummary
        {
            private readonly RunningStat ambient = new RunningStat();
            private readonly RunningStat density = new RunningStat();
            private readonly RunningStat altitude = new RunningStat();
            private readonly RunningStat solar = new RunningStat();
            private readonly RunningStat wind = new RunningStat();
            private readonly RunningStat game = new RunningStat();
            private readonly RunningStat convection = new RunningStat();

            /// <summary>Depth, over the rows that were actually underground.</summary>
            private readonly RunningStat depth = new RunningStat();

            /// <summary>Ambient with weather in force, and what the weather was worth.</summary>
            private readonly RunningStat weatherOffset = new RunningStat();

            private readonly Dictionary<string, int> weathers = new Dictionary<string, int>();

            /// <summary>Warmest and coldest readings with the sun above and below the horizon.</summary>
            private readonly RunningStat day = new RunningStat();
            private readonly RunningStat night = new RunningStat();

            private readonly Dictionary<string, int> materials = new Dictionary<string, int>();

            public void Add(ref EnvironmentRow row)
            {
                ambient.Add(row.AmbientKelvin);
                density.Add(row.AirDensity);
                altitude.Add((float)row.AltitudeSurface);
                solar.Add(row.SolarEnergy);
                wind.Add(row.WindSpeed);
                game.Add(row.GameTemperature);
                convection.Add(row.ConvectionCoefficient);

                if (row.SunElevationDegrees > 0f) day.Add(row.AmbientKelvin);
                else night.Add(row.AmbientKelvin);

                // Only the rows it happened on. Averaging a storm against the clear days either
                // side of it reports a drizzle that never fell.
                if (row.Depth > 0f) depth.Add(row.Depth);

                if (!string.IsNullOrEmpty(row.Weather) && row.WeatherIntensity > 0f)
                {
                    weatherOffset.Add(row.WeatherAmbientOffset);

                    int seen;
                    weathers.TryGetValue(row.Weather, out seen);
                    weathers[row.Weather] = seen + 1;
                }

                if (string.IsNullOrEmpty(row.SurfaceMaterial)) return;

                int count;
                materials.TryGetValue(row.SurfaceMaterial, out count);
                materials[row.SurfaceMaterial] = count + 1;
            }

            public void Write(StringBuilder sb)
            {
                Field(sb, "    ambient C", Celsius(ambient));
                Field(sb, "    by day C", Celsius(day));
                Field(sb, "    by night C", Celsius(night));
                Field(sb, "    air density", density.Format("n4"));
                Field(sb, "    altitude m", altitude.Format("n0"));
                Field(sb, "    solar W", solar.Format("n0"));
                Field(sb, "    wind m/s", wind.Format("n1"));
                Field(sb, "    convection W/m2K", convection.Format("n1"));
                Field(sb, "    game temperature", game.Format("n3"));

                if (depth.Count > 0)
                {
                    Field(sb, "    depth m", depth.Format("n0"));
                }

                if (weathers.Count > 0)
                {
                    Field(sb, "    weather ambient K", weatherOffset.Format("n1"));
                    Field(sb, "    weather", Counted(weathers));
                }

                if (materials.Count == 0) return;

                Field(sb, "    ground", Counted(materials));
            }

            /// <summary>"Snow 56, Sand_02 56" — what was seen and how often.</summary>
            private static string Counted(Dictionary<string, int> counts)
            {
                StringBuilder seen = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    if (seen.Length > 0) seen.Append(", ");
                    seen.Append(pair.Key).Append(' ').Append(pair.Value);
                }

                return seen.ToString();
            }

            private static string Celsius(RunningStat stat)
            {
                if (stat.Count == 0) return "-";

                return Tools.KelvinToCelsius(stat.SafeMin).ToString("n1") + " / "
                    + Tools.KelvinToCelsius((float)stat.Mean).ToString("n1") + " / "
                    + Tools.KelvinToCelsius(stat.SafeMax).ToString("n1")
                    + " (n " + stat.Count + ")";
            }
        }

        private static string BuildEnvironmentCsv()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("time_s,grid,grid_id,planet,altitude_surface_m,altitude_sealevel_m,latitude_deg,");
            sb.Append("sun_elevation_deg,air_density,atmosphere_factor,ambient_k,ambient_c,underground,depth_m,");
            sb.Append("solar_w,solar_occlusion,convection_coeff,wind_speed,wind_bearing_deg,wind_ceiling,");
            sb.Append("weather,weather_intensity,weather_ambient_k,game_temperature,");
            sb.Append("surface_material,grid_mean_k,grid_peak_k\n");

            IList<GridTelemetry> grids = Telemetry.Grids;

            for (int g = 0; g < grids.Count; g++)
            {
                GridTelemetry record = grids[g];
                string name = Truncate(record.Name, 40);

                for (int i = 0; i < record.Environment.Count; i++)
                {
                    EnvironmentRow row = record.Environment[i];

                    Csv(sb, row.Seconds);
                    Csv(sb, name);
                    Csv(sb, record.EntityId);
                    Csv(sb, Truncate(row.Planet ?? "", 40));

                    Csv(sb, row.AltitudeSurface);
                    Csv(sb, row.AltitudeSealevel);
                    Csv(sb, row.LatitudeDegrees);
                    Csv(sb, row.SunElevationDegrees);

                    Csv(sb, row.AirDensity);
                    Csv(sb, row.AtmosphereFactor);
                    Csv(sb, row.AmbientKelvin);
                    Csv(sb, Tools.KelvinToCelsius(row.AmbientKelvin));
                    Csv(sb, row.Underground ? 1 : 0);
                    Csv(sb, row.Depth);

                    Csv(sb, row.SolarEnergy);
                    Csv(sb, row.SolarOcclusion);
                    Csv(sb, row.ConvectionCoefficient);
                    Csv(sb, row.WindSpeed);
                    Csv(sb, row.WindBearingDegrees);
                    Csv(sb, row.WindCeiling);

                    Csv(sb, Truncate(row.Weather ?? "", 32));
                    Csv(sb, row.WeatherIntensity);
                    Csv(sb, row.WeatherAmbientOffset);
                    Csv(sb, row.GameTemperature);

                    Csv(sb, Truncate(row.SurfaceMaterial ?? "", 32));
                    Csv(sb, row.GridMeanKelvin);
                    CsvLast(sb, row.GridPeakKelvin);
                }
            }

            return sb.ToString();
        }

        private static string BuildGridCsv()
        {
            StringBuilder sb = new StringBuilder(8 * 1024);
            sb.Append("entity_id,name,grid_size,is_static,closed,lifetime_s,");
            sb.Append("peak_cells,mean_cells,peak_links,peak_rooms,peak_loops,");
            sb.Append("peak_external_cells,peak_solid_cells,peak_room_cells,max_leaked_cells,max_open_block_cells,");
            sb.Append("simulation_steps,node_updates,sampled_nodes,substeps_mean,clamped_steps,");
            sb.Append("peak_temperature,mean_hottest,critical_max,damage_events,total_damage,");
            sb.Append("ambient_min,ambient_mean,ambient_max,air_density_mean,wind_mean,wind_max,speed_max,");
            sb.Append("occluded_fraction,atmosphere_fraction,");
            sb.Append("blocks_added,blocks_removed,blocks_restored,rooms_restored,splits,merges,door_changes,surface_refreshes,");
            sb.Append("mapper_passes,loops_created,saves,loads,save_bytes,load_bytes,");
            sb.Append("sim_ms_total,sim_ms_max,topology_ms_total,mapping_ms_total,exposure_ms_total,solver_ms_total,solver_ms_max,");
            sb.Append("solar_ms_total,solar_ms_max,save_ms_total,load_ms_total,");
            sb.Append("ticks,first_tick_frame,last_tick_frame,");
            sb.Append("substeps_required_mean,substeps_required_max,floored_nodes_mean,");
            sb.Append("demand_uncapped,demand_configured,demand_conduction_share,");
            sb.Append("demand_room_air,demand_loop,environment_stiff_nodes,substep_driver,");

            // Generated from the same array the projection walks, so a cap added there cannot
            // leave the header describing columns that are no longer the ones being written.
            int[] capColumns = ThermalSolver.SubstepProfile.ProjectedCaps;
            for (int c = 0; c < capColumns.Length; c++)
            {
                sb.Append("cap").Append(capColumns[c]).Append("_substeps,");
            }
            for (int c = 0; c < capColumns.Length; c++)
            {
                sb.Append("cap").Append(capColumns[c]).Append("_blocks");
                sb.Append(c == capColumns.Length - 1 ? "\n" : ",");
            }

            List<GridTelemetry> grids = SortedGrids();
            for (int i = 0; i < grids.Count; i++)
            {
                GridTelemetry g = grids[i];

                Csv(sb, g.EntityId);
                Csv(sb, g.Name);
                Csv(sb, g.GridSize);
                Csv(sb, g.IsStatic ? 1 : 0);
                Csv(sb, g.IsClosed ? 1 : 0);
                Csv(sb, g.LifetimeSeconds);

                Csv(sb, g.PeakCellCount);
                Csv(sb, g.CellCount.Mean);
                Csv(sb, g.NeighborLinks.SafeMax);
                Csv(sb, g.RoomCount.SafeMax);
                Csv(sb, g.CoolantLoops.SafeMax);

                Csv(sb, g.ExternalCells.SafeMax);
                Csv(sb, g.SolidCells.SafeMax);
                Csv(sb, g.RoomCells.SafeMax);
                Csv(sb, g.LeakedCells.SafeMax);
                Csv(sb, g.OpenBlockCells.SafeMax);

                Csv(sb, g.SimulationSteps);
                Csv(sb, g.NodeUpdates);
                Csv(sb, g.SampledNodes);
                Csv(sb, g.Substeps.Mean);
                Csv(sb, g.ClampedSteps);

                Csv(sb, g.PeakTemperature == float.MinValue ? 0 : g.PeakTemperature);
                Csv(sb, g.HottestBlockTemperature.Mean);
                Csv(sb, g.CriticalBlocks.SafeMax);
                Csv(sb, g.DamageEvents);
                Csv(sb, g.TotalDamage);

                Csv(sb, g.AmbientTemperature.SafeMin);
                Csv(sb, g.AmbientTemperature.Mean);
                Csv(sb, g.AmbientTemperature.SafeMax);
                Csv(sb, g.AirDensity.Mean);
                Csv(sb, g.WindSpeed.Mean);
                Csv(sb, g.WindSpeed.SafeMax);
                Csv(sb, g.Speed.SafeMax);

                Csv(sb, g.OccludedFraction);
                Csv(sb, g.AtmosphereFraction);

                Csv(sb, g.BlocksAdded);
                Csv(sb, g.BlocksRemoved);
                Csv(sb, g.BlocksRestored);
                Csv(sb, g.RoomsRestored);
                Csv(sb, g.Splits);
                Csv(sb, g.Merges);
                Csv(sb, g.DoorStateChanges);
                Csv(sb, g.SurfaceRecalcs);
                Csv(sb, g.MapperCompletions);
                Csv(sb, g.CoolantLoopsCreated);
                Csv(sb, g.Saves);
                Csv(sb, g.Loads);
                Csv(sb, g.SaveBytes);
                Csv(sb, g.LoadBytes);

                Csv(sb, g.SimulationTime.TotalMilliseconds);
                Csv(sb, g.SimulationTime.MaxMilliseconds);
                Csv(sb, g.Profiler.Topology.TotalMilliseconds);
                Csv(sb, g.Profiler.RoomMapping.TotalMilliseconds);
                Csv(sb, g.Profiler.Exposure.TotalMilliseconds);
                Csv(sb, g.Profiler.Solver.TotalMilliseconds);
                Csv(sb, g.Profiler.Solver.MaxMilliseconds);
                Csv(sb, g.SolarTime.TotalMilliseconds);
                Csv(sb, g.SolarTime.MaxMilliseconds);
                Csv(sb, g.SaveTime.TotalMilliseconds);
                Csv(sb, g.LoadTime.TotalMilliseconds);

                Csv(sb, g.SimulationTime.Calls);
                Csv(sb, g.FirstTickFrame);
                Csv(sb, g.LastTickFrame);

                Csv(sb, g.RequiredSubsteps.Mean);
                Csv(sb, g.RequiredSubsteps.SafeMax);
                Csv(sb, g.FlooredNodes.Mean);

                ThermalSolver.SubstepProfile p = g.Profile;
                Csv(sb, p == null ? 0 : p.RequiredSubsteps);
                Csv(sb, p == null ? 0 : p.RequiredSubstepsInForce);
                Csv(sb, p == null ? 0 : p.WorstNodeConductionShare);
                Csv(sb, p == null ? 0 : p.WorstRoomAirDemand);
                Csv(sb, p == null ? 0 : p.WorstLoopDemand);
                Csv(sb, p == null ? 0 : p.EnvironmentDominatedNodes);
                Csv(sb, g.WorstSubstepBlock);

                int[] caps = ThermalSolver.SubstepProfile.ProjectedCaps;
                for (int c = 0; c < caps.Length; c++)
                {
                    Csv(sb, p == null ? 0 : p.CapRequiredSubsteps[c]);
                }
                for (int c = 0; c < caps.Length; c++)
                {
                    if (c == caps.Length - 1) CsvLast(sb, p == null ? 0 : p.CapNodesFloored[c]);
                    else Csv(sb, p == null ? 0 : p.CapNodesFloored[c]);
                }
            }

            return sb.ToString();
        }

        private static void Csv(StringBuilder sb, string value)
        {
            TelemetryFormat.AppendCsv(sb, value);
        }

        private static void Csv(StringBuilder sb, long value)
        {
            TelemetryFormat.AppendCsv(sb, value);
        }

        private static void Csv(StringBuilder sb, double value)
        {
            TelemetryFormat.AppendCsv(sb, value);
        }

        private static void CsvLast(StringBuilder sb, double value)
        {
            TelemetryFormat.AppendCsvLast(sb, value);
        }

        private static void CsvLast(StringBuilder sb, long value)
        {
            TelemetryFormat.AppendCsvLast(sb, value);
        }

        private static void CsvLast(StringBuilder sb, string value)
        {
            TelemetryFormat.AppendCsvLast(sb, value);
        }

        private static string Truncate(string value, int length)
        {
            return TelemetryFormat.Truncate(value, length);
        }

        private static string FormatPeak(float value)
        {
            return TelemetryFormat.Peak(value);
        }
    }
}
