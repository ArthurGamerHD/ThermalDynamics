using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Stand-in block definitions for scenarios and tests.
    ///
    /// **The six blocks that stand in for vanilla ones are derived from them**, through
    /// <see cref="Vanilla"/>'s transcribed build costs and the shipped derivation: same size, same
    /// mass, same thermal properties a player's block has. They were hand-typed approximations
    /// until backlog.md `C4`, and four of the six were out by more than
    /// five per cent — so a scenario temperature was quoted off a block nobody had compared with
    /// the thing it mirrors.
    ///
    /// The rest — the mod's own hardware and the shaped rigs — are still written here, and say so.
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

        /// <summary>
        /// Applied to every set of thermal properties this catalogue hands out, when set.
        ///
        /// The companion to <see cref="GridBuilder.SettingsOverride"/>: settings reach a scenario
        /// through the builder, but material properties reach it through here, and a profile that
        /// changes the conduction pace has to move both or it is only half applied. Null by
        /// default.
        /// </summary>
        /// <remarks>
        /// Thread-local. xUnit runs test classes in parallel, and a plain static here
        /// leaked one test's profile into every other test running at that moment —
        /// sixty-six unrelated failures, none of them reproducible alone. The sweep is
        /// single-threaded, so it is unaffected.
        /// </remarks>
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

        /// <summary>
        /// The default preset before any override.
        ///
        /// The derived presets start from this rather than from <see cref="DefaultThermal"/>: that
        /// one has already had the override applied, and a profile that scales conductivity would
        /// otherwise scale it twice — squaring the pace on every block built from a preset, which
        /// is a factor no reader of the table would suspect.
        /// </summary>
        private static BlockThermalProperties RawDefault()
        {
            return new BlockThermalProperties
            {
                Conductivity = 50f,          // mild steel, W/(m K)
                SpecificHeat = 450f,       // mild steel
                Emissivity = 0.125f,
                ExposedSurfaceMultiplier = 1f,
                ProducerWasteEnergy = 0.05f,
                ConsumerWasteEnergy = 0.05f,
                CriticalTemperature = 900f,
                OverheatDamagePerKelvin = 1f,
            };
        }

        /// <summary>
        /// A reactor-flavoured material preset: graphite-shielded steel, rated to 1,200 K.
        ///
        /// **Not what a scenario reactor is made of any more** — `Catalog.Reactor()` derives from
        /// the block it stands in for (backlog.md `C4`). This is kept for
        /// the rigs that want *a hot-rated block* rather than a reactor: a heater with a fraction
        /// of one, a profile sweep's source, a block with no rating. Its waste fractions are the
        /// preset's own and describe nothing that ships.
        /// </summary>
        public static BlockThermalProperties ReactorThermal()
        {
            BlockThermalProperties t = RawDefault();
            t.Conductivity = 50f;      // steel
            t.SpecificHeat = 600f;     // steel with graphite shielding
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
            t.Conductivity = 237f;     // aluminium
            t.SpecificHeat = 900f;     // aluminium
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
            t.Conductivity = 400f;     // copper
            t.SpecificHeat = 385f;     // copper
            t.ProducerWasteEnergy = 0f;
            t.ConsumerWasteEnergy = 0f;
            t.CriticalTemperature = 1000f;
            return Apply(t);
        }

        // ---- blocks -----------------------------------------------------------------------

        /// <summary>
        /// A stand-in built from the block it stands in for: `Vanilla`'s transcribed size and mass,
        /// and the thermal properties the shipped derivation gives that build cost.
        ///
        /// <para>
        /// **This is what closed backlog.md `C4`.** The six stand-ins were
        /// hand-typed approximations and four of them were out by more than five per cent — the
        /// large thruster by 4.32x, the battery by 3.70x because it carried the *small-grid*
        /// battery's mass under a large-grid name — while `Catalog.ReactorThermal` said a quarter
        /// of a reactor's output becomes heat against the shipped hundredth. Every scenario in this
        /// repository is built out of these, so every scenario temperature was quoted off numbers
        /// nobody had compared with the blocks they mirror.
        /// </para>
        ///
        /// <para>
        /// The name is kept as the catalogue's rather than the game's, because scenarios, dumps and
        /// pinned claims are keyed on it and a rename is a second change (`M6`).
        /// </para>
        /// </summary>
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

        /// <summary>
        /// A light armour block stretched along X. Multi-cell blocks are where partial coverage
        /// happens: a neighbour can cover part of a long face and leave the rest radiating.
        /// </summary>
        public static BlockModel LightArmorBar(int length)
        {
            return BlockModel.Solid("LightArmorBar", new Vector3I(length, 1, 1), 500f * length, DefaultThermal());
        }

        /// <summary>A cubic multi-cell armour block, for the interior-faces case.</summary>
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
