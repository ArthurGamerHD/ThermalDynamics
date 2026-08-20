using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// What one build component is made of, thermally.
    ///
    /// Conductivity and specific heat are real units, the same ones a materials table gives, and
    /// the game's pace is set once elsewhere — in <c>ThermalConstants.ConductionScale</c> and
    /// <c>HeatTimeScale</c> — so nothing here is a balance figure.
    /// </summary>
    public struct BlockMaterial
    {
        /// <summary>Thermal conductivity, W/(m K).</summary>
        public float Conductivity;

        /// <summary>Specific heat capacity, J/(kg K).</summary>
        public float SpecificHeat;

        /// <summary>Grey-body emissivity, 0..1. Also used as solar absorptivity.</summary>
        public float Emissivity;

        /// <summary>
        /// Kelvin at which the material stops doing its job — not its melting point.
        ///
        /// Steel melts at 1,700 K and has lost half its yield strength by 850. Motor windings are
        /// limited by their insulation, not their copper. Silicon runs to about 425. A block's
        /// critical temperature is derived from these, so this is where the interesting spread
        /// between block types comes from: a block full of electronics is genuinely more fragile
        /// than a block of steel, and now says so.
        /// </summary>
        public float ServiceLimit;

        /// <summary>
        /// True when the component has no real-world counterpart and these figures are invented.
        ///
        /// Kept as a flag rather than a comment because it is the honest answer to "where did this
        /// number come from" for a third of the table, and a reader deserves to be told which
        /// third. `EveryInventedMaterialSitsInsideTheRangeOfTheRealOnes` keeps the invention modest.
        /// </summary>
        public bool Invented;
    }

    /// <summary>
    /// The material properties of every component Space Engineers builds blocks out of.
    ///
    /// This is the basis for <see cref="BlockThermalDerivation"/>, and through it for every value in
    /// `Data/Cubes.xml`. A block's thermal properties were previously an opinion per block type;
    /// they are now a consequence of what the block is built from, which is a fact the game already
    /// publishes and which no one has to maintain by hand.
    ///
    /// Real figures are ordinary engineering references — mild steel 50 W/(m K) and 466 J/(kg K),
    /// soda-lime glass 1.0 and 840, copper 400 and 385, lithium-ion cells about 1,000 J/(kg K).
    /// Where a component is invented, so are its numbers, and <see cref="BlockMaterial.Invented"/>
    /// says which.
    ///
    /// Free of any Space Engineers type, so the whole derivation is testable outside a session.
    /// </summary>
    public static class BlockMaterials
    {
        /// <summary>
        /// Mild steel: the fallback for any component this table does not know, including
        /// components added by a future game update or by another mod.
        /// </summary>
        public static readonly BlockMaterial Steel = new BlockMaterial
        {
            Conductivity = 50f,
            SpecificHeat = 466f,
            Emissivity = 0.15f,
            ServiceLimit = 900f,
        };

        private static readonly Dictionary<string, BlockMaterial> Table = Build();

        public static ICollection<string> Names
        {
            get { return Table.Keys; }
        }

        /// <summary>The material for a component, or <see cref="Steel"/> when it is unknown.</summary>
        public static BlockMaterial Get(string component)
        {
            BlockMaterial material;
            return component != null && Table.TryGetValue(component, out material) ? material : Steel;
        }

        public static bool IsKnown(string component)
        {
            return component != null && Table.ContainsKey(component);
        }

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

        private static Dictionary<string, BlockMaterial> Build()
        {
            Dictionary<string, BlockMaterial> t = new Dictionary<string, BlockMaterial>();

            // ---- structural steel ------------------------------------------------------------
            // Mild steel throughout. They differ only in surface: a flat plate is smoother than a
            // mesh or a girder, and emissivity is a surface property.
            Add(t, "SteelPlate", 50f, 466f, 0.15f, 900f);
            Add(t, "Construction", 50f, 466f, 0.15f, 900f);
            Add(t, "SmallTube", 50f, 466f, 0.20f, 900f);
            Add(t, "LargeTube", 50f, 466f, 0.20f, 900f);
            Add(t, "MetalGrid", 50f, 466f, 0.30f, 900f);
            Add(t, "Girder", 50f, 466f, 0.25f, 900f);

            // A thin painted interior panel. Lighter gauge and a painted face, which is why it
            // radiates far better than bare plate — paint of any colour runs about 0.9.
            Add(t, "InteriorPlate", 40f, 500f, 0.35f, 800f);

            // ---- glass -----------------------------------------------------------------------
            // Soda-lime glass: 1.0 W/(m K), 840 J/(kg K), emissivity 0.92. Two orders of magnitude
            // below steel in conductivity, which is the single largest material distinction in the
            // game and the reason windows deserve an entry of their own.
            Add(t, "BulletproofGlass", 1.0f, 840f, 0.92f, 800f);

            // ---- electrical ------------------------------------------------------------------
            // Copper windings on steel laminations, roughly half and half by mass. Limited by the
            // winding insulation — class H is 453 K — long before either metal cares.
            Add(t, "Motor", 120f, 420f, 0.20f, 450f);

            // Copper-stabilised filament. Very conductive, bright, and it quenches when warm.
            Add(t, "Superconductor", 350f, 390f, 0.05f, 400f);

            // Epoxy board, silicon and a little copper. The epoxy sets the bulk properties and the
            // silicon sets the limit: a junction is done at about 425 K.
            Add(t, "Computer", 15f, 700f, 0.85f, 400f);
            Add(t, "Detector", 15f, 700f, 0.80f, 400f);
            Add(t, "RadioCommunication", 30f, 700f, 0.60f, 400f);
            Add(t, "Display", 1.5f, 800f, 0.90f, 400f);

            // Encapsulated silicon under glass, so it behaves far more like the glass than like
            // the wafer. Dark and deliberately absorptive, which is what a solar cell is for.
            Add(t, "SolarCell", 30f, 700f, 0.85f, 400f);

            // Lithium-ion: about 1,000 J/(kg K), poorly conductive across the stack, and in thermal
            // runaway by 420 K. The most fragile thing in ordinary use.
            Add(t, "PowerCell", 3.0f, 1000f, 0.85f, 360f);

            // ---- reactive and refractory -----------------------------------------------------
            // Fuel in a graphite and steel assembly. Built to run hot, which is the whole point.
            Add(t, "Reactor", 30f, 600f, 0.25f, 1200f);

            // A nozzle alloy in the Inconel family: about 15 W/(m K), 500 J/(kg K), and oxidised
            // to a high emissivity by use.
            Add(t, "Thrust", 15f, 500f, 0.40f, 1600f);

            // Chemical explosive. Cooks off well below anything structural.
            Add(t, "Explosives", 0.3f, 1400f, 0.90f, 450f);

            // ---- fittings and fluids ---------------------------------------------------------
            // A medical bay is mostly water, plastics and fluids, which is why it holds so much
            // heat per kilogram and tolerates so little.
            Add(t, "Medical", 3f, 1500f, 0.90f, 350f);

            // ---- soft goods ------------------------------------------------------------------
            // Fabric and stuffing. Almost an insulator, and it holds a great deal of heat for its
            // mass. Also the fix for a real defect: a plushie was being simulated as a kilogram of
            // steel with a heat capacity of 2 J/K, which made it the stiffest object on a fleet and
            // set the substep count for whole capital ships. See docs/field-tuning.md.
            Add(t, "EngineerPlushie", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "EngineerPlushieSE2", 0.05f, 1300f, 0.95f, 500f);
            Add(t, "SabiroidPlushie", 0.05f, 1300f, 0.95f, 500f);

            // ---- invented --------------------------------------------------------------------
            // Nothing below has a real counterpart. The figures are chosen inside the range the
            // real materials above already span, so an invented component can flavour a block but
            // cannot take it anywhere the rest of the table could not.
            Add(t, "GravityGenerator", 60f, 450f, 0.20f, 1000f, true);
            Add(t, "ZoneChip", 15f, 700f, 0.80f, 1000f, true);

            // Prototech: recovered alloys, better than anything built from ore. Slightly more
            // conductive than steel, a little more heat-tolerant, and in the cooling unit's case
            // deliberately built to move heat.
            Add(t, "PrototechFrame", 80f, 500f, 0.25f, 1500f, true);
            Add(t, "PrototechPanel", 90f, 500f, 0.30f, 1500f, true);
            Add(t, "PrototechMachinery", 70f, 480f, 0.25f, 1500f, true);
            Add(t, "PrototechCircuitry", 25f, 700f, 0.80f, 600f, true);
            Add(t, "PrototechCapacitor", 20f, 800f, 0.60f, 600f, true);
            Add(t, "PrototechPropulsionUnit", 20f, 520f, 0.40f, 1600f, true);
            Add(t, "PrototechCoolingUnit", 200f, 700f, 0.60f, 1200f, true);

            return t;
        }

        /// <summary>The lowest and highest figure any *real* material in the table carries.</summary>
        public static void RealRange(Func<BlockMaterial, float> property, out float lowest, out float highest)
        {
            lowest = float.MaxValue;
            highest = float.MinValue;

            foreach (BlockMaterial material in Table.Values)
            {
                if (material.Invented) continue;

                float value = property(material);
                if (value < lowest) lowest = value;
                if (value > highest) highest = value;
            }
        }
    }
}
