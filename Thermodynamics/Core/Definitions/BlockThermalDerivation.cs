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
    /// What a block is thermally, derived from what it is built out of. Material properties follow
    /// from the components the game already publishes; the functional ones follow from what a block
    /// does with power, which its build cost cannot say, and live in <c>Data/Cubes.xml</c> per type.
    /// See definitions.md, Where a block's properties come from.
    /// </summary>
    public static class BlockThermalDerivation
    {
        /// <summary>
        /// The waste fraction a block's own stated charging efficiency implies, or -1 where the
        /// game states none.
        ///
        /// <para>
        /// **The one functional property the game publishes.** A block's build cost cannot say what
        /// it does with power, which is why the functional properties are authored per type in
        /// `Cubes.xml` — but the jump drive's definition carries `PowerEfficiency`, and the power a
        /// charging block draws and does not store is heat. So this is derivation rather than
        /// authoring, and it covers a modded drive nobody has written an entry for.
        /// </para>
        ///
        /// <para>
        /// **It is here rather than in either adapter** because the mod reads the efficiency off a
        /// live definition and the harness reads it out of the `.sbc`, and one rule with two
        /// readers is what stops the two answering differently (`P5`).
        /// </para>
        /// </summary>
        public static float WasteFromEfficiency(float efficiency)
        {
            // Zero is *not stated* rather than *wastes everything*: the field is absent on almost
            // every definition in the game, and reading its absence as total loss would make every
            // block a heater. Above one is a definition claiming to store more than it draws.
            if (efficiency <= 0f || efficiency > 1f) return -1f;

            return 1f - efficiency;
        }

        /// <summary>
        /// The material properties of a block with this build cost. A block with no priced
        /// components comes back as mild steel.
        /// </summary>
        public static BlockThermalProperties Derive(IList<BlockComponent> components)
        {
            return Material(components).Clamp();
        }

        /// <summary>
        /// The material half: a mass-weighted blend over the block's components. **Specific heat is
        /// exact**, since capacity is additive. **Conductivity and critical temperature are
        /// approximations**, deliberately — a build cost does not say how the phases are arranged.
        /// **Emissivity is neither**: it belongs to the surface, so it comes from the heaviest
        /// component. See definitions.md, Where a block's properties come from.
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
