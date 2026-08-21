using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The block mix of a real ship, measured, so that a synthetic hull behaves like one.
    ///
    /// <para>
    /// The benchmarks used to build a ship out of 3,300 kg heavy armour with one 200 kg grating in
    /// eight, and that hull was wrong in the one way that mattered most. A step is divided into as
    /// many substeps as the <em>stiffest</em> block needs, so the cost of a grid is decided by its
    /// lightest block and not by its average one — and the lightest block in that catalogue was
    /// twelve times heavier than the lightest block on a real ship. The benchmark hull asked for
    /// 2.25 substeps where a field dump measured 21 to 31, which means every scale figure this
    /// repository ever published was taken on a ship an order of magnitude softer than the ones it
    /// claimed to describe.
    /// </para>
    ///
    /// <para>
    /// The tiers below are the block population of a 1,381-block ship, taken from the block-type
    /// table of the telemetry dump of 19 August 2026 and bucketed by heat capacity. They are not a
    /// guess at what a ship contains; they are what one contained. A hull built from them asks for
    /// about the substeps a real hull asks for, and — more subtly — has the same *shape* of
    /// distribution, which is what decides how many blocks a substep cap reaches.
    /// </para>
    ///
    /// <para>
    /// Refine this as more dumps arrive. One ship is one ship: the tiers are a better hypothesis
    /// than heavy armour and gratings, not a census of Space Engineers. What should stay true is
    /// the method — read the population out of a field report and build against it, rather than
    /// choosing blocks that make a benchmark convenient.
    /// </para>
    /// </summary>
    public static class Census
    {
        /// <summary>One measured band of the population.</summary>
        public class Tier
        {
            /// <summary>What the band is, in the terms a reader would recognise.</summary>
            public string Name;

            /// <summary>Share of the blocks on the ship, 0..1.</summary>
            public float Share;

            /// <summary>Mean mass of the band, kg. Heat capacity follows from specific heat.</summary>
            public float Mass;

            /// <summary>Mean conduction quality of the band.</summary>
            public float Conductivity;

            /// <summary>Mean temperature the band takes damage above, K.</summary>
            public float CriticalTemperature;

            /// <summary>The most populous subtype in the band, for the record.</summary>
            public string Example;
        }

        /// <summary>
        /// The measured bands, heaviest share first. Shares sum to one.
        ///
        /// Read as: a third of a ship is ordinary large armour at about 440 kg, and the tail that
        /// decides the substep count is the one per cent below 64 J/K — the light fittings.
        /// </summary>
        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "light fitting",   Share = 0.012f, Mass =    20f, Conductivity = 50f, CriticalTemperature =  900f, Example = "SmallLight" },
            new Tier { Name = "armour tip",      Share = 0.068f, Mass =    59f, Conductivity = 50f, CriticalTemperature =  900f, Example = "LargeBlockArmorCorner2Tip" },
            new Tier { Name = "armour slope",    Share = 0.147f, Mass =   108f, Conductivity = 50f, CriticalTemperature =  900f, Example = "LargeBlockArmorSlope2Tip" },
            new Tier { Name = "half armour",     Share = 0.104f, Mass =   194f, Conductivity = 50f, CriticalTemperature =  900f, Example = "LargeHalfArmorBlock" },
            new Tier { Name = "light armour",    Share = 0.347f, Mass =   440f, Conductivity = 50f, CriticalTemperature =  900f, Example = "LargeBlockArmorBlock" },
            new Tier { Name = "conveyor",        Share = 0.157f, Mass =   750f, Conductivity = 50f, CriticalTemperature =  900f, Example = "ConveyorTubeDuctT" },
            new Tier { Name = "heavy armour",    Share = 0.135f, Mass =  2430f, Conductivity = 56.667f, CriticalTemperature =  931f, Example = "LargeHeavyBlockArmorBlock" },
            new Tier { Name = "machinery",       Share = 0.030f, Mass = 11281f, Conductivity = 55f, CriticalTemperature =  929f, Example = "LargeBlockGyro" },
        };

        /// <summary>
        /// Share of blocks that generate waste heat, and what one of them makes.
        ///
        /// The field ship carried 150 heat producers in 1,381 blocks — eleven per cent — making
        /// 16.7 MW between them. The distribution is very skewed (four large thrusters were a
        /// tenth of it), but for a benchmark what matters is the total and the count, because that
        /// is what sets the equilibrium the hull settles at.
        /// </summary>
        public const float ProducerShare = 0.109f;

        /// <summary>Waste heat one producer makes, W. Measured mean across the field ship.</summary>
        public const float ProducerWatts = 111000f;

        /// <summary>
        /// Temperature above which a producer is damaged, K. Thrusters run hotter than structure
        /// and are rated for it — the field ship's thrusters were rated 1,050 K against 900 K for
        /// everything bolted around them, and they were the blocks that got hot.
        /// </summary>
        public const float ProducerCriticalTemperature = 1050f;

        /// <summary>Mass of a heat producer, kg. The field ship's thrusters and reactors.</summary>
        public const float ProducerMass = 2430f;

        private static BlockModel producer;

        /// <summary>
        /// The block a heat producer is, as distinct from the structure around it.
        ///
        /// Modelling producers as "whichever tier the cell landed on" was wrong in a way that
        /// only showed up once damage was being tested: it made a 900 K-rated armour block the
        /// thing generating 111 kW, so the hull burned at powers the field ship runs at happily.
        /// On a real ship the things making heat are the things rated to take it.
        /// </summary>
        public static BlockModel Producer()
        {
            if (producer != null) return producer;

            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.Conductivity = 56.667f;
            thermal.CriticalTemperature = ProducerCriticalTemperature;

            producer = BlockModel.Solid("producer", Vector3I.One, ProducerMass, thermal);
            return producer;
        }

        /// <summary>
        /// What the field reports actually said, kept beside the tiers they were derived from.
        ///
        /// These are the numbers the synthetic ship is supposed to reproduce, and
        /// <c>CensusFidelityTests</c> holds it to them. Updating the census when a new dump
        /// arrives means editing both — the tiers to match the new population, and these to match
        /// what that population was observed to do — and the tests then say whether the two
        /// still agree.
        /// </summary>
        public static class Field
        {
            /// <summary>Substeps a full step was measured to need, across the ships seen so far.</summary>
            public const float LeastDemand = 21.35f;   // STR Hound, 1,293 blocks, 19 Aug
            public const float MostDemand = 31.25f;    // UNSC Infinity, 42,051 blocks, 18 Aug

            /// <summary>
            /// Share of blocks a cap would raise, from the projection in the 19 Aug single-ship
            /// dump. The shape of this curve is what decides which cap is safe to ship, so it is
            /// the property a synthetic hull most needs to get right.
            /// </summary>
            public const float RaisedAtCap8 = 0.0116f;
            public const float RaisedAtCap4 = 0.0601f;
            public const float RaisedAtCap2 = 0.2368f;
            public const float RaisedAtCap1 = 0.3910f;

            /// <summary>
            /// Hottest block a ship reached under its own power, K, and what it was rated for.
            /// Two sessions failed to reach a critical temperature; this is how close they came.
            /// </summary>
            public const float HottestObserved = 938.9f;
            public const float HottestRating = 1050f;
        }

        private static BlockModel[] models;

        /// <summary>One block model per tier, built once.</summary>
        public static BlockModel[] Models()
        {
            if (models != null) return models;

            BlockModel[] built = new BlockModel[Tiers.Length];
            for (int i = 0; i < Tiers.Length; i++)
            {
                Tier tier = Tiers[i];

                BlockThermalProperties thermal = Catalog.DefaultThermal();
                thermal.Conductivity = tier.Conductivity;
                thermal.CriticalTemperature = tier.CriticalTemperature;

                built[i] = BlockModel.Solid(tier.Name, Vector3I.One, tier.Mass, thermal);
            }

            models = built;
            return models;
        }

        /// <summary>
        /// The tier the block at <paramref name="index"/> belongs to, walking the population in
        /// the measured proportions.
        ///
        /// Deterministic and stateless: a low-discrepancy walk rather than a random draw, so the
        /// mix is exactly right at every prefix rather than only in expectation, and two runs of
        /// the same benchmark build the same ship. That matters more than it sounds — the tail
        /// tier is one block in eighty, so a sampler that is merely unbiased can leave a small
        /// hull with no light fittings at all and quietly measure the wrong thing.
        /// </summary>
        public static int TierAt(int index)
        {
            // Walk the cumulative distribution at the position this block occupies in it.
            float position = ((index * 2654435761u) % 1000000u) / 1000000f;

            float running = 0f;
            for (int i = 0; i < Tiers.Length; i++)
            {
                running += Tiers[i].Share;
                if (position < running) return i;
            }

            return Tiers.Length - 1;
        }

        /// <summary>True when the block at <paramref name="index"/> is a heat producer.</summary>
        public static bool ProducesHeatAt(int index)
        {
            int period = (int)Math.Round(1f / ProducerShare);
            return period > 0 && (index % period) == 0;
        }

        /// <summary>
        /// Places one block per cell in the measured proportions, and returns the builder.
        ///
        /// Heat generation is left to the caller: a hull that is cooling and a hull that is being
        /// driven are different measurements, and only one of them wants sources.
        /// </summary>
        public static GridBuilder PlaceCensus(this GridBuilder builder, IEnumerable<Vector3I> cells)
        {
            BlockModel[] tiers = Models();
            BlockModel source = Producer();

            int index = 0;
            foreach (Vector3I cell in cells)
            {
                // A producer stands in place of whatever tier the cell would have held, so the
                // remaining tiers keep their proportions to each other and the hull keeps its
                // block count.
                builder.Place(ProducesHeatAt(index) ? source : tiers[TierAt(index)], cell);
                index++;
            }

            return builder;
        }

        /// <summary>
        /// Switches on the measured share of heat producers, each making
        /// <see cref="ProducerWatts"/> of waste heat.
        /// </summary>
        /// <returns>How many blocks were made producers.</returns>
        public static int DriveCensus(ThermalSimulation simulation)
        {
            return DriveCensus(simulation, ProducerWatts);
        }

        /// <summary>
        /// The same, at a chosen wattage. The census figure settles a hull below its rating, which
        /// is what the field measured; multiplying it is how a benchmark reaches the other side of
        /// a critical temperature without changing anything else about the ship.
        /// </summary>
        public static int DriveCensus(ThermalSimulation simulation, float watts)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int producers = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!ProducesHeatAt(i)) continue;

                // The waste fraction is what turns power into heat, so the power a producer draws
                // is the heat it wants divided by that fraction.
                float waste = nodes[i].Thermal.ProducerWasteEnergy;
                if (waste <= 0f) continue;

                nodes[i].Block.PowerProducedWatts = watts / waste;
                nodes[i].RefreshHeatGeneration();
                producers++;
            }

            return producers;
        }
    }
}
