using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class Isolation
    {
/// <summary>DeadWorld operation.</summary>
        public static ThermalSettings DeadWorld()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }
    }
}
