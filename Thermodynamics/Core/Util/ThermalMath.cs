namespace Thermodynamics.Core
{
    /// <summary>
    /// The clamp to the unit interval, stated once. Ten identical private copies had accumulated
    /// across the solver, the environment, the pumps, the definitions and the overlays —
    /// identical today, and ten chances for the next one to be subtly different: a bound made
    /// exclusive, a NaN swallowed. NaN passes through on purpose — a clamp is not a validity
    /// check, and the places that must refuse a NaN test for one by name.
    /// </summary>
    public static class ThermalMath
    {
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
