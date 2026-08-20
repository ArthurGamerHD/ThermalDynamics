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

            public static State Idle
            {
                get { return new State { Consumers = IdleFraction, Thrust = 0f, Tools = 0f }; }
            }

            public static State Full
            {
                get { return new State { Consumers = 1f, Thrust = 0f, Tools = 1f }; }
            }

            public static State Burn(int direction)
            {
                return new State { Consumers = 0.5f, Thrust = 1f, ThrustDirection = direction, Tools = 0f };
            }

            /// <summary>
            /// Everything at once, in every direction. Physically impossible — a ship cannot burn
            /// six ways — and included deliberately as the absolute ceiling, which is the right rig
            /// for asking where heat *concentrates* rather than what a ship really reaches.
            /// </summary>
            public static State Everything
            {
                get { return new State { Consumers = 1f, Thrust = 1f, ThrustDirection = null, Tools = 1f }; }
            }
        }

        /// <summary>
        /// Applies a state to a built simulation, and returns the watts of heat it implies.
        ///
        /// Producers are set to what the consumers actually ask for, capped at what is installed. A
        /// ship whose reactors out-rate its load runs them part-loaded, which is the ordinary case
        /// and the one a plate rating gets wrong.
        /// </summary>
        public static float Apply(ThermalSimulation simulation, State state)
        {
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();
            ThermalSolver solver = simulation.Solver;

            List<BlockInstance> producers = new List<BlockInstance>();
            float installed = 0f;
            float demand = 0f;

            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                BlockInstance block = solver.Nodes[i].Block;

                block.PowerProducedWatts = 0f;
                block.PowerConsumedWatts = 0f;
                block.ThrustWatts = 0f;

                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(block.Name, out definition)) continue;

                if (definition.PowerOutputWatts > 0f)
                {
                    producers.Add(block);
                    installed += definition.PowerOutputWatts;
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

                float share = IsTool(definition.TypeId) ? state.Tools : state.Consumers;
                block.PowerConsumedWatts = definition.PowerDrawWatts * share;
                demand += block.PowerConsumedWatts;
            }

            // Batteries and reactors share the load in proportion to their rating.
            float supplied = demand < installed ? demand : installed;
            if (installed > 0f)
            {
                float fraction = supplied / installed;

                for (int i = 0; i < producers.Count; i++)
                {
                    GameBlocks.Definition definition = definitions[producers[i].Name];
                    producers[i].PowerProducedWatts = definition.PowerOutputWatts * fraction;
                }
            }

            solver.RefreshHeatGeneration();

            float watts = 0f;
            for (int i = 0; i < solver.Nodes.Count; i++) watts += solver.Nodes[i].HeatGenerationWatts;
            return watts;
        }

        /// <summary>
        /// Blocks that run only when the player is doing something with them, as against the ones
        /// that draw whenever the ship is powered.
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
                case "JumpDrive":
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
