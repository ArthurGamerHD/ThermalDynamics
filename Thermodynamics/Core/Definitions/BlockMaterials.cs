using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Represents a physical material with thermal properties for block simulation.
    /// </summary>
    public struct BlockMaterial
    {
        /// <summary>
        /// Thermal conductivity in W/m·K (Watts per meter per Kelvin).
        /// Higher values mean heat transfers more easily through the material.
        /// </summary>
        public float Conductivity;

        /// <summary>
        /// Specific heat capacity in J/kg·K (Joules per kilogram per Kelvin).
        /// Amount of energy required to raise the temperature of 1kg of material by 1K.
        /// </summary>
        public float SpecificHeat;

        /// <summary>
        /// Thermal emissivity (dimensionless, 0.0 to 1.0).
        /// Efficiency of emitting thermal radiation. High emissivity = good radiator.
        /// </summary>
        public float Emissivity;

        /// <summary>
        /// Maximum service temperature in Kelvin before material degradation.
        /// Used as a basis for calculating CriticalTemperature in blocks.
        /// </summary>
        public float ServiceLimit;

        /// <summary>
        /// True if this material is from an invented/advanced technology.
        /// These are filtered out when computing real-world property ranges.
        /// </summary>
        public bool Invented;
    }

    /// <summary>
    /// Material database for all block types in the simulation.
    /// Maps component names to their thermal properties.
    /// </summary>
    public static class BlockMaterials
    {
        /// <summary>
        /// Default steel material used as fallback when component is not found.
        /// Based on MildSteel reference properties with emissivity 0.15 and service limit 900K.
        /// </summary>
        public static readonly BlockMaterial Steel = new BlockMaterial
        {
            Conductivity = ReferenceMaterials.MildSteel.Conductivity,
            SpecificHeat = ReferenceMaterials.MildSteel.SpecificHeat,
            Emissivity = 0.15f,
            ServiceLimit = 900f,
        };

        // Lookup table built from static initialization
        private static readonly Dictionary<string, BlockMaterial> Table = Build();

        /// <summary>
        /// Gets the names of all known materials in the database.
        /// Returns a collection of component names that can be looked up.
        /// </summary>
        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }

        /// <summary>
        /// Looks up a block material by component name.
        /// Returns the material if found, otherwise returns Steel as a safe default.
        /// </summary>
        /// <param name="component">The component name (case-insensitive lookup via Build()).</param>
        /// <returns>The BlockMaterial configuration, or Steel if not found.</returns>
        public static BlockMaterial Get(string component)
        {
            BlockMaterial material;
            return component != null && Table.TryGetValue(component, out material) ? material : Steel;
        }

        /// <summary>
        /// Checks if a component name exists in the material database.
        /// </summary>
        /// <param name="component">The component name to check.</param>
        /// <returns>True if the component is known, false otherwise.</returns>
        public static bool IsKnown(string component)
        {
            return component != null && Table.ContainsKey(component);
        }

        /// <summary>
        /// Adds a material definition to the lookup table.
        /// </summary>
        /// <param name="table">The dictionary to populate.</param>
        /// <param name="component">The component name key.</param>
        /// <param name="conductivity">Thermal conductivity in W/m·K.</param>
        /// <param name="specificHeat">Specific heat capacity in J/kg·K.</param>
        /// <param name="emissivity">Thermal emissivity (0.0 to 1.0).</param>
        /// <param name="serviceLimit">Maximum service temperature in Kelvin.</param>
        /// <param name="invented">True if this is an invented/advanced material.</param>
        private static void Add(Dictionary<string, BlockMaterial> table, string component,
            float conductivity, float specificHeat, float emissivity, float serviceLimit,
            bool invented = false)
        {
            table[component] = new BlockMaterial
            {
                Conductivity = conductivity,
                SpecificHeat = specificHeat,
                Emissivity = emissivity,
                ServiceLimit = serviceLimit,
                Invented = invented,
            };
        }

        /// <summary>
        /// Adds a material definition by copying base properties from a ReferenceMaterial
        /// and overriding emissivity and serviceLimit.
        /// </summary>
        private static void AddOf(Dictionary<string, BlockMaterial> table, string component,
            ReferenceMaterials.Reference material, float emissivity, float serviceLimit)
        {
            Add(table, component, material.Conductivity, material.SpecificHeat,
                emissivity, serviceLimit);
        }

        /// <summary>
        /// Builds the complete material lookup table.
        /// Populates materials for standard components like steel, glass, electronics,
        /// reactors, and advanced inventions.
        /// </summary>
        /// <returns>Initialized dictionary mapping component names to BlockMaterial configurations.</returns>
        private static Dictionary<string, BlockMaterial> Build()
        {
            Dictionary<string, BlockMaterial> t = new Dictionary<string, BlockMaterial>();

            // Steel-based materials (constructions, tubing, grids, girders)
            // Emissivity increases with surface area (more exposed surfaces)
            AddOf(t, "SteelPlate", ReferenceMaterials.MildSteel, 0.15f, 900f);
            AddOf(t, "Construction", ReferenceMaterials.MildSteel, 0.15f, 900f);
            AddOf(t, "SmallTube", ReferenceMaterials.MildSteel, 0.20f, 900f);
            AddOf(t, "LargeTube", ReferenceMaterials.MildSteel, 0.20f, 900f);
            AddOf(t, "MetalGrid", ReferenceMaterials.MildSteel, 0.30f, 900f);
            AddOf(t, "Girder", ReferenceMaterials.MildSteel, 0.25f, 900f);

            // Interior materials with different properties
            Add(t, "InteriorPlate", 40f, 500f, 0.35f, 800f);

            // Glass (high conductivity, moderate emissivity)
            AddOf(t, "BulletproofGlass", ReferenceMaterials.SodaLimeGlass, 0.92f, 800f);

            // Mechanical components (motors have high conductivity for heat dissipation)
            Add(t, "Motor", 120f, 420f, 0.20f, 450f);

            // Superconductor (very high conductivity, low emissivity)
            Add(t, "Superconductor", 350f, 390f, 0.05f, 400f);

            // Electronic components (lower conductivity, high emissivity for cooling)
            Add(t, "Computer", 15f, 700f, 0.85f, 400f);
            Add(t, "Detector", 15f, 700f, 0.80f, 400f);
            Add(t, "RadioCommunication", 30f, 700f, 0.60f, 400f);
            Add(t, "Display", 1.5f, 800f, 0.90f, 400f);

            // Power generation (solar cells have moderate conductivity)
            Add(t, "SolarCell", 30f, 700f, 0.85f, 400f);

            // Energy storage
            Add(t, "PowerCell", 3.0f, 1000f, 0.85f, 360f);

            // Reactors (high service limit for high-temperature operation)
            Add(t, "Reactor", 30f, 600f, 0.25f, 1200f);

            // Thrust (high service limit for overheating conditions)
            Add(t, "Thrust", 15f, 500f, 0.40f, 1600f);

            // Explosives (very low conductivity, high specific heat for stability)
            Add(t, "Explosives", 0.3f, 1400f, 0.90f, 450f);

            // Medical (low conductivity, high specific heat for stability)
            Add(t, "Medical", 3f, 1500f, 0.90f, 350f);

            // Plushie items (very low conductivity, high specific heat - joke materials)
            Add(t, "EngineerPlushie", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "EngineerPlushieSE2", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "SabiroidPlushie", 0.05f, 1300f, 0.95f, 500f);

            // Invented/advanced technology materials
            Add(t, "GravityGenerator", 60f, 450f, 0.20f, 1000f, true);
            Add(t, "ZoneChip", 15f, 700f, 0.80f, 1000f, true);

            Add(t, "PrototechFrame", 80f, 500f, 0.25f, 1500f, true);
            Add(t, "PrototechPanel", 90f, 500f, 0.30f, 1500f, true);
            Add(t, "PrototechMachinery", 70f, 480f, 0.25f, 1500f, true);
            Add(t, "PrototechCircuitry", 25f, 700f, 0.80f, 600f, true);
            Add(t, "PrototechCapacitor", 20f, 800f, 0.60f, 600f, true);
            Add(t, "PrototechPropulsionUnit", 20f, 520f, 0.40f, 1600f, true);
            Add(t, "PrototechCoolingUnit", 200f, 700f, 0.60f, 1200f, true);

            return t;
        }

        /// <summary>
        /// Computes the range of a given material property across all real (non-invented) materials.
        /// This is used to determine valid ranges for properties like conductivity, emissivity, etc.
        /// </summary>
        /// <param name="property">A function that extracts the property value from a BlockMaterial.</param>
        /// <param name="lowest">Outputs the minimum property value found.</param>
        /// <param name="highest">Outputs the maximum property value found.</param>
        /// <remarks>Materials marked as Invented are excluded from the calculation.</remarks>
        public static void RealRange(Func<BlockMaterial, float> property, out float lowest, out float highest)
        {
            lowest = float.MaxValue;
            highest = float.MinValue;

            foreach (BlockMaterial material in Table.Values)
            {
                // Skip invented materials to get real-world ranges
                if (material.Invented) continue;

                float value = property(material);
                if (value < lowest) lowest = value;
                if (value > highest) highest = value;
            }
        }
    }
}
