using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One dial, moved one step at a time, with everything else held still.
    ///
    /// <para>
    /// The corpus survey answers "what do ships do at the shipped settings". It cannot answer "what
    /// happens if I halve emissivity", and re-simulating 8,142 ships per value of per dial is weeks.
    /// This is the other instrument: a small standing panel, chosen for spread rather than for size,
    /// run across a grid of configurations so that each dial gets a measured response curve instead
    /// of an argument.
    /// </para>
    ///
    /// <para>
    /// **One at a time, and that is a real limitation.** Each configuration moves a single dial off
    /// the shipped value, so what comes out is a set of partial derivatives and not a model of the
    /// whole space. Two dials moved together will not in general do what their curves added
    /// together say — emissivity and exposed area multiply in the radiation term, and heat capacity
    /// and waste fraction trade against each other in the time constant. The curves are for finding
    /// the range a dial should live in, and for ranking dials against each other. They are a
    /// ballpark for anything beyond that, and the report says so.
    /// </para>
    ///
    /// <para>
    /// **Each dial is measured only where it can act.** Sweeping the solar constant through the
    /// shadow scenarios would spend hours proving that a dial nothing reads does nothing. A knob
    /// declares its own scenarios, and the cost of the sweep is roughly the sum over knobs of levels
    /// times scenarios times the panel, which is about three hours rather than a week.
    /// </para>
    /// </summary>
    public static class KnobLab
    {
        /// <summary>The three states every knob is measured under: parked, loaded, burning.</summary>
        private static readonly string[] Core = { "idle", "full-electrical", "burn-forward" };

        /// <summary>Where sunlight is actually delivered.</summary>
        private static readonly string[] Sunlit = { "vacuum-sunlit", "surface-hot-noon" };

        /// <summary>Where the hull is moving through air, so friction has a term.</summary>
        private static readonly string[] Moving = { "flight-100", "reentry" };

        /// <summary>Emissivity is absorptivity too, so it is measured in sun as well as in shadow.</summary>
        private static readonly string[] CoreAndSun =
            { "idle", "full-electrical", "burn-forward", "vacuum-sunlit" };

        /// <summary>One dial and the values it is swept through.</summary>
        public class Knob
        {
            public string Name;

            /// <summary>What moving it is supposed to change, printed with the results.</summary>
            public string Intent;

            /// <summary>True when levels multiply the shipped value; false when they replace it.</summary>
            public bool Multiplier;

            /// <summary>The shipped value, so a curve can be read against where the mod sits now.</summary>
            public float Shipped;

            public float[] Levels;

            public string[] Scenarios;

            /// <summary>Rewrites a block's materials for this level, or null for a world dial.</summary>
            public Func<float, Func<BlockThermalProperties, BlockThermalProperties>> Material;

            /// <summary>Rewrites the world for this level, or null for a block dial.</summary>
            public Action<ThermalSettings, float> World;
        }

        /// <summary>One knob at one level: a runnable configuration.</summary>
        public class Configuration
        {
            public Knob Knob;
            public float Level;
            public string[] Scenarios;

            /// <summary>True where this level is the shipped value, so it doubles as a control.</summary>
            public bool IsShipped;

            public ThermalSettings Settings()
            {
                ThermalSettings settings = new ThermalSettings();
                if (Knob.World != null) Knob.World(settings, Level);
                return settings.Derive();
            }

            public Func<BlockThermalProperties, BlockThermalProperties> Material()
            {
                return Knob.Material == null ? null : Knob.Material(Level);
            }

            public override string ToString()
            {
                return Knob.Name + "=" + Level.ToString("0.###");
            }
        }

        /// <summary>
        /// Copies a block's properties, so an override never mutates the shared derived instance.
        /// The models cache is keyed by subtype and handed out to every ship; writing through it
        /// would leak one configuration's world into the next.
        /// </summary>
        private static BlockThermalProperties Copy(BlockThermalProperties source)
        {
            return new BlockThermalProperties
            {
                Conductivity = source.Conductivity,
                SpecificHeat = source.SpecificHeat,
                Emissivity = source.Emissivity,
                ExposedSurfaceMultiplier = source.ExposedSurfaceMultiplier,
                ProducerWasteEnergy = source.ProducerWasteEnergy,
                ConsumerWasteEnergy = source.ConsumerWasteEnergy,
                CriticalTemperature = source.CriticalTemperature,
                OverheatDamagePerKelvin = source.OverheatDamagePerKelvin,
                HeatSourceWatts = source.HeatSourceWatts,
                ExcludeFromSimulation = source.ExcludeFromSimulation,
            };
        }

        /// <summary>Builds a block dial from a setter, so each knob below is one line of intent.</summary>
        private static Func<float, Func<BlockThermalProperties, BlockThermalProperties>> Block(
            Action<BlockThermalProperties, float> set)
        {
            return level => source =>
            {
                BlockThermalProperties copy = Copy(source);
                set(copy, level);
                return copy;
            };
        }

        private static readonly float[] Quarters = { 0.25f, 0.5f, 1f, 2f, 4f };

        public static List<Knob> Knobs()
        {
            List<Knob> knobs = new List<Knob>();

            // ---- the block dials ---------------------------------------------------------------

            // Heat capacity is the time constant. The survey's finding is that damage arrives in
            // six to nine seconds of play, and this is the dial that acts on *when* rather than on
            // *where it ends up* — a hull with four times the capacity reaches the same temperature
            // four times more slowly.
            knobs.Add(new Knob
            {
                Name = "specific-heat",
                Intent = "how long a block takes to move — the time constant",
                Multiplier = true, Shipped = 1f, Levels = Quarters, Scenarios = Core,
                Material = Block((p, x) => p.SpecificHeat *= x),
            });

            // Emissivity sets both how hard a block radiates and how much sun it takes, which is
            // why it is measured in the light as well as in shadow: raising it cools a shadowed
            // hull and heats a sunlit one, and the two effects have to be seen together.
            knobs.Add(new Knob
            {
                Name = "emissivity",
                Intent = "radiating strength, and solar absorption with it",
                Multiplier = true, Shipped = 1f, Levels = Quarters, Scenarios = CoreAndSun,
                Material = Block((p, x) => p.Emissivity = Math.Min(1f, p.Emissivity * x)),
            });

            // Conduction is what moves heat out of the block that made it, and the survey says that
            // is exactly what fails: a median of two blocks over critical with the hull around them
            // cold. If any dial addresses concentration rather than total heat, it is this one.
            knobs.Add(new Knob
            {
                Name = "conductivity",
                Intent = "how fast heat leaves the block that made it",
                Multiplier = true, Shipped = 1f, Levels = Quarters, Scenarios = Core,
                Material = Block((p, x) => p.Conductivity *= x),
            });

            // Radiating area, independent of emissivity, so the two halves of the radiation term
            // can be told apart.
            knobs.Add(new Knob
            {
                Name = "exposed-surface",
                Intent = "radiating area per block, apart from emissivity",
                Multiplier = true, Shipped = 1f, Levels = new[] { 0.5f, 1f, 2f, 4f },
                Scenarios = Core,
                Material = Block((p, x) => p.ExposedSurfaceMultiplier *= x),
            });

            // The heat a generator makes. HydrogenEngine ships at 0.60 and is the hottest block on
            // a third of runaway rows, so this dial and the panel's engine-heavy hulls are the
            // pairing the survey pointed at.
            knobs.Add(new Knob
            {
                Name = "producer-waste",
                Intent = "share of generated power that becomes heat",
                Multiplier = true, Shipped = 1f, Levels = Quarters, Scenarios = Core,
                Material = Block((p, x) => p.ProducerWasteEnergy *= x),
            });

            knobs.Add(new Knob
            {
                Name = "consumer-waste",
                Intent = "share of drawn power that becomes heat",
                Multiplier = true, Shipped = 1f, Levels = Quarters, Scenarios = Core,
                Material = Block((p, x) => p.ConsumerWasteEnergy *= x),
            });

            // Critical temperature does not change any heat flow; it moves the line a block has to
            // cross to start dying. It is in the sweep because it is the cheapest dial to move and
            // the one most likely to be reached for, and its curve should be read against the
            // others as the one that changes the verdict without changing the physics.
            knobs.Add(new Knob
            {
                Name = "critical-temperature",
                Intent = "where damage begins — moves the verdict, not the heat",
                Multiplier = true, Shipped = 1f, Levels = new[] { 0.5f, 0.75f, 1f, 1.5f, 2f },
                Scenarios = Core,
                Material = Block((p, x) => p.CriticalTemperature *= x),
            });

            // ---- the world dials ---------------------------------------------------------------

            // The largest departure from physics in the model and the one that makes it a game.
            // Swept around the shipped 225 rather than multiplied, because the interesting question
            // is which value to ship and not what a factor does.
            knobs.Add(new Knob
            {
                Name = "heat-time-scale",
                Intent = "seconds of physical time per second of play",
                Multiplier = false, Shipped = 225f,
                Levels = new[] { 25f, 56f, 112f, 225f, 450f, 900f }, Scenarios = Core,
                World = (s, x) => s.HeatTimeScale = x,
            });

            knobs.Add(new Knob
            {
                Name = "solar-energy",
                Intent = "watts per square metre from the sun",
                Multiplier = false, Shipped = 1000f,
                Levels = new[] { 0f, 500f, 1000f, 1361f, 2000f }, Scenarios = Sunlit,
                World = (s, x) => s.SolarEnergy = x,
            });

            knobs.Add(new Knob
            {
                Name = "vacuum-temperature",
                Intent = "the floor a hull radiates towards",
                Multiplier = false, Shipped = 2.7f,
                Levels = new[] { 2.7f, 50f, 150f, 250f },
                Scenarios = new[] { "idle", "vacuum-sunlit" },
                World = (s, x) => s.VacuumTemperature = x,
            });

            knobs.Add(new Knob
            {
                Name = "friction-scale",
                Intent = "how hard airflow heats the leading face",
                Multiplier = true, Shipped = 1f,
                Levels = new[] { 0f, 0.5f, 1f, 2f, 4f }, Scenarios = Moving,
                World = (s, x) => s.FrictionScale *= x,
            });

            knobs.Add(new Knob
            {
                Name = "friction-threshold",
                Intent = "the speed friction starts at, m/s",
                Multiplier = false, Shipped = 50f,
                Levels = new[] { 10f, 25f, 50f, 100f }, Scenarios = Moving,
                World = (s, x) => s.FrictionAtSpeedsAbove = x,
            });

            return knobs;
        }

        /// <summary>
        /// The planetary and orbital states, run once at the shipped settings.
        ///
        /// Not a dial: a world is a place rather than a number, so what these answer is "where does
        /// a hull sit in each environment the game has", which is the control the dials above are
        /// read against.
        /// </summary>
        public static readonly string[] Environments =
        {
            "vacuum-shadow", "vacuum-sunlit", "orbit-cycling", "surface-hot-noon",
            "surface-cold-night", "surface-windy", "underground", "storm-parked", "reentry",
        };

        /// <summary>Every configuration the sweep runs, in a deterministic order.</summary>
        public static List<Configuration> All()
        {
            List<Configuration> configurations = new List<Configuration>();

            foreach (Knob knob in Knobs())
            {
                foreach (float level in knob.Levels)
                {
                    configurations.Add(new Configuration
                    {
                        Knob = knob,
                        Level = level,
                        Scenarios = knob.Scenarios,
                        IsShipped = knob.Multiplier
                            ? Math.Abs(level - 1f) < 1e-6f
                            : Math.Abs(level - knob.Shipped) < 1e-6f,
                    });
                }
            }

            return configurations;
        }
    }
}
