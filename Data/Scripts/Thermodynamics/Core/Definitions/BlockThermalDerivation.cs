using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>One line of a block's build cost: a component, how many, and what each weighs.</summary>
    public struct BlockComponent
    {
        public string Component;
        public int Count;

        /// <summary>Kilograms per unit, from the game's own component definition.</summary>
        public float MassEach;

        public float Mass
        {
            get { return Count * MassEach; }
        }

        public BlockComponent(string component, int count, float massEach)
        {
            Component = component;
            Count = count;
            MassEach = massEach;
        }
    }

    /// <summary>
    /// What a block is thermally, derived from what it is built out of.
    ///
    /// A block's thermal properties used to be an opinion per block type, hand-written for the
    /// twenty-eight types anyone had got round to, with every other block in the game — and every
    /// block of every other mod — falling through to one entry describing mild steel. A window and
    /// a battery were the same object.
    ///
    /// They are now a consequence of the build components, which the game already publishes for
    /// every block that exists. The same derivation generates `Data/Cubes.xml` and answers at
    /// runtime for anything not named in it, so a modded block gets properties that describe it
    /// rather than properties that describe armour. This is the arrangement
    /// <see cref="PlanetThermalDerivation"/> already uses for worlds, for the same reason.
    ///
    /// **Two halves, and they are not alike.** *Material* properties — conductivity, specific heat,
    /// emissivity, critical temperature — follow from the components and are derived. *Functional*
    /// properties — the waste-energy fractions, the exposed-area multiplier, the damage rate —
    /// follow from what the block does with power, which its build cost cannot say. Those come
    /// from <see cref="FunctionOf"/>, a table keyed by block type, and they are the only opinions
    /// left in the file.
    /// </summary>
    public static class BlockThermalDerivation
    {
        /// <summary>
        /// The thermal properties of a block with this build cost and this type.
        ///
        /// A block with no priced components — which happens for a definition that lists none, and
        /// for anything this derivation cannot see — comes back as steel with its type's function,
        /// which is exactly what it would have got before.
        /// </summary>
        public static BlockThermalProperties Derive(IList<BlockComponent> components, string typeId)
        {
            BlockThermalProperties properties = Material(components);
            Apply(properties, FunctionOf(typeId));
            return properties.Clamp();
        }

        /// <summary>
        /// The material half: a mass-weighted blend over the block's components.
        ///
        /// **Specific heat is exact.** Heat capacity is additive, so the capacity of a block is the
        /// sum of its components' capacities and the mass-weighted mean specific heat is the right
        /// answer rather than an approximation of one.
        ///
        /// **Conductivity and critical temperature are approximations, deliberately.** Conduction
        /// through a composite depends on how the phases are arranged — a copper wire through a
        /// steel block is not the same as copper powder mixed into it — and nothing in a build cost
        /// says which. A mass-weighted mean is monotone, cheap and has no arrangement to get wrong.
        /// Critical temperature is the same choice for a different reason: the honest rule is that
        /// a block fails when its weakest significant part fails, but taken literally that puts
        /// every block containing a single computer at the silicon limit, which is a cliff rather
        /// than a gradient. Weighting by mass lets a component that dominates a block dominate its
        /// limit, and lets one small part of it not.
        ///
        /// **Emissivity is neither.** It is a property of the surface, not of the bulk, so a block
        /// radiates like whatever it is clad in. Cladding is taken as the heaviest component, on
        /// the grounds that what a block is mostly made of is usually what you can see.
        /// </summary>
        public static BlockThermalProperties Material(IList<BlockComponent> components)
        {
            BlockThermalProperties properties = BlockThermalProperties.Default();

            float mass = 0f;
            float conductivity = 0f;
            float specificHeat = 0f;
            float serviceLimit = 0f;

            float heaviest = 0f;
            BlockMaterial cladding = BlockMaterials.Steel;

            if (components != null)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    BlockComponent line = components[i];
                    if (line.Mass <= 0f) continue;

                    BlockMaterial material = BlockMaterials.Get(line.Component);

                    mass += line.Mass;
                    conductivity += material.Conductivity * line.Mass;
                    specificHeat += material.SpecificHeat * line.Mass;
                    serviceLimit += material.ServiceLimit * line.Mass;

                    if (line.Mass > heaviest)
                    {
                        heaviest = line.Mass;
                        cladding = material;
                    }
                }
            }

            if (mass <= 0f)
            {
                properties.Conductivity = BlockMaterials.Steel.Conductivity;
                properties.SpecificHeat = BlockMaterials.Steel.SpecificHeat;
                properties.Emissivity = BlockMaterials.Steel.Emissivity;
                properties.CriticalTemperature = BlockMaterials.Steel.ServiceLimit;
                return properties;
            }

            properties.Conductivity = conductivity / mass;
            properties.SpecificHeat = specificHeat / mass;
            properties.CriticalTemperature = serviceLimit / mass;
            properties.Emissivity = cladding.Emissivity;

            return properties;
        }

        // ------------------------------------------------------------------------------------
        // The functional half
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// What a block does with power, and how hard it is to break. None of this follows from a
        /// build cost: two blocks of identical construction, one a thruster and one a girder,
        /// differ entirely in what they put into the ship.
        /// </summary>
        public struct BlockFunction
        {
            /// <summary>Fraction of delivered power that becomes heat. Producers only.</summary>
            public float ProducerWasteEnergy;

            /// <summary>Fraction of drawn power that becomes heat. Consumers and thrusters.</summary>
            public float ConsumerWasteEnergy;

            /// <summary>Multiplier on geometric face area, for a finned or folded surface.</summary>
            public float ExposedSurfaceMultiplier;

            /// <summary>Damage per kelvin of overshoot, per second.</summary>
            public float OverheatDamagePerKelvin;
        }

        /// <summary>An ordinary block: small losses either way, plain surface, ordinary toughness.</summary>
        public static readonly BlockFunction Ordinary = new BlockFunction
        {
            ProducerWasteEnergy = 0.05f,
            ConsumerWasteEnergy = 0.05f,
            ExposedSurfaceMultiplier = 1f,
            OverheatDamagePerKelvin = 1f,
        };

        private static readonly Dictionary<string, BlockFunction> Functions = BuildFunctions();

        public static ICollection<string> FunctionTypes
        {
            get { return Functions.Keys; }
        }

        /// <summary>The function of a block type, or <see cref="Ordinary"/> when it has none.</summary>
        public static BlockFunction FunctionOf(string typeId)
        {
            BlockFunction function;
            return typeId != null && Functions.TryGetValue(typeId, out function) ? function : Ordinary;
        }

        private static void Apply(BlockThermalProperties properties, BlockFunction function)
        {
            properties.ProducerWasteEnergy = function.ProducerWasteEnergy;
            properties.ConsumerWasteEnergy = function.ConsumerWasteEnergy;
            properties.ExposedSurfaceMultiplier = function.ExposedSurfaceMultiplier;
            properties.OverheatDamagePerKelvin = function.OverheatDamagePerKelvin;
        }

        private static void Add(Dictionary<string, BlockFunction> t, string typeId,
            float producer, float consumer, float area = 1f, float damage = 1f)
        {
            t[typeId] = new BlockFunction
            {
                ProducerWasteEnergy = producer,
                ConsumerWasteEnergy = consumer,
                ExposedSurfaceMultiplier = area,
                OverheatDamagePerKelvin = damage,
            };
        }

        private static Dictionary<string, BlockFunction> BuildFunctions()
        {
            Dictionary<string, BlockFunction> t = new Dictionary<string, BlockFunction>();

            // ---- power producers -------------------------------------------------------------
            // A producer delivers through MyResourceSourceComponent, so only its producer fraction
            // ever applies and its consumer fraction is dead text. Getting that backwards is how
            // every reactor in the game ran at 0 W; see docs/balance.md.
            //
            // The reactor's fraction is measured rather than chosen — SE rates a 3x3x3 block at
            // 300 MW, so a real plant's efficiency would destroy every large reactor in any build.
            // It is the fraction at which the family survives bare in vacuum at full rating and
            // needs cooling once buried in hull.
            //
            // It was 0.02 while a reactor's critical temperature was the hand-written 1,200 K. The
            // derivation puts the four reactors between 938 and 1,090 K instead, from what they are
            // actually built out of, and at 0.02 the two smaller ones then cook themselves bare in
            // vacuum — a state no build can improve on. 0.01 restores both bounds against the
            // derived limits. The retune is the point of deriving rather than asserting: the
            // balance was resting on a number nobody had checked.
            Add(t, "Reactor", 0.01f, 0.01f, 1f, 0.25f);

            // A combustion engine is the hottest producer there is: most of what it burns leaves as
            // heat rather than as electricity, and unlike a reactor its rating is modest enough to
            // carry an honest fraction.
            Add(t, "HydrogenEngine", 0.60f, 0.05f, 1f, 0.5f);

            // A battery is a very efficient store — round-trip losses of a few per cent — and it is
            // the least heat-tolerant thing on a ship, which its materials already say. The damage
            // rate is left ordinary; the low critical temperature does the work.
            Add(t, "BatteryBlock", 0.03f, 0.03f);

            // A panel converts sunlight it has already absorbed. Charging it again through a waste
            // fraction would be double counting: the solar path put that energy in.
            Add(t, "SolarPanel", 0f, 0.05f);
            Add(t, "WindTurbine", 0f, 0.05f, 1.5f);

            // ---- thrust ----------------------------------------------------------------------
            // A thruster's heat is charged against thrust rather than against draw, which is what
            // lets a hydrogen thruster — drawing no electricity at all — run hot. A nozzle is a
            // folded, finned thing with far more surface than the cell it occupies.
            Add(t, "Thrust", 0f, 0.25f, 1.5f);

            // ---- lighting --------------------------------------------------------------------
            // A lamp turns nearly all of its draw into heat; the fraction that leaves as light is
            // the small part. The one place in the game where 0.9 is the conservative answer.
            Add(t, "InteriorLight", 0f, 0.9f);
            Add(t, "ReflectorLight", 0f, 0.9f);
            Add(t, "EmissiveBlock", 0f, 0.9f);
            Add(t, "Searchlight", 0f, 0.9f);

            // ---- electronics -----------------------------------------------------------------
            // Anything that computes or transmits turns essentially all of its draw into heat.
            // There is nowhere else for it to go: no work is done and nothing leaves the block.
            Add(t, "MyProgrammableBlock", 0f, 0.9f);
            Add(t, "TimerBlock", 0f, 0.9f);
            Add(t, "SensorBlock", 0f, 0.9f);
            Add(t, "CameraBlock", 0f, 0.9f);
            Add(t, "OreDetector", 0f, 0.9f);
            Add(t, "TextPanel", 0f, 0.9f);
            Add(t, "LCDPanelsBlock", 0f, 0.9f);
            Add(t, "ButtonPanel", 0f, 0.9f);
            Add(t, "TerminalBlock", 0f, 0.9f);
            Add(t, "RemoteControl", 0f, 0.9f);
            Add(t, "EventControllerBlock", 0f, 0.9f);
            Add(t, "PathRecorderBlock", 0f, 0.9f);
            Add(t, "BasicMissionBlock", 0f, 0.9f);
            Add(t, "FlightMovementBlock", 0f, 0.9f);
            Add(t, "DefensiveCombatBlock", 0f, 0.9f);
            Add(t, "OffensiveCombatBlock", 0f, 0.9f);
            Add(t, "EmotionControllerBlock", 0f, 0.9f);
            Add(t, "TurretControlBlock", 0f, 0.9f);
            Add(t, "Projector", 0f, 0.9f);
            Add(t, "Jukebox", 0f, 0.9f);
            Add(t, "SoundBlock", 0f, 0.9f);
            Add(t, "StoreBlock", 0f, 0.9f);
            Add(t, "VendingMachine", 0f, 0.9f);
            Add(t, "ContractBlock", 0f, 0.9f);
            Add(t, "Decoy", 0f, 0.9f);

            // A transmitter's draw does leave, as radiated power — but a fraction of a watt of it,
            // against kilowatts of amplifier loss. Treated as electronics with a little relief.
            Add(t, "RadioAntenna", 0f, 0.8f);
            Add(t, "Beacon", 0f, 0.8f);
            Add(t, "BroadcastController", 0f, 0.8f);
            Add(t, "TransponderBlock", 0f, 0.8f);

            // A laser antenna puts real power into a beam that leaves the ship, and it is built
            // around a superconductor. Half of its draw goes away with the beam.
            Add(t, "LaserAntenna", 0f, 0.5f);

            // ---- machinery that does work ----------------------------------------------------
            // Work done on the world leaves the block, so only the losses stay. A motor under load
            // is roughly ninety per cent efficient; the rest is winding and bearing loss.
            Add(t, "MotorStator", 0f, 0.1f);
            Add(t, "MotorAdvancedStator", 0f, 0.1f);
            Add(t, "MotorSuspension", 0f, 0.1f);
            Add(t, "PistonBase", 0f, 0.1f);
            Add(t, "ExtendedPistonBase", 0f, 0.1f);
            Add(t, "Gyro", 0f, 0.15f);

            // A drill, grinder or welder puts most of its energy into the thing it is working on.
            Add(t, "Drill", 0f, 0.2f);
            Add(t, "ShipGrinder", 0f, 0.2f);
            Add(t, "ShipWelder", 0f, 0.2f);

            // Production is chemistry and grinding: most of the draw ends up as heat in the block,
            // and a refinery is the classic industrial heat source.
            Add(t, "Refinery", 0f, 0.7f);
            Add(t, "Assembler", 0f, 0.6f);
            Add(t, "OxygenGenerator", 0f, 0.6f);
            Add(t, "SurvivalKit", 0f, 0.6f);

            // Moving gas is compression, and compression is heat.
            Add(t, "AirVent", 0f, 0.5f);
            Add(t, "OxygenTank", 0f, 0.1f);

            // ---- field generators ------------------------------------------------------------
            // A field does no work on anything that carries the energy away, so what goes in stays.
            Add(t, "GravityGenerator", 0f, 0.8f);
            Add(t, "GravityGeneratorSphere", 0f, 0.8f);
            Add(t, "VirtualMass", 0f, 0.8f);
            Add(t, "SafeZoneBlock", 0f, 0.8f);
            Add(t, "SpaceBall", 0f, 0.8f);

            // A jump drive charges a capacitor bank and dumps it. Storage is efficient; the dump
            // is not, and the block is built out of superconductor, which quenches warm.
            Add(t, "JumpDrive", 0f, 0.15f, 1f, 2f);

            // ---- weapons ---------------------------------------------------------------------
            // A gun's heat is chemical rather than electrical, so its draw fraction is modest and
            // the barrel's own mass carries what it makes. Turrets track, which costs motor loss.
            Add(t, "SmallGatlingGun", 0f, 0.3f);
            Add(t, "LargeGatlingTurret", 0f, 0.3f);
            Add(t, "InteriorTurret", 0f, 0.3f);
            Add(t, "SmallMissileLauncher", 0f, 0.2f);
            Add(t, "SmallMissileLauncherReload", 0f, 0.2f);
            Add(t, "LargeMissileTurret", 0f, 0.2f);

            // A warhead full of explosive cooks off. It is the one block where reaching its
            // critical temperature should be dramatic rather than gradual.
            Add(t, "Warhead", 0f, 0.05f, 1f, 4f);

            // ---- surfaces --------------------------------------------------------------------
            // The vent block exists to shed heat and is modelled as the fins it is: far more
            // surface than the cell it occupies.
            Add(t, "HeatVentBlock", 0f, 0.3f, 3f);

            // A lattice, ladder or grating is mostly hole. It presents more surface than a solid
            // cell of the same size, and far less mass, which its components already say.
            Add(t, "Ladder2", 0f, 0.05f, 1.5f);
            Add(t, "Passage", 0f, 0.05f, 1.2f);
            Add(t, "ExhaustBlock", 0f, 0.2f, 2f);

            // A parachute is packed fabric, and a solar farm and a planter are glass boxes.
            Add(t, "Parachute", 0f, 0.05f, 0.5f);
            Add(t, "OxygenFarm", 0f, 0.3f, 1.2f);
            Add(t, "Planter", 0f, 0.1f, 1.2f);

            // ---- life support and interiors --------------------------------------------------
            // A cryo chamber refrigerates, which means rejecting more heat than it draws.
            Add(t, "CryoChamber", 0f, 0.6f);
            Add(t, "MedicalRoom", 0f, 0.6f);
            Add(t, "Kitchen", 0f, 0.8f);

            // ---- inert -----------------------------------------------------------------------
            // A block that draws nothing makes nothing. Naming them is not pedantry: it stops a
            // future default from quietly giving a cargo container a heat source.
            Add(t, "CargoContainer", 0f, 0.05f);
            Add(t, "Conveyor", 0f, 0.05f);
            Add(t, "ConveyorConnector", 0f, 0.05f);
            Add(t, "ConveyorSorter", 0f, 0.1f);
            Add(t, "Collector", 0f, 0.1f);
            Add(t, "Door", 0f, 0.05f);
            Add(t, "AirtightHangarDoor", 0f, 0.05f);
            Add(t, "AirtightSlideDoor", 0f, 0.05f);
            Add(t, "LandingGear", 0f, 0.05f);
            Add(t, "ShipConnector", 0f, 0.05f);
            Add(t, "MergeBlock", 0f, 0.05f);
            Add(t, "Wheel", 0f, 0.05f);
            Add(t, "MotorRotor", 0f, 0.05f);
            Add(t, "MotorAdvancedRotor", 0f, 0.05f);
            Add(t, "PistonTop", 0f, 0.05f);
            Add(t, "Cockpit", 0f, 0.2f);
            Add(t, "TargetDummyBlock", 0f, 0.05f);

            return t;
        }
    }
}
