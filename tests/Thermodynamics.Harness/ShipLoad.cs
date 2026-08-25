using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a ship has switched on.
    ///
    /// <para>
    /// A blueprint has no session, so nothing on it is running: every block sits at zero watts
    /// until something says otherwise. This is that something — it walks a built grid and sets the
    /// power, draw and thrust figures the solver turns into heat, according to a named state the
    /// ship is supposed to be in.
    /// </para>
    ///
    /// <para>
    /// **Thrust is per direction, and that is the point.** A ship burning forward heats the
    /// thrusters at its stern and nothing at its bow, so where the heat lands depends on which way
    /// it is going. Applying every thruster at once — which the first version of the screening pass
    /// did — both inflates the total several-fold and erases the hot spot that makes the case
    /// interesting.
    /// </para>
    /// </summary>
    public static class ShipLoad
    {
        /// <summary>Nothing running but the lights: a ship parked with the crew aboard.</summary>
        public const float IdleFraction = 0.05f;

        public class State
        {
            /// <summary>Share of each consumer's rating that is drawing, 0..1.</summary>
            public float Consumers;

            /// <summary>Share of thrust rating, applied to one direction only.</summary>
            public float Thrust;

            /// <summary>Which way the ship is burning. Null applies thrust to every direction.</summary>
            public int? ThrustDirection;

            /// <summary>Share of weapon and tool blocks running.</summary>
            public float Tools = 1f;

            /// <summary>
            /// Share of each jump drive's charging draw, 0..1.
            ///
            /// <para>
            /// **Separate from <see cref="Consumers"/> because a drive is not a consumer that runs
            /// at a level — it is one that is either charging or full.** In game it draws its whole
            /// requirement until it is charged and then draws essentially nothing, so a ship at a
            /// steady cruise has drives at zero and a ship that has just jumped has them at one.
            /// </para>
            ///
            /// <para>
            /// **It is on its own dial because of how much of the answer it is.** Jump drives are
            /// 71.3 % of the corpus's full-load waste heat, so whether they are charging is most of
            /// the shape of every load result — which is why the two states are measured as a pair
            /// of bounds rather than averaged into one number nobody's ship is at.
            /// See balance.md, The confound this rests on.
            /// </para>
            /// </summary>
            public float Drives = 1f;

            /// <summary>
            /// Run every generator at its plate rating rather than at what the ship is asking for.
            ///
            /// Producers normally follow demand, which is the ordinary case and the one worth
            /// reporting — but it means a hull with 300 MW of reactors and 2 MW of draw makes the
            /// waste heat of 2 MW, and a ceiling that leaves 298 MW of it on the table is not a
            /// ceiling. Where the question is the most heat a ship can be made to produce, the
            /// answer takes whichever side is larger: everything it can burn, or everything it can
            /// draw.
            /// </summary>
            public bool ProducersAtRating;

            public static State Idle
            {
                get
                {
                    return new State
                    { Consumers = IdleFraction, Thrust = 0f, Tools = 0f, Drives = 0f };
                }
            }

            public static State Full
            {
                get { return new State { Consumers = 1f, Thrust = 0f, Tools = 1f }; }
            }

            /// <summary>
            /// Everything drawing except the jump drives, which are charged.
            ///
            /// The other bound on a ship under sustained load. <see cref="Full"/> holds every drive
            /// charging for as long as the run lasts, which no ship does — a drive charges once and
            /// then holds — and drives carry most of the heat, so the two states bracket what a
            /// loaded ship really makes rather than either one describing it.
            /// </summary>
            public static State Charged
            {
                get { return new State { Consumers = 1f, Thrust = 0f, Tools = 1f, Drives = 0f }; }
            }

            public static State Burn(int direction)
            {
                return new State
                {
                    Consumers = 0.5f,
                    Thrust = 1f,
                    ThrustDirection = direction,
                    Tools = 0f,

                    // Zero rather than the default, because a drive was a tool until this pass and
                    // a burn has always had its tools off. A ship under thrust is not charging.
                    Drives = 0f,
                };
            }

            /// <summary>
            /// Everything at once, in every direction, with the reactors flat out. Physically
            /// impossible — a ship cannot burn six ways, and nothing is drawing what the reactors
            /// are making — and included deliberately as the absolute ceiling, which is the right
            /// rig for asking where heat *concentrates* rather than what a ship really reaches.
            /// </summary>
            public static State Everything
            {
                get
                {
                    return new State
                    {
                        Consumers = 1f,
                        Thrust = 1f,
                        ThrustDirection = null,
                        Tools = 1f,
                        ProducersAtRating = true,
                    };
                }
            }
        }

        /// <summary>
        /// Applies a state to a built simulation, and returns the watts of heat it implies.
        ///
        /// Producers are set to what the consumers actually ask for, capped at what is installed. A
        /// ship whose reactors out-rate its load runs them part-loaded, which is the ordinary case
        /// and the one a plate rating gets wrong.
        /// </summary>
        public static float Apply(ShipAssembly assembly, State state)
        {
            // Keyed on what a placed block is called, which is its type where the game states no
            // subtype — otherwise every base variant draws whatever the first of the thirteen did.
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.ByModelName();

            List<BlockInstance> generators = new List<BlockInstance>();
            List<BlockInstance> stores = new List<BlockInstance>();
            float installed = 0f;
            float demand = 0f;

            foreach (ThermalNode node in assembly.Nodes)
            {
                BlockInstance block = node.Block;

                block.PowerProducedWatts = 0f;
                block.PowerConsumedWatts = 0f;
                block.ThrustWatts = 0f;

                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(block.Name, out definition)) continue;

                if (definition.PowerOutputWatts > 0f)
                {
                    // A store carries both an output and a draw — a battery is rated 12 MW each
                    // way — and it is never doing both. Generators carry the load; a store only
                    // covers what they cannot, which is what keeps a ship full of batteries from
                    // reporting every one of them discharging at full rating beside its reactors.
                    if (IsStore(definition.TypeId)) stores.Add(block);
                    else
                    {
                        generators.Add(block);
                        installed += definition.PowerOutputWatts;
                    }
                    continue;
                }

                if (definition.ThrustNewtons > 0f)
                {
                    bool burning = state.ThrustDirection == null
                        || Direction(block) == state.ThrustDirection.Value;

                    if (burning && state.Thrust > 0f)
                    {
                        block.ThrustWatts = definition.ThrustNewtons * state.Thrust;

                        // A thruster's electrical draw is charged too, where it has one: an ion
                        // thruster draws and a hydrogen one does not, and only the draw is real
                        // power off the reactors.
                        block.PowerConsumedWatts = definition.PowerDrawWatts * state.Thrust;
                        demand += block.PowerConsumedWatts;
                    }
                    continue;
                }

                // Drives before tools, because a jump drive used to be filed as a tool and that
                // is what made `Consumers` a dial that did not reach 71 % of a loaded ship's heat.
                float share = state.Consumers;
                if (IsDrive(definition.TypeId)) share = state.Drives;
                else if (IsTool(definition.TypeId)) share = state.Tools;

                block.PowerConsumedWatts = definition.PowerDrawWatts * share;
                demand += block.PowerConsumedWatts;
            }

            // Generators take the load in proportion to their rating, up to what they have — or
            // all of it, where the state is after the ceiling rather than after the ordinary case.
            float fromGenerators = demand < installed ? demand : installed;
            if (state.ProducersAtRating) fromGenerators = installed;
            Share(definitions, generators, installed, fromGenerators);

            // Only a shortfall reaches the stores. A ship whose reactors cover its draw has its
            // batteries sitting there, which is what they do — unless the question is the ceiling,
            // where every store is discharging flat out beside the reactors. A hull that carries
            // batteries and no reactor has no other way to reach its own maximum.
            float reserve = 0f;
            for (int i = 0; i < stores.Count; i++) reserve += definitions[stores[i].Name].PowerOutputWatts;

            float shortfall = state.ProducersAtRating ? reserve : demand - fromGenerators;
            if (shortfall > 0f)
            {
                Share(definitions, stores, reserve, shortfall < reserve ? shortfall : reserve);
            }

            for (int i = 0; i < assembly.Simulations.Count; i++)
            {
                assembly.Simulations[i].Solver.RefreshHeatGeneration();
            }

            float watts = 0f;
            foreach (ThermalNode node in assembly.Nodes) watts += node.HeatGenerationWatts;
            return watts;
        }

        /// <summary>
        /// How long this ship's jump drives take to fill from empty, in simulated seconds, or zero
        /// where it carries none.
        ///
        /// <para>
        /// **The length of the largest thermal event most ships have, and the game states it.** A
        /// drive holds `PowerNeededForJump` megawatt-hours, draws `RequiredPowerInput` megawatts
        /// while filling, and stores `PowerEfficiency` of what it draws — so the shipped large
        /// drive holds 3 MWh, draws 32 MW, keeps 80 % of it, and fills in **421.9 s**. Nothing here
        /// is chosen: every figure is read off the definition the game ships.
        /// </para>
        ///
        /// <para>
        /// **The longest drive on the ship sets it**, because the ship is charging until the last
        /// one is done and that is when the load falls away.
        /// </para>
        /// </summary>
        public static float ChargeSeconds(ShipAssembly assembly)
        {
            if (assembly == null) return 0f;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.ByModelName();
            float longest = 0f;

            foreach (ThermalNode node in assembly.Nodes)
            {
                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(node.Block.Name, out definition)) continue;
                if (!IsDrive(definition.TypeId)) continue;

                float stored = definition.PowerDrawWatts * definition.PowerEfficiency;
                if (stored <= 0f || definition.JumpEnergyJoules <= 0f) continue;

                float seconds = definition.JumpEnergyJoules / stored;
                if (seconds > longest) longest = seconds;
            }

            return longest;
        }

        /// <summary>Spreads a supplied total over a set of producers in proportion to their rating.</summary>
        private static void Share(Dictionary<string, GameBlocks.Definition> definitions,
            List<BlockInstance> producers, float installed, float supplied)
        {
            if (installed <= 0f) return;

            float fraction = supplied / installed;
            for (int i = 0; i < producers.Count; i++)
            {
                producers[i].PowerProducedWatts = definitions[producers[i].Name].PowerOutputWatts * fraction;
            }
        }

        /// <summary>
        /// Blocks that store energy rather than making it, and so carry a rating in both
        /// directions while only ever doing one of them at a time.
        /// </summary>
        public static bool IsStore(string typeId)
        {
            return typeId == "BatteryBlock";
        }

        /// <summary>
        /// Blocks that draw to fill a reservoir and then stop, rather than drawing while they run.
        ///
        /// Only the jump drive today, and it is here rather than folded into the consumers because
        /// it carries 71.3 % of the corpus's full-load waste heat on its own.
        /// </summary>
        public static bool IsDrive(string typeId)
        {
            return typeId == "JumpDrive";
        }

        /// <summary>
        /// Blocks that run only when the player is doing something with them, as against the ones
        /// that draw whenever the ship is powered.
        ///
        /// **The jump drive was on this list and is not one.** It is not used, it is *filled*: it
        /// draws its whole requirement until it is charged and then draws nothing. Filing it here
        /// meant <see cref="State.Tools"/> governed it, so <see cref="State.Consumers"/> — the dial
        /// that reads as *the ship's electrical load* — never reached the block carrying 71.3 % of
        /// the corpus's full-load waste heat. Every published figure is unaffected, because the two
        /// shares agree in all four states that existed; what it cost was a dial nobody could use.
        /// </summary>
        private static bool IsTool(string typeId)
        {
            switch (typeId)
            {
                case "Drill":
                case "ShipGrinder":
                case "ShipWelder":
                case "Refinery":
                case "Assembler":
                case "SmallGatlingGun":
                case "LargeGatlingTurret":
                case "InteriorTurret":
                case "SmallMissileLauncher":
                case "SmallMissileLauncherReload":
                case "LargeMissileTurret":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Which of the six grid directions a block faces, after its orientation.</summary>
        public static int Direction(BlockInstance block)
        {
            Vector3I facing = block.Orientation.Rotate(Vector3I.Forward);

            for (int face = 0; face < Face.Count; face++)
            {
                if (Face.Offsets[face] == facing) return face;
            }
            return 0;
        }

        /// <summary>The direction names, for a scenario to be readable.</summary>
        public static string DirectionName(int face)
        {
            switch (face)
            {
                case Face.Forward: return "forward";
                case Face.Backward: return "backward";
                case Face.Left: return "left";
                case Face.Right: return "right";
                case Face.Up: return "up";
                default: return "down";
            }
        }
    }
}
