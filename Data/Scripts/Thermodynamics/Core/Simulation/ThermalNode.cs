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
        /// Set whenever a value the solver mirrors into its own arrays changes. The solver
        /// clears it when it picks the change up, so a step only re-reads the nodes that
        /// actually moved rather than all of them.
        /// </summary>
        public bool StateDirty = true;

        /// <summary>
        /// How many conduction links touch this node. A count rather than a list of indices:
        /// the solver walks links, never a node's links, so the list was one heap object per
        /// block rewritten on every topology change and read by nothing.
        /// </summary>
        public int LinkCount;

        // ---- last-step diagnostics ---------------------------------------------------------

        public float LastConductionWatts;
        public float LastRadiationWatts;
        public float LastConvectionWatts;
        public float LastSolarWatts;
        public float LastFrictionWatts;
        public float LastDeltaTemperature;

        private readonly float cellFaceArea;

        /// <summary>
        /// Heat capacity is divided by this. See <see cref="ThermalSettings.HeatTimeScale"/>:
        /// specific heat is stated in real J/(kg K), and this is what turns real thermal time
        /// into playable thermal time.
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
            cellFaceArea = gridSize * gridSize * Math.Max(0f, block.Thermal.SurfaceAreaScaler);

            RefreshThermalMass();
            RefreshExposure();
            RefreshHeatGeneration();
        }

        public BlockThermalProperties Thermal
        {
            get { return Block.Thermal; }
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

        /// <summary>
        /// Recomputes waste heat from the block's current power figures.
        /// </summary>
        public void RefreshHeatGeneration()
        {
            float produced = Math.Max(0f, Block.PowerProducedWatts) * Thermal.ProducerWasteEnergy;
            float consumed = (Math.Max(0f, Block.PowerConsumedWatts) + Math.Max(0f, Block.ThrustWatts))
                * Thermal.ConsumerWasteEnergy;
            HeatGenerationWatts = produced + consumed;
            StateDirty = true;
        }

        /// <summary>
        /// Average effectiveness of this node's exposed faces against a direction, 0..1.
        /// A face pointing straight into the direction contributes 1, a face at right angles 0.
        /// </summary>
        public float DirectionalIntensity(ref Vector3 directionLocal)
        {
            if (TotalExposedFaces == 0) return 0f;

            float intensity = 0f;
            for (int face = 0; face < Face.Count; face++)
            {
                int count = ExposedFaces[face];
                if (count == 0) continue;

                float dot = Vector3.Dot(Face.Normals[face], directionLocal);
                if (dot > 0f) intensity += dot * count;
            }

            return intensity / TotalExposedFaces;
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
