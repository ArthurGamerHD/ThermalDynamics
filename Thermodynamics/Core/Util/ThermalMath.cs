namespace Thermodynamics.Core
{
    /// <summary>
    /// Mathematical utility functions for thermal calculations.
    /// Provides clamping, bounds checking, and other helper operations
    /// used throughout the thermal simulation code.
    /// </summary>
    public static class ThermalMath
    {
        /// <summary>
        /// Clamps a value to the range [0, 1].
        /// Returns 0 if value is less than 0, 1 if value > 1, otherwise returns the value unchanged.
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <returns>Clamped value between 0 and 1 inclusive.</returns>
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
