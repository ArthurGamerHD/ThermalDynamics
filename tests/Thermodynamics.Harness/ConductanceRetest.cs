using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    public static class ConductanceRetest
    {
        public const float OldReference = 200f;

        public const float OldDefaultQuality = 0.6f;

        public static float PreDefault
        {
            get { return ReferenceMaterials.MildSteel.Conductivity * ThermalConstants.ConductionScale; }
        }

        public static float PreBest
        {
            get { return PreDefault * (OldReference / (OldReference * OldDefaultQuality)); }
        }


        public static float Authored(float effective)
        {
            return effective / ThermalConstants.ConductionScale;
        }

        private static readonly string[] BestTypes = { "Thrust", "Reactor" };


        public static bool IsModBlock(string subtype)
        {
            return subtype != null && subtype.StartsWith("Gauge_", StringComparison.Ordinal);
        }


        public static bool WasBest(string typeId, string subtype)
        {
            if (IsModBlock(subtype)) return true;

            for (int i = 0; i < BestTypes.Length; i++)
            {
                if (string.Equals(typeId, BestTypes[i], StringComparison.Ordinal)) return true;
            }

            return false;
        }


        public static float PreConversion(string typeId, string subtype)
        {
            return WasBest(typeId, subtype) ? PreBest : PreDefault;
        }

        public class World
        {
            public string Name;

            public string Restores;

            public bool IsShipped
            {
                get { return Reaches == null; }
            }

            public Func<string, string, bool> Reaches;


            public Func<string, string, BlockThermalProperties, BlockThermalProperties> Material()
            {
                if (Reaches == null) return null;

                Func<string, string, bool> reaches = Reaches;
                return (typeId, subtype, source) =>
                {
                    if (!reaches(typeId, subtype)) return source;

                    BlockThermalProperties copy = source.Clone();

                    copy.Conductivity = Authored(PreConversion(typeId, subtype));
                    return copy;
                };
            }
        }


        public static List<World> All()
        {

            List<World> worlds = new List<World>();

            worlds.Add(new World
            {
                Name = "shipped",
                Restores = "nothing: real materials everywhere, the world as published",
            });

            worlds.Add(new World
            {
                Name = "pre-units",
                Restores = "the whole pre-conversion table: 120 everywhere, 200 for thrust, "
                    + "reactors and the mod's own blocks",
                Reaches = (typeId, subtype) => true,
            });

            worlds.Add(new World
            {
                Name = "vanilla-flat",
                Restores = "the 0.6 fall-through for every vanilla block, leaving thrust, "
                    + "reactors and the mod's blocks as shipped",
                Reaches = (typeId, subtype) => !WasBest(typeId, subtype),
            });

            worlds.Add(new World
            {
                Name = "thrust-and-reactors",
                Restores = "quality 1 for thrusters and reactors, which the conversion took to "
                    + "0.23× on an ion thruster, 0.60× on a hydrogen one and 0.52× on a reactor",
                Reaches = (typeId, subtype) =>
                    !IsModBlock(subtype) && WasBest(typeId, subtype),
            });

            worlds.Add(new World
            {
                Name = "mod-blocks",
                Restores = "quality 1 for the coolant pipes, pumps, heat pumps and radiators, "
                    + "which the conversion took to 4.8× and 2.84×",
                Reaches = (typeId, subtype) => IsModBlock(subtype),
            });

            return worlds;
        }



        public class Move
        {
            public string Subtype;
            public string TypeId;

            public float Before;

            public float After;

            public float Ratio
            {
                get { return Before <= 0f ? 0f : After / Before; }
            }
        }

        private static readonly string[] Notable =
        {
            "LargeBlockArmorBlock",
            "LargeHeavyBlockArmorBlock",
            "LargeBlockLargeThrust",
            "LargeBlockLargeHydrogenThrust",
            "LargeBlockLargeAtmosphericThrust",
            "LargeBlockLargeGenerator",
            "LargeBlockBatteryBlock",
            "LargeJumpDrive",
            "LargeBlockSolarPanel",
            "LargeBlockGyro",
            "LargeBlockCockpit",
            "LargeBlockConveyor",
            "LargeBlockLargeContainer",
        };


        public static List<Move> Moves()
        {

            List<Move> moves = new List<Move>();
            if (!GameBlocks.IsInstalled) return moves;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            for (int i = 0; i < Notable.Length; i++)
            {
                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(Notable[i], out definition)) continue;

                BlockThermalProperties thermal = Blueprints.Model(definition).Thermal;

                moves.Add(new Move
                {
                    Subtype = Notable[i],
                    TypeId = definition.TypeId,

                    Before = PreConversion(definition.TypeId, Notable[i]),
                    After = thermal.Conductivity * ThermalConstants.ConductionScale,
                });
            }

            return moves;
        }


        public static List<string> Missing()
        {

            List<string> missing = new List<string>();
            if (!GameBlocks.IsInstalled) return missing;

            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();
            for (int i = 0; i < Notable.Length; i++)
            {
                if (!definitions.ContainsKey(Notable[i])) missing.Add(Notable[i]);
            }

            return missing;
        }
    }
}
