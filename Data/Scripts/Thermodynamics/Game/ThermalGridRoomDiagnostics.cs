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
    /// <summary>
    /// Compares this model's room map against the game's own sealing test, which is finer than a cell.
    /// Every cell the map calls external is offered to the game, and each connected group it calls
    /// airtight is a compartment this model lost silently. Runs only when the room overlay is up or
    /// telemetry is on. See thermal-model.md, Diagnostics.
    /// </summary>
    public partial class ThermalGrid
    {
        /// <summary>One compartment the game seals and this model does not, ready to be reported.</summary>
        public class LostRoom
        {
            /// <summary>Index in the published list. Stable while the grid's shape is.</summary>
            public int Index;

            public Vector3I Anchor;
            public int CellCount;

            /// <summary>Room volume, m^3, on the same basis as a mapped room's.</summary>
            public float Volume;

            /// <summary>The cells, for the overlay to draw.</summary>
            public HashSet<Vector3I> Cells;

            /// <summary>
            /// Air vents standing in or against this compartment, by terminal name, with their
            /// readings. The identity a player can quote.
            /// </summary>
            public string Vents;

            /// <summary>True when a vent in it reports the game considers the room airtight.</summary>
            public bool VentSaysPressurised;

            /// <summary>Highest oxygen level any vent in it reports, or -1 when none reported.</summary>
            public float OxygenLevel = -1f;

            /// <summary>Faces where this model lets air in and the game does not.</summary>
            public int LeakCount;

            /// <summary>Block subtypes across those faces, most frequent first.</summary>
            public string LeakingBlocks;
        }

        /// <summary>
        /// What both models make of one room this map did find.
        ///
        /// The diagnostic case is a room this model found, gave no air, and the game calls airtight:
        /// neither a missing room nor a working one, and indistinguishable from both in the overlay,
        /// which draws a dry room as a transparent box.
        /// </summary>
        public struct RoomVerdict
        {
            /// <summary>Lexicographically smallest cell, and the room's size.</summary>
            public Vector3I Anchor;

            public int CellCount;

            /// <summary>The game's airtightness verdict at the room's anchor cell.</summary>
            public bool GameAirtight;

            /// <summary>The air vents opening onto it, by terminal name.</summary>
            public string Vents;

            /// <summary>True when a vent on it reports the game considers its room pressurised.</summary>
            public bool VentPressurised;

            /// <summary>Highest oxygen level any vent on it reports, or -1 when none reported.</summary>
            public float VentOxygen;

            /// <summary>
            /// Oxygen the game has in this room, 0..1, or -1 when it could not be asked.
            ///
            /// This is what decides whether a dry room is a fault. <see cref="GameAirtight"/> and
            /// <see cref="VentPressurised"/> both mean sealed rather than full: an enclosed space
            /// that was never filled, on a grid in vacuum, is airtight and empty in both models.
            /// </summary>
            public float GameOxygen;

            /// <summary>True when this model is running air in it.</summary>
            public bool HasAir;

            /// <summary>True when it stands open to the sky through a door, so holding no air is correct.</summary>
            public bool Vented;

            /// <summary>
            /// True when the game has oxygen in this room and this model does not.
            ///
            /// Compares oxygen rather than airtightness: a sealed empty room is not a disagreement,
            /// and flagging one would bury the real cases among correct compartments.
            /// </summary>
            public bool IsDisagreement
            {
                get { return RoomPressure.Disagrees(HasAir, Vented, GameOxygen); }
            }
        }

        /// <summary>Per mapped room, by room index, as of the last scan.</summary>
        public IList<RoomVerdict> RoomVerdicts
        {
            get { return roomVerdicts; }
        }

        private readonly List<RoomVerdict> roomVerdicts = new List<RoomVerdict>();

        /// <summary>Compartments the game seals and this model lost, as of the last scan.</summary>
        public IList<LostRoom> LostRooms
        {
            get { return lostRooms; }
        }

        private readonly List<LostRoom> lostRooms = new List<LostRoom>();

        /// <summary>True when the last scan stopped at the cell limit rather than finishing.</summary>
        public bool LostRoomScanTruncated { get; private set; }

        /// <summary>Whether a scan has ever completed, distinguishing zero from unmeasured.</summary>
        public bool HasLostRoomScan { get; private set; }

        private readonly List<UnmappedRooms.Region> regionScratch = new List<UnmappedRooms.Region>();
        private Func<Vector3I, bool> airtightProbe;

        /// <summary>
        /// The cadence, and the room map revision the last scan answered for: a lost compartment is a
        /// property of the map, so until the mapper completes another pass there is nothing to find.
        /// </summary>
        private readonly RescanGate lostRoomScan = new RescanGate(LostRoomScanInterval);

        /// <summary>
        /// Steps between scans. A scan costs one call into the game per external cell, and a lost
        /// compartment stays lost until the grid is rebuilt, so the cadence is deliberately slow.
        /// </summary>
        private const int LostRoomScanInterval = 240;

        /// <summary>
        /// Rescans when something is reading and enough steps have passed. Called from the grid's
        /// update; costs one bool test and one integer compare when nothing is reading.
        /// </summary>
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

        /// <summary>
        /// What the scan's answer depends on: the room map, which changes only when the mapper
        /// completes a pass. Doors and block changes both reach it that way.
        /// </summary>
        private int RoomMapRevision
        {
            get { return Simulation == null ? 0 : Simulation.Rooms.CompletedPasses; }
        }

        /// <summary>
        /// Whether anything reads these results. The room overlay and the telemetry report are the
        /// only readers, neither of which is active in ordinary play.
        /// </summary>
        private static bool WantsLostRooms()
        {
            if (Telemetry.Enabled) return true;
            return ThermalDebugView.Current == ThermalDebugView.Mode.Rooms;
        }

        /// <summary>
        /// Runs the comparison and republishes <see cref="LostRooms"/>.
        ///
        /// Public so the telemetry report can force a scan at dump time rather than reporting the
        /// last cadence's result, which may predate a recent build change.
        /// </summary>
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

        /// <summary>
        /// Queries the game about each room this model already found.
        ///
        /// One call per room rather than per cell, so this is the cheap half of the scan. It catches
        /// a room that was found and then not filled, which is a distinct failure from a room never
        /// found at all.
        /// </summary>
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

        /// <summary>
        /// One room's cells as a set, for the vent tests below — the only caller that searches a room,
        /// so the set is built here and reused rather than every room being kept as one for the life
        /// of the grid. See memory.md, 4.
        /// </summary>
        private readonly HashSet<Vector3I> roomCellScratch = new HashSet<Vector3I>(Vector3I.Comparer);

        private HashSet<Vector3I> AsSet(RoomMap.RoomCells cells)
        {
            roomCellScratch.Clear();
            for (int i = 0; i < cells.Count; i++) roomCellScratch.Add(cells[i]);
            return roomCellScratch;
        }

        /// <summary>The lexicographically smallest cell, matching <see cref="RoomAirNode.Anchor"/>.</summary>
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

        /// <summary>What the vents standing on one compartment report about it.</summary>
        private struct VentReading
        {
            public string Names;
            public bool Pressurised;
            public float Oxygen;
        }

        /// <summary>
        /// Walks the vents once and returns every figure any caller needs: names, the pressurised
        /// flag and the oxygen level, which would otherwise be three separate walks over the same
        /// list.
        /// </summary>
        private VentReading ReadVentsOn(HashSet<Vector3I> cells)
        {
            VentReading reading = new VentReading();
            reading.Oxygen = -1f;

            StringBuilder names = null;

            for (int i = 0; i < vents.Count; i++)
            {
                ThermalBlock bound = vents[i];
                IMyAirVent vent = bound.Vent;
                if (vent == null || bound.Block.FatBlock == null || bound.Block.FatBlock.Closed) continue;
                if (!TouchesRegion(bound, cells)) continue;

                try
                {
                    if (names == null) names = new StringBuilder();
                    else names.Append(" | ");
                    names.Append(vent.CustomName);

                    if (vent.IsPressurized()) reading.Pressurised = true;

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

        /// <summary>
        /// Oxygen the game has in a room, 0..1, or -1 when it cannot be asked.
        ///
        /// Read from the game's gas system, which knows its rooms by its own sealing test
        /// independently of what this model made of the same cells. A vent can answer only for the
        /// room it stands in, so most compartments have no vent measurement available.
        /// </summary>
        private float GameOxygenIn(HashSet<Vector3I> cells)
        {
            float best = -1f;

            foreach (Vector3I cell in cells)
            {
                float level = GameOxygenAt(cell);
                if (level > best) best = level;
            }

            // The gas system answered for no cell. A vent standing on the compartment is the only
            // remaining source, and it can answer only for its own room.
            return best >= 0f ? best : VentOxygenAround(cells);
        }

        /// <summary>
        /// The game's oxygen level in whatever room it holds at this cell, 0..1, or -1 when it holds
        /// none there or cannot be asked. Its rooms are coarser than this model's, so a compartment
        /// split into several pieces here returns the same level for all of them.
        /// </summary>
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
                // Recorded once rather than retried per room per sweep: if the gas system cannot be
                // read at all, the vents are the fallback for the rest of the session.
                gasSystemFailed = true;
                Telemetry.Exception("ThermalGrid.GameOxygenAt", e);
                return -1f;
            }
        }

        /// <summary>Set when the gas system throws, so the fallback is taken without retrying.</summary>
        private bool gasSystemFailed;

        /// <summary>Highest oxygen level any vent on these cells reports, or -1 when none does.</summary>
        private float VentOxygenAround(HashSet<Vector3I> cells)
        {
            float best = -1f;

            for (int i = 0; i < vents.Count; i++)
            {
                ThermalBlock bound = vents[i];
                IMyAirVent vent = bound.Vent;
                if (vent == null || bound.Block.FatBlock == null || bound.Block.FatBlock.Closed) continue;
                if (!TouchesRegion(bound, cells)) continue;

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

        /// <summary>True when any vent opening onto these cells calls its own room pressurised.</summary>
        private bool VentSaysPressurised(HashSet<Vector3I> cells)
        {
            for (int i = 0; i < vents.Count; i++)
            {
                ThermalBlock bound = vents[i];
                IMyAirVent vent = bound.Vent;
                if (vent == null || bound.Block.FatBlock == null || bound.Block.FatBlock.Closed) continue;
                if (!TouchesRegion(bound, cells)) continue;

                try
                {
                    if (vent.IsPressurized()) return true;
                }
                catch (Exception e)
                {
                    Telemetry.Exception("ThermalGrid.VentSaysPressurised", e);
                }
            }

            return false;
        }

        /// <summary>The game's airtightness verdict at one cell.</summary>
        private bool IsAirtightByGame(Vector3I cell)
        {
            return Grid.IsRoomAtPositionAirtight(cell);
        }

        /// <summary>
        /// Block subtypes standing across the faces this model leaves open, most frequent first.
        ///
        /// Identifies which definition's surface bits to correct; cell coordinates alone would only
        /// establish that something does not seal.
        /// </summary>
        private void DescribeLeaks(UnmappedRooms.Region region, LostRoom lost)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < region.Leaks.Count; i++)
            {
                UnmappedRooms.Leak leak = region.Leaks[i];

                // Whatever should have sealed is one of the two blocks either side of the face. The
                // neighbour is named first as the block standing in the way; where the neighbour
                // cell is empty, the region's own cell holds the block that failed.
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

        /// <summary>
        /// The vents standing in this compartment, and what the game reports to them about it.
        ///
        /// <c>IsPressurized</c> is the game's verdict on the vent's own room, which is the answer
        /// this model cannot otherwise obtain for a compartment it did not find. Diagnostic only:
        /// pressurisation still comes from the map.
        /// </summary>
        private void DescribeVents(LostRoom lost)
        {
            VentReading reading = ReadVentsOn(lost.Cells);

            lost.Vents = reading.Names;
            lost.VentSaysPressurised = reading.Pressurised;
            lost.OxygenLevel = reading.Oxygen;
        }

        /// <summary>True when any face of any of the vent's cells opens onto the region.</summary>
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
