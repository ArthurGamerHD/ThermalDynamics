using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The thermal state of one block. Geometry and definition data are read from the
    /// <see cref="BlockInstance"/>; everything here is either simulation state or a value
    /// derived from the block that would be wasteful to recompute each step.
    /// </summary>
    public class ThermalNode
    {
        public readonly BlockInstance Block;

        /// <summary>Index into the solver's node array. Stable while the node is registered.</summary>
        public int Index = -1;

        /// <summary>Current temperature, K.</summary>
        public float Temperature;

        /// <summary>Heat capacity of the whole block, J/K. Never below a small floor.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>
        /// Heat capacity of coolant this block is holding outside any loop, in real J/K before
        /// <see cref="HeatTimeScale"/>. Zero for every block that is not a pipe whose ring has been
        /// broken.
        ///
        /// <para>
        /// A ring that stops being a ring has no successor loop and its parcels have nowhere to
        /// live, so each pipe takes the parcel that was inside it. Taking only the *temperature*
        /// destroys `M_s / (M_n + M_s)` of the ring's heat; taking the mass as well is exact,
        /// because the fluid really is still in the block. It is handed back the moment a ring runs
        /// through this pipe again, which is exact in the other direction too: a mixture is at one
        /// temperature, so splitting it at that temperature conserves energy term by term.
        /// </para>
        ///
        /// <para>
        /// It is a separate field rather than a bigger <see cref="BlockInstance.Mass"/> because the
        /// host owns that figure and recomputes it from build progress, damage and inventory; a
        /// value this model added would be overwritten the next time the game reported one.
        /// </para>
        /// </summary>
        public float HeldCoolantCapacity
        {
            get { return heldCoolantCapacity; }
            set
            {
                float clamped = value > 0f ? value : 0f;
                if (clamped == heldCoolantCapacity) return;
                heldCoolantCapacity = clamped;
                RefreshThermalMass();
            }
        }

        private float heldCoolantCapacity;

        /// <summary>
        /// Exposed cell faces per face direction, six counts packed into one field.
        ///
        /// <para>
        /// **An `int[6]` cost 48 bytes a node to hold six small counts** — the array's header and
        /// the reference to it, for 24 bytes of payload
        /// (backlog.md `E2`). Ten bits a face holds 1,023, which is
        /// larger than any face of any block the game has: the widest vanilla block is ten cells
        /// across, so a hundred is the most a single face can carry.
        /// </para>
        ///
        /// <para>
        /// Read and written through <see cref="GetExposedFaces"/> and
        /// <see cref="SetExposedFaces"/>, which is what keeps the packing in one place.
        /// </para>
        /// </summary>
        private long exposedFaces;

        /// <summary>Bits each face's count occupies. Ten holds 1,023 against a real worst case of 100.</summary>
        private const int FaceBits = 10;

        private const long FaceMask = (1L << FaceBits) - 1L;

        /// <summary>The largest count a face can hold before the packing would lose it.</summary>
        public const int MaxExposedPerFace = (int)FaceMask;

        /// <summary>Exposed cell faces in one direction.</summary>
        public int GetExposedFaces(int face)
        {
            return (int)((exposedFaces >> (face * FaceBits)) & FaceMask);
        }

        /// <summary>
        /// Sets one direction's count. A count past what the packing holds is clamped rather than
        /// wrapped: losing the high bits would silently turn a fully exposed face into a bare one,
        /// and the clamp is unreachable for any block the game ships.
        ///
        /// <para>
        /// **Nothing under `Data/Scripts` calls this any more, and it stays anyway.** The exposure
        /// stage went to <see cref="SetExposedFaces(int[])"/> in Pass 9, Iteration 6; this overload
        /// and <see cref="RefreshExposure()"/> together are the *oracle* that overload is pinned
        /// against (`D8`), which is a job only the code that was replaced can do. A `D2` sweep for
        /// what is reached by nothing will find it, and this paragraph is the answer: it is reached
        /// by `FacePackingTests`, on purpose, and deleting it unpins an optimisation rather than
        /// removing dead weight.
        /// </para>
        /// </summary>
        public void SetExposedFaces(int face, int count)
        {
            if (count < 0) count = 0;
            if (count > MaxExposedPerFace) count = MaxExposedPerFace;

            int shift = face * FaceBits;
            exposedFaces = (exposedFaces & ~(FaceMask << shift)) | ((long)count << shift);
        }

        /// <summary>Total exposed cell faces.</summary>
        public int TotalExposedFaces { get; private set; }

        /// <summary>Total exposed area, m^2.</summary>
        public float ExposedArea { get; private set; }

        /// <summary>Emissivity * Stefan-Boltzmann * exposed area, precomputed.</summary>
        public float RadiationCoefficient { get; private set; }

        /// <summary>Waste heat from power and thrust, W. Recomputed when the host reports a change.</summary>
        public float HeatGenerationWatts { get; private set; }

        /// <summary>
        /// Set whenever a value the solver mirrors into its own arrays changes, and cleared by the
        /// solver when it reads the change, so a step re-reads only the nodes that moved.
        /// </summary>
        public bool StateDirty = true;

        /// <summary>
        /// True while this node is waiting for its conduction links to be built.
        ///
        /// Read only by the incremental builder, to decide which end of a pair of newly placed
        /// neighbours adds the link between them. Both ends see each other as a neighbour, so
        /// without it the pair would be added twice and the joint would conduct double.
        /// </summary>
        public bool PendingLinks;

        /// <summary>
        /// How many conduction links touch this node. A count rather than a list of indices: the
        /// solver walks the link array, not a node's links, so a per-node list would be a heap
        /// object per block rewritten on every topology change and read by nothing.
        /// </summary>
        public int LinkCount;

        // ---- last-step diagnostics ---------------------------------------------------------

        public float LastConductionWatts;
        public float LastRadiationWatts;
        public float LastConvectionWatts;
        public float LastSolarWatts;
        public float LastFrictionWatts;

        /// <summary>Watts from mod-registered point heat sources, summed over every source.</summary>
        public float LastHeatSourceWatts;

        /// <summary>Watts exchanged with the air of the rooms this block faces.</summary>
        public float LastRoomWatts;

        public float LastDeltaTemperature;

        private readonly float cellFaceArea;

        /// <summary>
        /// Divisor applied to heat capacity. See <see cref="ThermalSettings.HeatTimeScale"/>: specific
        /// heat is stated in real J/(kg K), and this converts real thermal time into playable
        /// thermal time.
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

        public ThermalNode(BlockInstance block, float gridSize, float initialTemperature)
            : this(block, gridSize, initialTemperature, 1f)
        {
        }

        public ThermalNode(BlockInstance block, float gridSize, float initialTemperature, float heatTimeScale)
        {
            if (block == null) throw new ArgumentNullException("block");

            Block = block;
            Temperature = initialTemperature;
            this.heatTimeScale = heatTimeScale > 0f ? heatTimeScale : 1f;
            cellFaceArea = gridSize * gridSize * Math.Max(0f, block.Thermal.ExposedSurfaceMultiplier);

            RefreshThermalMass();
            RefreshExposure();
            RefreshHeatGeneration();
        }

        public BlockThermalProperties Thermal
        {
            get { return Block.Thermal; }
        }

        /// <summary>
        /// Area of one of this block's cell faces, m^2, including the definition's
        /// <c>ExposedSurfaceMultiplier</c>. The unit every area in the simulation is measured in.
        /// </summary>
        public float CellFaceArea
        {
            get { return cellFaceArea; }
        }

        /// <summary>
        /// Recomputes heat capacity from the block's current mass. Call after the host reports a
        /// mass change — build progress, damage, or inventory.
        /// </summary>
        public void RefreshThermalMass()
        {
            float mass = Math.Max(0f, Block.Mass);

            // Held coolant is stated in real J/K like the block's own, so it goes through the same
            // divide. Storing the divided figure instead would leave it on whatever clock was
            // running when the ring broke, and a world that changed `HeatTimeScale` afterwards
            // would carry one capacity on two clocks with nothing saying so.
            float capacity = (((Thermal.SpecificHeat * mass) + heldCoolantCapacity) / heatTimeScale);
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, capacity);
            StateDirty = true;
        }

        /// <summary>Recomputes the exposure-derived values from the packed face counts.</summary>
        public void RefreshExposure()
        {
            int total = 0;
            for (int i = 0; i < Face.Count; i++)
            {
                total += GetExposedFaces(i);
            }

            SetTotalAndDerived(total);
        }

        /// <summary>
        /// Sets all six counts from one array and refreshes what they derive, in one walk.
        ///
        /// <para>
        /// **This is the exposure stage's inner loop, and two thirds of the stage was in it.**
        /// Setting a face at a time is a read-modify-write of <see cref="exposedFaces"/> per face —
        /// six of them, each dependent on the last — followed by <see cref="RefreshExposure"/>,
        /// which unpacks all six again to total them. Both loops are over a compile-time constant
        /// six, over data the caller already holds in a local array, and the field they contend
        /// over is on a heap object being streamed at a hundred and twenty-six thousand nodes a
        /// pass. Packed into a local and stored once, the six writes become one and the total falls
        /// out of the same walk. See performance.md, Pass 9, Iteration 1 for the ablation that
        /// found it, and Iteration 2 for what it was worth.
        /// </para>
        ///
        /// <para>
        /// The clamp is per face and identical to <see cref="SetExposedFaces"/>'s, which is what
        /// lets `FacePackingTests` hold the two together: a count past what ten bits hold is
        /// clamped rather than wrapped, because losing the high bits would silently turn a fully
        /// exposed face into a bare one.
        /// </para>
        /// </summary>
        /// <param name="countsByFace">At least <see cref="Face.Count"/> counts, indexed by face.</param>
        public void SetExposedFaces(int[] countsByFace)
        {
            if (countsByFace == null || countsByFace.Length < Face.Count)
            {
                throw new ArgumentException("countsByFace must have at least six entries");
            }

            long packed = 0L;
            int total = 0;

            for (int face = 0; face < Face.Count; face++)
            {
                int count = countsByFace[face];
                if (count < 0) count = 0;
                if (count > MaxExposedPerFace) count = MaxExposedPerFace;

                packed |= (long)count << (face * FaceBits);
                total += count;
            }

            exposedFaces = packed;
            SetTotalAndDerived(total);
        }

        /// <summary>
        /// The four values a change to the face counts derives, written once. Shared by the two
        /// ways of setting them so neither can drift from the other (`D3`).
        /// </summary>
        private void SetTotalAndDerived(int total)
        {
            TotalExposedFaces = total;
            ExposedArea = total * cellFaceArea;
            RadiationCoefficient = Thermal.Emissivity * ThermalConstants.StefanBoltzmann * ExposedArea;
            StateDirty = true;
        }

        /// <summary>Recomputes waste heat from the block's current power figures.</summary>
        public void RefreshHeatGeneration()
        {
            float produced = Math.Max(0f, Block.PowerProducedWatts) * Thermal.ProducerWasteEnergy;
            float consumed = (Math.Max(0f, Block.PowerConsumedWatts) + Math.Max(0f, Block.ThrustWatts))
                * Thermal.ConsumerWasteEnergy;
            // A block may be hot because of what it is, not only because of the power crossing it.
            HeatGenerationWatts = produced + consumed + Math.Max(0f, Thermal.HeatSourceWatts);
            StateDirty = true;
        }

        /// <summary>Total stored energy above absolute zero, J. Used by conservation checks.</summary>
        public float Energy
        {
            get { return Temperature * ThermalMass; }
        }

        public override string ToString()
        {
            return Block.Name + " T=" + Temperature.ToString("n2") + "K";
        }
    }
}
