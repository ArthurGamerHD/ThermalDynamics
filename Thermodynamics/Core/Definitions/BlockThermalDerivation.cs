using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public struct BlockComponent
    {
        public string Component;
        public int Count;

        public float MassEach;

        public float Mass
        {
            get { return Count * MassEach; }
        }


        public BlockComponent(string component, int count, float massEach)
        {
            Component = component;
            Count = count;
            MassEach = massEach;
        }
    }

    public static class BlockThermalDerivation
    {

        public static float WasteFromEfficiency(float efficiency)
        {
            if (efficiency <= 0f || efficiency > 1f) return -1f;

            return 1f - efficiency;
        }


        public static BlockThermalProperties Derive(IList<BlockComponent> components)
        {
            return Material(components).Clamp();
        }


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

                    if (line.Mass > heaviest)
                    {
                        heaviest = line.Mass;
                        cladding = material;
                    }
                }
            }

            if (mass <= 0f)
            {
                properties.Conductivity = BlockMaterials.Steel.Conductivity;
                properties.SpecificHeat = BlockMaterials.Steel.SpecificHeat;
                properties.Emissivity = BlockMaterials.Steel.Emissivity;
                properties.CriticalTemperature = BlockMaterials.Steel.ServiceLimit;
                return properties;
            }

            properties.Conductivity = conductivity / mass;
            properties.SpecificHeat = specificHeat / mass;
            properties.CriticalTemperature = serviceLimit / mass;
            properties.Emissivity = cladding.Emissivity;

            return properties;
        }
    }
}
