using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The force the air is already taking out of a ship's energy, in the form a physics engine can
    /// apply.
    ///
    /// <para>
    /// **The mod has computed this all along and thrown it away.** Aerodynamic friction is
    /// `FrictionScale × ρ × v_rel³ × A_windward`, and real drag power is `½ C_d ρ A v³` — the same
    /// expression. So the solver knows, every step, the rate at which the air is doing work on the
    /// hull; it turns that into heat and takes nothing from the motion. Over the 8,144 published
    /// blueprints at `reentry` the median hull is given 5.05 MW this way, which at 300 m/s is
    /// 16.8 kN of force that is never applied (thermal-model.md's change log).
    /// </para>
    ///
    /// <para>
    /// **Derived from the friction watts rather than recomputed, which is what makes it consistent.**
    /// Power is force times speed, so a second expression for the same quantity is a second place
    /// for the exposure, the wind direction and the density to be got subtly differently — and the
    /// two would disagree only on the ships where it mattered. What is here is one division.
    /// </para>
    /// </summary>
    public static class DragForce
    {
        /// <summary>
        /// Newtons of drag implied by <paramref name="frictionWatts"/> at <paramref name="speed"/>.
        ///
        /// <para>
        /// The friction term is the share of the drag work that lands in the surface, so the whole
        /// of it is that divided by `η = FrictionScale / (½ C_d)`. Written out and cancelled, the
        /// force is `frictionWatts × C_d / (2 × FrictionScale × v)`.
        /// </para>
        ///
        /// <para>
        /// **Zero where there is nothing to divide by** (`E8`). A ship at rest takes no drag however
        /// much heat it holds, and a world with `FrictionScale` at nought has switched the whole
        /// term off — deriving a force from it would be dividing by the thing that says *there is no
        /// friction here*.
        /// </para>
        /// </summary>
        public static float Newtons(float frictionWatts, float speed, ThermalSettings settings)
        {
            if (settings == null || frictionWatts <= 0f || speed <= 0f) return 0f;
            if (settings.FrictionScale <= 0f || settings.DragCoefficient <= 0f) return 0f;

            return frictionWatts * settings.DragCoefficient
                / (2f * settings.FrictionScale * speed);
        }

        /// <summary>
        /// The drag force as a vector, along `−v_rel`.
        ///
        /// <para>
        /// **Against the relative wind, not against the ship's own velocity.** A ship parked in a
        /// storm is pushed downwind, and one flying into a headwind is retarded harder than its
        /// ground speed alone would say. Both fall out of using `v_rel`, which is what the heat term
        /// already uses — `EnvironmentSample.ComposeRelativeWind` — so the force and the heat are
        /// about the same airflow by construction.
        /// </para>
        /// </summary>
        public static Vector3 Vector(float frictionWatts, Vector3 relativeWind, ThermalSettings settings)
        {
            float speed = relativeWind.Length();
            float newtons = Newtons(frictionWatts, speed, settings);
            if (newtons <= 0f) return Vector3.Zero;

            // `relativeWind` is the air's motion relative to the ship, so the force on the ship is
            // *along* it: air moving past a parked ship pushes the ship the way the air is going.
            return relativeWind / speed * newtons;
        }
    }
}
