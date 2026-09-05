using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// How much of a full room remap is rediscovery: after a single-block sealing change, the
    /// whole bounding box refloods, and this lab counts how many cells actually changed their
    /// classification against how many the pass visited.
    ///
    /// <para>
    /// This is the evaluation instrument for the change-local remap redesign
    /// (redesign.md). `D2`'s wait is structural — the box over a
    /// 4,096-cell tick budget — so no per-cell work can shorten it; what could is visiting fewer
    /// cells, and the upper bound of that win is exactly the rediscovery share measured here.
    /// **The criterion, fixed before the first run** (`E1`): a change-local remap earns a design
    /// row if the median changed-to-visited ratio across this panel is under one per cent.
    /// </para>
    ///
    /// <para>
    /// The comparison is exact, not statistical: every cell of the union box is classified in
    /// both maps — solid, external, or its room — with a room named by the smallest cell key it
    /// holds, so renumbering between passes cannot read as change. A mutation whose diff finds
    /// no changed cell at all throws, because a sealing change that changed nothing means the
    /// instrument is blind, not that the grid is stable (`E8`).
    /// </para>
    /// </summary>
    public static class RemapLocalityLab
    {
        public class Row
        {
            public string Mutation;
            public long BoxCells;
            public long CellsVisited;
            public long CellsChanged;
            public int RoomsBefore;
            public int RoomsAfter;
            public int SettleTicks;
        }

        /// <summary>
        /// One census hull, mutated one block at a time, with a full remap and an exact map diff
        /// after each mutation. Mutations are applied in sequence and each diff is against the
        /// map just before it, so every row is one change.
        /// </summary>
        public static List<Row> Run(int blocks, Action<string> log = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build("ship", blocks));
            ThermalSimulation simulation = new ThermalSimulation(Hulls.Uncapped(), builder.Grid);
            for (int i = 0; i < builder.Placed.Count; i++) simulation.Solver.AddBlock(builder.Placed[i], 293.15f);
            simulation.RebuildAll();

            List<Row> rows = new List<Row>();

            // Each pick names its rule (`M10`): the cheapest plausible change, the two kinds of
            // removal, and the one aimed at a room boundary — the case where the *right* answer
            // is a large change, so the panel holds both sides of the criterion.
            rows.Add(Measure(simulation, "skin add", log, sim =>
            {
                Vector3I cell = ExternalNeighbourOfAnExposedBlock(sim);
                sim.AddBlock(new BlockInstance(Catalog.LightArmor(), cell, BlockOrientation.Identity), 293.15f);
            }));

            rows.Add(Measure(simulation, "skin remove", log, sim =>
            {
                sim.RemoveBlock(ExposedBlock(sim, awayFromRooms: true));
            }));

            rows.Add(Measure(simulation, "buried remove", log, sim =>
            {
                sim.RemoveBlock(BuriedBlock(sim));
            }));

            rows.Add(Measure(simulation, "room boundary remove", log, sim =>
            {
                sim.RemoveBlock(RoomBoundaryBlock(sim));
            }));

            return rows;
        }

        private static Row Measure(ThermalSimulation simulation, string mutation,
            Action<string> log, Action<ThermalSimulation> change)
        {
            if (log != null) log(mutation);

            Snapshot before = Take(simulation);
            change(simulation);

            long visited = simulation.Work.RoomCellsVisited;
            simulation.Rooms.RequestRestart(simulation.Grid);
            int ticks = 0;
            while (simulation.Rooms.HasWorkPending)
            {
                simulation.Rooms.Step(4096);
                ticks++;
            }
            visited = simulation.Work.RoomCellsVisited - visited;

            Snapshot after = Take(simulation);

            Row row = new Row();
            row.Mutation = mutation;
            row.CellsVisited = visited;
            row.RoomsBefore = before.Rooms;
            row.RoomsAfter = after.Rooms;
            row.SettleTicks = ticks;
            Diff(before, after, row);

            if (row.CellsChanged == 0)
            {
                throw new InvalidOperationException(
                    mutation + ": a sealing change moved no cell's classification, so this instrument saw nothing (E8)");
            }

            return row;
        }

        // ---- classification snapshots ------------------------------------------------------

        private sealed class Snapshot
        {
            public Vector3I Min;
            public Vector3I MaxExclusive;
            public long[] Category;   // -2 solid, -1 external, otherwise the room's smallest cell key
            public int Rooms;
        }

        private static Snapshot Take(ThermalSimulation simulation)
        {
            RoomMap map = simulation.Rooms.Map;
            GridModel grid = simulation.Grid;

            Snapshot snapshot = new Snapshot();
            snapshot.Min = grid.Min - Vector3I.One;
            snapshot.MaxExclusive = grid.Max + new Vector3I(2, 2, 2);
            snapshot.Rooms = map.RoomCount;

            long sizeX = snapshot.MaxExclusive.X - snapshot.Min.X;
            long sizeY = snapshot.MaxExclusive.Y - snapshot.Min.Y;
            long sizeZ = snapshot.MaxExclusive.Z - snapshot.Min.Z;
            snapshot.Category = new long[sizeX * sizeY * sizeZ];

            // A room is named by the smallest cell key it holds, so a pass that renumbers the
            // same partition reads as no change.
            long[] canonical = new long[map.RoomCount];
            for (int r = 0; r < map.RoomCount; r++)
            {
                long smallest = long.MaxValue;
                RoomMap.RoomCells cells = map.CellsOf(r);
                for (int i = 0; i < cells.Count; i++)
                {
                    long key = GridMath.Key(cells[i]);
                    if (key < smallest) smallest = key;
                }
                canonical[r] = smallest;
            }

            long at = 0;
            for (int z = snapshot.Min.Z; z < snapshot.MaxExclusive.Z; z++)
            for (int y = snapshot.Min.Y; y < snapshot.MaxExclusive.Y; y++)
            for (int x = snapshot.Min.X; x < snapshot.MaxExclusive.X; x++, at++)
            {
                Vector3I cell = new Vector3I(x, y, z);
                if (map.IsSolid(cell))
                {
                    snapshot.Category[at] = -2;
                    continue;
                }
                int room = map.RoomIndexOf(cell);
                snapshot.Category[at] = room >= 0 ? canonical[room] : -1;
            }

            return snapshot;
        }

        /// <summary>
        /// Counts cells whose classification differs, over the union of the two boxes. A cell
        /// outside a snapshot's box is external by definition, which is also what the map answers.
        /// </summary>
        private static void Diff(Snapshot before, Snapshot after, Row row)
        {
            Vector3I min = Vector3I.Min(before.Min, after.Min);
            Vector3I maxExclusive = Vector3I.Max(before.MaxExclusive, after.MaxExclusive);

            long changed = 0;
            long boxCells = 0;

            for (int z = min.Z; z < maxExclusive.Z; z++)
            for (int y = min.Y; y < maxExclusive.Y; y++)
            for (int x = min.X; x < maxExclusive.X; x++)
            {
                boxCells++;
                Vector3I cell = new Vector3I(x, y, z);
                if (At(before, cell) != At(after, cell)) changed++;
            }

            row.BoxCells = boxCells;
            row.CellsChanged = changed;
        }

        private static long At(Snapshot snapshot, Vector3I cell)
        {
            if (cell.X < snapshot.Min.X || cell.X >= snapshot.MaxExclusive.X
                || cell.Y < snapshot.Min.Y || cell.Y >= snapshot.MaxExclusive.Y
                || cell.Z < snapshot.Min.Z || cell.Z >= snapshot.MaxExclusive.Z)
            {
                return -1;
            }

            long sizeX = snapshot.MaxExclusive.X - snapshot.Min.X;
            long sizeY = snapshot.MaxExclusive.Y - snapshot.Min.Y;
            long index = (cell.X - snapshot.Min.X)
                + ((cell.Y - snapshot.Min.Y) * sizeX)
                + ((cell.Z - snapshot.Min.Z) * sizeX * sizeY);
            return snapshot.Category[index];
        }

        // ---- the mutation picks ------------------------------------------------------------

        private static Vector3I ExternalNeighbourOfAnExposedBlock(ThermalSimulation simulation)
        {
            RoomMap map = simulation.Rooms.Map;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces <= 0) continue;
                Vector3I[] cells = nodes[i].Block.Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        Vector3I outside = cells[c] + Face.Offsets[face];
                        if (simulation.Grid.IsOccupied(outside)) continue;
                        if (!map.IsExternal(outside)) continue;
                        if (map.RoomIndexOf(outside) >= 0) continue;
                        return outside;
                    }
                }
            }
            throw new InvalidOperationException("no exposed block with an external neighbour; the fixture is not a hull");
        }

        private static BlockInstance ExposedBlock(ThermalSimulation simulation, bool awayFromRooms)
        {
            RoomMap map = simulation.Rooms.Map;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces <= 0) continue;
                BlockInstance block = nodes[i].Block;
                if (block.Cells.Length != 1) continue;
                if (awayFromRooms && TouchesARoom(map, block)) continue;
                return block;
            }
            throw new InvalidOperationException("no exposed one-cell block found; the fixture is not a hull");
        }

        private static BlockInstance BuriedBlock(ThermalSimulation simulation)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TotalExposedFaces > 0) continue;
                if (nodes[i].Block.Cells.Length != 1) continue;
                return nodes[i].Block;
            }
            throw new InvalidOperationException("no buried one-cell block found; the fixture has no interior");
        }

        private static BlockInstance RoomBoundaryBlock(ThermalSimulation simulation)
        {
            RoomMap map = simulation.Rooms.Map;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                BlockInstance block = nodes[i].Block;
                if (block.Cells.Length != 1) continue;
                if (TouchesARoom(map, block)) return block;
            }
            throw new InvalidOperationException("no block bounds a room; the fixture has no compartments (E8)");
        }

        private static bool TouchesARoom(RoomMap map, BlockInstance block)
        {
            Vector3I[] cells = block.Cells;
            for (int c = 0; c < cells.Length; c++)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    if (map.RoomIndexOf(cells[c] + Face.Offsets[face]) >= 0) return true;
                }
            }
            return false;
        }

        // ---- reporting ---------------------------------------------------------------------

        public static string Report(int blocks, Action<string> log = null)
        {
            return Table(Run(blocks, log));
        }

        public static string Table(List<Row> rows)
        {
            StringBuilder text = new StringBuilder();
            text.Append("mutation".PadRight(24))
                .Append("box cells".PadLeft(12))
                .Append("visited".PadLeft(12))
                .Append("changed".PadLeft(10))
                .Append("changed/visited".PadLeft(17))
                .Append("rooms".PadLeft(10))
                .Append("ticks".PadLeft(7))
                .Append('\n');

            foreach (Row row in rows)
            {
                text.Append(row.Mutation.PadRight(24))
                    .Append(row.BoxCells.ToString("n0", CultureInfo.InvariantCulture).PadLeft(12))
                    .Append(row.CellsVisited.ToString("n0", CultureInfo.InvariantCulture).PadLeft(12))
                    .Append(row.CellsChanged.ToString("n0", CultureInfo.InvariantCulture).PadLeft(10))
                    .Append(((double)row.CellsChanged / Math.Max(1, row.CellsVisited)).ToString("p3", CultureInfo.InvariantCulture).PadLeft(17))
                    .Append((row.RoomsBefore + " -> " + row.RoomsAfter).PadLeft(10))
                    .Append(row.SettleTicks.ToString("n0", CultureInfo.InvariantCulture).PadLeft(7))
                    .Append('\n');
            }

            return text.ToString();
        }
    }
}
