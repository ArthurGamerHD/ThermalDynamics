using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The per-tick pump: sample the world, step the simulation, hand the results back to the
    /// game.
    /// </summary>
    public partial class ThermalGrid
    {
        /// <summary>Simulation steps completed by this grid.</summary>
        public long StepsRun;

        /// <summary>Solver steps between refreshes of the hottest-block readout.</summary>
        private const int HottestInterval = 4;

        /// <summary>
        /// Solver steps a full pass of the mass sweep is spread over on a grid small enough for
        /// that to be affordable. A large grid takes longer, because the slice is capped.
        /// </summary>
        private const int MassSweepInterval = 8;

        /// <summary>
        /// Blocks the mass sweep will look at in one tick, at most.
        ///
        /// Mass changes with build progress and damage, and the game raises no event a mod can
        /// hook for either, so the only way to notice is to look. Looking at every block on the
        /// grid is what it used to do, once every eight steps: free on a fighter and a pass over
        /// a million game blocks on a station, landing inside a single tick and asking the game
        /// for each block's mass on the way.
        ///
        /// A cap turns that into a rota. A grid under the cap is still swept completely every
        /// eight steps, exactly as before. A larger one takes proportionally longer to come
        /// round — a million blocks is about a minute — which is the right trade: a block's
        /// thermal mass being a minute out of date on a station that size is invisible, and a
        /// stall every eight steps is not.
        /// </summary>
        private const int MassSweepCap = 4096;

        private int stepsSinceHottest;
        private int stepsSinceMassSweep;

        public override void UpdateBeforeSimulation10()
        {
            if (disabled || !started) return;

            // Stats is null when telemetry is off, and also when the grid record cap was hit.
            if (Telemetry.Enabled && Stats != null)
            {
                SimulationWork work = Simulation.Work;
                long topologyBefore = work.TopologyNodeVisits;
                long exposureBefore = work.ExposureNodeVisits;
                long cellsBefore = work.RoomCellsVisited;

                Stats.SimulationTime.Begin();
                UpdateInternal();
                Stats.SimulationTime.End();

                Telemetry.FrameCost.AddGrid(
                    Grid == null ? "(grid)" : Grid.DisplayName,
                    Stats.SimulationTime.LastMilliseconds,
                    blocks.Count);

                Telemetry.FrameCost.AddWork(
                    work.TopologyNodeVisits - topologyBefore,
                    work.ExposureNodeVisits - exposureBefore,
                    work.RoomCellsVisited - cellsBefore);
                return;
            }

            UpdateInternal();
        }

        /// <summary>
        /// Per-mechanism watt figures are only produced for someone who is going to read them:
        /// the telemetry report, a client with the crosshair readout switched on, or the debug
        /// overlay showing a view built from one of them. A dedicated server in ordinary play
        /// writes none of them.
        /// </summary>
        private void RefreshDiagnosticsFlag()
        {
            bool client = MyAPIGateway.Utilities == null || !MyAPIGateway.Utilities.IsDedicated;

            bool wanted = Telemetry.Enabled
                || (client && Settings.Instance.DebugTextOnScreen)
                || (client && ThermalDebugView.NeedsWatts);

            Simulation.Solver.CollectDiagnostics = wanted;
        }

        private void UpdateInternal()
        {
            try
            {
                RefreshDiagnosticsFlag();
                bool stepping = Simulation.Scheduler.WouldStep(TickSeconds);

                // Building a sample means a planet lookup and, occasionally, a raycast. On a tick
                // that will not integrate, none of it would be read.
                EnvironmentSample sample = stepping ? Sample() : default(EnvironmentSample);

                // The pumps have to know whether they are switched on and powered before the step
                // that spends the power, not after it.
                if (stepping) PushHeatPumpState();

                long before = Simulation.Scheduler.StepsRun;
                Simulation.Update(TickSeconds, sample);
                long stepped = Simulation.Scheduler.StepsRun - before;

                if (stepped <= 0) return;

                StepsRun += stepped;
                AfterSteps((int)stepped);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.Update", e);
            }
        }

        /// <summary>
        /// Everything that reads the simulation's output. Kept off the stepping path so the cost
        /// of observing the model never lands inside it.
        /// </summary>
        private void AfterSteps(int steps)
        {
            ApplyOverheatDamage();
            RaiseThresholdCrossings();
            PublishHeatPumpDemand();

            SweepMass(steps);

            stepsSinceMassSweep += steps;
            if (stepsSinceMassSweep >= MassSweepInterval)
            {
                stepsSinceMassSweep = 0;
                SweepRoomPressure();
            }

            // Off unless the room overlay is up or telemetry is on; one bool test otherwise.
            RefreshLostRooms();

            stepsSinceHottest += steps;
            if (stepsSinceHottest >= HottestInterval)
            {
                stepsSinceHottest = 0;
                if (NeedsReadouts()) HottestNode = Simulation.Solver.HottestNode();
            }

            if (Telemetry.Enabled)
            {
                Telemetry.OnGridStepped(this, steps);
                ProfileEnvironment();
            }
        }

        /// <summary>
        /// The hottest-block figure feeds the HUD and the report and nothing else, so a dedicated
        /// server with telemetry off never pays for it.
        /// </summary>
        private bool NeedsReadouts()
        {
            return Telemetry.Enabled
                || MyAPIGateway.Utilities == null
                || !MyAPIGateway.Utilities.IsDedicated;
        }

        private void ApplyOverheatDamage()
        {
            IList<OverheatEvent> overheats = Simulation.Overheats;
            CriticalBlocks = overheats.Count;
            if (overheats.Count == 0) return;

            // Damage is server authoritative; clients run the same simulation but never apply it.
            if (!MyAPIGateway.Session.IsServer) return;

            for (int i = 0; i < overheats.Count; i++)
            {
                OverheatEvent overheat = overheats[i];

                ThermalBlock bound = Get(overheat.Block.Position);
                if (bound == null) continue;

                if (Telemetry.Enabled) Telemetry.OnCriticalDamage(bound, overheat.Damage);

                bound.Block.DoDamage(overheat.Damage, ThermalDamage, false);
            }
        }

        /// <summary>
        /// Refreshes a slice of the grid's blocks, resuming where the last tick stopped.
        ///
        /// The slice is the share of the grid that keeps a full pass to
        /// <see cref="MassSweepInterval"/> steps, capped at <see cref="MassSweepCap"/> so no tick
        /// pays more than its share however large the grid. Every other budgeted stage in the
        /// simulation works this way; this one was the last pass over the whole grid that did not.
        /// </summary>
        private void SweepMass(int steps)
        {
            int count = sweepOrder.Count;
            int share = SimulationScheduler.SweepSlice(count, steps, MassSweepInterval, MassSweepCap);
            if (share <= 0) return;

            if (massSweepCursor >= count) massSweepCursor = 0;

            for (int i = 0; i < share; i++)
            {
                if (massSweepCursor >= count) massSweepCursor = 0;
                sweepOrder[massSweepCursor++].RefreshMass();
            }
        }

        /// <summary>
        /// Hands each heat pump its switch and its power situation before the step.
        ///
        /// Nothing here decides anything: whether a pump can do what it is being asked is worked
        /// out by the solver from the two temperatures either side of it, and the answer comes
        /// back out as a power demand.
        /// </summary>
        private void PushHeatPumpState()
        {
            if (heatPumps.Count == 0) return;

            for (int i = 0; i < heatPumps.Count; i++)
            {
                ThermalBlock bound = heatPumps[i];
                HeatPumpDevice device = Simulation.GetHeatPump(bound.Instance);
                if (device == null) continue;

                ThermalHeatPumpBlock electrical = bound.HeatPump;
                if (electrical == null)
                {
                    // No electrical half means no way to charge for the work, so it does not run.
                    device.Enabled = false;
                    device.PowerAvailable = 0f;
                    continue;
                }

                device.Enabled = electrical.IsRunning;
                device.PowerAvailable = electrical.PowerAvailable;
            }
        }

        /// <summary>
        /// Tells each pump's resource sink what the simulation decided it wants to draw.
        ///
        /// What it asked for, not what it managed: a pump that lowers its request because its
        /// request went unmet would never climb back when the power returned.
        /// </summary>
        private void PublishHeatPumpDemand()
        {
            if (heatPumps.Count == 0) return;

            for (int i = 0; i < heatPumps.Count; i++)
            {
                ThermalBlock bound = heatPumps[i];
                if (bound.HeatPump == null) continue;

                HeatPumpDevice device = Simulation.GetHeatPump(bound.Instance);
                bound.HeatPump.SetDemandWatts(device == null ? 0f : device.LastDemandWatts);
            }
        }

        /// <summary>
        /// Tells the simulation how full of air its rooms are, from the game's own answers.
        ///
        /// Three things can empty a room and none of them belong to this mod. The world can have
        /// oxygen or pressurisation switched off, in which case nothing anywhere holds air. The
        /// game's sealing test can disagree with this model's — it knows the real shape of a sloped
        /// block where this knows a cell — and it wins. And the room can simply be empty.
        ///
        /// How full it is comes from the game's own gas system, per room, through
        /// <see cref="ThermalGrid.GameOxygenAt"/>. That is not what this used to do: it read the
        /// air vents and gave air only to compartments a vent physically touched, on the belief
        /// that "a vent is the only place a mod can read a room's oxygen level". It is not.
        /// <c>IMyCubeGrid.GasSystem</c> answers for every room the game has, and the game's rooms
        /// are the whole connected volume rather than this model's pieces of it — so a cabin with
        /// no vent of its own, joined through a doorway to one that has, is full, and this now
        /// says so. Measured on one ship: seven of twelve compartments held air in the game and
        /// none here, every one of them for want of a vent against that particular piece.
        ///
        /// The vents remain as the fallback for a world whose gas system cannot be read, where the
        /// old limit still applies — a compartment nobody ever piped air into is indistinguishable
        /// from one nobody can measure.
        /// </summary>
        private void SweepRoomPressure()
        {
            if (!Settings.Instance.EnableRoomAir) return;

            IList<RoomAirNode> air = Simulation.RoomAir;
            if (air.Count == 0) return;

            bool worldPressurised = WorldPressurised();

            // The game's own gas system first: it knows how full each of its rooms is, and its
            // rooms are the connected volumes rather than this model's pieces of them. A
            // compartment with no vent in it but joined through a doorway to one that has is full
            // in the game, and this is the only way to find that out.
            GasLevels.Clear();
            bool anyUnanswered = false;

            for (int i = 0; i < air.Count; i++)
            {
                float level = worldPressurised
                    ? GameOxygenAt(air[i].Anchor)
                    : RoomPressure.NotReported;

                GasLevels.Add(level);
                if (level < 0f) anyUnanswered = true;
            }

            // Only when something went unanswered — a world whose gas system cannot be read, or a
            // compartment the game has no room for. Reading nine vents to answer questions already
            // answered is exactly the kind of cost this mod is supposed not to pay.
            if (anyUnanswered)
            {
                // Everything starts at "nobody said", so a room whose vent was removed empties
                // rather than keeping the last figure that vent ever gave it.
                for (int i = 0; i < air.Count; i++)
                {
                    VentLevels[air[i].RoomIndex] = RoomPressure.NotReported;
                }

                ReadVents();
            }

            for (int i = 0; i < air.Count; i++)
            {
                RoomAirNode room = air[i];

                float reported = GasLevels[i];

                if (reported < 0f && !VentLevels.TryGetValue(room.RoomIndex, out reported))
                {
                    reported = RoomPressure.NotReported;
                }

                bool sealedByGame = worldPressurised && Grid.IsRoomAtPositionAirtight(room.Anchor);

                float level = RoomPressure.Level(worldPressurised, sealedByGame, reported);

                // Through the solver, not onto the field. Pressure is what decides whether a room
                // has any links at all, so setting it is a structural change: the solver rebuilds
                // the room's links to the blocks bounding it, seeds air appearing for the first
                // time from the temperature of those walls, and recomputes the conductance totals
                // the integrator sizes its substeps from.
                //
                // Writing the field and refreshing the mass by hand did none of those. It gave the
                // room a hundred and fifty kilograms of air, linked to nothing, at whatever
                // temperature the rebuild happened to leave behind — 2.7 K for a ship in vacuum.
                // Every test and the mod API went through the solver, so the mechanism was sound
                // everywhere except the one path that runs in the game.
                Simulation.SetRoomPressure(room.Anchor, level);
            }
        }

        /// <summary>
        /// Whether this world models pressurisation at all. Both switches matter: pressurisation
        /// without oxygen is not a state the game has.
        /// </summary>
        private static bool WorldPressurised()
        {
            if (MyAPIGateway.Session == null) return false;

            MyObjectBuilder_SessionSettings settings = MyAPIGateway.Session.SessionSettings;
            if (settings == null) return false;

            return settings.EnableOxygen && settings.EnableOxygenPressurization;
        }

        /// <summary>
        /// Collects what each vent says about the rooms it opens into, by room index.
        ///
        /// A vent sits in a wall, so the room is on whichever side of it has one — and a vent set
        /// to depressurise is emptying its room, whatever it currently reads.
        ///
        /// <b>Every</b> room it touches, which is the correction. This used to stop at the first
        /// room found on the first cell, so a vent in a bulkhead between two compartments gave one
        /// of them air and the other nothing — and which one it was came down to the order the six
        /// faces happen to be indexed in. Measured on a ship with two vents in the same bulkhead:
        /// an eight-cell space got the air, and the thirty-cell cabin the player was standing in,
        /// with both vents on it and the game reporting it sealed and 99% full, got zero.
        ///
        /// Reporting to all of them over-reports where a vent serves only one side, and the game
        /// exposes no way to ask which room a vent is actually on. It is bounded: every room is
        /// still tested against <c>IsRoomAtPositionAirtight</c> on its own before it is given
        /// anything, so what this can do is give air to a compartment the game also calls sealed
        /// and that has a working vent against it. Denying the main cabin its air was the worse
        /// mistake of the two.
        /// </summary>
        private void ReadVents()
        {
            for (int i = vents.Count - 1; i >= 0; i--)
            {
                ThermalBlock bound = vents[i];
                IMyAirVent vent = bound.Vent;

                if (vent == null || bound.Block.FatBlock == null || bound.Block.FatBlock.Closed)
                {
                    vents.RemoveAt(i);
                    continue;
                }

                float level = vent.Depressurize ? 0f : vent.GetOxygenLevel();

                Vector3I[] cells = bound.Instance.Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    for (int face = 0; face < Face.Count; face++)
                    {
                        int room = Simulation.Rooms.Map.RoomIndexOf(cells[c] + Face.Offsets[face]);
                        if (room < 0) continue;

                        // The lowest reading wins where two vents share a room: one of them
                        // emptying it is the fact that matters.
                        float existing;
                        if (!VentLevels.TryGetValue(room, out existing) || level < existing
                            || existing <= RoomPressure.NotReported)
                        {
                            VentLevels[room] = level;
                        }
                    }
                }
            }
        }

        /// <summary>Vent readings by room index, reused so a sweep allocates nothing.</summary>
        private readonly Dictionary<int, float> VentLevels = new Dictionary<int, float>();

        /// <summary>The gas system's answer per room, in the order the air nodes are held.</summary>
        private readonly List<float> GasLevels = new List<float>();

        /// <summary>
        /// Hands threshold crossings to whoever registered them. Callbacks belong to other mods,
        /// so each one is isolated: a subscriber that throws is reported and dropped rather than
        /// taking the grid's update with it.
        /// </summary>
        private void RaiseThresholdCrossings()
        {
            IList<ThresholdCrossing> crossings = Simulation.Crossings;
            if (crossings.Count == 0) return;

            for (int i = 0; i < crossings.Count; i++)
            {
                ThresholdCrossing crossing = crossings[i];
                ThermalBlock bound = Get(crossing.Block.Position);
                if (bound == null) continue;

                ThermalApi.RaiseThreshold(crossing, bound.Block);
            }
        }
    }
}
