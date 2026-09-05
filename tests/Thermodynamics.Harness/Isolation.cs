using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The dead-world settings the loop rigs share: environment, sun, friction and damage off,
    /// so heat moves only where the experiment puts it.
    ///
    /// Four test classes each stated this control independently under the same name, which is a
    /// control that can drift per experiment without anything saying so. One statement, so a
    /// mechanism added to the isolation list reaches every rig that claims to be isolated. A
    /// class whose experiment needs a *different* exclusion — a pinned pace, a substep ceiling,
    /// waste heat off — states its own beside its tests, where a reader of the experiment can
    /// see the choice; those are deviations, not copies.
    /// </summary>
    public static class Isolation
    {
        /// <summary>A derived settings object with the world switched off.</summary>
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
