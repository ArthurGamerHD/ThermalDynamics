using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What the SE2 lattice costs each structure of the model, measured: one dealt hull, the same
    /// blocks at every rung, re-expressed on a lattice one, two, five and ten times finer per
    /// axis by <see cref="Se2Refine"/> — ten being SE2's 25 cm under SE1's 2.5 m.
    ///
    /// <para>
    /// **The claim under test is that cells multiply and nodes do not.** A structure priced per
    /// node or per link — the solver's arrays, the conduction graph — should hold flat across the
    /// rungs; a structure priced per cell or per bounding volume — the block table, the surface
    /// map, the room flood — should climb by the factor squared or cubed. The lab measures where
    /// each stage actually lands, which is the shopping list for a port: whatever climbs must be
    /// re-keyed or re-priced before SE2, and whatever holds flat ports as it is.
    /// </para>
    ///
    /// <para>
    /// The stage rows are <see cref="StageLab"/> rows under its own settling protocol, so the
    /// figures carry the same evidence `bench stages` carries. The repeat floor is lowered here —
    /// the effects under test are multiples, not percents, and a `place` repeat at factor ten is
    /// nine million cell inserts.
    /// </para>
    /// </summary>
    public static class Se2LatticeLab
    {
        /// <summary>The rungs a default run measures. Ten is the SE2 factor.</summary>
        public static readonly int[] DefaultFactors = { 1, 2, 5, 10 };

        /// <summary>The stages worth pricing across lattices, in a grid's build order.</summary>
        public static readonly string[] Stages = { "place", "surfaces", "links", "rooms", "exposure", "solver" };

        public class StageRow
        {
            public int Factor;
            public StageLab.Row Row;
        }

        /// <summary>The counts that say which axis each structure actually scales on.</summary>
        public class Summary
        {
            public int Factor;
            public int Blocks;
            public long Cells;
            public long BoxCells;
            public int SurfaceCells;
            public int Links;
            public int Rooms;
            public long RoomCells;
            public long ExternalCells;

            /// <summary>Managed bytes the built grid, surfaces, rooms and solver hold together.</summary>
            public long RetainedBytes;
        }

        public static void Run(string shape, int blocks, int[] factors,
            List<Summary> summaries, List<StageRow> stageRows, Action<string> log)
        {
            GridBuilder source = GridBuilder.Large();
            source.PlaceCensus(LoadShapes.Build(shape, blocks));

            for (int f = 0; f < factors.Length; f++)
            {
                int factor = factors[f];
                if (log != null) log("factor " + factor + ", refining "
                    + source.Placed.Count.ToString("n0") + " blocks");

                GridBuilder fine = Se2Refine.Refined(source, factor);
                summaries.Add(Summarise(factor, fine));

                for (int s = 0; s < Stages.Length; s++)
                {
                    if (log != null) log("factor " + factor + ", " + Stages[s]);
                    StageLab.Settle();

                    StageRow row = new StageRow();
                    row.Factor = factor;
                    row.Row = StageLab.Measure(Stages[s], fine);
                    StageLab.Summarise(row.Row);
                    stageRows.Add(row);
                }
            }
        }

        private static Summary Summarise(int factor, GridBuilder fine)
        {
            StageLab.Settle();
            long before = GC.GetTotalMemory(true);

            ThermalSimulation simulation = StageLab.Registered(fine);
            simulation.Surfaces.Rebuild(simulation.Grid);
            simulation.Solver.RebuildLinks();
            simulation.Rooms.RequestRestart(simulation.Grid);
            if (!simulation.Rooms.RunToCompletion())
            {
                throw new InvalidOperationException(
                    "the room pass did not finish at factor " + factor);
            }

            long after = GC.GetTotalMemory(true);

            Summary summary = new Summary();
            summary.Factor = factor;
            summary.Blocks = simulation.Grid.BlockCount;
            summary.SurfaceCells = simulation.Surfaces.CellCount;
            summary.Links = simulation.Solver.LinkCount;
            summary.RetainedBytes = after - before;

            long cells = 0;
            for (int i = 0; i < fine.Placed.Count; i++) cells += fine.Placed[i].CellCount;
            summary.Cells = cells;

            VRageMath.Vector3I span = simulation.Grid.Max + VRageMath.Vector3I.One
                - simulation.Grid.Min + new VRageMath.Vector3I(2, 2, 2);
            summary.BoxCells = (long)span.X * span.Y * span.Z;

            RoomMap map = simulation.Rooms.Map;
            summary.Rooms = map.RoomCount;
            summary.RoomCells = map.RoomCellCount;
            summary.ExternalCells = map.ExternalCellCount;

            GC.KeepAlive(simulation);
            return summary;
        }

        public static string SummaryTable(IList<Summary> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  factor    blocks         cells      box cells   surface cells       links   rooms     room cells    retained MB");
            for (int i = 0; i < rows.Count; i++)
            {
                Summary row = rows[i];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6} {1,9:n0}  {2,12:n0}  {3,13:n0}  {4,14:n0}  {5,10:n0}  {6,6:n0}  {7,13:n0}  {8,13:n1}",
                    row.Factor, row.Blocks, row.Cells, row.BoxCells, row.SurfaceCells,
                    row.Links, row.Rooms, row.RoomCells, row.RetainedBytes / (1024d * 1024d)));
            }
            return text.ToString();
        }

        public static string StageTable(IList<StageRow> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("  factor  stage        blocks       best ms    median ms      worst ms  repeats  stopped          work  unit              ns/unit      alloc KB");
            for (int i = 0; i < rows.Count; i++)
            {
                StageLab.Row row = rows[i].Row;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6}  {1,-10} {2,9:n0}  {3,12:n3}  {4,11:n3}  {5,12:n3}  {6,7:n0}  {7,-9}  {8,12:n0}  {9,-16}  {10,8:n1}  {11,12:n0}",
                    rows[i].Factor, row.Stage, row.Blocks, row.BestMs, row.MedianMs, row.WorstMs,
                    row.Repeats, row.Stop, row.Work, row.WorkUnit, row.NsPerWork,
                    row.AllocatedBytes / 1024));
            }
            return text.ToString();
        }

        public static string SummaryCsv(IList<Summary> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("factor,blocks,cells,box_cells,surface_cells,links,rooms,room_cells,external_cells,retained_bytes,taken_utc,host");
            for (int i = 0; i < rows.Count; i++)
            {
                Summary row = rows[i];
                text.AppendLine(string.Join(",",
                    row.Factor.ToString(CultureInfo.InvariantCulture),
                    row.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.Cells.ToString(CultureInfo.InvariantCulture),
                    row.BoxCells.ToString(CultureInfo.InvariantCulture),
                    row.SurfaceCells.ToString(CultureInfo.InvariantCulture),
                    row.Links.ToString(CultureInfo.InvariantCulture),
                    row.Rooms.ToString(CultureInfo.InvariantCulture),
                    row.RoomCells.ToString(CultureInfo.InvariantCulture),
                    row.ExternalCells.ToString(CultureInfo.InvariantCulture),
                    row.RetainedBytes.ToString(CultureInfo.InvariantCulture),
                    StageLab.TakenUtc,
                    StageLab.Host));
            }
            return text.ToString();
        }

        public static string StageCsv(IList<StageRow> rows)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("factor,stage,blocks,best_ms,median_ms,worst_ms,repeats,stopped,work,work_unit,ns_per_unit,allocated_bytes,taken_utc,host");
            for (int i = 0; i < rows.Count; i++)
            {
                StageLab.Row row = rows[i].Row;
                text.AppendLine(string.Join(",",
                    rows[i].Factor.ToString(CultureInfo.InvariantCulture),
                    row.Stage,
                    row.Blocks.ToString(CultureInfo.InvariantCulture),
                    row.BestMs.ToString("r", CultureInfo.InvariantCulture),
                    row.MedianMs.ToString("r", CultureInfo.InvariantCulture),
                    row.WorstMs.ToString("r", CultureInfo.InvariantCulture),
                    row.Repeats.ToString(CultureInfo.InvariantCulture),
                    row.Stop,
                    row.Work.ToString(CultureInfo.InvariantCulture),
                    row.WorkUnit,
                    row.NsPerWork.ToString("r", CultureInfo.InvariantCulture),
                    row.AllocatedBytes.ToString(CultureInfo.InvariantCulture),
                    StageLab.TakenUtc,
                    StageLab.Host));
            }
            return text.ToString();
        }
    }
}
