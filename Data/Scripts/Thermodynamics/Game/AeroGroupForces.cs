using System.Collections.Generic;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The aerodynamic sum over a resolved grid group: mass-weighted centre, drag and lift.
    ///
    /// One statement for the pass that applies the force and the overlay that draws it. The
    /// overlay's own summary promises *the force the server applies rather than a second opinion
    /// about it*, and as two copies the promise had already broken: the drag pass vetoes any
    /// group with a static member and the overlay's copy did not know that, so a base standing
    /// in wind drew arrows the server never applies. The veto is <see cref="Anchored"/>, kept
    /// separate from the sum because it is the *appliers'* policy — the overlay still sums an
    /// anchored group to draw its centre of mass, and then shows no force, naming the veto as a
    /// gate.
    /// </summary>
    public static class AeroGroupForces
    {
        /// <summary>
        /// True when the group has a static member. An anchored assembly takes no aerodynamic
        /// force at all — a force on it buys nothing and loads the joints between the station
        /// and whatever is docked to it — and this test is cheap on purpose, so the drag pass
        /// can bail on a base in wind without touching a single thermal adapter.
        /// </summary>
        public static bool Anchored(List<IMyCubeGrid> grids)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i] != null && grids[i].IsStatic) return true;
            }
            return false;
        }

        /// <summary>
        /// Sums the group's mass-weighted world centre, its mass, and the drag and lift the
        /// current state produces. Drag and lift are separate because the overlay draws them as
        /// two arrows; the drag pass applies their sum. The wind each grid's force is taken
        /// against is the same relative wind its heat was computed from (`EnvironmentState`),
        /// rotated to world space because forces are applied there.
        /// </summary>
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

                    // Lift's vector returns zero unless the shape term and lift are both on, so
                    // a world with neither pays a length and a branch.
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
