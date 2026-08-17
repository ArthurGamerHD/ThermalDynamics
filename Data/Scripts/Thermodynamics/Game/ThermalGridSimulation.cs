using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Thermodynamics.Core;
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

        /// <summary>Solver steps between block mass refreshes for damaged or unfinished blocks.</summary>
        private const int MassSweepInterval = 8;

        private int stepsSinceHottest;
        private int stepsSinceMassSweep;

        public override void UpdateBeforeSimulation10()
        {
            if (disabled || !started) return;

            // Stats is null when telemetry is off, and also when the grid record cap was hit.
            if (Telemetry.Enabled && Stats != null)
            {
                Stats.SimulationTime.Begin();
                UpdateInternal();
                Stats.SimulationTime.End();
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

            stepsSinceMassSweep += steps;
            if (stepsSinceMassSweep >= MassSweepInterval)
            {
                stepsSinceMassSweep = 0;
                SweepMass();
                SweepRoomPressure();
            }

            stepsSinceHottest += steps;
            if (stepsSinceHottest >= HottestInterval)
            {
                stepsSinceHottest = 0;
                if (NeedsReadouts()) HottestNode = Simulation.Solver.HottestNode();
            }

            if (Telemetry.Enabled) Telemetry.OnGridStepped(this, steps);
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
        /// Mass changes with build progress and damage. The game raises no event a mod can hook
        /// for either, so the alternative to a slow sweep is reading every block every step.
        /// </summary>
        private void SweepMass()
        {
            foreach (ThermalBlock bound in blocks.Values)
            {
                bound.RefreshMass();
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
        /// Tells the simulation how full of air its rooms are.
        ///
        /// Pressurisation is the game's model, not a thermal one, and the only place a mod can
        /// read it is an air vent — which reports the room it is in. So a room is pressurised as
        /// far as this mod is concerned when a vent in it says so, and holds no air otherwise.
        /// Grids with no vents skip the sweep entirely.
        /// </summary>
        private void SweepRoomPressure()
        {
            if (!Settings.Instance.EnableRoomAir || vents.Count == 0) return;

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

                // The vent sits in a wall; the room is on whichever side of it has one.
                Vector3I[] cells = bound.Instance.Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    bool applied = false;
                    for (int face = 0; face < Face.Count; face++)
                    {
                        if (Simulation.SetRoomPressure(cells[c] + Face.Offsets[face], level))
                        {
                            applied = true;
                            break;
                        }
                    }
                    if (applied) break;
                }
            }
        }

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
