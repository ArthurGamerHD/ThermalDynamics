using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The transverse half of the aerodynamic force — lift — from the pressure sum the solver
    /// already keeps.
    ///
    /// <para>
    /// **Lift is not a second model here, it is the direction drag throws away.** Newtonian impact
    /// theory puts the force on a surface element along `−n̂` with magnitude `2q(n̂·ŵ)²dA`. The
    /// solver sums exactly that magnitude per node, and <see cref="ShapeNormal.Factor"/> collapses
    /// it to a scalar at the moment of use, keeping only what points along the flow.
    /// <see cref="ThermalSolver.LastPressureWatts"/> is the same sum with the normals left on, so
    /// its component perpendicular to the flow is lift and needs no new coefficient to exist.
    /// See backlog.md `K23`.
    /// </para>
    ///
    /// <para>
    /// **An addition, never a re-derivation.** Drag is still <see cref="DragForce"/>'s scalar along
    /// the wind, bit-identical whether lift is on or off, so a world that turns lift on keeps the
    /// handling it had and gains a force perpendicular to it. Deriving both from the vector would
    /// have changed drag the moment lift was switched on, which is a retune disguised as a feature.
    /// </para>
    /// </summary>
    public static class LiftForce
    {
        /// <summary>
        /// The lift vector in the same space <paramref name="pressureWatts"/> and
        /// <paramref name="relativeWind"/> are given in, or zero where there is nothing to take.
        ///
        /// <para>
        /// The axial part is removed rather than scaled away: what is left is by construction
        /// perpendicular to the flow, so this cannot quietly add to or subtract from drag however
        /// the hull is shaped.
        /// </para>
        /// </summary>
        public static Vector3 Vector(Vector3 pressureWatts, Vector3 relativeWind,
            ThermalSettings settings)
        {
            if (settings == null || !settings.EnableLift) return Vector3.Zero;
            if (settings.LiftCoefficient <= 0f) return Vector3.Zero;

            float speed = relativeWind.Length();
            if (speed <= 0f) return Vector3.Zero;

            Vector3 flow = relativeWind / speed;

            // The transverse remainder. Anything along the flow is drag and is already applied.
            Vector3 transverse = pressureWatts - (Vector3.Dot(pressureWatts, flow) * flow);

            float watts = transverse.Length();
            if (watts <= 0f) return Vector3.Zero;

            // Watts become newtons by the same division drag uses, so the two halves of one
            // pressure field are priced identically and cannot drift apart.
            float newtons = DragForce.Newtons(watts, speed, settings) * settings.LiftCoefficient;
            if (newtons <= 0f) return Vector3.Zero;

            return transverse / watts * newtons;
        }
    }
}
