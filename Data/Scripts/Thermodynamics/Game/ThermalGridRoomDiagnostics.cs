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
    /// Measures this model's room map against the game's own sealing test.
    ///
    /// The two decide sealing differently and are allowed to: this model reads each definition's
    /// pressurisation table cell by cell, and the game knows the real shape of a sloped block
    /// where this knows a cell. What is not allowed is losing a compartment silently, which is
    /// what happens today — the fill walks in from outside, no room is created, and because
    /// pressurisation is only ever asked about rooms this model already found, nothing notices
    /// that the game has the room sealed and a vent in it reporting full.
    ///
    /// So the disagreement is measured rather than assumed. Every cell the map calls external is
    /// offered to the game, and each connected group it calls airtight is a room the model lost.
    /// Each one is named by the air vent standing in it, because that is how a player reports it:
    /// not by coordinates, but by the vent whose terminal says pressurised while the overlay
    /// shows nothing.
    ///
    /// None of it runs unless the room overlay is up or telemetry is on, and it runs on its own
    /// slow cadence when it does.
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
            /// The air vents standing in or against this compartment, by terminal name, and what
            /// they say. This is the identity a player can actually use.
            /// </summary>
            public string Vents;

            /// <summary>True when a vent in it reports the game considers the room airtight.</summary>
            public bool VentSaysPressurised;

            /// <summary>Highest oxygen level any vent in it reports, or -1 when none did.</summary>
            public float OxygenLevel = -1f;

            /// <summary>Faces where this model lets air in and the game does not.</summary>
            public int LeakCount;

            /// <summary>The block subtypes across those faces, worst first.</summary>
            public string LeakingBlocks;
        }

        /// <summary>
        /// What the two models make of one room this map did find.
        ///
        /// The interesting row is a room this model found, gave no air, and the game calls
        /// airtight. That is not a missing room and not a working one, and until now it looked
        /// exactly like both: the overlay drew a dry room as a fully transparent box, so a
        /// compartment the model had detected and failed to fill was indistinguishable from one it
        /// had never seen.
        /// </summary>
        public struct RoomVerdict
        {
            /// <summary>Lexicographically smallest cell, and how big the room is.</summary>
            public Vector3I Anchor;

            public int CellCount;

            /// <summary>The game's own answer at the room's anchor cell.</summary>
            public bool GameAirtight;

            /// <summary>The air vents opening onto it, by terminal name.</summary>
            public string Vents;

            /// <summary>A vent on it reports the game considers its room pressurised.</summary>
            public bool VentPressurised;

            /// <summary>The highest level any vent on it reports, or -1 when none did.</summary>
            public float VentOxygen;

            /// <summary>
            /// How much oxygen the game has in this room, 0..1, or -1 when it could not be asked.
            ///
            /// This is the figure that decides whether a dry room is a fault, and getting that
            /// wrong is what the first version of this did. <see cref="GameAirtight"/> and
            /// <see cref="VentPressurised"/> both mean <em>sealed</em> — neither means <em>full</em>.
            /// A cupboard nobody ever piped air into, on a ship in vacuum, is airtight and empty,
            /// and both models are right about it.
            /// </summary>
            public float GameOxygen;

            /// <summary>This model is running air in it.</summary>
            public bool HasAir;

            /// <summary>Standing open to the sky through a door, so having no air is correct.</summary>
            public bool Vented;

            /// <summary>
            /// The game has air in this room and this model does not.
            ///
            /// Oxygen, not airtightness. A sealed empty room is not a disagreement however sealed
            /// it is, and flagging one is worse than useless: it paints eight correct compartments
            /// as faults and buries the one that is not.
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

        /// <summary>True when the last scan gave up on the cell limit rather than finishing.</summary>
        public bool LostRoomScanTruncated { get; private set; }

        /// <summary>Whether a scan has ever completed, so a reader can tell zero from unmeasured.</summary>
        public bool HasLostRoomScan { get; private set; }

        private readonly List<UnmappedRooms.Region> regionScratch = new List<UnmappedRooms.Region>();
        private Func<Vector3I, bool> airtightProbe;
        private int stepsSinceLostRoomScan = int.MaxValue;

        /// <summary>
        /// Steps between scans. The scan costs one call into the game per external cell, so it is
        /// deliberately rare: a compartment that has been lost stays lost until somebody rebuilds
        /// the wall, and a reading a minute old is as good as a fresh one.
        /// </summary>
        private const int LostRoomScanInterval = 240;

        /// <summary>
        /// Rescans if anything is reading and enough steps have passed. Called from the grid's
        /// update; one bool test and one integer compare when nothing is reading.
        /// </summary>
        public void RefreshLostRooms()
        {
            if (!WantsLostRooms())
            {
                if (lostRooms.Count > 0) lostRooms.Clear();
                stepsSinceLostRoomScan = int.MaxValue;
                return;
            }

            if (stepsSinceLostRoomScan < LostRoomScanInterval)
            {
                stepsSinceLostRoomScan++;
                return;
            }

            stepsSinceLostRoomScan = 0;
            ScanLostRooms();
        }

        /// <summary>
        /// Whether anything is looking. The room overlay and the telemetry report are the only
        /// two readers, and neither is on in ordinary play.
        /// </summary>
        private static bool WantsLostRooms()
        {
            if (Telemetry.Enabled) return true;
            return ThermalDebugView.Current == ThermalDebugView.Mode.Rooms;
        }

        /// <summary>
        /// Runs the comparison and republishes <see cref="LostRooms"/>.
        ///
        /// Public so the telemetry report can force one at dump time rather than reporting
        /// whatever the last cadence happened to leave behind — a dump written seconds after a
        /// wall was welded should describe the ship as it is.
        /// </summary>
        public void ScanLostRooms()
        {
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
        /// Asks the game about each room this model already found.
        ///
        /// One call per room rather than per cell, so this is the cheap half of the scan — twelve
        /// calls on the ship that prompted it — and it is the half that catches a room being found
        /// and then not filled, which is a different failure from a room not being found at all
        /// and had no diagnostic of its own.
        /// </summary>
        private void ScanRoomVerdicts(RoomMap map)
        {
            IList<HashSet<Vector3I>> rooms = map.Rooms;
            IList<RoomAirNode> air = Simulation.RoomAir;

            for (int i = 0; i < rooms.Count; i++)
            {
                HashSet<Vector3I> cells = rooms[i];

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

        /// <summary>What the vents standing on one compartment say about it.</summary>
        private struct VentReading
        {
            public string Names;
            public bool Pressurised;
            public float Oxygen;
        }

        /// <summary>
        /// Walks the vents once and answers everything anything wants to know about them.
        ///
        /// Once, rather than once per question. The names, the pressurised flag and the oxygen
        /// level had become three separate walks over the same list, asking the same blocks the
        /// same things, in two different files.
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
        /// How much oxygen the game has in a room, 0..1, or -1 when it cannot be asked.
        ///
        /// Straight from the game's own gas system, which knows its rooms by its own sealing test
        /// and does not care what this model made of the same cells. That independence is the
        /// point: a vent can only answer for the room it is in, so a compartment with no vent —
        /// most of them — had no measurement at all, and the first version of this diagnostic
        /// filled that gap by assuming sealed meant full. It does not.
        /// </summary>
        private float GameOxygenIn(HashSet<Vector3I> cells)
        {
            float best = -1f;

            foreach (Vector3I cell in cells)
            {
                float level = GameOxygenAt(cell);
                if (level > best) best = level;
            }

            // Nothing in the gas system answered for any cell. A vent standing on the compartment
            // is the only other thing that can, and it can only answer for its own room.
            return best >= 0f ? best : VentOxygenAround(cells);
        }

        /// <summary>
        /// The game's oxygen level in whatever room it has at this cell, 0..1, or -1 when it has
        /// none there or cannot be asked.
        ///
        /// One call, and the one the pressurisation sweep runs per room. The game's rooms are its
        /// own — coarser than this model's, because its sealing test is finer than a cell — so a
        /// compartment this model has split into six pieces gets the same answer for all six,
        /// which is the correct answer and the one a vent could never have given.
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
                // Once, not every room every sweep. If the gas system cannot be read at all, the
                // vents are the fallback and there is no point paying for the failure repeatedly.
                gasSystemFailed = true;
                Telemetry.Exception("ThermalGrid.GameOxygenAt", e);
                return -1f;
            }
        }

        /// <summary>Set when the gas system throws, so the fallback is taken without retrying.</summary>
        private bool gasSystemFailed;

        /// <summary>The highest oxygen level any vent on these cells reports, or -1 when none does.</summary>
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

        /// <summary>Whether any vent opening onto these cells calls its own room pressurised.</summary>
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

        /// <summary>The game's own answer at one cell.</summary>
        private bool IsAirtightByGame(Vector3I cell)
        {
            return Grid.IsRoomAtPositionAirtight(cell);
        }

        /// <summary>
        /// Which block subtypes stand across the faces this model leaves open, worst first.
        ///
        /// This is the list the fix is made from. A compartment losing forty faces to one subtype
        /// names that definition's surface bits as the thing to correct, where the cell
        /// coordinates alone would only say that something, somewhere, does not seal.
        /// </summary>
        private void DescribeLeaks(UnmappedRooms.Region region, LostRoom lost)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();

            for (int i = 0; i < region.Leaks.Count; i++)
            {
                UnmappedRooms.Leak leak = region.Leaks[i];

                // Whatever should have sealed is one of the two blocks either side of the face.
                // The neighbour is named first because it is the one standing in the way; where
                // there is nothing there, the region's own cell holds the block that failed.
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
        /// The vents standing in this compartment, and what the game tells them about it.
        ///
        /// <c>IsPressurized</c> is the game's verdict on the vent's own room, which is precisely
        /// the answer this model never gets to hear for a compartment it did not find. It drives
        /// nothing — pressurisation still comes from the map, deliberately — but it is what turns
        /// "the overlay shows nothing" into "the game holds this room and we do not".
        /// </summary>
        private void DescribeVents(LostRoom lost)
        {
            VentReading reading = ReadVentsOn(lost.Cells);

            lost.Vents = reading.Names;
            lost.VentSaysPressurised = reading.Pressurised;
            lost.OxygenLevel = reading.Oxygen;
        }

        /// <summary>Whether any face of any of the vent's cells opens onto the region.</summary>
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
