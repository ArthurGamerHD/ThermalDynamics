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
        /// Coolant per cubic metre of the cell a pipe occupies, kg/m³ — **the charge, and the one a
        /// ring uses unless something states a flat mass instead.**
        ///
        /// <para>
        /// Per pipe rather than per loop, so segment capacity and contact area scale together and a
        /// longer ring is a bigger buffer rather than a free cooling multiplier. A *density* rather
        /// than a mass, because a flat mass has no grid size in it: the 50 kg this shipped until
        /// `C43` is 3.2 kg/m³ in a 2.5 m cube, which is a gas, against 400 kg/m³ in a 0.5 m one,
        /// which is a liquid and outweighs the 32 kg pipe block carrying it. That is the same defect
        /// <see cref="HeatTransferCoefficient"/> was corrected for when it stopped being a
        /// conductivity divided by half a cell.
        /// </para>
        ///
        /// <para>
        /// 33 kg/m³ is a bore one fifth of the cell across running down the middle of it, filled
        /// with water-glycol at 1,050 kg/m³: `π/4 × (0.2g)² × g × 1050 / g³`. That is 515.6 kg a pipe
        /// on a large grid and 4.1 kg on a small one.
        /// </para>
        ///
        /// <para>
        /// **What it changes is the swing, not the mean.** At steady state a ring's temperature is
        /// set by what comes in against what radiates out and a charged capacity is in neither: the
        /// correction moved a rig's mean 6.9 K on a large grid and 0.8 K on a small one, and moved
        /// the spread between the sink face and the far side of the ring from 100.2 K to 10.0 K and
        /// from 7.7 K to 94.6 K. See balance.md, *The coolant mass is doing an undeclared job*.
        /// </para>
        /// </summary>
        public float CoolantKilogramsPerCubicMetre = 33f;

        /// <summary>
        /// A flat coolant mass per pipe block, kg, or **zero to derive it from
        /// <see cref="CoolantKilogramsPerCubicMetre"/> and the cell** — which is what ships.
        ///
        /// <para>
        /// **Kept as an override rather than repurposed** (`P15`). It was the charge until `C43`,
        /// and the name is stated in `Loops.xml`, in a world's settings and in any third-party loop
        /// definition — so a file that states a flat 50 still gets a flat 50, and only silence means
        /// the density. Reading a stated per-pipe mass as a density would hand a large-grid ring
        /// fifty kilograms per cubic metre: sixteen times the fluid it asked for.
        /// </para>
        /// </summary>
        public float CoolantMassPerPipe = 0f;

        /// <summary>
        /// Coolant one pipe block carries on a grid of this cell size, kg. The flat mass where one
        /// is stated, and the density times the cell's volume otherwise.
        /// </summary>
        public float MassPerPipe(float cellSizeMetres)
        {
            if (CoolantMassPerPipe > 0f) return CoolantMassPerPipe;

            // A ring always knows its cell — the builder takes it from the grid — so this is
            // defensive. It falls to the large grid rather than to the threshold between the two,
            // because the threshold is a *comparison* value and is not a cell size anything has.
            if (cellSizeMetres <= 0f) cellSizeMetres = LargeGridCellMetres;

            return Math.Max(ThermalConstants.MinimumThermalMass,
                CoolantKilogramsPerCubicMetre * cellSizeMetres * cellSizeMetres * cellSizeMetres);
        }

        /// <summary>
        /// How well heat crosses between the fluid and the wall it touches, W/(m²·K).
        ///
        /// <para>
        /// **A convective coefficient, because that is what the transfer is.** It was a 0…1 quality
        /// against a reference conductivity of 200 W/(m·K), divided by half a cell — which gave the
        /// game a second conduction pace nothing reconciled with the first
        /// and made the coefficient it implied
        /// depend on grid size: 160 on a large grid and **800 on a small one**, for the same fluid
        /// against the same wall. Convection has no length in it, so neither does this.
        /// </para>
        ///
        /// <para>
        /// 160 is what the shipped large-grid loop was already running at, so a large grid is
        /// unchanged. Forced convection of a water-glycol mix in a pipe is a few hundred to a few
        /// thousand; 160 is a slow flow, which is what a ring driven by one pump is.
        /// </para>
        /// </summary>
        public float HeatTransferCoefficient = 1000f;

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

        /// <summary>
        /// The excess a refill is priced at, K. Energy to restore one kilogram is
        /// `SpecificHeat × this / HeatTimeScale` — the heat that kilogram holds at this many kelvin
        /// above ambient.
        ///
        /// <para>
        /// **So this is the excess at which venting and refilling exactly break even**, and the
        /// number is derived rather than picked: a pump's `ConsumerWasteEnergy` is 1, so every joule
        /// spent refilling lands back in the ship as heat, and charging the parcel's own heat makes
        /// the cycle neutral by construction. Above this excess dumping coolant still pays and below
        /// it costs, which is the behaviour to want — worth doing when the coolant is genuinely hot,
        /// worthless as a pump.
        /// </para>
        ///
        /// <para>
        /// 100 K because that is where `ThermalGlow` starts: the excess this mod already treats as
        /// the point a block is worth telling the player about. See thermal-model.md, *Coolant is a
        /// consumable*, and backlog.md `B43`.
        /// </para>
        /// </summary>
        public float RefillEquivalentKelvin = 100f;

        /// <summary>
        /// Watts a refill running at full rate asks for, on a world at this clock.
        ///
        /// **One definition, because two things need it**: `CoolantLoop.RefillDemandWatts` publishes
        /// it every step, and the pump block's resource sink needs the same figure as the ceiling it
        /// is constructed with — a ceiling under the real request is a request the distributor
        /// quietly trims.
        /// </summary>
        public float RefillWattsAt(float heatTimeScale)
        {
            if (heatTimeScale <= 0f) heatTimeScale = 1f;
            return RefillKilogramsPerSecond * SpecificHeat * RefillEquivalentKelvin / heatTimeScale;
        }

        /// <summary>
        /// How fast a ring refills, kg/s. Bounds the cycle: however many grinders are aboard, the
        /// coolant comes back at this rate and no faster.
        ///
        /// <para>
        /// **The one figure here with no derivation under it, and it says so.** Venting is instant
        /// and refilling is not, and that asymmetry is the mechanic — an emergency dump buys relief
        /// now and pays it back while the radiators work. 5 kg/s refills a full eight-pipe large-grid
        /// ring in 80 s, inside the 2–5 minute window `G8` asks a significant thermal event to land
        /// in, and it is 2.5 % of the 200 kg/s a large-grid pump circulates.
        /// </para>
        /// </summary>
        public float RefillKilogramsPerSecond = 5f;

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

        /// <summary>Space Engineers' large cell edge, m. The fallback where a caller states none.</summary>
        public const float LargeGridCellMetres = 2.5f;

        /// <summary>Space Engineers' small cell edge, m.</summary>
        public const float SmallGridCellMetres = 0.5f;

        /// <summary>
        /// Fraction of full transfer that survives with no circulation at all, 0..1.
        ///
        /// Not zero: a stagnant pipe still conducts into the fluid touching it, it just cannot carry
        /// that heat anywhere. Only the transport between segments stops dead.
        /// </summary>
        public float StagnantTransferFraction = 0.16f;

        public static LoopThermalProperties Default()
        {
            return new LoopThermalProperties();
        }

        public LoopThermalProperties Clamp()
        {
            // Zero is the value that means "derive from the density", so it is not clamped
            // away; a negative one would be, and is the only thing this line now catches.
            CoolantMassPerPipe = Math.Max(0f, CoolantMassPerPipe);
            CoolantKilogramsPerCubicMetre = Math.Max(0f, CoolantKilogramsPerCubicMetre);
            RefillEquivalentKelvin = Math.Max(0f, RefillEquivalentKelvin);
            RefillKilogramsPerSecond = Math.Max(0f, RefillKilogramsPerSecond);
            LargeGridFlowRate = Math.Max(0f, LargeGridFlowRate);
            SmallGridFlowRate = Math.Max(0f, SmallGridFlowRate);
            StagnantTransferFraction = Math.Max(0f, Math.Min(1f, StagnantTransferFraction));
            HeatTransferCoefficient = Math.Max(0f, HeatTransferCoefficient);
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
