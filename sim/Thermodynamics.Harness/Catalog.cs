using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Stand-in block definitions for scenarios and tests.
    ///
    /// Masses are approximations of the real Space Engineers blocks and thermal properties mirror
    /// Data/Cubes.xml. They exist so scenarios behave plausibly, not so results can be quoted as
    /// exact in-game numbers.
    ///
    /// Specific heat is in real J/(kg K), as it is in the definitions: steel 450, copper 385,
    /// aluminium 900. The playable pace comes from <see cref="ThermalSettings.HeatTimeScale"/>,
    /// not from writing the capacities small.
    /// </summary>
    public static class Catalog
    {
        public const float LargeGridSize = 2.5f;
        public const float SmallGridSize = 0.5f;

        // ---- thermal property presets, mirroring Data/Cubes.xml ---------------------------

        public static BlockThermalProperties DefaultThermal()
        {
            return new BlockThermalProperties
            {
                Conductivity = 0.60f,
                SpecificHeat = 450f,       // mild steel
                Emissivity = 0.125f,
                SurfaceAreaScaler = 1f,
                ProducerWasteEnergy = 0.05f,
                ConsumerWasteEnergy = 0.05f,
                CriticalTemperature = 900f,
                CriticalTemperatureScaler = 1f,
            };
        }

        public static BlockThermalProperties ReactorThermal()
        {
            BlockThermalProperties t = DefaultThermal();
            t.Conductivity = 1f;
            t.SpecificHeat = 600f;     // steel with graphite shielding
            t.Emissivity = 0.25f;
            t.ProducerWasteEnergy = 0.25f;
            t.ConsumerWasteEnergy = 0.25f;
            t.CriticalTemperature = 1200f;
            t.CriticalTemperatureScaler = 0.25f;
            return t;
        }

        public static BlockThermalProperties ThrusterThermal()
        {
            BlockThermalProperties t = DefaultThermal();
            t.Conductivity = 1f;
            t.SpecificHeat = 450f;     // steel and nickel alloy
            t.Emissivity = 0.15f;
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0.25f;
            t.CriticalTemperature = 1050f;
            t.CriticalTemperatureScaler = 0.25f;
            return t;
        }

        public static BlockThermalProperties RadiatorThermal()
        {
            BlockThermalProperties t = DefaultThermal();
            t.Conductivity = 1f;
            t.SpecificHeat = 900f;     // aluminium
            t.Emissivity = 0.35f;
            t.SurfaceAreaScaler = 1.25f;
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0f;
            t.CriticalTemperature = 1000f;
            return t;
        }

        public static BlockThermalProperties CoolantThermal()
        {
            BlockThermalProperties t = DefaultThermal();
            t.Conductivity = 1f;
            t.SpecificHeat = 385f;     // copper
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0f;
            t.CriticalTemperature = 1000f;
            return t;
        }

        // ---- blocks -----------------------------------------------------------------------

        public static BlockModel LightArmor()
        {
            return BlockModel.Solid("LightArmorBlock", Vector3I.One, 500f, DefaultThermal());
        }

        public static BlockModel HeavyArmor()
        {
            return BlockModel.Solid("HeavyArmorBlock", Vector3I.One, 3300f, DefaultThermal());
        }

        public static BlockModel Reactor()
        {
            return BlockModel.Solid("SmallReactor", Vector3I.One, 3000f, ReactorThermal());
        }

        public static BlockModel LargeReactor()
        {
            return BlockModel.Solid("LargeReactor", new Vector3I(3, 3, 3), 52000f, ReactorThermal());
        }

        public static BlockModel Battery()
        {
            return BlockModel.Solid("Battery", Vector3I.One, 1040f, DefaultThermal());
        }

        public static BlockModel Thruster()
        {
            return BlockModel.Solid("LargeThruster", new Vector3I(3, 3, 4), 10000f, ThrusterThermal());
        }

        /// <summary>The shipped radiator: 1x5x2, mounts only on top and bottom.</summary>
        public static BlockModel Radiator()
        {
            BlockModel model = BlockModel.Solid("Radiator", new Vector3I(1, 5, 2), 900f, RadiatorThermal());

            // Only the top and bottom faces carry mount surfaces.
            foreach (Vector3I cell in model.LocalCells())
            {
                int state = CellSurface.SelfAirtightMask;
                state = CellSurface.WithSelfMount(state, Face.Up, true);
                state = CellSurface.WithSelfMount(state, Face.Down, true);
                model.SetLocalSurface(cell, state);
            }
            return model;
        }

        /// <summary>
        /// An airtight sliding door, described the way the game describes the real one.
        ///
        /// The sides it is bolted in by seal whatever it is doing; the way through seals only
        /// while it is closed. That second half comes from the game's door rule and not from the
        /// definition's pressurisation table, which is exactly the distinction that used to be
        /// missing — a door that never sealed left every room around it open.
        /// </summary>
        public static BlockModel SlideDoor()
        {
            BlockModel model = BlockModel.Solid("AirtightSlideDoor", Vector3I.One, 1065f, DefaultThermal());

            int closed = CellSurface.SelfMountMask;
            int open = CellSurface.SelfMountMask;

            for (int face = 0; face < Face.Count; face++)
            {
                // the frame: sealed in both states
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

        /// <summary>
        /// A door that is a wall when shut and a hole when open, on every face — the airtight
        /// hangar door shape, and the simplest thing that is a door at all.
        ///
        /// Its cell seals completely while shut, which makes it the case that has to be handled
        /// specially in the mapper: it would read as solid structure, and a portal needs a region
        /// on the door's own side to join to.
        /// </summary>
        public static BlockModel AirtightDoor()
        {
            BlockModel model = BlockModel.Solid("AirtightDoor", Vector3I.One, 400f, DefaultThermal());
            model.LocalSurfacesWhenOpen = new int[] { CellSurface.SelfMountMask };
            return model;
        }

        /// <summary>An open lattice: mounts everywhere, seals nothing.</summary>
        public static BlockModel Grating()
        {
            return BlockModel.Open("Grating", Vector3I.One, 200f, DefaultThermal());
        }

        // ---- coolant --------------------------------------------------------------------

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

        /// <summary>A 1x1x1 pump, matching the large-grid block.</summary>
        public static BlockModel CoolantPump()
        {
            BlockModel model = BlockModel.Solid("CoolantPump", Vector3I.One, 600f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pump(Vector3I.Forward, Vector3I.Backward, 1));
            return model;
        }

        /// <summary>A 1x1x3 pump, matching the small-grid block.</summary>
        public static BlockModel CoolantPumpLong()
        {
            BlockModel model = BlockModel.Solid("CoolantPump_Long", new Vector3I(1, 1, 3), 600f, CoolantThermal());
            model.WithCoolant(CoolantShape.Pump(Vector3I.Forward, Vector3I.Backward, 3));
            return model;
        }

        // ---- heat pump --------------------------------------------------------------------

        /// <summary>
        /// The shipped large-grid heat pump: draws heat from the block on its forward face and
        /// rejects it, plus the work, into the block behind it.
        /// </summary>
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
