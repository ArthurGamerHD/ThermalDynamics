using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class Catalog
    {
        public const float LargeGridSize = 2.5f;
        public const float SmallGridSize = 0.5f;


        [ThreadStatic]
        public static Func<BlockThermalProperties, BlockThermalProperties> MaterialOverride;


        private static BlockThermalProperties Apply(BlockThermalProperties properties)
        {
            return MaterialOverride == null ? properties : MaterialOverride(properties);
        }


        public static BlockThermalProperties DefaultThermal()
        {
            return Apply(RawDefault());
        }


        private static BlockThermalProperties RawDefault()
        {
            return new BlockThermalProperties
            {
                Conductivity = 50f,
                SpecificHeat = 450f,
                Emissivity = 0.125f,
                ExposedSurfaceMultiplier = 1f,
                ProducerWasteEnergy = 0.05f,
                ConsumerWasteEnergy = 0.05f,
                CriticalTemperature = 900f,
                OverheatDamagePerKelvin = 1f,
            };
        }


        public static BlockThermalProperties ReactorThermal()
        {

            BlockThermalProperties t = RawDefault();
            t.Conductivity = 50f;
            t.SpecificHeat = 600f;
            t.Emissivity = 0.25f;
            t.ProducerWasteEnergy = 0.25f;
            t.ConsumerWasteEnergy = 0.25f;
            t.CriticalTemperature = 1200f;
            t.OverheatDamagePerKelvin = 0.25f;

            return Apply(t);
        }


        public static BlockThermalProperties RadiatorThermal()
        {

            BlockThermalProperties t = RawDefault();
            t.Conductivity = 237f;
            t.SpecificHeat = 900f;
            t.Emissivity = 0.35f;
            t.ExposedSurfaceMultiplier = 1.25f;
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0f;
            t.CriticalTemperature = 1000f;

            return Apply(t);
        }


        public static BlockThermalProperties CoolantThermal()
        {

            BlockThermalProperties t = RawDefault();
            t.Conductivity = 400f;
            t.SpecificHeat = 385f;
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0f;
            t.CriticalTemperature = 1000f;

            return Apply(t);
        }



        private static BlockModel From(string name, string subtype)
        {
            Vanilla.Block real = Vanilla.Find(subtype);
            if (real == null)
            {
                throw new InvalidOperationException(
                    "the catalogue stands in for " + subtype + ", which Vanilla.Reference does not"
                    + " carry — add the row there rather than hand-typing a mass here");
            }

            return BlockModel.Solid(name, real.Size, real.Mass, Apply(real.Thermal));
        }


        public static BlockModel LightArmor()
        {

            return From("LightArmorBlock", "LargeBlockArmorBlock");
        }


        public static BlockModel LightArmorBar(int length)
        {
            return BlockModel.Solid("LightArmorBar", new Vector3I(length, 1, 1), 500f * length, DefaultThermal());
        }


        public static BlockModel LightArmorCube(int size)
        {
            return BlockModel.Solid(

                "LightArmorCube", new Vector3I(size, size, size), 500f * size * size * size, DefaultThermal());
        }


        public static BlockModel HeavyArmor()
        {

            return From("HeavyArmorBlock", "LargeHeavyBlockArmorBlock");
        }


        public static BlockModel Reactor()
        {

            return From("SmallReactor", "LargeBlockSmallGenerator");
        }


        public static BlockModel LargeReactor()
        {

            return From("LargeReactor", "LargeBlockLargeGenerator");
        }


        public static BlockModel Battery()
        {

            return From("Battery", "LargeBlockBatteryBlock");
        }


        public static BlockModel Thruster()
        {

            return From("LargeThruster", "LargeBlockLargeThrust");
        }


        public static BlockModel Radiator()
        {
            BlockModel model = BlockModel.Solid("Radiator", new Vector3I(1, 5, 2), 900f, RadiatorThermal());

            foreach (Vector3I cell in model.LocalCells())
            {
                int state = CellSurface.SelfAirtightMask;
                state = CellSurface.WithSelfMount(state, Face.Up, true);
                state = CellSurface.WithSelfMount(state, Face.Down, true);
                model.SetLocalSurface(cell, state);
            }
            return model;
        }


        public static BlockModel SlideDoor()
        {
            BlockModel model = BlockModel.Solid("AirtightSlideDoor", Vector3I.One, 1065f, DefaultThermal());

            int closed = CellSurface.SelfMountMask;
            int open = CellSurface.SelfMountMask;

            for (int face = 0; face < Face.Count; face++)
            {
                bool throughWay = face == Face.Forward || face == Face.Backward;
                if (throughWay) continue;

                closed = CellSurface.WithSelfAirtight(closed, face, true);
                open = CellSurface.WithSelfAirtight(open, face, true);
            }

            closed = CellSurface.WithSelfAirtight(closed, Face.Forward, true);

            model.LocalSurfaces = new int[] { closed };
            model.LocalSurfacesWhenOpen = new int[] { open };
            return model;
        }


        public static BlockModel AirtightDoor()
        {
            BlockModel model = BlockModel.Solid("AirtightDoor", Vector3I.One, 400f, DefaultThermal());
            model.LocalSurfacesWhenOpen = new int[] { CellSurface.SelfMountMask };
            return model;
        }


        public static BlockModel Grating()
        {
            return BlockModel.Open("Grating", Vector3I.One, 200f, DefaultThermal());
        }



        public static BlockModel CoolantPipeStraight(params Vector3I[] sinkDirections)
        {
            BlockModel model = BlockModel.Solid("CoolantPipe_Straight", Vector3I.One, 220f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pipe(Vector3I.Forward, Vector3I.Backward, sinkDirections));
            return model;
        }


        public static BlockModel CoolantPipeCorner(params Vector3I[] sinkDirections)
        {
            BlockModel model = BlockModel.Solid("CoolantPipe_Corner", Vector3I.One, 220f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pipe(Vector3I.Forward, Vector3I.Left, sinkDirections));
            return model;
        }


        public static BlockModel CoolantPump()
        {
            BlockModel model = BlockModel.Solid("CoolantPump", Vector3I.One, 600f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pump(Vector3I.Forward, Vector3I.Backward, 1));
            return model;
        }


        public static BlockModel CoolantPumpLong()
        {
            BlockModel model = BlockModel.Solid("CoolantPump_Long", new Vector3I(1, 1, 3), 600f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pump(Vector3I.Forward, Vector3I.Backward, 3));
            return model;
        }



        public static BlockModel HeatPump()
        {

            return HeatPump(60000f, 20000f);
        }


        public static BlockModel HeatPump(float ratedWatts, float maxPowerWatts)
        {
            BlockModel model = BlockModel.Solid("HeatPump", Vector3I.One, 800f, DefaultThermal());
            model.WithHeatPump(HeatPumpShape.Along(Vector3I.Forward, ratedWatts, maxPowerWatts));
            return model;
        }
    }
}
