using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One block, alone in shadow, run to steady state — bare, or under one cell of light armour.
    ///
    /// <para>
    /// **Two bounds, because one bound is not a decision.** <b>Bare</b> is the most favourable
    /// arrangement that exists: the block alone on its grid, every face radiating to a 2.7 K sky,
    /// nothing else to warm. A waste fraction that cooks a block here cooks it in every build, so
    /// it sets the ceiling. <b>Skinned</b> wraps the same block in one cell of light armour, which
    /// is how a block is actually installed; a fraction the skinned rig survives at full rating is
    /// a fraction nobody ever has to cool, so it sets the floor.
    /// </para>
    ///
    /// <para>
    /// **It is here rather than inside a lab because two labs need the same two bounds.**
    /// <see cref="ReactorLab"/> asks the question of a producer and <see cref="OxygenGeneratorLab"/>
    /// of a consumer, and the only difference between them is which power figure the block carries
    /// — see <see cref="Power"/>. A second copy of this rig would be a second set of run lengths,
    /// a second armour choice and a second definition of *bare*, all of which have to match for the
    /// two families' numbers to be readable against each other.
    /// </para>
    ///
    /// <para>
    /// **The fraction is applied by the solver, not by this class.** The block is given the watts
    /// it produces or draws and its thermal properties carry the fraction, which is the path a real
    /// block's heat takes through <c>ThermalNode.RefreshHeatGeneration</c>. Multiplying the two
    /// here would measure arithmetic rather than the model.
    /// </para>
    /// </summary>
    public static class SoloBlockRig
    {
        /// <summary>Which power figure the block under test carries.</summary>
        public enum Power
        {
            /// <summary>Watts it generates, wasted at <c>ProducerWasteEnergy</c>. Reactors.</summary>
            Produced,

            /// <summary>Watts it draws, wasted at <c>ConsumerWasteEnergy</c>. Everything else.</summary>
            Consumed,
        }

        /// <summary>The vanilla light armour cubes the skin is built from, one per grid size.</summary>
        public const string LargeArmour = "LargeBlockArmorBlock";

        /// <summary>The small-grid half of <see cref="LargeArmour"/>.</summary>
        public const string SmallArmour = "SmallBlockArmorBlock";

        /// <summary>
        /// Seconds of simulated time the rig runs for, and the step it runs at.
        ///
        /// Long enough that every block either settles or is past critical and staying there:
        /// four hours at a twenty-minute step. Named here so the two labs cannot drift onto
        /// different clocks and have their kelvin compared anyway (`M1`).
        /// </summary>
        public const float Seconds = 14400f;

        /// <summary>The step <see cref="Seconds"/> is walked in.</summary>
        public const float StepSeconds = 1200f;

        /// <summary>
        /// Where <paramref name="block"/> settles, kelvin, with <paramref name="thermal"/> as its
        /// material and <paramref name="watts"/> on the power figure <paramref name="power"/> names.
        /// </summary>
        public static float Settled(Vanilla.Block block, BlockThermalProperties thermal,
            float watts, Power power, bool skinned)
        {
            if (block == null) throw new ArgumentNullException("block");

            GridBuilder builder = block.Large ? GridBuilder.Large() : GridBuilder.Small();
            builder.Place(BlockModel.Solid(block.Subtype, block.Size, block.Mass, thermal), Vector3I.Zero);

            if (power == Power.Produced) builder.Producing(watts);
            else builder.Consuming(watts);

            BlockInstance instance = builder.Last;

            // One cell of light armour over every face. The shell's own cells are the surface of a
            // box one larger than the block in each direction, so it wraps it without overlapping,
            // and the block is left with no face on open space.
            if (skinned)
            {
                Vanilla.Block plate = Vanilla.Find(block.Large ? LargeArmour : SmallArmour);
                BlockModel armour = BlockModel.Solid(plate.Subtype, Vector3I.One, plate.Mass,
                    Catalog.DefaultThermal());
                builder.Shell(armour, -Vector3I.One, block.Size + Vector3I.One);
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            ScenarioRunner runner = new ScenarioRunner(simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Track("subject", instance);
            runner.Run(Seconds, StepSeconds);

            float value;
            return runner.Final.Tracked.TryGetValue("subject", out value) ? value : 0f;
        }

        /// <summary>
        /// A copy of <paramref name="source"/>, so a rig can move one property without editing the
        /// definition every other rig reads.
        /// </summary>
        public static BlockThermalProperties Clone(BlockThermalProperties source)
        {
            return new BlockThermalProperties
            {
                ExcludeFromSimulation = source.ExcludeFromSimulation,
                Conductivity = source.Conductivity,
                SpecificHeat = source.SpecificHeat,
                Emissivity = source.Emissivity,
                ExposedSurfaceMultiplier = source.ExposedSurfaceMultiplier,
                ProducerWasteEnergy = source.ProducerWasteEnergy,
                ConsumerWasteEnergy = source.ConsumerWasteEnergy,
                CriticalTemperature = source.CriticalTemperature,
                OverheatDamagePerKelvin = source.OverheatDamagePerKelvin,
            };
        }
    }
}
