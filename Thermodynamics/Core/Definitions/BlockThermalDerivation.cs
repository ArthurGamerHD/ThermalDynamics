using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Represents a single component type in a block, with quantity and mass.
    /// Used for deriving block thermal properties from material composition.
    /// </summary>
    public struct BlockComponent
    {
        /// <summary>
        /// The material/component name (e.g., "SteelPlate", "Computer").
        /// Used to look up thermal properties from BlockMaterials.
        /// </summary>
        public string Component;

        /// <summary>
        /// Number of components of this type in the block.
        /// </summary>
        public int Count;

        /// <summary>
        /// Mass of a single component in kilograms.
        /// </summary>
        public float MassEach;

        /// <summary>
        /// Total mass of this component type (Count × MassEach).
        /// </summary>
        public float Mass
        {
            get { return Count * MassEach; }
        }


        /// <summary>
        /// Creates a new BlockComponent instance.
        /// </summary>
        /// <param name="component">The material/component name.</param>
        /// <param name="count">Number of components.</param>
        /// <param name="massEach">Mass of each component in kilograms.</param>
        public BlockComponent(string component, int count, float massEach)
        {
            Component = component;
            Count = count;
            MassEach = massEach;
        }
    }

    /// <summary>
    /// Derives thermal properties for a block based on its component composition.
    /// Performs weighted averaging of material properties and clamps values to valid ranges.
    /// </summary>
    public static class BlockThermalDerivation
    {
        /// <summary>
        /// Calculates waste heat from a device's efficiency.
        /// Waste = 1 - Efficiency (assuming power input is normalized to 1).
        /// </summary>
        /// <param name="efficiency">Device efficiency as a fraction (0 to 1).</param>
        /// <returns>Waste fraction. Returns -1 if efficiency is invalid.</returns>
        public static float WasteFromEfficiency(float efficiency)
        {
            if (efficiency <= 0f || efficiency > 1f) return -1f;

            return 1f - efficiency;
        }


        /// <summary>
        /// Derives complete thermal properties for a block from its components.
        /// Performs material averaging and applies validation clamping.
        /// </summary>
        /// <param name="components">List of block components with their masses.</param>
        /// <returns>A fully configured BlockThermalProperties with clamped values.</returns>
        public static BlockThermalProperties Derive(IList<BlockComponent> components)
        {
            return Material(components).Clamp();
        }


        /// <summary>
        /// Calculates thermal properties from component materials using mass-weighted averages.
        /// The cladding (heaviest component) determines emissivity, while all components
        /// contribute to conductivity, specific heat, and service limit proportionally to their mass.
        /// </summary>
        /// <param name="components">List of block components with their masses.</param>
        /// <returns>A BlockThermalProperties with calculated (but unclamped) values.</returns>
        /// <remarks>
        /// The derivation uses weighted averages:
        ///   Conductivity = Σ(conductivity_i × mass_i) / totalMass
        ///   SpecificHeat = Σ(specificHeat_i × mass_i) / totalMass
        ///   ServiceLimit = Σ(serviceLimit_i × mass_i) / totalMass
        ///   Emissivity = emissivity_of_heaviest_component
        /// If no components are provided or total mass is zero, defaults to Steel properties.
        /// </remarks>
        public static BlockThermalProperties Material(IList<BlockComponent> components)
        {
            BlockThermalProperties properties = BlockThermalProperties.Default();

            float mass = 0f;
            float conductivity = 0f;
            float specificHeat = 0f;
            float serviceLimit = 0f;

            float heaviest = 0f;
            BlockMaterial cladding = BlockMaterials.Steel;

            if (components != null)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    BlockComponent line = components[i];
                    if (line.Mass <= 0f) continue;

                    BlockMaterial material = BlockMaterials.Get(line.Component);

                    mass += line.Mass;
                    conductivity += material.Conductivity * line.Mass;
                    specificHeat += material.SpecificHeat * line.Mass;
                    serviceLimit += material.ServiceLimit * line.Mass;

                    // Track the heaviest component for emissivity (cladding determines radiation)
                    if (line.Mass > heaviest)
                    {
                        heaviest = line.Mass;
                        cladding = material;
                    }
                }
            }

            // Fallback to steel properties if no valid components
            if (mass <= 0f)
            {
                properties.Conductivity = BlockMaterials.Steel.Conductivity;
                properties.SpecificHeat = BlockMaterials.Steel.SpecificHeat;
                properties.Emissivity = BlockMaterials.Steel.Emissivity;
                properties.CriticalTemperature = BlockMaterials.Steel.ServiceLimit;
                return properties;
            }

            // Calculate weighted averages
            properties.Conductivity = conductivity / mass;
            properties.SpecificHeat = specificHeat / mass;
            properties.CriticalTemperature = serviceLimit / mass;
            properties.Emissivity = cladding.Emissivity;

            return properties;
        }
    }
}
