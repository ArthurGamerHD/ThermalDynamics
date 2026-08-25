using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One dial, moved one step at a time, with everything else held still, over a standing panel
    /// chosen for spread — because re-simulating 8,142 ships per value per dial is weeks.
    ///
    /// <para>
    /// **One at a time is a real limitation**: what comes out is a set of partial derivatives, and two
    /// dials moved together will not do what their curves added together say. Good for the range a
    /// dial should live in and for ranking dials; a ballpark beyond that, and the report says so.
    /// Each dial is measured only in the scenarios it can act in.
    /// See balance.md, and balance-lab.md, Sweep the settings space.
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

            /// <summary>The block type this dial acts on, or null where it acts on every block.</summary>
            public string OnlyType;

            /// <summary>Rewrites a block's materials for this level, or null for a world dial.</summary>
            public Func<float, Func<string, string, BlockThermalProperties, BlockThermalProperties>> Material;

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

            public Func<string, string, BlockThermalProperties, BlockThermalProperties> Material()
            {
                return Knob.Material == null ? null : Knob.Material(Level);
            }

            public override string ToString()
            {
                return Knob.Name + "=" + Level.ToString("0.###");
            }
        }

        /// <summary>Builds a dial that acts on every block, so each knob below is one line of intent.</summary>
        private static Func<float, Func<string, string, BlockThermalProperties, BlockThermalProperties>> Block(
            Action<BlockThermalProperties, float> set)
        {
            return level => (typeId, subtype, source) =>
            {
                BlockThermalProperties copy = source.Clone();
                set(copy, level);
                return copy;
            };
        }

        /// <summary>
        /// Builds a dial that acts on one block type and leaves the rest of the game alone.
        ///
        /// This is the shape most balance changes actually take. A global multiplier asks what
        /// happens if every block in the game changes at once, which is almost never what anyone
        /// wants to ship; the survey found a short list of types carrying the tail, and the useful
        /// question is what moving those does.
        /// </summary>
        private static Func<float, Func<string, string, BlockThermalProperties, BlockThermalProperties>> OnlyOn(
            string typeId, Action<BlockThermalProperties, float> set)
        {
            return level => (blockType, subtype, source) =>
            {
                if (!string.Equals(blockType, typeId, StringComparison.Ordinal)) return source;

                BlockThermalProperties copy = source.Clone();
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

            // ---- the offenders, one type at a time -----------------------------------------------
            //
            // The survey and the census between them name the types that carry the tail:
            // LargeJumpDrive is 67 % of the corpus's full-load heat and every ship that mounts one
            // loses a block; LargeHydrogenEngine is hottest on 34 % of runaway rows against 4 % of
            // rows overall; thrusters and reactors follow. These are the dials a balance pass would
            // actually reach for, because they move the offenders without touching everything else.

            float[] cuts = { 0.1f, 0.25f, 0.5f, 1f, 2f };

            knobs.Add(new Knob
            {
                Name = "jumpdrive-waste",
                Intent = "waste fraction of jump drives alone — 67 % of the corpus's load heat",
                Multiplier = true, Shipped = 1f, Levels = cuts, Scenarios = Core,
                OnlyType = "JumpDrive",
                Material = OnlyOn("JumpDrive", (p, x) => p.ConsumerWasteEnergy *= x),
            });

            knobs.Add(new Knob
            {
                Name = "jumpdrive-capacity",
                Intent = "heat capacity of jump drives alone — how long one takes to cook",
                Multiplier = true, Shipped = 1f, Levels = new[] { 1f, 2f, 4f, 8f }, Scenarios = Core,
                OnlyType = "JumpDrive",
                Material = OnlyOn("JumpDrive", (p, x) => p.SpecificHeat *= x),
            });

            knobs.Add(new Knob
            {
                Name = "engine-waste",
                Intent = "waste fraction of hydrogen engines alone — ships at 0.60",
                Multiplier = true, Shipped = 1f, Levels = cuts, Scenarios = Core,
                OnlyType = "HydrogenEngine",
                Material = OnlyOn("HydrogenEngine", (p, x) => p.ProducerWasteEnergy *= x),
            });

            knobs.Add(new Knob
            {
                Name = "engine-conductivity",
                Intent = "how fast a hydrogen engine sheds into its neighbours",
                Multiplier = true, Shipped = 1f, Levels = new[] { 1f, 2f, 4f, 8f }, Scenarios = Core,
                OnlyType = "HydrogenEngine",
                Material = OnlyOn("HydrogenEngine", (p, x) => p.Conductivity *= x),
            });

            knobs.Add(new Knob
            {
                Name = "thruster-waste",
                Intent = "waste fraction of thrusters alone — charged against thrust, not draw",
                Multiplier = true, Shipped = 1f, Levels = cuts, Scenarios = Core,
                OnlyType = "Thrust",
                Material = OnlyOn("Thrust", (p, x) => p.ConsumerWasteEnergy *= x),
            });

            knobs.Add(new Knob
            {
                Name = "reactor-waste",
                Intent = "waste fraction of reactors alone — ships at 0.01",
                Multiplier = true, Shipped = 1f, Levels = new[] { 0.5f, 1f, 2f, 4f, 8f },
                Scenarios = Core,
                OnlyType = "Reactor",
                Material = OnlyOn("Reactor", (p, x) => p.ProducerWasteEnergy *= x),
            });

            // ---- the world dials ---------------------------------------------------------------

            // The largest departure from physics in the model and the one that makes it a game.
            // Swept around the shipped value rather than multiplied, because the interesting
            // question is which value to ship and not what a factor does.
            //
            // **Re-centred on 90 when `C24` moved the default there from 225.** The ladder is
            // doublings either side of what ships, and centred on 225 it had stopped containing
            // the shipped value at all — 90 falls between its 56 and its 112, so every cell of the
            // sweep was a value nobody runs and the column comparing them to *shipped* was
            // comparing them to a level that is not.
            knobs.Add(new Knob
            {
                Name = "heat-time-scale",
                Intent = "seconds of physical time per second of play",
                Multiplier = false, Shipped = 90f,
                Levels = new[] { 11f, 22f, 45f, 90f, 180f, 360f }, Scenarios = Core,
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
