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
    /// A closed run of coolant pipes, carrying one parcel of fluid per pipe block.
    ///
    /// The fluid used to be a single well-mixed mass: one temperature for the whole ring, linked to
    /// every pipe and every sink face at once. That made a pump unnecessary to the physics — heat
    /// crossed from a reactor to a radiator on the far side of the ship instantly, whether anything
    /// was circulating or not — so "pump off" could only be modelled by deleting the loop, which
    /// deleted the heat it held with it.
    ///
    /// Now each pipe holds its own parcel at its own temperature. A parcel exchanges heat only with
    /// its own pipe and the blocks on that pipe's sink faces; the only way heat reaches the far side
    /// of the ring is for the fluid to be <see cref="Advect"/>ed there. So a stopped pump leaves the
    /// coolant beside a reactor to saturate while the coolant at the radiator stays cold, which is
    /// what a stopped pump does, and the ring keeps every joule it was holding.
    ///
    /// Transport is advective rather than diffusive on purpose. Diffusion around a ring of N parcels
    /// mixes in time proportional to N squared, so a long ring would need a conductance high enough
    /// to make the solver unusably stiff. Advection carries a parcel N places in time proportional
    /// to N, and its stability limit — one parcel per substep — does not depend on N at all.
    /// </summary>
    public class CoolantLoop
    {
        /// <summary>Pipe blocks forming the ring, in crawl order.</summary>
        public readonly List<BlockInstance> Pipes = new List<BlockInstance>();

        /// <summary>Conduction paths to block nodes.</summary>
        public readonly List<LoopLink> Links = new List<LoopLink>();

        public LoopThermalProperties Properties;

        /// <summary>
        /// Mean coolant temperature over the whole ring, K.
        ///
        /// Reading it averages the parcels; writing it sets every parcel to that value. Kept as a
        /// single number because that is what a readout, a save file and a report all want — the
        /// distribution around the ring is a transient that circulation re-establishes in seconds,
        /// while the mean carries the energy.
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

        /// <summary>Temperature of one coolant parcel, K, indexed as <see cref="Pipes"/>.</summary>
        public float SegmentTemperature(int index)
        {
            if (segments == null || index < 0 || index >= segmentCount) return seeded;
            return segments[index];
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
        private float[] carried = new float[0];
        private int segmentCount;

        /// <summary>Temperature a parcel takes when the ring gains one, and the value before any exist.</summary>
        private float seeded;

        /// <summary>Heat capacity of the whole ring's fluid, J/K.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>Heat capacity of one parcel, J/K. Constant, whatever the ring's length.</summary>
        public float SegmentThermalMass { get; private set; }

        /// <summary>
        /// Coolant parcels the pumps are pushing past a point each second. Set by the host from the
        /// pumps in this ring; zero when nothing is circulating.
        /// </summary>
        public float FlowSegmentsPerSecond;

        /// <summary>True when at least one pipe in the ring is a pump.</summary>
        public bool HasPump;

        /// <summary>
        /// The pumps in this ring. A ring is a loop whether or not it has any — a pumpless ring is a
        /// loop that holds coolant and circulates none — so this list may be empty.
        /// </summary>
        public readonly List<CoolantPump> Pumps = new List<CoolantPump>();

        /// <summary>
        /// Recomputes <see cref="FlowSegmentsPerSecond"/> from the pumps in the ring.
        ///
        /// Flow goes as the square root of the pumps' combined demand, not the sum. That is the real
        /// behaviour of pumps in parallel against a fixed circuit: pressure loss in turbulent flow
        /// rises with the square of flow rate, so doubling the pumping doubles the head and multiplies
        /// the flow by about 1.41. Four pumps carry twice one pump's flow, not four times.
        ///
        /// It is also the behaviour worth having in a game: a second pump is a real gain and a
        /// meaningful redundancy, and the tenth is nearly free to leave switched off.
        /// </summary>
        public void RefreshFlow()
        {
            float demand = 0f;
            for (int i = 0; i < Pumps.Count; i++)
            {
                demand += Pumps[i].Contribution;
            }

            FlowSegmentsPerSecond = demand <= 0f
                ? 0f
                : Properties.SegmentsPerSecondAtFullFlow * (float)Math.Sqrt(demand);
        }

        /// <summary>Combined pump demand in the ring, in units of one pump at full speed.</summary>
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
            float perSegment = (Properties.SpecificHeat * Properties.MassPerPipe) / heatTimeScale;
            SegmentThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, perSegment);

            int count = Pipes.Count;
            if (count != segmentCount)
            {
                float[] resized = new float[count];
                for (int i = 0; i < count; i++)
                {
                    resized[i] = i < segmentCount ? segments[i] : seeded;
                }
                segments = resized;
                carried = new float[count];
                segmentCount = count;
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

        /// <summary>Applies watts to one parcel over <paramref name="h"/> seconds.</summary>
        internal void ApplySegmentWatts(int index, float watts, float h, float effectiveMass)
        {
            if (segments == null || index < 0 || index >= segmentCount) return;

            float updated = segments[index] + (watts * h / effectiveMass);
            segments[index] = updated < ThermalConstants.MinimumTemperature
                ? ThermalConstants.MinimumTemperature
                : updated;
        }

        /// <summary>
        /// Carries the fluid one step around the ring.
        ///
        /// First-order upwind: each parcel gives up a fraction of its temperature to the next and
        /// takes the same fraction from the previous. Because every parcel has the same capacity, the
        /// sum of temperatures is unchanged by construction, so this moves heat without creating or
        /// destroying any — which a scheme with uneven parcels would not.
        ///
        /// <paramref name="h"/> times the flow rate is the fraction, and it is clamped at one parcel:
        /// past that the scheme would be pulling from fluid that has already moved on. The solver
        /// demands enough substeps to stay under that, so the clamp is a backstop rather than the
        /// normal path.
        /// </summary>
        /// <summary>
        /// When set, every parcel is levelled to the ring's mean after each substep, which reproduces
        /// the older well-mixed fluid exactly: one temperature for the whole ring, and transport that
        /// does not depend on a pump. See <see cref="ThermalSettings.WellMixedCoolant"/>.
        /// </summary>
        public bool WellMixed;

        /// <summary>Levels every parcel to the ring's mean. Conserves energy: the masses are equal.</summary>
        internal void Mix()
        {
            if (segmentCount < 2) return;

            float total = 0f;
            for (int i = 0; i < segmentCount; i++) total += segments[i];

            float mean = total / segmentCount;
            for (int i = 0; i < segmentCount; i++) segments[i] = mean;
        }

        internal void Advect(float h)
        {
            if (WellMixed)
            {
                Mix();
                return;
            }

            if (segmentCount < 2 || FlowSegmentsPerSecond <= 0f) return;

            float fraction = FlowSegmentsPerSecond * h;
            if (fraction <= 0f) return;
            if (fraction > 1f) fraction = 1f;

            for (int i = 0; i < segmentCount; i++)
            {
                carried[i] = segments[i];
            }

            // Pipes are held in crawl order, so the parcel before index i is at i-1 around the ring.
            for (int i = 0; i < segmentCount; i++)
            {
                int previous = i == 0 ? segmentCount - 1 : i - 1;
                segments[i] = carried[i] + (fraction * (carried[previous] - carried[i]));
            }
        }

        // ---- what the loop moved, per step -------------------------------------------------

        /// <summary>
        /// Heat the fluid drew out of the blocks it touches over the last step, W.
        ///
        /// Gross rather than net, and that is the whole point of reporting two figures. A working
        /// loop settles where what it draws off a reactor equals what it sheds into a radiator, so
        /// its *net* is around zero exactly when it is doing the most work. A single figure would
        /// make a loop moving 40 kW indistinguishable from a loop moving nothing.
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
