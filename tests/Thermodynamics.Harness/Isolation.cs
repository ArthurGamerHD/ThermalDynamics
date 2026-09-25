using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class Isolation
    {

        public static ThermalSettings DeadWorld()
        {

            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            return settings.Derive();
        }
    }
}
