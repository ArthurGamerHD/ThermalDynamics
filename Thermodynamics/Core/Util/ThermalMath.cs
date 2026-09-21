namespace Thermodynamics.Core
{
    public static class ThermalMath
    {
/// <summary>Clamp01 operation.</summary>
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
