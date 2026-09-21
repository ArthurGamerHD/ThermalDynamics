using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ShipLoad
    {
        public const float IdleFraction = 0.05f;

        public class State
        {
            public float Consumers;

            public float Thrust;

            public int? ThrustDirection;

            public float Tools = 1f;

            public float Drives = 1f;

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

            public static State Charged
            {
                get { return new State { Consumers = 1f, Thrust = 0f, Tools = 1f, Drives = 0f }; }
            }

/// <summary>Burn operation.</summary>
            public static State Burn(int direction)
            {
                return new State
                {
                    Consumers = 0.5f,
                    Thrust = 1f,
                    ThrustDirection = direction,
                    Tools = 0f,

                    Drives = 0f,
                };
            }

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

/// <summary>Applies the .</summary>
        public static float Apply(ShipAssembly assembly, State state)
        {
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.ByModelName();

/// <summary>List operation.</summary>
            List<BlockInstance> generators = new List<BlockInstance>();
/// <summary>List operation.</summary>
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
/// <summary>Direction operation.</summary>
                        || Direction(block) == state.ThrustDirection.Value;

                    if (burning && state.Thrust > 0f)
                    {
                        block.ThrustWatts = definition.ThrustNewtons * state.Thrust;

                        block.PowerConsumedWatts = definition.PowerDrawWatts * state.Thrust;
                        demand += block.PowerConsumedWatts;
                    }
                    continue;
                }

                float share = state.Consumers;
                if (IsDrive(definition.TypeId)) share = state.Drives;
                else if (IsTool(definition.TypeId)) share = state.Tools;

                block.PowerConsumedWatts = definition.PowerDrawWatts * share;
                demand += block.PowerConsumedWatts;
            }

            float fromGenerators = demand < installed ? demand : installed;
            if (state.ProducersAtRating) fromGenerators = installed;
            Share(definitions, generators, installed, fromGenerators);

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

/// <summary>ChargeSeconds operation.</summary>
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

/// <summary>Share operation.</summary>
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

/// <summary>IsStore operation.</summary>
        public static bool IsStore(string typeId)
        {
            return typeId == "BatteryBlock";
        }

/// <summary>IsDrive operation.</summary>
        public static bool IsDrive(string typeId)
        {
            return typeId == "JumpDrive";
        }

/// <summary>IsTool operation.</summary>
        public static bool IsTool(string typeId)
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

/// <summary>Direction operation.</summary>
        public static int Direction(BlockInstance block)
        {
            Vector3I facing = block.Orientation.Rotate(Vector3I.Forward);

            for (int face = 0; face < Face.Count; face++)
            {
                if (Face.Offsets[face] == facing) return face;
            }
            return 0;
        }

/// <summary>DirectionName operation.</summary>
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
