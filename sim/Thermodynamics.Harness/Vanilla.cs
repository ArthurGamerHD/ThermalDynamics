using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Space Engineers' own numbers, transcribed so this repository can balance against them
    /// without a game install.
    ///
    /// The mod's blocks are only meaningful next to the blocks a player already has. "Is the
    /// radiator worth its mass?" has no answer in isolation; it has a clear one against the light
    /// armour block it displaces. Everything here exists so those comparisons are made against the
    /// real figures rather than against remembered ones.
    ///
    /// **These are transcribed, not derived at runtime.** The game is not installed on every
    /// machine that runs this suite, so the numbers are checked in and
    /// <c>BalanceTests.TheVanillaReferenceStillMatchesTheInstalledGame</c> re-derives them from
    /// <c>Content/Data</c> when a copy *is* present and fails if they have drifted. That is the
    /// same arrangement <see cref="Census"/> uses for its field observations, and for the same
    /// reason: a transcription nobody checks becomes fiction.
    ///
    /// To refresh after a game update, run that test and copy the values it reports.
    ///
    /// Transcribed from Space Engineers 1, Content/Data/CubeBlocks/*.sbc and Components.sbc,
    /// on 2026-08-19.
    /// </summary>
    public static class Vanilla
    {
        /// <summary>
        /// Component masses in kilograms, from <c>Content/Data/Components.sbc</c>.
        ///
        /// A block's mass is the sum of its build components, which is why these matter: the
        /// simulation's heat capacity is <c>mass x specific heat</c>, so getting a mass wrong
        /// scales every temperature that block ever reaches. The mod's own definitions are costed
        /// in these same components, so <see cref="ShippedBlocks"/> uses this table to derive the
        /// real mass of a radiator rather than carrying a hand-written guess.
        /// </summary>
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
        };

        /// <summary>Total mass in kilograms of a component list, or 0 for an unknown component.</summary>
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

        /// <summary>One vanilla block, reduced to the fields a balance comparison needs.</summary>
        public class Block
        {
            public string Subtype;
            public string TypeId;
            public bool Large;
            public Vector3I Size;

            /// <summary>Kilograms, summed from the build components.</summary>
            public float Mass;

            public int Pcu;
            public float BuildSeconds;

            /// <summary>Rated electrical output, megawatts. Reactors and batteries.</summary>
            public float PowerOutputMegawatts;

            /// <summary>Rated electrical draw, megawatts. Thrusters.</summary>
            public float PowerDrawMegawatts;

            /// <summary>
            /// Build cost, as component name and count. Transcribed with the rest of this table,
            /// and checked against the install by
            /// <c>TheVanillaReferenceStillMatchesTheInstalledGame</c>.
            ///
            /// It is here because these blocks' thermal properties are *derived* from it — see
            /// <see cref="BlockThermalDerivation"/> — and the derivation has to produce the same
            /// answer on a machine with no game on it as on one with the game installed.
            /// </summary>
            public string[] ComponentNames = new string[0];
            public int[] ComponentCounts = new int[0];

            /// <summary>The build cost priced with <see cref="ComponentMasses"/>.</summary>
            public List<BlockComponent> Components
            {
                get
                {
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

            /// <summary>What this block is thermally, derived from its build cost and its type.</summary>
            public BlockThermalProperties Thermal
            {
                get { return BlockThermalDerivation.Derive(Components, TypeId); }
            }

            public int CellCount
            {
                get { return Size.X * Size.Y * Size.Z; }
            }

            /// <summary>Grid cell edge in metres — 2.5 for large, 0.5 for small.</summary>
            public float GridSize
            {
                get { return Large ? Catalog.LargeGridSize : Catalog.SmallGridSize; }
            }
        }

        private static Block Row(string subtype, string typeId, bool large, int x, int y, int z,
            float mass, int pcu, float buildSeconds, float outputMw, float drawMw, string components = "")
        {
            List<string> names = new List<string>();
            List<int> counts = new List<int>();

            // "SteelPlate:20 MetalGrid:5" — the same order and totals the definition lists, with
            // repeated components summed, since only the total matters to a mass blend.
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
                Size = new Vector3I(x, y, z),
                Mass = mass,
                Pcu = pcu,
                BuildSeconds = buildSeconds,
                PowerOutputMegawatts = outputMw,
                PowerDrawMegawatts = drawMw,
                ComponentNames = names.ToArray(),
                ComponentCounts = counts.ToArray(),
            };
        }

        /// <summary>
        /// The comparison set: the two structural blocks a player builds hulls out of, and the
        /// three families that put heat into a ship. Those are the only vanilla blocks a
        /// thermal balance needs — the rest are neither the load nor the alternative.
        /// </summary>
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
        };

        /// <summary>The reference block with this subtype, or null.</summary>
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
