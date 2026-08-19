using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Coolant description, from the <c>ThermalLoopProperties</c> group. Describes the fluid in
    /// a loop, not the pipe blocks it runs through.
    /// </summary>
    public class LoopThermalProperties
    {
        /// <summary>
        /// Coolant mass carried by each pipe block in the ring, kg. The loop's total is this times
        /// its length.
        ///
        /// Per pipe rather than per loop, which it used to be. A fixed mass per loop meant a longer
        /// ring divided the same fluid into smaller parcels, so every parcel got stiffer as a player
        /// added plumbing — and the whole ring coupled harder to the grid while holding no more
        /// coolant, which made length a free cooling multiplier. Per pipe, the segment capacity and
        /// the contact area both scale together: a longer ring is a bigger thermal buffer rather than
        /// a better cooler, and its solver cost per pipe is constant.
        /// </summary>
        public float MassPerPipe = 50f;

        /// <summary>Transfer quality, 0..1.</summary>
        public float Conductivity = 1f;

        /// <summary>
        /// Specific heat capacity of the coolant, real J/(kg K). Water-glycol is about 3400, against
        /// steel's 450.
        ///
        /// Divided by <see cref="ThermalSettings.HeatTimeScale"/> exactly as a block's is, so the
        /// fluid runs on the same clock as the blocks it exchanges with.
        /// </summary>
        public float SpecificHeat = 3400f;

        /// <summary>Contact area scaler between the fluid and the pipe block it runs through.</summary>
        public float PipeSurfaceAreaScaler = 1f;

        /// <summary>Contact area scaler between the fluid and a block on a sink face.</summary>
        public float PlateSurfaceAreaScaler = 1f;

        /// <summary>
        /// Coolant parcels a pump at full speed pushes past a point each second, per unit of flow.
        ///
        /// The advection rate, and the reason a stopped pump stops cooling rather than slowing it: a
        /// segment only reaches a radiator on the far side of the ring if something carries it there.
        /// Expressed per segment rather than per ring, so the rate — and the substeps it demands — do
        /// not depend on how much pipe a player laid.
        /// </summary>
        public float SegmentsPerSecondAtFullFlow = 4f;

        /// <summary>
        /// Fraction of full transfer that survives with no circulation at all, 0..1.
        ///
        /// Not zero: a stagnant pipe still conducts into the fluid touching it, it just cannot carry
        /// that heat anywhere. Only the transport between segments stops dead.
        /// </summary>
        public float StagnantTransferFraction = 1f;

        public static LoopThermalProperties Default()
        {
            return new LoopThermalProperties();
        }

        public LoopThermalProperties Clamp()
        {
            MassPerPipe = Math.Max(1f, MassPerPipe);
            SegmentsPerSecondAtFullFlow = Math.Max(0f, SegmentsPerSecondAtFullFlow);
            StagnantTransferFraction = Math.Max(0f, Math.Min(1f, StagnantTransferFraction));
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
