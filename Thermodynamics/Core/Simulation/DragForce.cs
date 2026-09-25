using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Calculates aerodynamic drag forces from friction heating data.
    /// Used for simulating atmospheric drag effects on moving grids.
    /// </summary>
    public static class DragForce
    {
        /// <summary>
        /// Calculates drag force in Newtons from friction power and speed.
        /// Derived from the relationship between friction power, drag force, and velocity:
        ///   FrictionPower = DragForce * Velocity
        ///   DragForce = FrictionPower / Velocity
        /// 
        /// The formula includes additional factors from the settings:
        ///   DragForce = FrictionWatts * DragCoefficient / (2 * FrictionScale * Velocity)
        /// </summary>
        /// <param name="frictionWatts">Friction power dissipation in watts.</param>
        /// <param name="speed">Speed of the grid in meters per second.</param>
        /// <param name="settings">Thermal settings including FrictionScale and DragCoefficient.</param>
        /// <returns>Drag force in Newtons.</returns>
        /// <remarks>
        /// The friction scale (FrictionScale) represents how friction power
        /// scales with speed. The drag coefficient (DragCoefficient) is a
        /// dimensionless multiplier for the base drag calculation.
        /// 
        /// If any parameter is invalid (null, zero, or negative), returns 0.
        /// </remarks>
        public static float Newtons(float frictionWatts, float speed, ThermalSettings settings)
        {
            if (settings == null || frictionWatts <= 0f || speed <= 0f) return 0f;
            if (settings.FrictionScale <= 0f || settings.DragCoefficient <= 0f) return 0f;

            return frictionWatts * settings.DragCoefficient
                / (2f * settings.FrictionScale * speed);
        }


        /// <summary>
        /// Calculates the full drag force vector from friction power and relative wind.
        /// The drag force acts opposite to the direction of motion.
        /// </summary>
        /// <param name="frictionWatts">Friction power dissipation in watts.</param>
        /// <param name="relativeWind">Relative wind vector (m/s). Direction is wind velocity.</param>
        /// <param name="settings">Thermal settings for drag coefficient calculations.</param>
        /// <returns>Drag force vector in Newtons, opposing the relative wind direction.</returns>
        /// <remarks>
        /// Calculation:
        /// 1. Compute speed from relative wind magnitude
        /// 2. Calculate scalar drag force using Newtons()
        /// 3. Apply drag force in the direction opposite to relative wind
        /// 
        /// Note: The relativeWind vector represents wind velocity, so the drag
        /// force acts in the direction of relative wind (air pushing on the grid).
        /// </remarks>
        public static Vector3 Vector(float frictionWatts, Vector3 relativeWind, ThermalSettings settings)
        {
            float speed = relativeWind.Length();

            float newtons = Newtons(frictionWatts, speed, settings);
            if (newtons <= 0f) return Vector3.Zero;

            // Apply drag in the direction of relative wind
            return relativeWind / speed * newtons;
        }
    }
}
