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
    /// Per-tick driver: samples the world, advances the simulation, and applies the results back to
    /// the game.
    /// </summary>
    public partial class ThermalGrid
    {
        /// <summary>Simulation steps completed by this grid.</summary>
        public long StepsRun;

        /// <summary>Solver steps between refreshes of the hottest-block readout.</summary>
        private const int HottestInterval = 4;

        /// <summary>
        /// Solver steps a full mass sweep is spread over, for a grid small enough to fit under
        /// <see cref="MassSweepCap"/>. A larger grid takes proportionally longer.
        /// </summary>
        private const int MassSweepInterval = 8;

        /// <summary>
        /// Most blocks the mass sweep visits in one tick.
        ///
        /// Mass changes with build progress and damage, neither of which the game raises an event
        /// for, so the only way to detect a change is to poll. Without a cap that poll is a pass
        /// over every block on the grid inside one tick, querying the game for each block's mass.
        ///
        /// The cap makes it a rota. A grid under the cap is still swept completely every
        /// <see cref="MassSweepInterval"/> steps; a larger one takes proportionally longer, about a
        /// minute for a million blocks. A thermal mass that lags by that much is not observable; a
        /// stall every eight steps is.
        /// </summary>
        private const int MassSweepCap = 4096;

        private int stepsSinceHottest;
        private int stepsSinceMassSweep;

        /// <summary>
        /// Intentionally empty: the entity's ten-frame callback does not drive this mod.
        ///
        /// The engine calls every grid's ten-frame update on the same frame, so a world of two
        /// hundred grids would do all of its thermal work on one frame in ten. Measured on a
        /// 203-grid save, 1,392 of 13,915 frames did any work, averaging 117 ms each, with two in
        /// three exceeding a 60 fps frame budget — the same total cost that spreads to about twelve
        /// milliseconds a frame.
        ///
        /// Instead <see cref="ThermalGridScheduler"/> gives every grid a share of every frame from
        /// the session component, and each grid spreads its solver step across the frames of its
        /// simulation window.
        ///
        /// Requesting a per-frame entity callback is not a usable alternative: <c>MyCubeGrid</c>
        /// clears <c>EACH_FRAME</c> from its update flags whenever its scheduled-work queue empties,
        /// so a mod depending on that flag silently stops running.
        /// </summary>
        public override void UpdateBeforeSimulation10()
        {
        }

        /// <summary>
        /// This grid's share of one frame. Called by <see cref="ThermalGridScheduler"/> every frame;
        /// the fraction of a step that share represents comes from <c>Frequency</c> and
        /// <c>SimulationSpeed</c>.
        /// </summary>
        public void Tick(float frameSeconds)
        {
            if (disabled || !started) return;

            this.frameSeconds = frameSeconds;

            // Stats is null when telemetry is off, and when the grid record cap has been reached.
            if (Telemetry.Enabled && Stats != null)
            {
                SimulationWork work = Simulation.Work;
                long topologyBefore = work.TopologyNodeVisits;
                long exposureBefore = work.ExposureNodeVisits;
                long cellsBefore = work.RoomCellsVisited;

                Stats.SimulationTime.Begin();
                UpdateInternal();
                Stats.SimulationTime.End();
                Stats.NoteTick(Telemetry.FramesObserved);

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

        /// <summary>Length of the frame being served, set by the scheduler before each tick.</summary>
        private float frameSeconds = ThermalGridScheduler.FrameSeconds;

        /// <summary>
        /// Whether per-mechanism watt figures are collected. True only when something reads them:
        /// the telemetry report, a client with the crosshair readout on, or a debug overlay built
        /// from one of them. False on a dedicated server in ordinary play.
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

                // A sample costs a planet lookup and sometimes a raycast, and is read once when a
                // step begins rather than on every frame the step spans.
                bool starting = Simulation.NeedsEnvironmentSample;
                EnvironmentSample sample = starting ? Sample() : default(EnvironmentSample);

                // Pumps must know their switch and power state before the step that spends the
                // power, not after it.
                if (starting) PushHeatPumpState();

                long before = Simulation.Scheduler.StepsRun;
                Simulation.Update(frameSeconds, sample);
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
        /// Everything that reads the simulation's output, kept off the stepping path so observation
        /// cost is never charged to a step.
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

            // Skipped unless the room overlay is up or telemetry is on.
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
        /// Refreshes the hottest-block figure, which feeds the HUD and the report only. Skipped on a
        /// dedicated server with telemetry off.
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

            // Damage is server authoritative: clients run the same simulation but never apply it.
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
        /// Refreshes a slice of the grid's block masses, resuming where the last tick stopped.
        ///
        /// The slice is sized to complete a full pass in <see cref="MassSweepInterval"/> steps,
        /// capped at <see cref="MassSweepCap"/> so no tick exceeds its share however large the grid.
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
        /// Supplies each heat pump with its switch state and available power before the step.
        ///
        /// Decides nothing about the pump's output: the solver derives what it can move from the
        /// two temperatures either side of it and returns a power demand.
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
                    // Without a resource sink there is no way to charge for the work, so the pump
                    // does not run.
                    device.Enabled = false;
                    device.PowerAvailable = 0f;
                    continue;
                }

                device.Enabled = electrical.IsRunning;
                device.PowerAvailable = electrical.PowerAvailable;
            }
        }

        /// <summary>
        /// Reports each pump's power demand to its resource sink.
        ///
        /// Reports what the pump requested rather than what it achieved: a pump that lowered its
        /// request after being refused would never recover when power returned.
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
        /// Sets how full of air each room is, from the game's own answers.
        ///
        /// Three conditions empty a room, none of them owned by this mod: the world may have oxygen
        /// or pressurisation disabled; the game's sealing test may disagree with this model's, since
        /// it knows the true shape of a sloped block where this knows only a cell, and it wins; or
        /// the room may hold no air.
        ///
        /// The fill level comes per room from the game's gas system through
        /// <see cref="ThermalGrid.GameOxygenAt"/>. The game's rooms are whole connected volumes
        /// rather than this model's pieces of them, so a cabin with no vent of its own but joined
        /// through a doorway to one that has is reported full.
        ///
        /// Air vents are the fallback where the gas system cannot be read. That path can only see
        /// compartments a vent physically touches.
        /// </summary>
        private void SweepRoomPressure()
        {
            if (!Settings.Instance.EnableRoomAir) return;

            IList<RoomAirNode> air = Simulation.RoomAir;
            if (air.Count == 0) return;

            bool worldPressurised = WorldPressurised();

            // The gas system first: it reports every one of the game's rooms, which are whole
            // connected volumes rather than this model's pieces of them.
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

            // Only for rooms the gas system left unanswered: a world where it cannot be read, or a
            // compartment the game holds no room for.
            if (anyUnanswered)
            {
                // Every room starts unreported, so a room whose vent was removed empties rather
                // than retaining that vent's last reading.
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

                // Set through the solver rather than written onto the field. Pressure decides
                // whether a room has any links at all, so changing it is a structural change: the
                // solver rebuilds the room's links to the blocks bounding it, seeds newly appearing
                // air from the temperature of those walls, and recomputes the conductance totals the
                // integrator sizes its substeps from. Writing the field directly would leave the
                // room with air, no links, and whatever temperature the last rebuild left.
                Simulation.SetRoomPressure(room.Anchor, level);
            }
        }

        /// <summary>
        /// Whether this world models pressurisation. Requires both the oxygen and pressurisation
        /// switches, since the game has no state with one and not the other.
        /// </summary>
        private static bool WorldPressurised()
        {
            if (MyAPIGateway.Session == null) return false;

            MyObjectBuilder_SessionSettings settings = MyAPIGateway.Session.SessionSettings;
            if (settings == null) return false;

            return settings.EnableOxygen && settings.EnableOxygenPressurization;
        }

        /// <summary>
        /// Collects what each vent reports about the rooms it opens into, keyed by room index.
        ///
        /// A vent sits in a wall, so its room is on whichever side has one, and a vent set to
        /// depressurise empties its room whatever level it currently reads.
        ///
        /// Reports to every room the vent touches. The game exposes no way to ask which room a vent
        /// actually serves, so a vent in a bulkhead between two compartments would otherwise supply
        /// one of them and not the other, decided by face iteration order. Over-reporting is
        /// bounded: each room is still tested with <c>IsRoomAtPositionAirtight</c> before it is
        /// given anything, so the worst case is air in a compartment the game also calls sealed and
        /// that has a working vent against it.
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

                        // The lowest reading wins where two vents share a room, so one of them
                        // depressurising empties it.
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

        /// <summary>Vent readings by room index. Reused so a sweep allocates nothing.</summary>
        private readonly Dictionary<int, float> VentLevels = new Dictionary<int, float>();

        /// <summary>Gas system readings per room, in air node order.</summary>
        private readonly List<float> GasLevels = new List<float>();

        /// <summary>
        /// Delivers threshold crossings to registered subscribers. Callbacks belong to other mods,
        /// so each is isolated: a subscriber that throws is logged and dropped rather than failing
        /// the grid's update.
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
