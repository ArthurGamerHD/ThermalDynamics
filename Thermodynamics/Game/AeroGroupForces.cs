using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static class AeroGroupForces
    {
        public static bool Anchored(List<IMyCubeGrid> grids)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i] != null && grids[i].IsStatic) return true;
            }
            return false;
        }

        public static void Sum(List<IMyCubeGrid> grids,
            out Vector3D weightedCentre, out float mass, out Vector3 drag, out Vector3 lift)
        {
            weightedCentre = Vector3D.Zero;
            mass = 0f;
            drag = Vector3.Zero;
            lift = Vector3.Zero;

            for (int i = 0; i < grids.Count; i++)
            {
                IMyCubeGrid grid = grids[i];
                if (grid == null || grid.Physics == null || grid.GameLogic == null) continue;

                ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
                if (thermals == null || thermals.Simulation == null) continue;

                float watts = thermals.Simulation.FrictionWatts;
                if (watts > 0f)
                {
                    EnvironmentState state = thermals.LastState;
                    Vector3 localWind = state.WindDirectionLocal * state.WindSpeed;
                    Vector3 worldWind = Vector3.TransformNormal(localWind, grid.WorldMatrix);

                    drag += DragForce.Vector(watts, worldWind, thermals.Simulation.Settings);

                    Vector3 pressure = Vector3.TransformNormal(
                        thermals.Simulation.Solver.LastPressureWatts, grid.WorldMatrix);
                    lift += LiftForce.Vector(pressure, worldWind, thermals.Simulation.Settings);
                }

                float gridMass = grid.Physics.Mass;
                if (gridMass > 0f)
                {
                    weightedCentre += grid.Physics.CenterOfMassWorld * gridMass;
                    mass += gridMass;
                }
            }
        }
    }
}
