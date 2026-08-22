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

        /// <summary>Exposed cell faces, per face direction.</summary>
        public readonly int[] ExposedFaces = new int[Face.Count];

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
            float capacity = (Thermal.SpecificHeat * mass) / heatTimeScale;
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, capacity);
            StateDirty = true;
        }

        /// <summary>Recomputes the exposure-derived values from <see cref="ExposedFaces"/>.</summary>
        public void RefreshExposure()
        {
            int total = 0;
            for (int i = 0; i < Face.Count; i++)
            {
                total += ExposedFaces[i];
            }

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
