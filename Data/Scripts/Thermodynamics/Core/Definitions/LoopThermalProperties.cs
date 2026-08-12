using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Coolant description, from the <c>ThermalLoopProperties</c> group. Describes the fluid in
    /// a loop, not the pipe blocks it runs through.
    /// </summary>
    public class LoopThermalProperties
    {
        /// <summary>Coolant mass for the whole loop, kg.</summary>
        public float Mass = 500f;

        /// <summary>Transfer quality, 0..1.</summary>
        public float Conductivity = 1f;

        /// <summary>
        /// Specific heat capacity of the coolant, real J/(kg K). Water-glycol is about 3400,
        /// which is why coolant carries heat so much better than the steel around it.
        ///
        /// This is divided by <see cref="ThermalSettings.HeatTimeScale"/> exactly as a block's
        /// is — the fluid has to run on the same clock as the blocks it exchanges with, or a
        /// loop moves heat at a different pace from everything it is cooling.
        /// </summary>
        public float SpecificHeat = 3400f;

        /// <summary>Contact area scaler between the fluid and the pipe block it runs through.</summary>
        public float PipeSurfaceAreaScaler = 1f;

        /// <summary>Contact area scaler between the fluid and a block on a sink face.</summary>
        public float PlateSurfaceAreaScaler = 1f;

        public static LoopThermalProperties Default()
        {
            return new LoopThermalProperties();
        }

        public LoopThermalProperties Clamp()
        {
            Mass = Math.Max(1f, Mass);
            Conductivity = Math.Max(0f, Math.Min(1f, Conductivity));
            SpecificHeat = Math.Max(ThermalConstants.MinimumThermalMass, SpecificHeat);
            PipeSurfaceAreaScaler = Math.Max(0f, PipeSurfaceAreaScaler);
            PlateSurfaceAreaScaler = Math.Max(0f, PlateSurfaceAreaScaler);
            return this;
        }

        public LoopThermalProperties Clone()
        {
            return (LoopThermalProperties)MemberwiseClone();
        }
    }
}
