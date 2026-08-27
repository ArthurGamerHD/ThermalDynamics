using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>A conduction path between a coolant loop and one block node.</summary>
    public struct LoopLink
    {
        public int NodeIndex;
        public float Conductance;

        /// <summary>
        /// Which coolant parcel this link touches: the index in <see cref="CoolantLoop.Pipes"/> of
        /// the pipe the fluid is in. A sink face belongs to the segment inside the pipe carrying it,
        /// not to the loop as a whole — that locality is the whole point of segmenting the fluid.
        /// </summary>
        public int SegmentIndex;

        public LoopLink(int nodeIndex, float conductance, int segmentIndex)
        {
            NodeIndex = nodeIndex;
            Conductance = conductance;
            SegmentIndex = segmentIndex;
        }
    }

    /// <summary>
    /// A closed run of coolant pipes, carrying one parcel of fluid per pipe block. A parcel exchanges
    /// heat only with its own pipe and that pipe's sink faces, so heat reaches the far side of the
    /// ring only by being <see cref="Advect"/>ed there.
    ///
    /// <para>
    /// The fluid does not move; the ring's *origin* does. Parcels sit in a fixed array and each pipe
    /// reads the parcel passing through it, offset by a rotation this class advances once a substep.
    /// See thermal-model.md, Coolant loops.
    /// </para>
    /// </summary>
    public class CoolantLoop
    {
        /// <summary>Pipe blocks forming the ring, in crawl order.</summary>
        public readonly List<BlockInstance> Pipes = new List<BlockInstance>();

        /// <summary>Conduction paths to block nodes.</summary>
        public readonly List<LoopLink> Links = new List<LoopLink>();

        public LoopThermalProperties Properties;

        /// <summary>
        /// Mean coolant temperature over the whole ring, K. Reading it averages the parcels; writing
        /// it sets every parcel to that value, which is what a readout, a save file and a report want.
        /// </summary>
        public float Temperature
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float total = 0f;
                for (int i = 0; i < segmentCount; i++) total += segments[i];
                return total / segmentCount;
            }
            set
            {
                seeded = value;
                for (int i = 0; i < segmentCount; i++) segments[i] = value;
            }
        }

        /// <summary>
        /// Temperature of the coolant currently inside pipe <paramref name="index"/>, K.
        ///
        /// The argument is a position in <see cref="Pipes"/>, not a slot in the parcel array: the
        /// parcels rotate past the pipes. Every caller wants "what is in this pipe now", so the
        /// mapping lives here rather than at each call site.
        /// </summary>
        public float SegmentTemperature(int index)
        {
            // **Bounded by the pipe count, not the parcel count.** `ParcelOf` already folds a pipe
            // index onto a parcel slot, and the two counts differ under `WellMixedCoolant`, where
            // the ring is one parcel however many pipes it has: guarding on the parcel count there
            // sent every pipe past the first to `seeded` instead of to the fluid.
            if (segments == null || segmentCount == 0) return seeded;
            if (index < 0 || index >= Pipes.Count) return seeded;
            return segments[ParcelOf(index)];
        }

        /// <summary>
        /// Parcel array slot currently sitting in pipe <paramref name="pipeIndex"/>.
        ///
        /// Subtracted rather than added: the fluid runs forward along the ring, so after one parcel of
        /// travel the coolant that was in pipe i-1 is in pipe i.
        /// </summary>
        public int ParcelOf(int pipeIndex)
        {
            if (segmentCount <= 0) return 0;

            int slot = (pipeIndex - shift) % segmentCount;
            return slot < 0 ? slot + segmentCount : slot;
        }

        /// <summary>Parcels of coolant the ring carries. One per pipe, or one in total when well mixed.</summary>
        public int ParcelCount
        {
            get { return segmentCount; }
        }

        /// <summary>Parcels of travel since the ring was built. Whole parcels; the fraction is carried.</summary>
        private int shift;

        /// <summary>Fractional travel not yet worth a whole parcel of rotation.</summary>
        private float travelled;

        /// <summary>
        /// Sets the temperature of the coolant currently inside pipe <paramref name="pipeIndex"/>, K.
        ///
        /// The counterpart to <see cref="SegmentTemperature"/>, and the only way to put an uneven
        /// profile into a ring from outside — which a restored save, a test and a host all need, since
        /// <see cref="Temperature"/> levels the whole ring.
        /// </summary>
        public void SetSegmentTemperature(int pipeIndex, float temperature)
        {
            // The same bound, and the same reason: under `WellMixedCoolant` this silently did
            // nothing for every pipe but the first.
            if (segments == null || segmentCount == 0) return;
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count) return;

            segments[ParcelOf(pipeIndex)] = temperature < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : temperature;
        }

        /// <summary>Hottest and coldest parcel in the ring, K. The spread a stopped pump opens up.</summary>
        public float HottestSegment
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float worst = segments[0];
                for (int i = 1; i < segmentCount; i++)
                {
                    if (segments[i] > worst) worst = segments[i];
                }
                return worst;
            }
        }

        public float ColdestSegment
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded;

                float best = segments[0];
                for (int i = 1; i < segmentCount; i++)
                {
                    if (segments[i] < best) best = segments[i];
                }
                return best;
            }
        }

        private float[] segments = new float[0];
        private int segmentCount;

        /// <summary>Temperature a parcel takes when the ring gains one, and the value before any exist.</summary>
        private float seeded;

        /// <summary>Heat capacity of the whole ring's fluid, J/K.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>Heat capacity of one parcel, J/K. Constant, whatever the ring's length.</summary>
        public float SegmentThermalMass { get; private set; }

        /// <summary>
        /// How full the ring is, 0 to 1. Coolant is a consumable: venting empties it and refilling
        /// costs energy and time — thermal-model.md, *Coolant is a consumable*.
        ///
        /// <para>
        /// **A part-full ring holds proportionally less and couples proportionally less**, and the
        /// second half is what keeps it cheap. The integrator sizes a substep from
        /// `SegmentConductance / SegmentThermalMass`, and scaling only the capacity would make a
        /// 5 %-full loop twenty times stiffer on an element that already competes to be a grid's
        /// worst. Scaling both leaves the ratio invariant at every level, and it is the physical
        /// answer as well: half the fluid touching a wall carries half the heat through it.
        /// </para>
        ///
        /// <para>
        /// **A dry ring is still a ring.** It exists, holds nothing, transports nothing and can be
        /// refilled. *The loop stops existing* is the shape this area has failed in twice, once at
        /// 190 MJ through a ground pump, which is why zero is a level rather than a deletion.
        /// </para>
        /// </summary>
        public float FillFraction
        {
            get { return fill; }
            set
            {
                float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
                if (clamped == fill) return;

                fill = clamped;
                RefreshThermalMass();
            }
        }

        private float fill = 1f;

        /// <summary>
        /// Kilograms one pipe of this ring carries. **The cell size is the ring's own parcel
        /// length**, which the builder sets from the grid, so a small-grid ring charges itself from
        /// the same density as a large-grid one rather than from a flat figure that suits neither.
        /// </summary>
        public float MassPerPipe
        {
            get { return Properties.MassPerPipe(ParcelLengthMetres); }
        }

        /// <summary>Kilograms of coolant a full ring holds. What a refill is priced against.</summary>
        public float CapacityKilograms
        {
            get { return MassPerPipe * Math.Max(1, Pipes.Count); }
        }

        /// <summary>Kilograms it is currently holding.</summary>
        public float HeldKilograms
        {
            get { return CapacityKilograms * fill; }
        }

        /// <summary>
        /// Energy to restore one kilogram of coolant, J. The heat that kilogram holds at
        /// <see cref="LoopThermalProperties.RefillEquivalentKelvin"/> above ambient.
        ///
        /// **Divided by the clock for the same reason a parcel's capacity is.** The heat a vent
        /// removes is `capacity × excess` and the capacity the solver carries is already scaled, so
        /// a refill priced in unscaled joules would cost `HeatTimeScale` times what the vent saved.
        /// The two have to be in the same currency or the exchange rate is a factor of ninety out.
        /// </summary>
        public float RefillJoulesPerKilogram
        {
            get
            {
                return (Properties.SpecificHeat * Properties.RefillEquivalentKelvin) / heatTimeScale;
            }
        }

        /// <summary>
        /// Whether anything in the ring is driving fluid: a pump that is switched on, undamaged and
        /// turned up past zero.
        ///
        /// <para>
        /// **Refilling needs one, and so does asking the grid to pay for it.** `HasPump` says the
        /// ring has the hardware; this says the hardware is doing something. The two used to be the
        /// same test here, so a ring whose pumps were all switched off refilled itself — and
        /// refilled *free*, because a pump asking the distributor for nothing reports every watt it
        /// asked for as supplied.
        /// </para>
        /// </summary>
        public bool HasDrivingPump
        {
            get
            {
                for (int i = 0; i < Pumps.Count; i++)
                {
                    CoolantPump pump = Pumps[i];
                    if (pump == null || !pump.Enabled) continue;
                    if (pump.Speed > 0f) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// What refilling is asking for right now, W — the rate times the price, or zero when the
        /// ring is full or nothing is driving it.
        ///
        /// **This is what a pump adds to its power request**, so the grid's distributor decides
        /// whether the ship can afford to refill, and the heat arrives through the pump's own
        /// `ConsumerWasteEnergy` with no second path. A ring with no *running* pump asks for
        /// nothing and therefore never refills, which is the right answer: something has to drive
        /// the fluid in.
        /// </summary>
        public float RefillDemandWatts
        {
            get
            {
                if (fill >= 1f || !HasDrivingPump) return 0f;
                return Properties.RefillWattsAt(heatTimeScale);
            }
        }

        /// <summary>
        /// Puts <paramref name="deltaSeconds"/> of refilling into the ring and returns the watts it
        /// drew doing so — zero when the ring is already full.
        ///
        /// <para>
        /// **The watts are the caller's to spend**, because what makes this cost anything is the
        /// pump's own waste fraction of 1: the host adds them to the pump block's drawn power and
        /// the existing heat path turns all of it into heat where the pump stands. Nothing new
        /// generates heat here, which is why the exchange rate needs no authored threshold.
        /// </para>
        ///
        /// <para>
        /// Returns watts rather than joules so a caller can hand it to a power model that thinks in
        /// watts, and so a partial step — the last one of a refill, which restores less than a full
        /// tick's worth — is priced for what it actually moved.
        /// </para>
        /// </summary>
        public float Refill(float deltaSeconds)
        {
            return Refill(deltaSeconds, 1f);
        }

        /// <summary>
        /// The same, at the share of its request the grid actually supplied. **No power, no
        /// fill**: a ship that cannot afford the refill does not get it, and gets no heat from it
        /// either, which is the same rule every other draw in this mod follows.
        ///
        /// <para>
        /// That share only means anything because <see cref="RefillDemandWatts"/> is inside what
        /// the pump block asks the distributor for. It was not until `B44`'s last piece: the refill
        /// was billed to the pump's *drawn* power, which is what makes it heat, and never *asked*
        /// for — so a ship with nothing to spare paid for its refill in heat and got the coolant
        /// anyway.
        /// </para>
        /// </summary>
        public float Refill(float deltaSeconds, float availableFraction)
        {
            if (deltaSeconds <= 0f || fill >= 1f || !HasDrivingPump) return 0f;

            float available = availableFraction < 0f ? 0f
                : (availableFraction > 1f ? 1f : availableFraction);
            if (available <= 0f) return 0f;

            float capacity = CapacityKilograms;
            if (capacity <= 0f) return 0f;

            float wanted = Properties.RefillKilogramsPerSecond * available * deltaSeconds;
            float room = (1f - fill) * capacity;
            float added = wanted < room ? wanted : room;
            if (added <= 0f) return 0f;

            FillFraction = fill + added / capacity;

            return (added * RefillJoulesPerKilogram) / deltaSeconds;
        }

        /// <summary>
        /// Empties the ring and returns the heat that left with the fluid, J.
        ///
        /// <para>
        /// **The heat is what the fluid was carrying above ambient**, which is exactly what a refill
        /// at the same excess costs to put back. That is the whole of `B43`'s answer: venting at
        /// <see cref="LoopThermalProperties.RefillEquivalentKelvin"/> is neutral, above it pays, and
        /// below it costs.
        /// </para>
        ///
        /// <para>
        /// The ring survives. It holds nothing and transports nothing until it is refilled, which is
        /// what backlog.md `B44` requires: *the loop stops existing* is the shape this area has
        /// failed in twice.
        /// </para>
        /// </summary>
        public float Vent(float ambientKelvin)
        {
            if (fill <= 0f) return 0f;

            float excess = Temperature - ambientKelvin;
            float heat = excess > 0f ? excess * ThermalMass : 0f;

            FillFraction = 0f;
            return heat;
        }

        /// <summary>
        /// The conductance of one link as the solver must use it: what the geometry gives, scaled
        /// by how full the ring is.
        ///
        /// <para>
        /// **Scaled here rather than baked into `Links`**, because refilling moves the fill every
        /// tick and the links are built at topology time — folding it in would mean rebuilding the
        /// grid's link graph to add a kilogram of water. The stored figure stays the geometry's;
        /// this is the one every consumer reads.
        /// </para>
        ///
        /// <para>
        /// **Every consumer must read it**, and one that does not is a silent asymmetry rather than
        /// a compile error: the capacity is scaled too, so a site left unscaled makes a part-full
        /// ring stiffer than a full one instead of identical to it. `CoolantFillTests` asserts the
        /// substep demand is invariant in fill, which is what actually catches that.
        /// </para>
        /// </summary>
        public float LinkConductance(int index)
        {
            if (index < 0 || index >= Links.Count) return 0f;
            return Links[index].Conductance * fill * StagnantFactor;
        }

        /// <summary>
        /// What a link carries as a share of its flowing value, given whether the ring is
        /// circulating: one while it flows, <see cref="LoopThermalProperties.StagnantTransferFraction"/>
        /// while it does not.
        ///
        /// <para>
        /// **Fluid-to-wall transfer is convective, so it depends on the flow.** A pumped ring is
        /// forced convection and a stopped one is natural convection against the same wall, which
        /// is the best part of an order of magnitude apart in any handbook. Nothing in the model
        /// expressed that: <see cref="LoopThermalProperties.HeatTransferCoefficient"/> applied
        /// whole whether or not anything was moving, so a pump earned its power only by evening the
        /// ring out and never by making the ring conduct.
        /// </para>
        ///
        /// <para>
        /// **The field is older than this use and did nothing before it.** It was authored for the
        /// segment-to-segment transport, which <see cref="Advect"/> already stops dead by returning
        /// on zero flow — so as written it was a slider a player could move that multiplied nothing,
        /// found by `LoopDialReachTests`. Its name, its range and what the menu promises are all
        /// unchanged; it now has the leg it describes.
        /// </para>
        ///
        /// <para>
        /// It ships at 1, so a ring behaves exactly as it did until someone decides otherwise —
        /// see balance.md, *The joint, not the panel*.
        /// </para>
        /// </summary>
        public float StagnantFactor
        {
            get
            {
                if (FlowSegmentsPerSecond != 0f) return 1f;

                float fraction = Properties.StagnantTransferFraction;
                return fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
            }
        }

        /// <summary>
        /// Coolant parcels the pumps are pushing past a point each second. Set by the host from the
        /// pumps in this ring; zero when nothing is circulating. Signed: negative runs the ring the
        /// other way, which works just as well.
        /// </summary>
        public float FlowSegmentsPerSecond;

        /// <summary>
        /// Length of one parcel of coolant, m. One pipe block, so the grid's cell size.
        ///
        /// Approximate for a pump longer than one cell — the small grid pump is three cells and still
        /// carries one parcel — which makes this a nominal velocity rather than a surveyed one. It is
        /// the figure a player can reason about, which is what it is for.
        /// </summary>
        public float ParcelLengthMetres = 2.5f;

        /// <summary>
        /// How fast the coolant is moving, m/s. Signed as <see cref="FlowSegmentsPerSecond"/> is.
        ///
        /// Parcels per second is the number the solver works in; metres per second is the number a
        /// player has any intuition for.
        /// </summary>
        public float FlowMetresPerSecond
        {
            get { return FlowSegmentsPerSecond * ParcelLengthMetres; }
        }

        /// <summary>True when at least one pipe in the ring is a pump.</summary>
        public bool HasPump;

        /// <summary>
        /// The pumps in this ring. A ring is a loop whether or not it has any — a pumpless ring is a
        /// loop that holds coolant and circulates none — so this list may be empty.
        /// </summary>
        public readonly List<CoolantPump> Pumps = new List<CoolantPump>();

        /// <summary>
        /// Recomputes <see cref="FlowSegmentsPerSecond"/> from the pumps in the ring: the square root
        /// of their combined demand, signed by which way each faces so opposed pumps subtract.
        /// See thermal-model.md, Coolant loops.
        /// </summary>
        public void RefreshFlow()
        {
            float demand = 0f;
            for (int i = 0; i < Pumps.Count; i++)
            {
                demand += Pumps[i].Contribution;
            }

            if (demand == 0f)
            {
                FlowSegmentsPerSecond = 0f;
                return;
            }

            // The definition is in metres per second; the solver carries parcels, one pipe block
            // long. Dividing here rather than storing parcels is what makes one definition mean the
            // same speed on both grid sizes.
            float parcelLength = ParcelLengthMetres > 0f ? ParcelLengthMetres : 1f;
            float parcelsAtFullFlow = Properties.FlowRateFor(parcelLength) / parcelLength;

            float magnitude = parcelsAtFullFlow * (float)Math.Sqrt(Math.Abs(demand));
            FlowSegmentsPerSecond = demand < 0f ? -magnitude : magnitude;
        }

        /// <summary>
        /// Net pump demand in the ring, in units of one pump at full speed. Signed: opposed pumps
        /// subtract, so a ring whose pumps cancel reports zero however many are running.
        /// </summary>
        public float PumpDemand
        {
            get
            {
                float demand = 0f;
                for (int i = 0; i < Pumps.Count; i++) demand += Pumps[i].Contribution;
                return demand;
            }
        }


        /// <summary>
        /// Identity stable across saves: an order-independent hash of every pipe position in the
        /// ring. An index-based key would let a reload swap two loops' temperatures, and using the
        /// smallest key in the ring would collide with the empty-loop value for a ring including the
        /// grid origin.
        /// </summary>
        public long Signature { get; private set; }

        /// <summary>
        /// Divisor applied to heat capacity, as for a block. See
        /// <see cref="ThermalSettings.HeatTimeScale"/>. The coolant must run on the same clock as
        /// the blocks it is cooling.
        /// </summary>
        public float HeatTimeScale
        {
            get { return heatTimeScale; }
            set
            {
                heatTimeScale = value > 0f ? value : 1f;
                RefreshThermalMass();
            }
        }

        private float heatTimeScale = 1f;

        public CoolantLoop(LoopThermalProperties properties, float initialTemperature)
            : this(properties, initialTemperature, 1f)
        {
        }

        public CoolantLoop(LoopThermalProperties properties, float initialTemperature, float heatTimeScale)
        {
            Properties = (properties ?? LoopThermalProperties.Default()).Clone().Clamp();
            Temperature = initialTemperature;
            this.heatTimeScale = heatTimeScale > 0f ? heatTimeScale : 1f;
            RefreshThermalMass();
        }

        public int PipeCount
        {
            get { return Pipes.Count; }
        }

        /// <summary>
        /// Recomputes the parcel capacity and resizes the parcel array to the ring's length.
        ///
        /// New parcels take <see cref="seeded"/> — the temperature the loop was last told to hold —
        /// so a ring extended by one pipe does not acquire a parcel at absolute zero.
        /// </summary>
        public void RefreshThermalMass()
        {
            // Scaled by the fill, along with every link's conductance, so the ratio the integrator
            // sizes a substep from does not move — see FillFraction.
            float perSegment =
                (fill * Properties.SpecificHeat * MassPerPipe) / heatTimeScale;
            SegmentThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, perSegment);

            // The well-mixed model is a ring carrying exactly one parcel. Expressing it that way rather
            // than as a special case in the solver means every path below — the links, the watts, the
            // integration, the energy — is the same code, and the per-parcel work genuinely collapses
            // to one accumulator and one integration rather than being levelled afterwards.
            int count = wellMixed ? Math.Min(1, Pipes.Count) : Pipes.Count;
            if (wellMixed) SegmentThermalMass = SegmentThermalMass * Math.Max(1, Pipes.Count);

            if (count != segmentCount)
            {
                float[] resized = new float[count];
                for (int i = 0; i < count; i++)
                {
                    resized[i] = i < segmentCount ? segments[i] : seeded;
                }
                segments = resized;
                segmentCount = count;

                // The rotation indexes the ring, so a ring that changed length cannot keep it. A
                // length change means a rebuilt loop anyway; this is the guard, not the mechanism.
                shift = 0;
                travelled = 0f;
            }

            ThermalMass = SegmentThermalMass * Math.Max(1, count);
        }

        /// <summary>Recomputes <see cref="Signature"/> from the current pipe set.</summary>
        public void RefreshSignature()
        {
            if (Pipes.Count == 0)
            {
                Signature = 0L;
                return;
            }

            unchecked
            {
                long combinedXor = 0L;
                long combinedSum = 0L;

                for (int i = 0; i < Pipes.Count; i++)
                {
                    long mixed = Mix(Pipes[i].Key);
                    combinedXor ^= mixed;
                    combinedSum += mixed;
                }

                long signature = (combinedXor * 31L) + combinedSum + Pipes.Count;
                Signature = signature == 0L ? 1L : signature;
            }
        }

        /// <summary>A 64-bit mixing finaliser, so nearby positions do not produce nearby hashes.</summary>
        private static long Mix(long value)
        {
            unchecked
            {
                ulong x = (ulong)value + 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                x = x ^ (x >> 31);
                return (long)x;
            }
        }

        public bool Contains(BlockInstance block)
        {
            for (int i = 0; i < Pipes.Count; i++)
            {
                if (Pipes[i] == block) return true;
            }
            return false;
        }

        /// <summary>
        /// Heat held by the fluid, J. Summed over parcels rather than taken from the mean, so it stays
        /// exact while the ring is unevenly heated — which, with a stopped pump, is most of the time.
        /// </summary>
        public float Energy
        {
            get
            {
                if (segments == null || segmentCount == 0) return seeded * ThermalMass;

                float total = 0f;
                for (int i = 0; i < segmentCount; i++) total += segments[i];
                return total * SegmentThermalMass;
            }
        }

        /// <summary>
        /// Watts accumulated against each parcel during the substep in flight. Owned here rather than
        /// in a flat solver array because it is ragged — one row per loop, each as long as its ring —
        /// and it is only ever touched once per substep per link.
        /// </summary>
        internal float[] SegmentWatts
        {
            get { return segmentWatts; }
        }

        private float[] segmentWatts = new float[0];

        internal void ClearSegmentWatts()
        {
            if (segmentWatts.Length != segmentCount) segmentWatts = new float[segmentCount];
            else Array.Clear(segmentWatts, 0, segmentWatts.Length);
        }

        /// <summary>
        /// Applies watts to one parcel over <paramref name="h"/> seconds. Indexed by parcel rather
        /// than by pipe, so that eight pipes feeding one parcel is simply what a well-mixed ring does
        /// rather than a special case.
        /// </summary>
        internal void ApplyParcelWatts(int parcel, float watts, float h, float effectiveMass)
        {
            if (segments == null || parcel < 0 || parcel >= segmentCount) return;

            float updated = segments[parcel] + (watts * h / effectiveMass);
            segments[parcel] = updated < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : updated;
        }

        /// <summary>
        /// When set, the ring carries one parcel instead of one per pipe: the older well-mixed fluid,
        /// where heat crosses the ring instantly whether anything is circulating or not.
        /// See <see cref="ThermalSettings.WellMixedCoolant"/>.
        /// </summary>
        public bool WellMixed
        {
            get { return wellMixed; }
            set
            {
                if (wellMixed == value) return;

                wellMixed = value;
                RefreshThermalMass();
            }
        }

        private bool wellMixed;

        /// <summary>
        /// Carries the coolant round the ring for <paramref name="h"/> seconds: a pure rotation below
        /// one parcel per substep, and above it a rotation plus mixing toward the ring's mean at
        /// <c>1 - 1/parcels</c>, which is both the physical limit and what removes the aliasing a bare
        /// rotation would suffer. See thermal-model.md, Coolant loops.
        /// </summary>
        public void Advect(float h)
        {
            if (segmentCount < 2 || FlowSegmentsPerSecond == 0f) return;

            float parcels = FlowSegmentsPerSecond * h;
            if (parcels == 0f) return;

            travelled += parcels;

            // Truncation toward zero is what makes this work in both directions: a fractional debt
            // stays on the books with its own sign until it is worth a whole parcel of travel.
            int whole = (int)travelled;
            if (whole != 0)
            {
                travelled -= whole;
                shift = (shift + whole) % segmentCount;
                if (shift < 0) shift += segmentCount;
            }

            // Faster than the step can see: converge on well mixed rather than alias. Direction has
            // no bearing on this — outrunning the step is outrunning the step.
            float magnitude = parcels < 0f ? -parcels : parcels;
            if (magnitude > 1f) MixToward(1f - (1f / magnitude));
        }

        /// <summary>
        /// Blends every parcel toward the ring's mean by <paramref name="fraction"/>, 0..1.
        ///
        /// Exactly conservative: the parcels have equal capacity, so moving each of them the same
        /// fraction of the way to their own mean leaves the sum untouched.
        /// </summary>
        private void MixToward(float fraction)
        {
            if (segmentCount < 2) return;
            if (fraction <= 0f) return;
            if (fraction > 1f) fraction = 1f;

            float total = 0f;
            for (int i = 0; i < segmentCount; i++) total += segments[i];

            float mean = total / segmentCount;
            for (int i = 0; i < segmentCount; i++)
            {
                segments[i] += fraction * (mean - segments[i]);
            }
        }

        /// <summary>
        /// How much of a substep's transport is being served by mixing rather than by carrying, 0..1,
        /// at the given substep length. Above zero means the flow is faster than the step resolves.
        /// </summary>
        public float MixingFraction(float substepSeconds)
        {
            float parcels = FlowSegmentsPerSecond * substepSeconds;
            if (parcels < 0f) parcels = -parcels;
            if (parcels <= 1f) return 0f;
            return 1f - (1f / parcels);
        }

        // ---- what the loop moved, per step -------------------------------------------------

        /// <summary>
        /// Heat the fluid drew out of the blocks it touches over the last step, W. **Gross rather than
        /// net**: a working loop's net is around zero exactly when it is doing the most work, so one
        /// figure would make a loop moving 40 kW look like a loop moving nothing.
        /// </summary>
        public float LastWattsAbsorbed;

        /// <summary>Heat the fluid pushed back into the blocks it touches over the last step, W.</summary>
        public float LastWattsRejected;

        /// <summary>
        /// Net change in the fluid's own heat content, W. Positive means the loop is still warming
        /// up; around zero with a large <see cref="LastWattsAbsorbed"/> means it is in balance and
        /// carrying its full load.
        /// </summary>
        public float LastNetWatts
        {
            get { return LastWattsAbsorbed - LastWattsRejected; }
        }

        /// <summary>Energy accumulated across the substeps of one step, J.</summary>
        internal float AbsorbedEnergy;
        internal float RejectedEnergy;

        internal void BeginStep()
        {
            AbsorbedEnergy = 0f;
            RejectedEnergy = 0f;
        }

        /// <summary>
        /// Converts the energy moved over a whole step into the rates a readout reports. Per step
        /// rather than per substep, as <see cref="HeatPumpDevice.EndStep"/> is and for the same
        /// reason: a value overwritten each substep describes only the last one.
        /// </summary>
        internal void EndStep(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            LastWattsAbsorbed = AbsorbedEnergy / deltaSeconds;
            LastWattsRejected = RejectedEnergy / deltaSeconds;
        }
    }
}
