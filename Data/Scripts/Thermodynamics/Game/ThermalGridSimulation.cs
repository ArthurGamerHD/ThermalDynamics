using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
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
        /// the telemetry report, or a client with the crosshair readout switched on. A dedicated
        /// server in ordinary play writes none of them.
        /// </summary>
        private void RefreshDiagnosticsFlag()
        {
            bool wanted = Telemetry.Enabled
                || (Settings.Instance.DebugTextOnScreen
                    && (MyAPIGateway.Utilities == null || !MyAPIGateway.Utilities.IsDedicated));

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

            stepsSinceMassSweep += steps;
            if (stepsSinceMassSweep >= MassSweepInterval)
            {
                stepsSinceMassSweep = 0;
                SweepMass();
            }

            stepsSinceHottest += steps;
            if (stepsSinceHottest >= HottestInterval)
            {
                stepsSinceHottest = 0;
                if (NeedsReadouts()) HottestNode = Simulation.Solver.HottestNode();
            }

            if (Telemetry.Enabled) Telemetry.OnGridStepped(this, steps);

            DrawDebugColors();
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
    }
}
