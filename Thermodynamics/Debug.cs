using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The crosshair readout: everything the simulation knows about the block being looked at.
    ///
    /// Client only, and behind <see cref="Settings.DebugTextOnScreen"/>. Reads state that already
    /// exists on the node, so enabling it costs a raycast and string building and changes nothing
    /// about the simulation.
    /// </summary>
    public static class Debug
    {
        public static void ShowDebugInfo()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (Settings.Instance == null || !Settings.Instance.DebugTextOnScreen) return;

            Crosshair.Target target;
            if (!Crosshair.Resolve(out target)) return;

            ThermalGrid thermals = target.Thermals;
            Vector3I cell = target.Cell;
            ThermalNode node = target.Block.Node;
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
                " links: " + solver.LinkCount +
                " loops: " + solver.Loops.Count +
                " rooms: " + simulation.Rooms.Map.RoomCount +
                " mapper queue: " + simulation.Rooms.PendingCells +
                " substeps: " + solver.LastSubsteps + (solver.LastStepWasClamped ? " (clamped)" : "") +
                " steps: " + thermals.StepsRun, 1, "White");

            MyAPIGateway.Utilities.ShowNotification(
                "[Surface] " + CellSurface.Describe(simulation.Surfaces.GetState(cell)), 1, "White");

            // How the room mapper classified this cell, and the space on the other side of each of
            // its faces. A hull block reading external is a cell the flood fill walked into, which is
            // what causes a sealed room to map as no room.
            RoomMap map = simulation.Rooms.Map;
            MyAPIGateway.Utilities.ShowNotification(
                "[Room] cell: " + Classify(map, cell) +
                " sealed by state: " + (node.Block.IsSealedByDoorState ? "yes" : "no (door open)") +
                " neighbours: " + Neighbours(map, simulation.Surfaces, cell), 1,
                map.IsExternal(cell) ? "Red" : "White");

            MyAPIGateway.Utilities.ShowNotification(
                "[Faces] " + Exposure(simulation, node.Block), 1, "White");

            MyAPIGateway.Utilities.ShowNotification("[Aero] " + Aero(solver, node, state), 1, "White");
        }

        /// <summary>
        /// This block's share of the aerodynamic force: its friction watts, the one blended
        /// surface normal the shape term reconstructed for it, and the pressure push those two
        /// make together, grid-local along `−n̂`.
        ///
        /// <para>
        /// **Per node and per normal, not per face — because that is the design.** The drag
        /// weighting is per face (the windward projection the [Faces] line describes), but lift is
        /// taken from one reconstructed normal per block, so a per-face lift figure does not exist
        /// to display. A zero normal names its own reason: the shape term off, or its budgeted
        /// pass not yet at this block.
        /// </para>
        /// </summary>
        private static string Aero(ThermalSolver solver, ThermalNode node, EnvironmentState state)
        {
            Vector3 normal = solver.NodeShapeNormal(node.Index);
            float watts = node.LastFrictionWatts;

            string text = "friction: " + watts.ToString("n1") + "W"
                + "  wind(local): " + Compact(state.WindDirectionLocal * state.WindSpeed) + "m/s";

            if (normal == Vector3.Zero)
            {
                return text + "  normal: none (shape term off, or its pass has not reached this block)";
            }

            // The node's contribution to LastPressureWatts: its friction watts pointed inward
            // along the reconstructed normal. What LiftForce keeps of the grid's sum is the part
            // of these, summed, that lies across the flow.
            Vector3 push = watts > 0f ? -normal * watts : Vector3.Zero;
            return text + "  normal: " + Compact(normal) + "  pressure push: " + Compact(push) + "W";
        }

        private static string Compact(Vector3 v)
        {
            return "(" + v.X.ToString("n1") + " " + v.Y.ToString("n1") + " " + v.Z.ToString("n1") + ")";
        }

        /// <summary>
        /// Why each of this block's faces counts as exposed, or does not.
        ///
        /// The model carries only a total, from which the three rules that can reject a cell face are
        /// indistinguishable. Each is named here.
        /// </summary>
        private static string Exposure(ThermalSimulation simulation, BlockInstance block)
        {
            SurfaceAudit.Explain(simulation.Surfaces, block, simulation.Rooms.Map, ExposureScratch);

            string text = "";
            for (int face = 0; face < Face.Count; face++)
            {
                FaceExposure result = ExposureScratch[face];
                if (result.Cells == 0) continue;

                text += Face.Name(face) + " " + result.Exposed + "/" + result.Cells;

                if (result.Sealed > 0) text += " sealed:" + result.Sealed;
                if (result.Mounted > 0) text += " bolted:" + result.Mounted;
                if (result.Interior > 0) text += " indoors:" + result.Interior;

                text += "  ";
            }

            return text;
        }

        /// <summary>Reused so the readout allocates one string rather than an array per frame.</summary>
        private static readonly FaceExposure[] ExposureScratch = new FaceExposure[Face.Count];

        /// <summary>How the room map classifies one cell.</summary>
        private static string Classify(RoomMap map, Vector3I cell)
        {
            if (map.IsSolid(cell)) return "structure";

            int room = map.RoomIndexOf(cell);
            if (room >= 0) return "room " + room;

            return "external";
        }

        /// <summary>
        /// The six neighbours, each as face name, classification, and whether the face between them
        /// seals. An unsealed face out of a hull block identifies a leak.
        /// </summary>
        private static string Neighbours(RoomMap map, SurfaceMap surfaces, Vector3I cell)
        {
            string text = "";
            for (int face = 0; face < Face.Count; face++)
            {
                if (text.Length > 0) text += " ";
                text += Face.Name(face).Substring(0, 1) + ":" +
                    (surfaces.IsFaceSealed(cell, face) ? "|" : "o") +
                    Classify(map, cell + Face.Offsets[face]).Substring(0, 1);
            }
            return text;
        }
    }
}
