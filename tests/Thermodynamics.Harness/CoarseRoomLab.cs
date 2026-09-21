using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class CoarseRoomLab
    {
        public class Row
        {
            public int Edge;

            public StageLab.Row Timing;

            public string Mismatch;

            public long SupercellsTaken;
            public long FineCellsVisited;
            public long SealingBytes;
        }

/// <summary>Run operation.</summary>
        public static List<Row> Run(GridBuilder builder, int[] edges, Action<string> log)
        {
            ThermalSimulation simulation = StageLab.Registered(builder);
            simulation.Surfaces.Rebuild(simulation.Grid);

/// <summary>List operation.</summary>
            List<Row> rows = new List<Row>();

            if (log != null) log("shipped mapper, "
                + simulation.Grid.BlockCount.ToString("n0") + " blocks");
            StageLab.Settle();
            rows.Add(Baseline(simulation));

            RoomMap oracle = simulation.Rooms.Map;

            for (int e = 0; e < edges.Length; e++)
            {
                if (log != null) log("supercell edge " + edges[e]);
                StageLab.Settle();
                rows.Add(Prototype(simulation, oracle, edges[e]));
            }

            return rows;
        }

/// <summary>Baseline operation.</summary>
        private static Row Baseline(ThermalSimulation simulation)
        {
/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Edge = 1;

            StageLab.Row timing = new StageLab.Row();
            timing.Stage = "mapper";
            timing.Blocks = simulation.Grid.BlockCount;
            timing.BestMs = double.MaxValue;
            timing.WorkUnit = "cells visited";

            for (int r = 0; !StageLab.Settled(timing); r++)
            {
                long before = simulation.Work.RoomCellsVisited;
                long allocated = r == 1 ? StageLab.Allocated() : 0;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                simulation.Rooms.RequestRestart(simulation.Grid);
                if (!simulation.Rooms.RunToCompletion())
                {
                    throw new InvalidOperationException("the room pass did not finish");
                }
                watch.Stop();
                if (r == 1) timing.AllocatedBytes = StageLab.Allocated() - allocated;
                StageLab.Take(timing, watch.Elapsed.TotalMilliseconds);
                StageLab.Work(timing, simulation.Work.RoomCellsVisited - before, r);
            }

            StageLab.Summarise(timing);
            row.Timing = timing;
            return row;
        }

/// <summary>Prototype operation.</summary>
        private static Row Prototype(ThermalSimulation simulation, RoomMap oracle, int edge)
        {
/// <summary>Row operation.</summary>
            Row row = new Row();
            row.Edge = edge;

/// <summary>CoarseRoomFlood operation.</summary>
            CoarseRoomFlood flood = new CoarseRoomFlood(edge);
            flood.Run(simulation.Grid, simulation.Surfaces);
/// <summary>Verify operation.</summary>
            row.Mismatch = Verify(oracle, flood);
            row.SupercellsTaken = flood.SupercellsTaken;
            row.FineCellsVisited = flood.FineCellsVisited;
            row.SealingBytes = flood.SealingBytes;

            StageLab.Row timing = new StageLab.Row();
            timing.Stage = "super" + edge;
            timing.Blocks = simulation.Grid.BlockCount;
            timing.BestMs = double.MaxValue;
            timing.WorkUnit = "supers+cells";

            for (int r = 0; !StageLab.Settled(timing); r++)
            {
                long allocated = r == 1 ? StageLab.Allocated() : 0;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                flood.Run(simulation.Grid, simulation.Surfaces);
                watch.Stop();
                if (r == 1) timing.AllocatedBytes = StageLab.Allocated() - allocated;
                StageLab.Take(timing, watch.Elapsed.TotalMilliseconds);
                StageLab.Work(timing, flood.SupercellsTaken + flood.FineCellsVisited, r);
            }

            StageLab.Summarise(timing);
            row.Timing = timing;
            return row;
        }

/// <summary>Verify operation.</summary>
        public static string Verify(RoomMap oracle, CoarseRoomFlood flood)
        {
            if (oracle.ExternalCellCount != flood.ExternalCells)
            {
                return "external " + oracle.ExternalCellCount.ToString("n0")
                    + " cells against the prototype's " + flood.ExternalCells.ToString("n0");
            }

            if (oracle.RoomCount != flood.RoomCount)
            {
                return "rooms " + oracle.RoomCount + " against the prototype's " + flood.RoomCount;
            }

            Dictionary<int, int> claimed = new Dictionary<int, int>();
            for (int r = 0; r < oracle.RoomCount; r++)
            {
                RoomMap.RoomCells cells = oracle.CellsOf(r);
                if (cells.Count == 0) continue;

                int region = flood.RegionOf(cells[0]);
                if (region <= 0)
                {
                    return "room " + r + "'s cell " + cells[0] + " maps to region " + region;
                }

                int taken;
                if (claimed.TryGetValue(region, out taken))
                {
                    return "rooms " + taken + " and " + r + " both map to region " + region;
                }
                claimed[region] = r;

                if (flood.RegionCells[region] != cells.Count)
                {
                    return "room " + r + " holds " + cells.Count.ToString("n0")
                        + " cells and region " + region + " holds "
                        + flood.RegionCells[region].ToString("n0");
                }

                foreach (Vector3I cell in cells)
                {
                    if (flood.RegionOf(cell) != region)
                    {
                        return "room " + r + "'s cell " + cell + " maps to region "
                            + flood.RegionOf(cell) + " where its first cell mapped to " + region;
                    }
                }
            }

            return null;
        }

/// <summary>Table operation.</summary>
        public static string Table(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.AppendLine("  walk        blocks       best ms    median ms  repeats  stopped          work  unit             supers     fine cells    sealing KB  match");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-9} {1,9:n0}  {2,12:n3}  {3,11:n3}  {4,7:n0}  {5,-9}  {6,12:n0}  {7,-14}  {8,9:n0}  {9,13:n0}  {10,12:n0}  {11}",
                    row.Timing.Stage, row.Timing.Blocks, row.Timing.BestMs, row.Timing.MedianMs,
                    row.Timing.Repeats, row.Timing.Stop, row.Timing.Work, row.Timing.WorkUnit,
                    row.SupercellsTaken, row.FineCellsVisited, row.SealingBytes / 1024,
                    row.Mismatch ?? (row.Edge == 1 ? "oracle" : "yes")));
                if (row.Mismatch != null)
                {
                    text.AppendLine("            MISMATCH: " + row.Mismatch);
                }
            }
            return text.ToString();
        }

/// <summary>Csv operation.</summary>
        public static string Csv(IList<Row> rows)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();
            text.AppendLine("walk,edge,blocks,best_ms,median_ms,repeats,stopped,work,supercells,fine_cells,sealing_bytes,match,taken_utc,host");
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Timing.Stage,
                    row.Edge.ToString(CultureInfo.InvariantCulture),
                    row.Timing.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.Timing.BestMs.ToString("r", CultureInfo.InvariantCulture),
                    row.Timing.MedianMs.ToString("r", CultureInfo.InvariantCulture),
                    row.Timing.Repeats.ToString(CultureInfo.InvariantCulture),
                    row.Timing.Stop,
                    row.Timing.Work.ToString(CultureInfo.InvariantCulture),
                    row.SupercellsTaken.ToString(CultureInfo.InvariantCulture),
                    row.FineCellsVisited.ToString(CultureInfo.InvariantCulture),
                    row.SealingBytes.ToString(CultureInfo.InvariantCulture),
                    row.Mismatch == null ? "yes" : "no",
                    StageLab.TakenUtc,
                    StageLab.Host));
            }
            return text.ToString();
        }
    }
}
