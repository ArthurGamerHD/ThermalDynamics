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

        /// <summary>Conduction links to other nodes. Each link appears on both of its nodes.</summary>
        public readonly List<int> LinkIndices = new List<int>();

        // ---- last-step diagnostics ---------------------------------------------------------

        public float LastConductionWatts;
        public float LastRadiationWatts;
        public float LastConvectionWatts;
        public float LastSolarWatts;
        public float LastFrictionWatts;
        public float LastDeltaTemperature;

        private readonly float cellFaceArea;

        public ThermalNode(BlockInstance block, float gridSize, float initialTemperature)
        {
            if (block == null) throw new ArgumentNullException("block");

            Block = block;
            Temperature = initialTemperature;
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
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, Thermal.SpecificHeat * mass);
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
