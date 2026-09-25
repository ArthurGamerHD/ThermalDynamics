using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    public partial class ThermalGrid
    {
        public class LostRoom
        {
            public int Index;

            public Vector3I Anchor;
            public int CellCount;

            public float Volume;

            public HashSet<Vector3I> Cells;

            public string Vents;

            public bool VentSaysPressurised;

            public float OxygenLevel = -1f;

            public int LeakCount;

            public string LeakingBlocks;
        }

        public struct RoomVerdict
        {
            public Vector3I Anchor;

            public int CellCount;

            public bool GameAirtight;

            public string Vents;

            public bool VentPressurised;

            public float VentOxygen;

            public float GameOxygen;

            public bool HasAir;

            public bool Vented;

            public bool IsDisagreement
            {
                get { return RoomPressure.Disagrees(HasAir, Vented, GameOxygen); }
            }
        }

        public IList<RoomVerdict> RoomVerdicts
        {
            get { return roomVerdicts; }
        }

        private readonly List<RoomVerdict> roomVerdicts = new List<RoomVerdict>();

        public IList<LostRoom> LostRooms
        {
            get { return lostRooms; }
        }

        private readonly List<LostRoom> lostRooms = new List<LostRoom>();

        public bool LostRoomScanTruncated { get; private set; }

        public bool HasLostRoomScan { get; private set; }

        private readonly List<UnmappedRooms.Region> regionScratch = new List<UnmappedRooms.Region>();
        private Func<Vector3I, bool> airtightProbe;

        private readonly RescanGate lostRoomScan = new RescanGate(LostRoomScanInterval);

        private const int LostRoomScanInterval = 240;

        public void RefreshLostRooms()
        {
            if (!WantsLostRooms())
            {
                if (lostRooms.Count > 0) lostRooms.Clear();
                lostRoomScan.Idle();
                return;
            }

            if (!lostRoomScan.Due(RoomMapRevision, 1)) return;

            ScanLostRooms();
        }

        private int RoomMapRevision
        {
            get { return Simulation == null ? 0 : Simulation.Rooms.CompletedPasses; }
        }

        private static bool WantsLostRooms()
        {
            if (Telemetry.Enabled) return true;
            return ThermalDebugView.Current == ThermalDebugView.Mode.Rooms;
        }

        public void ScanLostRooms()
        {
            lostRoomScan.Mark(RoomMapRevision);

            lostRooms.Clear();
            roomVerdicts.Clear();
            LostRoomScanTruncated = false;

            if (Simulation == null || Grid == null || Grid.Closed) return;

            RoomMap map = Simulation.Rooms.Map;
            if (map == null || map.IsEmpty) return;

            ScanRoomVerdicts(map);

            if (airtightProbe == null) airtightProbe = IsAirtightByGame;

            try
            {
                LostRoomScanTruncated = !UnmappedRooms.Find(
                    map, Simulation.Surfaces, airtightProbe, regionScratch);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.ScanLostRooms", e);
                return;
            }

            float cellVolume = Grid.GridSize * Grid.GridSize * Grid.GridSize;

            for (int i = 0; i < regionScratch.Count; i++)
            {
                UnmappedRooms.Region region = regionScratch[i];

                LostRoom lost = new LostRoom();
                lost.Index = i;
                lost.Anchor = region.Anchor;
                lost.CellCount = region.CellCount;
                lost.Volume = region.CellCount * cellVolume;
                lost.Cells = region.Cells;
                lost.LeakCount = region.Leaks.Count;

                DescribeLeaks(region, lost);
                DescribeVents(lost);

                lostRooms.Add(lost);
            }

            HasLostRoomScan = true;
        }

        private void ScanRoomVerdicts(RoomMap map)
        {
            IList<RoomAirNode> air = Simulation.RoomAir;

            for (int i = 0; i < map.RoomCount; i++)
            {
                HashSet<Vector3I> cells = AsSet(map.CellsOf(i));

                RoomVerdict verdict = new RoomVerdict();
                verdict.Vented = map.IsVented(i);
                verdict.CellCount = cells.Count;
                verdict.Anchor = LowestCell(cells);
                verdict.GameAirtight = IsAirtightByGame(verdict.Anchor);
                verdict.GameOxygen = GameOxygenIn(cells);

                for (int a = 0; a < air.Count; a++)
                {
                    if (air[a].RoomIndex != i) continue;
                    verdict.HasAir = air[a].HasAir;
                    break;
                }

                VentReading reading = ReadVentsOn(cells);
                verdict.Vents = reading.Names;
                verdict.VentPressurised = reading.Pressurised;
                verdict.VentOxygen = reading.Oxygen;

                roomVerdicts.Add(verdict);
            }
        }

        private readonly HashSet<Vector3I> roomCellScratch = new HashSet<Vector3I>(Vector3I.Comparer);

        private HashSet<Vector3I> AsSet(RoomMap.RoomCells cells)
        {
            roomCellScratch.Clear();
            for (int i = 0; i < cells.Count; i++) roomCellScratch.Add(cells[i]);
            return roomCellScratch;
        }

        private static Vector3I LowestCell(HashSet<Vector3I> cells)
        {
            bool first = true;
            Vector3I best = Vector3I.Zero;

            foreach (Vector3I cell in cells)
            {
                if (!first && !Less(cell, best)) continue;

                best = cell;
                first = false;
            }

            return best;
        }

        private static bool Less(Vector3I a, Vector3I b)
        {
            if (a.X != b.X) return a.X < b.X;
            if (a.Y != b.Y) return a.Y < b.Y;
            return a.Z < b.Z;
        }

        private struct VentReading
        {
            public string Names;
            public bool Pressurised;
            public float Oxygen;
        }

        private VentReading ReadVentsOn(HashSet<Vector3I> cells)
        {
            VentReading reading = new VentReading();
            reading.Oxygen = -1f;

            StringBuilder names = null;

            for (int i = 0; i < vents.Count; i++)
            {
                IMyAirVent vent = LiveVentOn(i, cells);
                if (vent == null) continue;

                try
                {
                    if (names == null) names = new StringBuilder();
                    else names.Append(" | ");
                    names.Append(vent.CustomName);

                    if (vent.CanPressurize) reading.Pressurised = true;

                    float level = vent.GetOxygenLevel();
                    if (level > reading.Oxygen) reading.Oxygen = level;
                }
                catch (Exception e)
                {
                    Telemetry.Exception("ThermalGrid.ReadVentsOn", e);
                }
            }

            reading.Names = names == null ? "" : names.ToString();
            return reading;
        }

        private float GameOxygenIn(HashSet<Vector3I> cells)
        {
            float best = -1f;

            foreach (Vector3I cell in cells)
            {
                float level = GameOxygenAt(cell);
                if (level > best) best = level;
            }

            return best >= 0f ? best : VentOxygenAround(cells);
        }

        public float GameOxygenAt(Vector3I cell)
        {
            if (gasSystemFailed) return -1f;

            try
            {
                IMyGridGasSystem gas = ((IMyCubeGrid)Grid).GasSystem;
                if (gas == null) return -1f;

                IMyOxygenRoom room = gas.GetOxygenRoomForCubeGridPosition(ref cell);
                if (room == null) return -1f;

                return room.OxygenLevel(Grid.GridSize);
            }
            catch (Exception e)
            {
                gasSystemFailed = true;
                Telemetry.Exception("ThermalGrid.GameOxygenAt", e);
                return -1f;
            }
        }

        private bool gasSystemFailed;

        private IMyAirVent LiveVentOn(int index, HashSet<Vector3I> cells)
        {
            ThermalBlock bound = vents[index];
            IMyAirVent vent = bound.Vent;
            if (vent == null || bound.Block.FatBlock == null || bound.Block.FatBlock.Closed) return null;
            if (!TouchesRegion(bound, cells)) return null;
            return vent;
        }

        private float VentOxygenAround(HashSet<Vector3I> cells)
        {
            float best = -1f;

            for (int i = 0; i < vents.Count; i++)
            {
                IMyAirVent vent = LiveVentOn(i, cells);
                if (vent == null) continue;

                try
                {
                    float level = vent.GetOxygenLevel();
                    if (level > best) best = level;
                }
                catch (Exception e)
                {
                    Telemetry.Exception("ThermalGrid.VentOxygenAround", e);
                }
            }

            return best;
        }

        private bool VentSaysPressurised(HashSet<Vector3I> cells)
        {
            for (int i = 0; i < vents.Count; i++)
            {
                IMyAirVent vent = LiveVentOn(i, cells);
                if (vent == null) continue;

                try
                {
                    if (vent.CanPressurize) return true;
                }
                catch (Exception e)
                {
                    Telemetry.Exception("ThermalGrid.VentSaysPressurised", e);
                }
            }

            return false;
        }

        private bool IsAirtightByGame(Vector3I cell)
        {
            return Grid.IsRoomAtPositionAirtight(cell);
        }

        private void DescribeLeaks(UnmappedRooms.Region region, LostRoom lost)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < region.Leaks.Count; i++)
            {
                UnmappedRooms.Leak leak = region.Leaks[i];

                BlockInstance block = Simulation.Grid.GetAtCell(leak.Neighbour)
                    ?? Simulation.Grid.GetAtCell(leak.Cell);

                string name = block == null ? "(open space)" : block.Name;

                int seen;
                counts.TryGetValue(name, out seen);
                counts[name] = seen + 1;
            }

            List<KeyValuePair<string, int>> ordered = new List<KeyValuePair<string, int>>(counts);
            ordered.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                if (a.Value != b.Value) return b.Value.CompareTo(a.Value);
                return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });

            StringBuilder blocks = new StringBuilder();
            for (int i = 0; i < ordered.Count && i < MaxLeakingBlocksReported; i++)
            {
                if (blocks.Length > 0) blocks.Append(" | ");
                blocks.Append(ordered[i].Key).Append(" x").Append(ordered[i].Value);
            }

            lost.LeakingBlocks = blocks.ToString();
        }

        private const int MaxLeakingBlocksReported = 6;

        private void DescribeVents(LostRoom lost)
        {
            VentReading reading = ReadVentsOn(lost.Cells);

            lost.Vents = reading.Names;
            lost.VentSaysPressurised = reading.Pressurised;
            lost.OxygenLevel = reading.Oxygen;
        }

        private static bool TouchesRegion(ThermalBlock bound, HashSet<Vector3I> cells)
        {
            Vector3I[] ventCells = bound.Instance.Cells;

            for (int c = 0; c < ventCells.Length; c++)
            {
                for (int face = 0; face < Face.Count; face++)
                {
                    if (cells.Contains(ventCells[c] + Face.Offsets[face])) return true;
                }
            }

            return false;
        }
    }
}
