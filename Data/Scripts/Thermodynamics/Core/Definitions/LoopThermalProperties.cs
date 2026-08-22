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
        /// Coolant mass carried by each pipe block in the ring, kg; the loop's total is this times its
        /// length. Per pipe rather than per loop, so segment capacity and contact area scale together
        /// and a longer ring is a bigger buffer rather than a free cooling multiplier.
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
        /// How fast the coolant moves on a **large grid** with one pump at full power, m/s — the unit
        /// the terminal reports, converted to the solver's parcels by
        /// <see cref="CoolantLoop.RefreshFlow"/>. The advection rate, and the reason a stopped pump
        /// stops cooling rather than slowing it.
        /// </summary>
        public float LargeGridFlowRate = 10f;

        /// <summary>
        /// The same, for a **small grid**. Split because it is a balance dial rather than a physical
        /// constant — and because a small-grid pipe is a fifth as long, so the same metres per second
        /// is five times the parcel rate and approaches a well-mixed ring sooner.
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
