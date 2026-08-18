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
            WriteGridTable(sb);
            WriteGridDetails(sb);
            WriteBlockTypes(sb);

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

            if (audit.Examples == null || audit.Examples.Count == 0) return;

            sb.Append("\n    block cells the map left outdoors\n");
            for (int i = 0; i < audit.Examples.Count; i++)
            {
                sb.Append("      ").Append(audit.Examples[i]).Append('\n');
            }
        }

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

            for (int i = 0; i < Telemetry.Grids.Count; i++)
            {
                GridTelemetry g = Telemetry.Grids[i];
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
                Field(sb, "  steps clamped", g.ClampedSteps.ToString("n0"));
                Field(sb, "  hottest block T", g.HottestBlockTemperature.Format("n1"));
                Field(sb, "  peak temperature", FormatPeak(g.PeakTemperature) + "  " + g.PeakTemperatureBlock);
                Field(sb, "  critical blocks", g.CriticalBlocks.Format("n0"));
                Field(sb, "  damage events", g.DamageEvents.ToString("n0"));
                Field(sb, "  total damage", g.TotalDamage.ToString("n1"));

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
            sb.Append("critical_updates,total_damage\n");

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
                CsvLast(sb, t.TotalDamage);
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

                if (row.SunElevationDegrees > 0f) day.Add(row.AmbientKelvin);
                else night.Add(row.AmbientKelvin);

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
                Field(sb, "    game temperature", game.Format("n3"));

                if (materials.Count == 0) return;

                StringBuilder ground = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in materials)
                {
                    if (ground.Length > 0) ground.Append(", ");
                    ground.Append(pair.Key).Append(' ').Append(pair.Value);
                }

                Field(sb, "    ground", ground.ToString());
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
            sb.Append("sun_elevation_deg,air_density,atmosphere_factor,ambient_k,ambient_c,underground,");
            sb.Append("solar_w,solar_occlusion,wind_speed,weather_intensity,game_temperature,");
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

                    Csv(sb, row.SolarEnergy);
                    Csv(sb, row.SolarOcclusion);
                    Csv(sb, row.WindSpeed);
                    Csv(sb, row.WeatherIntensity);
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
            sb.Append("solar_ms_total,solar_ms_max,save_ms_total,load_ms_total\n");

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
                CsvLast(sb, g.LoadTime.TotalMilliseconds);
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
