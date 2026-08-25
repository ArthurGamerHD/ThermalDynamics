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
        /// Most blocks the mass sweep visits in one tick, which makes it a rota rather than a pass.
        /// A grid under the cap is still swept whole every <see cref="MassSweepInterval"/> steps.
        /// See load-and-hitching.md, 5.
        /// </summary>
        private const int MassSweepCap = 4096;

        private int stepsSinceHottest;
        private int stepsSinceMassSweep;

        /// <summary>
        /// Intentionally empty: the engine fires every grid's ten-frame update on the same frame, so
        /// <see cref="ThermalGridScheduler"/> drives this mod from the session component instead.
        /// See load-and-hitching.md, 9.
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
            if (!PrepareTick(frameSeconds)) return;

            SolveTick();
            PublishTick();
        }

        /// <summary>
        /// The half of a tick that has to run on the game thread *before* the step: the world
        /// sample, which is a planet lookup and sometimes a raycast, and the switch and power state
        /// of every pump. Returns false when this grid has nothing to do.
        ///
        /// <para>
        /// **The split exists so a fleet can be stepped in parallel** — backlog.md
        /// `D19`, measured at 10.17× on 32 threads — and it is the boundary rather than the
        /// threading that matters: everything that touches the game happens here or in
        /// <see cref="PublishTick"/>, and <see cref="SolveTick"/> touches nothing outside its own
        /// grid. `ThermalGridScheduler` runs the three in order for one grid at a time, or all the
        /// prepares, then the solves together, then all the publishes.
        /// </para>
        /// </summary>
        public bool PrepareTick(float frameSeconds)
        {
            solveFailure = null;
            steppedThisFrame = 0;

            if (disabled || !started || Simulation == null) return false;

            this.frameSeconds = frameSeconds;

            // Everything below is guarded, because a null out of any of it reached the session and
            // took the game down with it once — a thermal mod should never be able to do that, and
            // an exception named here is worth more than a crash dump.
            try
            {
                RefreshDiagnosticsFlag();

                // A sample costs a planet lookup and sometimes a raycast, and is read once when a
                // step begins rather than on every frame the step spans.
                startingStep = Simulation.NeedsEnvironmentSample;
                pendingSample = startingStep ? TimedSample() : default(EnvironmentSample);

                // Pumps must know their switch and power state before the step that spends the
                // power, not after it.
                if (startingStep) PushHeatPumpState();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.PrepareTick", e);
                return false;
            }

            stepsBefore = Simulation.Scheduler.StepsRun;

            // Stats is null when telemetry is off, and when the grid record cap has been reached.
            timeSimulation = Telemetry.Enabled && Stats != null;
            if (timeSimulation)
            {
                SimulationWork work = Simulation.Work;
                topologyVisitsBefore = work.TopologyNodeVisits;
                exposureVisitsBefore = work.ExposureNodeVisits;
                roomCellsBefore = work.RoomCellsVisited;

                Stats.Profiler.InTick = true;
            }

            return true;
        }

        /// <summary>
        /// The half of a tick that touches nothing but this grid, and so may run on any thread.
        ///
        /// **Nothing in here may reach the game.** An exception is caught and held rather than
        /// raised, because a worker thread's stack is not a place the session can be told about
        /// anything; <see cref="PublishTick"/> reports it on the game thread.
        /// </summary>
        public void SolveTick()
        {
            if (Simulation == null) return;

            try
            {
                if (timeSimulation) Stats.SimulationTime.Begin();
                Simulation.Update(frameSeconds, pendingSample);
                if (timeSimulation) Stats.SimulationTime.End();

                steppedThisFrame = Simulation.Scheduler.StepsRun - stepsBefore;
            }
            catch (Exception e)
            {
                solveFailure = e;
            }
        }

        /// <summary>
        /// The half of a tick that has to run on the game thread *after* the step: overheat damage,
        /// threshold events, pump demand, the mass and pressure sweeps, and every telemetry total,
        /// which is a shared accumulator no worker may write to.
        /// </summary>
        public void PublishTick()
        {
            if (Simulation == null) return;

            if (solveFailure != null)
            {
                Telemetry.Exception("ThermalGrid.SolveTick", solveFailure);
                solveFailure = null;
                return;
            }

            try
            {
                PublishInternal();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.PublishTick", e);
            }
        }

        /// <summary>
        /// What the publish half does once a solve has landed: the steps are counted, the grid's
        /// own timings are handed to the frame's shared totals, and everything that reads the
        /// simulation's output runs.
        ///
        /// **The shared accumulators are written here and nowhere else.** `Telemetry.FrameCost` is
        /// one object for the whole session, so a worker adding to it while another worker does is
        /// a lost frame's accounting at best; the numbers are gathered per grid during the solve
        /// and added here, on the game thread, in the order the scheduler publishes.
        /// </summary>
        private void PublishInternal()
        {
            long stepped = steppedThisFrame;

            if (timeSimulation)
            {
                SimulationWork work = Simulation.Work;

                Stats.Profiler.InTick = false;
                Stats.NoteTick(Telemetry.FramesObserved);

                Telemetry.FrameCost.AddGrid(
                    Grid == null ? "(grid)" : Grid.DisplayName,
                    Stats.SimulationTime.LastMilliseconds,
                    blocks.Count);

                Telemetry.FrameCost.AddWork(
                    work.TopologyNodeVisits - topologyVisitsBefore,
                    work.ExposureNodeVisits - exposureVisitsBefore,
                    work.RoomCellsVisited - roomCellsBefore);
            }

            if (stepped <= 0) return;

            StepsRun += stepped;
            TimedAfterSteps((int)stepped);
        }

        /// <summary>Length of the frame being served, set by the scheduler before each tick.</summary>
        private float frameSeconds = ThermalGridScheduler.FrameSeconds;

        /// <summary>The world this grid's step was begun with, sampled on the game thread.</summary>
        private EnvironmentSample pendingSample;

        /// <summary>Whether the prepared tick begins a step, so the sample and the pumps are fresh.</summary>
        private bool startingStep;

        /// <summary>Steps the scheduler had run before this frame's solve, and what it added.</summary>
        private long stepsBefore;
        private long steppedThisFrame;

        /// <summary>Whatever the solve threw, held for the game thread to report.</summary>
        private Exception solveFailure;

        /// <summary>Work counters read before the solve, so the publish can charge the difference.</summary>
        private long topologyVisitsBefore;
        private long exposureVisitsBefore;
        private long roomCellsBefore;

        /// <summary>
        /// Whether this grid's stage timings are being collected this frame.
        ///
        /// `Stats` is null when telemetry is off and when the grid record cap has been reached, so
        /// this is settled once in the prepare and read by both halves — a solve that timed itself
        /// and a publish that did not would leave a stopwatch running across frames.
        /// </summary>
        private bool timeSimulation;

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

        /// <summary>
        /// The two stages the host drives around a step, timed. Both sit inside a grid's update and
        /// outside the simulation, so neither reached a row of the cost table until they did.
        /// </summary>
        private EnvironmentSample TimedSample()
        {
            GridProfiler profiler = Profiler;
            if (profiler == null) return Sample();

            profiler.EnvironmentSample.Begin();
            EnvironmentSample sample = Sample();
            profiler.EnvironmentSample.End();

            Telemetry.FrameCost.AddSample(profiler.EnvironmentSample.LastMilliseconds);
            return sample;
        }

        private void TimedAfterSteps(int steps)
        {
            GridProfiler profiler = Profiler;
            if (profiler == null)
            {
                AfterSteps(steps);
                return;
            }

            profiler.AfterStep.Begin();
            AfterSteps(steps);
            profiler.AfterStep.End();

            Telemetry.FrameCost.AddAfterStep(profiler.AfterStep.LastMilliseconds);
        }

        /// <summary>Stage timings for this grid, or null when telemetry is not collecting.</summary>
        private GridProfiler Profiler
        {
            get { return Telemetry.Enabled && Stats != null ? Stats.Profiler : null; }
        }

        /// <summary>
        /// Everything that reads the simulation's output, kept off the stepping path so observation
        /// cost is never charged to a step.
        /// </summary>
        private void AfterSteps(int steps)
        {
            GridProfiler profiler = Profiler;

            // Each part is timed separately when a profiler is attached. They are bounded by
            // different things — the damage list, the block count, the compartment count, the
            // external-cell count — so the total on its own names nothing.
            Begin(profiler == null ? null : profiler.Damage);
            ApplyOverheatDamage();
            RaiseThresholdCrossings();
            PublishHeatPumpDemand();
            End(profiler == null ? null : profiler.Damage);

            Begin(profiler == null ? null : profiler.MassSweep);
            SweepMass(steps);
            End(profiler == null ? null : profiler.MassSweep);

            stepsSinceMassSweep += steps;
            if (stepsSinceMassSweep >= MassSweepInterval)
            {
                stepsSinceMassSweep = 0;
                SweepRoomPressure();
            }

            // Skipped unless the room overlay is up or telemetry is on.
            Begin(profiler == null ? null : profiler.LostRooms);
            RefreshLostRooms();
            End(profiler == null ? null : profiler.LostRooms);

            stepsSinceHottest += steps;
            if (stepsSinceHottest >= HottestInterval)
            {
                stepsSinceHottest = 0;

                Begin(profiler == null ? null : profiler.Health);

                // Unconditional now, where it used to be skipped on a dedicated server with
                // telemetry off. It is answered from the index the step's write-back recorded, so
                // it is a bounds check and an array read, and the health check below needs it.
                HottestNode = Simulation.Solver.HottestNode();
                CheckHealth();

                End(profiler == null ? null : profiler.Health);
            }

            // Presentation, and client only: a dedicated server has nobody to show or play it to.
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                UpdateCues(steps, steps * Simulation.Settings.StepSeconds);
            }

            if (Telemetry.Enabled)
            {
                Begin(profiler == null ? null : profiler.Sampling);
                Telemetry.OnGridStepped(this, steps);
                ProfileEnvironment();
                End(profiler == null ? null : profiler.Sampling);
            }
        }

        private static void Begin(TimingStat stat)
        {
            if (stat != null) stat.Begin();
        }

        private static void End(TimingStat stat)
        {
            if (stat != null) stat.End();
        }

        /// <summary>
        /// The last health verdict reported for this grid, so a grid that has gone bad is reported
        /// on the transition rather than every four steps for the rest of the session.
        /// </summary>
        private TelemetryAnomalyKind lastHealth;

        /// <summary>
        /// Tests whether this grid has gone numerically bad, on every world, whether or not telemetry
        /// is collecting — three float tests every fourth step on figures the solver already summed.
        /// See telemetry.md, A grid that has gone numerically bad is also a fault.
        /// </summary>
        private void CheckHealth()
        {
            TelemetryAnomalyKind health = TelemetryAnomalies.ClassifyGrid(
                Simulation.EnvironmentWatts,
                Simulation.HeatGainWatts,
                HottestNode == null ? 0f : HottestNode.Temperature,
                Telemetry.ImplausibleTemperature);

            if (health == lastHealth) return;
            lastHealth = health;

            if (health == TelemetryAnomalyKind.None) return;

            Telemetry.GridFault(this, TelemetryAnomalies.GridName(health, Telemetry.ImplausibleTemperature));
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
            // Coolant pumps are pushed whether or not this grid has a heat pump. They shared a
            // method and therefore shared its early return, so a ring on a grid with no heat pump
            // never heard about its own switch.
            PushCoolantPumpState();

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
                device.PowerSetting = electrical.PowerSetting;
            }
        }

        /// <summary>
        /// Carries each coolant pump's switch and speed into the loop model. Walked through the loops
        /// rather than a registry of pumps, since a pump outside a ring drives nothing.
        /// </summary>
        private void PushCoolantPumpState()
        {
            IList<CoolantLoop> loops = Simulation.Solver.Loops;
            if (loops == null || loops.Count == 0) return;

            for (int i = 0; i < loops.Count; i++)
            {
                CoolantLoop loop = loops[i];
                IList<Core.CoolantPump> pumps = loop.Pumps;
                bool changed = false;

                for (int p = 0; p < pumps.Count; p++)
                {
                    Core.CoolantPump pump = pumps[p];
                    if (pump.Block == null) continue;

                    ThermalBlock bound = Get(pump.Block.Min);
                    ThermalCoolantPumpBlock control = bound == null ? null : bound.CoolantPump;

                    if (control == null)
                    {
                        // A pump the host cannot speak for keeps circulating. Losing the component
                        // is this mod's fault rather than the player's, and stopping their cooling
                        // over it would be the worse failure.
                        continue;
                    }

                    bool running = control.IsRunning;
                    float speed = control.Speed;
                    float available = control.PowerAvailable;
                    float rating = control.MaxPowerWatts;

                    if (pump.Enabled == running && pump.Speed == speed
                        && pump.PowerAvailable == available && pump.MaxPowerWatts == rating) continue;

                    pump.Enabled = running;
                    pump.Speed = speed;

                    // A pump the grid cannot feed circulates proportionally slower rather than
                    // stopping, so a ship losing its reactors loses its cooling gradually.
                    pump.PowerAvailable = available;
                    pump.MaxPowerWatts = rating;
                    changed = true;
                }

                // A pump's setting reaches the fluid through the ring's flow rate, which is
                // computed when a loop is built and cached from then on. Without this the switch
                // and the slider would move a number nothing reads — which is exactly the state
                // the switch was already in.
                if (changed) loop.RefreshFlow();
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
        /// Sets how full of air each room is, from the game's own answers: three vetoes, no
        /// requirement, and the fill level read per room from the gas system. Air vents are the
        /// fallback where that cannot be read. See thermal-model.md, Room air.
        /// </summary>
        private void SweepRoomPressure()
        {
            if (!Settings.Instance.EnableRoomAir) return;

            IList<RoomAirNode> air = Simulation.RoomAir;
            if (air.Count == 0) return;

            GridProfiler profiler = Stats == null ? null : Stats.Profiler;
            if (profiler != null) profiler.RoomPressure.Begin();

            try
            {
                if (Stats != null)
                {
                    Stats.RoomPressureSweeps++;
                    Stats.RoomPressureRoomVisits += air.Count;
                }

                bool worldPressurised = WorldPressurised();

                // Nothing in this world can hold air, so neither the gas system nor the vents can
                // change an answer. Asked once rather than per room, and it skips both walks.
                if (!worldPressurised)
                {
                    for (int i = 0; i < air.Count; i++)
                    {
                        Simulation.SetRoomPressure(air[i].Anchor, 0f);
                    }
                    return;
                }

                // The gas system first: it reports every one of the game's rooms, which are whole
                // connected volumes rather than this model's pieces of them.
                GasLevels.Clear();
                SealedByGame.Clear();
                bool anyUnanswered = false;

                for (int i = 0; i < air.Count; i++)
                {
                    float level = GameOxygenAt(air[i].Anchor);
                    bool sealedByGame = Grid.IsRoomAtPositionAirtight(air[i].Anchor);

                    GasLevels.Add(level);
                    SealedByGame.Add(sealedByGame);

                    // Both answers are taken here rather than one now and one in the loop below,
                    // so the sweep's cost in game calls is exactly two per room and countable.
                    if (RoomPressure.NeedsVentFallback(true, sealedByGame, level))
                    {
                        anyUnanswered = true;
                    }
                }

                if (Stats != null) Stats.RoomPressureGameQueries += air.Count * 2;

                // Only for rooms the gas system left unanswered *and* the game calls sealed. A
                // room the game does not seal is emptied whatever a vent reports, so reading the
                // vents for it would be a walk over the grid to produce a value that is discarded.
                if (anyUnanswered)
                {
                    // Every room starts unreported, so a room whose vent was removed empties
                    // rather than retaining that vent's last reading.
                    for (int i = 0; i < air.Count; i++)
                    {
                        VentLevels[air[i].RoomIndex] = RoomPressure.NotReported;
                    }

                    if (Stats != null) Stats.RoomPressureVentScans++;
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

                    float level = RoomPressure.Level(true, SealedByGame[i], reported);

                    // Set through the solver rather than written onto the field. Pressure decides
                    // whether a room has any links at all, so changing it is a structural change:
                    // the solver rebuilds the room's links to the blocks bounding it, seeds newly
                    // appearing air from the temperature of those walls, and recomputes the
                    // conductance totals the integrator sizes its substeps from. Writing the field
                    // directly would leave the room with air, no links, and whatever temperature
                    // the last rebuild left.
                    Simulation.SetRoomPressure(room.Anchor, level);
                }
            }
            finally
            {
                if (profiler != null) profiler.RoomPressure.End();
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
        /// Collects what each vent reports about the rooms it opens into, reporting to *every* room it
        /// touches: the game exposes no way to ask which one a vent serves, and face iteration order
        /// is not an answer. Each room is still tested for sealing before it is given anything.
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

                if (Stats != null) Stats.RoomPressureVentsWalked++;

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

        /// <summary>Whether the game calls each room airtight, in air node order.</summary>
        private readonly List<bool> SealedByGame = new List<bool>();

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
