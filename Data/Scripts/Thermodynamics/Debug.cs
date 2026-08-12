using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The crosshair readout: everything the simulation knows about the block being looked at.
    ///
    /// Client only, and behind <see cref="Settings.DebugTextOnScreen"/>. It reads state that
    /// already exists on the node, so switching it on costs a raycast and some string building
    /// and changes nothing about the simulation.
    /// </summary>
    public static class Debug
    {
        public static void ShowDebugInfo()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (Settings.Instance == null || !Settings.Instance.DebugTextOnScreen) return;

            MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;

            Vector3D start = matrix.Translation;
            Vector3D end = start + (matrix.Forward * 15);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(start, end, out hit);
            MyCubeGrid grid = hit == null ? null : hit.HitEntity as MyCubeGrid;
            if (grid == null) return;

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) return;

            Vector3I cell = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));
            ThermalBlock bound = thermals.GetAtCell(cell);
            if (bound == null || bound.Node == null) return;

            ThermalNode node = bound.Node;
            BlockThermalProperties thermal = node.Thermal;
            ThermalSimulation simulation = thermals.Simulation;

            MyAPIGateway.Utilities.ShowNotification(
                "[Block] " + node.Block.Name + " " + node.Block.Position +
                " T: " + node.Temperature.ToString("n3") +
                " dT: " + node.LastDeltaTemperature.ToString("n4") +
                " exposed: " + node.TotalExposedFaces +
                " (" + node.ExposedArea.ToString("n1") + " m2)", 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                "[Watts] conduction: " + node.LastConductionWatts.ToString("n1") +
                " radiation: " + node.LastRadiationWatts.ToString("n1") +
                " convection: " + node.LastConvectionWatts.ToString("n1") +
                " solar: " + node.LastSolarWatts.ToString("n1") +
                " friction: " + node.LastFrictionWatts.ToString("n1") +
                " generated: " + node.HeatGenerationWatts.ToString("n1"), 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                "[Block calc] mass: " + node.Block.Mass.ToString("n0") +
                " thermal mass: " + node.ThermalMass.ToString("n0") +
                " k: " + thermal.Conductivity.ToString("n2") +
                " sh: " + thermal.SpecificHeat.ToString("n2") +
                " em: " + thermal.Emissivity.ToString("n3") +
                " produced: " + node.Block.PowerProducedWatts.ToString("n0") + "W" +
                " consumed: " + (node.Block.PowerConsumedWatts + node.Block.ThrustWatts).ToString("n0") + "W", 1, "White");

            EnvironmentState state = thermals.LastState;
            MyAPIGateway.Utilities.ShowNotification(
                "[Env] ambient: " + state.AmbientTemperature.ToString("n1") + "K" +
                " air: " + state.AirDensity.ToString("n3") +
                " atmos: " + state.AtmosphereFactor.ToString("n3") +
                " wind: " + state.WindSpeed.ToString("n1") + "m/s" +
                " solar: " + state.SolarEnergy.ToString("n0") + "W/m2" +
                (state.IsSolarOccluded ? " (occluded)" : ""), 1, "White");

            ThermalSolver solver = simulation.Solver;
            MyAPIGateway.Utilities.ShowNotification(
                "[Grid] nodes: " + solver.Nodes.Count +
                " links: " + solver.Links.Count +
                " loops: " + solver.Loops.Count +
                " rooms: " + simulation.Rooms.Map.RoomCount +
                " mapper queue: " + simulation.Rooms.PendingCells +
                " substeps: " + solver.LastSubsteps + (solver.LastStepWasClamped ? " (clamped)" : "") +
                " steps: " + thermals.StepsRun, 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                "[Surface] " + CellSurface.Describe(simulation.Surfaces.GetState(cell)), 1, "White");
        }
    }
}
