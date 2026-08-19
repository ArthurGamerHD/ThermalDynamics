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
        public float CoolantMassPerPipe = 50f;

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
        public float PipeContactMultiplier = 1f;

        /// <summary>Contact area scaler between the fluid and a block on a sink face.</summary>
        public float SinkContactMultiplier = 1f;

        /// <summary>
        /// How fast the coolant moves on a **large grid** with one pump at full power, metres per
        /// second.
        ///
        /// The advection rate, and the reason a stopped pump stops cooling rather than slowing it:
        /// coolant only reaches a radiator on the far side of the ring if something carries it there.
        /// Independent of how much pipe a player laid, so the rate — and the substeps it demands —
        /// do not grow with the plumbing.
        ///
        /// In metres per second because that is the unit the terminal reports and the only one
        /// anybody has intuition for. The solver works in parcels — one pipe block of fluid — and
        /// <see cref="CoolantLoop.RefreshFlow"/> converts by dividing by the cell size.
        /// </summary>
        public float LargeGridFlowRate = 10f;

        /// <summary>
        /// The same, for a **small grid**.
        ///
        /// Split from the large-grid figure because it is a balance dial rather than a physical
        /// constant: a small-grid pump is a much smaller machine driving a much shorter ring, and
        /// whether it should push its coolant as fast as a capital ship's is a judgement, not a
        /// measurement. One number for both took that choice away from whoever is tuning.
        ///
        /// Note what the shared parcel model does with a difference here. A small-grid pipe is a
        /// fifth as long, so the same metres per second is five times the parcel rate — the ring
        /// laps far more often, and approaches a well-mixed loop sooner.
        /// </summary>
        public float SmallGridFlowRate = 10f;

        /// <summary>
        /// The flow rate for a ring built at this cell size, m/s.
        ///
        /// Space Engineers has exactly two cell sizes, 2.5 m and 0.5 m, with nothing between them,
        /// so a single threshold separates them safely. It lives here rather than at each call site
        /// so there is one answer to "which grid is this".
        /// </summary>
        public float FlowRateFor(float cellSizeMetres)
        {
            return cellSizeMetres < LargeGridCellThresholdMetres ? SmallGridFlowRate : LargeGridFlowRate;
        }

        /// <summary>Cell sizes below this count as small grid. Between SE's 0.5 m and 2.5 m.</summary>
        public const float LargeGridCellThresholdMetres = 1f;

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
            CoolantMassPerPipe = Math.Max(1f, CoolantMassPerPipe);
            LargeGridFlowRate = Math.Max(0f, LargeGridFlowRate);
            SmallGridFlowRate = Math.Max(0f, SmallGridFlowRate);
            StagnantTransferFraction = Math.Max(0f, Math.Min(1f, StagnantTransferFraction));
            Conductivity = Math.Max(0f, Math.Min(1f, Conductivity));
            SpecificHeat = Math.Max(ThermalConstants.MinimumThermalMass, SpecificHeat);
            PipeContactMultiplier = Math.Max(0f, PipeContactMultiplier);
            SinkContactMultiplier = Math.Max(0f, SinkContactMultiplier);
            return this;
        }

        public LoopThermalProperties Clone()
        {
            return (LoopThermalProperties)MemberwiseClone();
        }
    }
}
