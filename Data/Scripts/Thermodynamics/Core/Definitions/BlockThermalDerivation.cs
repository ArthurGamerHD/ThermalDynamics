using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>One line of a block's build cost: a component, how many, and what each weighs.</summary>
    public struct BlockComponent
    {
        public string Component;
        public int Count;

        /// <summary>Kilograms per unit, from the game's own component definition.</summary>
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

    /// <summary>
    /// What a block is thermally, derived from what it is built out of.
    ///
    /// Material properties — conductivity, specific heat, emissivity, critical temperature — follow
    /// from the build components the game publishes for every block, so a modded block gets
    /// properties describing it rather than describing armour.
    ///
    /// Functional properties — the waste fractions, the exposed-area multiplier, the damage rate —
    /// follow from what a block does with power, which its build cost cannot say. Those live in
    /// <c>Data/Cubes.xml</c> as per-type entries, so a player can override them without touching
    /// code; this class no longer holds an opinion about any of them.
    /// </summary>
    public static class BlockThermalDerivation
    {
        /// <summary>
        /// The material properties of a block with this build cost. A block with no priced
        /// components comes back as mild steel.
        /// </summary>
        public static BlockThermalProperties Derive(IList<BlockComponent> components)
        {
            return Material(components).Clamp();
        }

        /// <summary>
        /// The material half: a mass-weighted blend over the block's components.
        ///
        /// **Specific heat is exact.** Heat capacity is additive, so the capacity of a block is the
        /// sum of its components' capacities and the mass-weighted mean specific heat is the right
        /// answer rather than an approximation of one.
        ///
        /// **Conductivity and critical temperature are approximations, deliberately.** Conduction
        /// through a composite depends on how the phases are arranged — a copper wire through a
        /// steel block is not the same as copper powder mixed into it — and nothing in a build cost
        /// says which. A mass-weighted mean is monotone, cheap and has no arrangement to get wrong.
        /// Critical temperature is the same choice for a different reason: the honest rule is that
        /// a block fails when its weakest significant part fails, but taken literally that puts
        /// every block containing a single computer at the silicon limit, which is a cliff rather
        /// than a gradient. Weighting by mass lets a component that dominates a block dominate its
        /// limit, and lets one small part of it not.
        ///
        /// **Emissivity is neither.** It is a property of the surface, not of the bulk, so a block
        /// radiates like whatever it is clad in. Cladding is taken as the heaviest component, on
        /// the grounds that what a block is mostly made of is usually what you can see.
        /// </summary>
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
