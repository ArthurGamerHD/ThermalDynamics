using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public struct BlockMaterial
    {
        public float Conductivity;

        public float SpecificHeat;

        public float Emissivity;

        public float ServiceLimit;

        public bool Invented;
    }

    public static class BlockMaterials
    {
        public static readonly BlockMaterial Steel = new BlockMaterial
        {
            Conductivity = ReferenceMaterials.MildSteel.Conductivity,
            SpecificHeat = ReferenceMaterials.MildSteel.SpecificHeat,
            Emissivity = 0.15f,
            ServiceLimit = 900f,
        };

/// <summary>Builds the method table.</summary>
        private static readonly Dictionary<string, BlockMaterial> Table = Build();

        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }

/// <summary>Returns the .</summary>
        public static BlockMaterial Get(string component)
        {
            BlockMaterial material;
            return component != null && Table.TryGetValue(component, out material) ? material : Steel;
        }

/// <summary>IsKnown operation.</summary>
        public static bool IsKnown(string component)
        {
            return component != null && Table.ContainsKey(component);
        }

/// <summary>Adds a .</summary>
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

/// <summary>Adds a of.</summary>
        private static void AddOf(Dictionary<string, BlockMaterial> table, string component,
            ReferenceMaterials.Reference material, float emissivity, float serviceLimit)
        {
            Add(table, component, material.Conductivity, material.SpecificHeat,
                emissivity, serviceLimit);
        }

/// <summary>Builds the method table.</summary>
        private static Dictionary<string, BlockMaterial> Build()
        {
            Dictionary<string, BlockMaterial> t = new Dictionary<string, BlockMaterial>();

            AddOf(t, "SteelPlate", ReferenceMaterials.MildSteel, 0.15f, 900f);
            AddOf(t, "Construction", ReferenceMaterials.MildSteel, 0.15f, 900f);
            AddOf(t, "SmallTube", ReferenceMaterials.MildSteel, 0.20f, 900f);
            AddOf(t, "LargeTube", ReferenceMaterials.MildSteel, 0.20f, 900f);
            AddOf(t, "MetalGrid", ReferenceMaterials.MildSteel, 0.30f, 900f);
            AddOf(t, "Girder", ReferenceMaterials.MildSteel, 0.25f, 900f);

            Add(t, "InteriorPlate", 40f, 500f, 0.35f, 800f);

            AddOf(t, "BulletproofGlass", ReferenceMaterials.SodaLimeGlass, 0.92f, 800f);

            Add(t, "Motor", 120f, 420f, 0.20f, 450f);

            Add(t, "Superconductor", 350f, 390f, 0.05f, 400f);

            Add(t, "Computer", 15f, 700f, 0.85f, 400f);
            Add(t, "Detector", 15f, 700f, 0.80f, 400f);
            Add(t, "RadioCommunication", 30f, 700f, 0.60f, 400f);
            Add(t, "Display", 1.5f, 800f, 0.90f, 400f);

            Add(t, "SolarCell", 30f, 700f, 0.85f, 400f);

            Add(t, "PowerCell", 3.0f, 1000f, 0.85f, 360f);

            Add(t, "Reactor", 30f, 600f, 0.25f, 1200f);

            Add(t, "Thrust", 15f, 500f, 0.40f, 1600f);

            Add(t, "Explosives", 0.3f, 1400f, 0.90f, 450f);

            Add(t, "Medical", 3f, 1500f, 0.90f, 350f);

            Add(t, "EngineerPlushie", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "EngineerPlushieSE2", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "SabiroidPlushie", 0.05f, 1300f, 0.95f, 500f);

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

/// <summary>RealRange operation.</summary>
        public static void RealRange(Func<BlockMaterial, float> property, out float lowest, out float highest)
        {
            lowest = float.MaxValue;
            highest = float.MinValue;

            foreach (BlockMaterial material in Table.Values)
            {
                if (material.Invented) continue;

/// <summary>property operation.</summary>
                float value = property(material);
                if (value < lowest) lowest = value;
                if (value > highest) highest = value;
            }
        }
    }
}
