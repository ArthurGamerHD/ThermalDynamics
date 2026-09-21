using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class Vanilla
    {
        public static readonly Dictionary<string, float> ComponentMasses = new Dictionary<string, float>
        {
            { "SteelPlate", 20f },
            { "Construction", 8f },
            { "LargeTube", 25f },
            { "SmallTube", 4f },
            { "Motor", 24f },
            { "Computer", 0.2f },
            { "MetalGrid", 6f },
            { "InteriorPlate", 3f },
            { "Superconductor", 15f },
            { "PowerCell", 25f },
            { "Reactor", 25f },
            { "Detector", 5f },
            { "RadioCommunication", 8f },
            { "BulletproofGlass", 15f },
            { "Girder", 6f },
            { "Display", 8f },
            { "Medical", 150f },
            { "SolarCell", 6f },
            { "Explosives", 2f },
            { "Thrust", 40f },
            { "GravityGenerator", 800f },
            { "Canvas", 15f },
            { "ZoneChip", 0.25f },

            { "PrototechFrame", 100f },
            { "PrototechPanel", 30f },
            { "PrototechCapacitor", 20f },
            { "PrototechPropulsionUnit", 240f },
            { "PrototechMachinery", 80f },
            { "PrototechCircuitry", 60f },
            { "PrototechCoolingUnit", 250f },
        };

/// <summary>MassOf operation.</summary>
        public static float MassOf(IEnumerable<KeyValuePair<string, int>> components)
        {
            float total = 0f;
            foreach (KeyValuePair<string, int> component in components)
            {
                float each;
                if (ComponentMasses.TryGetValue(component.Key, out each)) total += each * component.Value;
            }
            return total;
        }

        public class Block
        {
            public string Subtype;
            public string TypeId;
            public bool Large;
            public Vector3I Size;

            public float Mass;

            public int Pcu;
            public float BuildSeconds;

            public float PowerOutputMegawatts;

            public float PowerDrawMegawatts;

            public float StandbyDrawMegawatts;

            public string[] ComponentNames = new string[0];
            public int[] ComponentCounts = new int[0];

            public List<BlockComponent> Components
            {
                get
                {
/// <summary>List operation.</summary>
                    List<BlockComponent> components = new List<BlockComponent>();

                    for (int i = 0; i < ComponentNames.Length && i < ComponentCounts.Length; i++)
                    {
                        float mass;
                        if (!ComponentMasses.TryGetValue(ComponentNames[i], out mass)) continue;

                        components.Add(new BlockComponent(ComponentNames[i], ComponentCounts[i], mass));
                    }

                    return components;
                }
            }

            public BlockThermalProperties Thermal
            {
                get { return ShippedBlocks.DeriveWithFunction(Components, TypeId); }
            }

            public int CellCount
            {
                get { return Size.X * Size.Y * Size.Z; }
            }

            public float GridSize
            {
                get { return Large ? Catalog.LargeGridSize : Catalog.SmallGridSize; }
            }
        }

/// <summary>Row operation.</summary>
        private static Block Row(string subtype, string typeId, bool large, int x, int y, int z,
            float mass, int pcu, float buildSeconds, float outputMw, float drawMw,
            string components = "", float standbyMw = 0f)
        {
/// <summary>List operation.</summary>
            List<string> names = new List<string>();
/// <summary>List operation.</summary>
            List<int> counts = new List<int>();

            foreach (string part in components.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] halves = part.Split(':');
                if (halves.Length != 2) continue;

                int count;
                if (!int.TryParse(halves[1], out count)) continue;

                int existing = names.IndexOf(halves[0]);
                if (existing >= 0) counts[existing] += count;
                else { names.Add(halves[0]); counts.Add(count); }
            }

            return new Block
            {
                Subtype = subtype,
                TypeId = typeId,
                Large = large,
/// <summary>Vector3I operation.</summary>
                Size = new Vector3I(x, y, z),
                Mass = mass,
                Pcu = pcu,
                BuildSeconds = buildSeconds,
                PowerOutputMegawatts = outputMw,
                PowerDrawMegawatts = drawMw,
                StandbyDrawMegawatts = standbyMw,
                ComponentNames = names.ToArray(),
                ComponentCounts = counts.ToArray(),
            };
        }

        public static readonly Block[] Reference = new Block[]
        {
            Row("LargeBlockArmorBlock", "CubeBlock", true, 1, 1, 1, 500f, 1, 8f, 0f, 0f,
                "SteelPlate:20 SteelPlate:5"),
            Row("LargeHeavyBlockArmorBlock", "CubeBlock", true, 1, 1, 1, 3300f, 1, 20f, 0f, 0f,
                "SteelPlate:135 MetalGrid:50 SteelPlate:15"),
            Row("SmallBlockArmorBlock", "CubeBlock", false, 1, 1, 1, 20f, 1, 3f, 0f, 0f,
                "SteelPlate:1"),
            Row("SmallHeavyBlockArmorBlock", "CubeBlock", false, 1, 1, 1, 112f, 1, 6f, 0f, 0f,
                "SteelPlate:4 MetalGrid:2 SteelPlate:1"),
            Row("LargeBlockSmallGenerator", "Reactor", true, 1, 1, 1, 4793f, 25, 40f, 15f, 0f,
                "SteelPlate:50 Construction:40 MetalGrid:4 LargeTube:8 Reactor:100 Motor:6 Computer:25 SteelPlate:30"),
            Row("LargeBlockLargeGenerator", "Reactor", true, 3, 3, 3, 73795f, 25, 100f, 300f, 0f,
                "SteelPlate:800 Construction:70 MetalGrid:40 LargeTube:40 Superconductor:100 Reactor:2000 Motor:20 Computer:75 SteelPlate:200"),
            Row("SmallBlockSmallGenerator", "Reactor", false, 1, 1, 1, 278f, 25, 20f, 0.5f, 0f,
                "SteelPlate:1 Construction:10 MetalGrid:2 LargeTube:1 Reactor:3 Motor:1 Computer:10 SteelPlate:2"),
            Row("SmallBlockLargeGenerator", "Reactor", false, 3, 3, 3, 3901f, 25, 30f, 14.75f, 0f,
                "SteelPlate:40 Construction:9 MetalGrid:9 LargeTube:3 Reactor:95 Motor:5 Computer:25 SteelPlate:20"),
            Row("LargeBlockBatteryBlock", "BatteryBlock", true, 1, 1, 1, 3845f, 15, 40f, 12f, 0f,
                "SteelPlate:20 Construction:10 PowerCell:80 Computer:25 Construction:20 SteelPlate:60"),
            Row("SmallBlockBatteryBlock", "BatteryBlock", false, 3, 2, 3, 1040.4f, 15, 20f, 4f, 0f,
                "SteelPlate:5 Construction:2 PowerCell:20 Computer:2 Construction:3 SteelPlate:20"),
            Row("LargeBlockSmallThrust", "Thrust", true, 1, 1, 2, 4380f, 12, 40f, 0f, 3.36f,
                "SteelPlate:15 Construction:40 LargeTube:8 Thrust:80 Construction:20 SteelPlate:10"),
            Row("LargeBlockLargeThrust", "Thrust", true, 3, 2, 4, 43200f, 12, 90f, 0f, 33.6f,
                "SteelPlate:100 Construction:70 LargeTube:40 Thrust:960 Construction:30 SteelPlate:50"),
            Row("SmallBlockSmallThrust", "Thrust", false, 1, 1, 2, 121f, 12, 10f, 0f, 0.2f,
                "SteelPlate:1 Construction:1 LargeTube:1 Thrust:1 Construction:1 SteelPlate:1"),
            Row("SmallBlockLargeThrust", "Thrust", false, 3, 2, 4, 721f, 12, 20f, 0f, 2.4f,
                "SteelPlate:1 Construction:1 LargeTube:5 Thrust:12 Construction:1 SteelPlate:4"),
            Row("", "OxygenGenerator", true, 1, 2, 1, 2587f, 50, 22f, 0f, 0.5f,
                "SteelPlate:110 Construction:5 LargeTube:2 Motor:4 Computer:5 SteelPlate:10", 0.001f),
            Row("IrrigationSystem", "OxygenGenerator", true, 1, 1, 2, 2555f, 50, 20f, 0f, 0.25f,
                "SteelPlate:80 Construction:20 LargeTube:10 Motor:6 Computer:5 SteelPlate:20", 0.001f),
            Row("LargeBlockOxygenGeneratorLab", "OxygenGenerator", true, 1, 2, 2, 4157f, 50, 30f, 0f, 0.5f,
                "SteelPlate:160 Construction:20 LargeTube:4 Motor:4 Computer:5 BulletproofGlass:40", 0.001f),
            Row("LargeBlockPrototechOxygenGenerator", "OxygenGenerator", true, 3, 2, 1, 6574f, 50, 100f, 0f, 1f,
                "PrototechFrame:1 PrototechPanel:100 Construction:40 PrototechCircuitry:10 LargeTube:10"
                + " PrototechMachinery:10 Computer:20 PrototechPanel:50", 0.005f),
            Row("OxygenGeneratorSmall", "OxygenGenerator", false, 3, 3, 2, 298.6f, 50, 14f, 0f, 0.1f,
                "SteelPlate:6 Construction:8 LargeTube:2 Motor:1 Computer:3 SteelPlate:2", 0.001f),
            Row("SmallBlockOxygenGeneratorLab", "OxygenGenerator", false, 2, 3, 3, 303.6f, 50, 14f, 0f, 0.1f,
                "SteelPlate:6 Construction:8 LargeTube:2 Motor:1 Computer:3 BulletproofGlass:3", 0.001f),
        };

/// <summary>Find operation.</summary>
        public static Block Find(string subtype)
        {
            foreach (Block block in Reference)
            {
                if (block.Subtype == subtype) return block;
            }
            return null;
        }
    }
}
