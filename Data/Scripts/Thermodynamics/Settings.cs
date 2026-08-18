using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// The world's configuration file, and the bridge from it to the simulation's own settings.
    ///
    /// This type is the serialised form and nothing else: it owns the XML shape, the defaults and
    /// the file, and it converts into <see cref="Core.ThermalSettings"/>, which is what every grid
    /// actually reads. The two are kept in step by <see cref="Apply"/>, which writes through to
    /// the same instance the grids already hold — so a value changed mid-session takes effect on
    /// the next step without reloading anything.
    ///
    /// Every field is reachable by name through <see cref="GetValue"/> and <see cref="SetValue"/>,
    /// which is what the chat commands, the terminal controls and the mod API all drive. Booleans
    /// read and write as 0 and 1, so one accessor pair covers the whole file.
    /// </summary>
    [ProtoContract]
    public class Settings
    {
        public const string Filename = "ThermodynamicsConfig.cfg";
        public const string Name = "Thermodynamics";

        /// <summary>
        /// Bumped whenever the file's shape changes. A file at a different version is replaced
        /// with defaults rather than partially applied.
        /// </summary>
        public const int CurrentVersion = 6;

        public static Settings Instance;

        public static readonly MyStringHash DefaultSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamics");
        public static readonly MyStringHash DefaultLoopSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamicsLoop");

        [ProtoMember(1)] public int Version;

        // ---- mechanisms --------------------------------------------------------------------

        [ProtoMember(10)] public bool EnableEnvironment;
        [ProtoMember(11)] public bool EnableConduction;
        [ProtoMember(12)] public bool EnableRadiation;
        [ProtoMember(13)] public bool EnableConvection;
        [ProtoMember(14)] public bool EnableSolarHeat;

        /// <summary>
        /// Whether a grid shadows itself: a face behind the ship's own structure takes no sunlight.
        /// Costs a pass over the grid's cells each time the sun moves appreciably. Off is the cheap
        /// model, which lights any face pointing at the sun.
        /// </summary>
        [ProtoMember(58)] public bool SolarSelfShadowing;

        /// <summary>
        /// Whether a planet can shadow a grid — night, and the shadow of a world seen from orbit.
        /// Analytic: an angle against the planet's radius, no raycast, so it is the cheap one.
        /// </summary>
        [ProtoMember(59)] public bool SolarOcclusionPlanets;

        /// <summary>
        /// Whether asteroids and other voxels can shadow a grid. Costs a physics raycast per
        /// candidate voxel per sample.
        /// </summary>
        /// <summary>
        /// Whether the planet's own terrain can shadow a grid: the mountain to the east at sunrise,
        /// the canyon wall, the cliff a base is parked against. Costs a short walk of ground-height
        /// lookups, and only for grids near a surface.
        /// </summary>
        [ProtoMember(74)] public bool SolarOcclusionTerrain;

        /// <summary>
        /// How far the terrain walk looks along the sun ray, in metres. Far ground shadows almost
        /// nothing — the cliff two hundred metres away is what matters — so this is short by
        /// design, and every metre of it costs lookups.
        /// </summary>
        [ProtoMember(75)] public float SolarTerrainRange;

        [ProtoMember(71)] public bool SolarOcclusionVoxels;

        /// <summary>
        /// Whether other grids can shadow a grid — a station's hull over a docked ship, a fleet in
        /// formation. Costs a ray against the other grid's blocks per candidate per sample.
        /// </summary>
        [ProtoMember(72)] public bool SolarOcclusionGrids;

        /// <summary>
        /// How many points across a grid are tested, 1..9. One is a single ray from the middle,
        /// which is all or nothing for the whole ship. More points spread through the hull turn a
        /// terminator crossing into a ramp, and cost their own share of the work each.
        /// </summary>
        [ProtoMember(73)] public int SolarOcclusionSamples;
        [ProtoMember(15)] public bool EnableHeatSources;
        [ProtoMember(16)] public bool EnableWasteHeat;
        [ProtoMember(17)] public bool EnablePlanets;
        [ProtoMember(18)] public bool EnableFriction;
        [ProtoMember(19)] public bool EnableDamage;
        [ProtoMember(20)] public bool EnableCoolantLoops;
        [ProtoMember(21)] public bool EnableRoomAir;
        [ProtoMember(22)] public bool EnableHeatPumps;

        // ---- solver ------------------------------------------------------------------------

        [ProtoMember(30)] public bool ClampConductionOvershoot;
        [ProtoMember(31)] public bool DamageIsPerSecond;
        [ProtoMember(32)] public int Frequency;
        [ProtoMember(33)] public float SimulationSpeed;
        [ProtoMember(34)] public float HeatTimeScale;

        // ---- environment -------------------------------------------------------------------

        [ProtoMember(40)] public float VacuumTemperature;
        [ProtoMember(41)] public float SolarEnergy;
        [ProtoMember(42)] public float FrictionAtSpeedsAbove;
        [ProtoMember(43)] public float FrictionScale;
        [ProtoMember(44)] public float RoomConvectionCoefficient;
        [ProtoMember(45)] public float RoomAirDensity;

        /// <summary>Solver steps between solar occlusion raycasts.</summary>
        [ProtoMember(46)] public int SolarOcclusionInterval;

        // ---- heat pumps --------------------------------------------------------------------

        /// <summary>How much of the Carnot limit a heat pump achieves, 0..1.</summary>
        [ProtoMember(47)] public float HeatPumpCarnotFraction;

        /// <summary>Ceiling on a heat pump's coefficient of performance.</summary>
        [ProtoMember(48)] public float HeatPumpMaxCoefficient;

        // ---- presentation ------------------------------------------------------------------

        /// <summary>Crosshair readout for the block being looked at. Client side.</summary>
        [ProtoMember(50)] public bool DebugTextOnScreen;

        /// <summary>Draws the sun ray from each grid, white when lit and red when occluded.</summary>
        [ProtoMember(51)] public bool DebugSolarRaycast;

        /// <summary>Draws the relative wind vector.</summary>
        [ProtoMember(52)] public bool DebugWindRaycast;

        /// <summary>
        /// Which value the block overlay starts a session showing, as a
        /// <see cref="ThermalDebugView.Mode"/>: 0 off, 1 temperature, 2 solar watts, 3 exposed
        /// faces, 4 friction watts, 5 rooms. Ctrl+Shift+= cycles it in play, client side.
        /// </summary>
        [ProtoMember(57)] public int DebugBlockOverlay;

        // ProtoMember numbers 53-56 were the block-colouring debug modes, which wrote real block
        // paint and have been replaced by the overlay above. 60-70 were the thermal vision
        // overlay. Both stay unused so an older config or an older peer's message does not land on
        // a different field.

        // ---- telemetry ---------------------------------------------------------------------

        [ProtoMember(80)] public bool EnableTelemetry;
        [ProtoMember(81)] public int TelemetrySampleStride;

        public static Settings GetDefaults()
        {
            Settings s = new Settings
            {
                Version = CurrentVersion,

                EnableEnvironment = true,
                EnableConduction = true,
                EnableRadiation = true,
                EnableConvection = true,
                EnableSolarHeat = true,
                SolarSelfShadowing = true,
                SolarOcclusionPlanets = true,
                SolarOcclusionTerrain = true,
                SolarTerrainRange = 4000f,
                SolarOcclusionVoxels = true,
                SolarOcclusionGrids = true,
                SolarOcclusionSamples = 1,
                EnableHeatSources = true,
                EnableWasteHeat = true,
                EnablePlanets = true,
                EnableFriction = true,
                EnableDamage = true,
                EnableCoolantLoops = true,
                EnableRoomAir = true,
                EnableHeatPumps = true,

                ClampConductionOvershoot = true,
                DamageIsPerSecond = true,
                Frequency = 4,
                SimulationSpeed = 1f,
                HeatTimeScale = 225f,

                VacuumTemperature = 2.7f,
                SolarEnergy = 1000f,
                FrictionAtSpeedsAbove = 50f,
                FrictionScale = 0.001f,
                RoomConvectionCoefficient = 8f,
                RoomAirDensity = 1.225f,
                SolarOcclusionInterval = 12,

                HeatPumpCarnotFraction = 0.4f,
                HeatPumpMaxCoefficient = 8f,

                // Presentation defaults to off. A fresh install should look like the game, not
                // like a debugger: the previous defaults repainted every grid in the world.
                DebugTextOnScreen = false,
                DebugSolarRaycast = false,
                DebugWindRaycast = false,
                DebugBlockOverlay = 0,

                EnableTelemetry = false,
                TelemetrySampleStride = 4,
            };

            s.Clamp();
            return s;
        }

        private void Clamp()
        {
            if (Frequency < 1) Frequency = 1;
            if (SimulationSpeed <= 0f) SimulationSpeed = 1f;
            if (HeatTimeScale <= 0f) HeatTimeScale = 1f;
            if (TelemetrySampleStride < 1) TelemetrySampleStride = 1;
            if (SolarOcclusionInterval < 1) SolarOcclusionInterval = 1;
            if (SolarTerrainRange < 0f) SolarTerrainRange = 0f;
            if (SolarOcclusionSamples < 1) SolarOcclusionSamples = 1;
            if (SolarOcclusionSamples > Core.SolarOcclusionSampler.MaxSamples)
                SolarOcclusionSamples = Core.SolarOcclusionSampler.MaxSamples;
            if (DebugBlockOverlay < 0) DebugBlockOverlay = 0;
            if (DebugBlockOverlay >= ThermalDebugView.ModeCount)
                DebugBlockOverlay = ThermalDebugView.ModeCount - 1;
            if (RoomConvectionCoefficient < 0f) RoomConvectionCoefficient = 0f;
            if (RoomAirDensity < 0f) RoomAirDensity = 0f;
            if (HeatPumpCarnotFraction < 0f) HeatPumpCarnotFraction = 0f;
            if (HeatPumpCarnotFraction > 1f) HeatPumpCarnotFraction = 1f;
            if (HeatPumpMaxCoefficient < 0f) HeatPumpMaxCoefficient = 0f;
        }

        // ---- conversion --------------------------------------------------------------------

        [XmlIgnore]
        private Core.ThermalSettings core;

        /// <summary>
        /// The same configuration in the form the simulation consumes.
        ///
        /// Built once and then written through, never replaced: every grid holds a reference to
        /// this exact instance, so replacing it would leave existing grids running the old values
        /// while new ones ran the new.
        /// </summary>
        public Core.ThermalSettings ToCore()
        {
            if (core == null) core = new Core.ThermalSettings();
            Apply();
            return core;
        }

        /// <summary>
        /// Pushes the current values into the simulation's settings and derives them, which is
        /// what makes every grid pick the change up on its next step.
        /// </summary>
        public void Apply()
        {
            Clamp();
            if (core == null) core = new Core.ThermalSettings();

            core.EnableEnvironment = EnableEnvironment;
            core.EnableConduction = EnableConduction;
            core.EnableRadiation = EnableRadiation;
            core.EnableConvection = EnableConvection;
            core.EnableSolarHeat = EnableSolarHeat;
            core.SolarSelfShadowing = SolarSelfShadowing;
            core.EnableHeatSources = EnableHeatSources;
            core.EnableWasteHeat = EnableWasteHeat;
            core.EnablePlanets = EnablePlanets;
            core.EnableFriction = EnableFriction;
            core.EnableDamage = EnableDamage;
            core.EnableCoolantLoops = EnableCoolantLoops;
            core.EnableRoomAir = EnableRoomAir;
            core.EnableHeatPumps = EnableHeatPumps;

            core.ClampConductionOvershoot = ClampConductionOvershoot;
            core.DamageIsPerSecond = DamageIsPerSecond;
            core.Frequency = Frequency;
            core.SimulationSpeed = SimulationSpeed;
            core.HeatTimeScale = HeatTimeScale;

            core.VacuumTemperature = VacuumTemperature;
            core.SolarEnergy = SolarEnergy;
            core.FrictionAtSpeedsAbove = FrictionAtSpeedsAbove;
            core.FrictionScale = FrictionScale;
            core.RoomConvectionCoefficient = RoomConvectionCoefficient;
            core.RoomAirDensity = RoomAirDensity;
            core.HeatPumpCarnotFraction = HeatPumpCarnotFraction;
            core.HeatPumpMaxCoefficient = HeatPumpMaxCoefficient;

            core.Derive();

            Telemetry.SampleStride = TelemetrySampleStride;
        }

        /// <summary>Solver steps per real second. Used by readouts that report rates.</summary>
        [XmlIgnore]
        public float StepsPerSecond
        {
            get { return core == null ? Frequency * SimulationSpeed : core.StepsPerSecond; }
        }

        // ---- access by name ----------------------------------------------------------------

        /// <summary>
        /// Every setting a player or a mod may change at runtime, in the order they are listed to
        /// a player. Booleans are 0 and 1.
        /// </summary>
        public static List<string> Names()
        {
            return new List<string>
            {
                "EnableEnvironment", "EnableConduction", "EnableRadiation", "EnableConvection",
                "EnableSolarHeat", "SolarSelfShadowing",
                "SolarOcclusionPlanets", "SolarOcclusionTerrain", "SolarTerrainRange",
                "SolarOcclusionVoxels", "SolarOcclusionGrids",
                "SolarOcclusionSamples",
                "EnableHeatSources", "EnableWasteHeat", "EnablePlanets",
                "EnableFriction", "EnableDamage", "EnableCoolantLoops", "EnableRoomAir",
                "EnableHeatPumps",
                "ClampConductionOvershoot", "DamageIsPerSecond",
                "Frequency", "SimulationSpeed", "HeatTimeScale",
                "VacuumTemperature", "SolarEnergy", "FrictionAtSpeedsAbove", "FrictionScale",
                "RoomConvectionCoefficient", "RoomAirDensity", "SolarOcclusionInterval",
                "HeatPumpCarnotFraction", "HeatPumpMaxCoefficient",
                "DebugTextOnScreen", "DebugSolarRaycast", "DebugWindRaycast",
                "DebugBlockOverlay",
                "EnableTelemetry", "TelemetrySampleStride",
            };
        }

        /// <summary>The named setting's value, or <see cref="float.NaN"/> when there is no such setting.</summary>
        public float GetValue(string name)
        {
            switch (name)
            {
                case "EnableEnvironment": return Flag(EnableEnvironment);
                case "EnableConduction": return Flag(EnableConduction);
                case "EnableRadiation": return Flag(EnableRadiation);
                case "EnableConvection": return Flag(EnableConvection);
                case "EnableSolarHeat": return Flag(EnableSolarHeat);
                case "SolarSelfShadowing": return Flag(SolarSelfShadowing);
                case "SolarOcclusionPlanets": return Flag(SolarOcclusionPlanets);
                case "SolarOcclusionTerrain": return Flag(SolarOcclusionTerrain);
                case "SolarTerrainRange": return SolarTerrainRange;
                case "SolarOcclusionVoxels": return Flag(SolarOcclusionVoxels);
                case "SolarOcclusionGrids": return Flag(SolarOcclusionGrids);
                case "SolarOcclusionSamples": return SolarOcclusionSamples;
                case "EnableHeatSources": return Flag(EnableHeatSources);
                case "EnableWasteHeat": return Flag(EnableWasteHeat);
                case "EnablePlanets": return Flag(EnablePlanets);
                case "EnableFriction": return Flag(EnableFriction);
                case "EnableDamage": return Flag(EnableDamage);
                case "EnableCoolantLoops": return Flag(EnableCoolantLoops);
                case "EnableRoomAir": return Flag(EnableRoomAir);
                case "EnableHeatPumps": return Flag(EnableHeatPumps);
                case "ClampConductionOvershoot": return Flag(ClampConductionOvershoot);
                case "DamageIsPerSecond": return Flag(DamageIsPerSecond);
                case "Frequency": return Frequency;
                case "SimulationSpeed": return SimulationSpeed;
                case "HeatTimeScale": return HeatTimeScale;
                case "VacuumTemperature": return VacuumTemperature;
                case "SolarEnergy": return SolarEnergy;
                case "FrictionAtSpeedsAbove": return FrictionAtSpeedsAbove;
                case "FrictionScale": return FrictionScale;
                case "RoomConvectionCoefficient": return RoomConvectionCoefficient;
                case "RoomAirDensity": return RoomAirDensity;
                case "HeatPumpCarnotFraction": return HeatPumpCarnotFraction;
                case "HeatPumpMaxCoefficient": return HeatPumpMaxCoefficient;
                case "SolarOcclusionInterval": return SolarOcclusionInterval;
                case "DebugTextOnScreen": return Flag(DebugTextOnScreen);
                case "DebugSolarRaycast": return Flag(DebugSolarRaycast);
                case "DebugWindRaycast": return Flag(DebugWindRaycast);
                case "DebugBlockOverlay": return DebugBlockOverlay;
                case "EnableTelemetry": return Flag(EnableTelemetry);
                case "TelemetrySampleStride": return TelemetrySampleStride;
                default: return float.NaN;
            }
        }

        /// <summary>
        /// Sets a setting by name. Returns false for a name that does not exist; the caller is
        /// expected to <see cref="Apply"/> afterwards.
        /// </summary>
        public bool SetValue(string name, float value)
        {
            switch (name)
            {
                case "EnableEnvironment": EnableEnvironment = Flag(value); return true;
                case "EnableConduction": EnableConduction = Flag(value); return true;
                case "EnableRadiation": EnableRadiation = Flag(value); return true;
                case "EnableConvection": EnableConvection = Flag(value); return true;
                case "EnableSolarHeat": EnableSolarHeat = Flag(value); return true;
                case "SolarSelfShadowing": SolarSelfShadowing = Flag(value); return true;
                case "SolarOcclusionPlanets": SolarOcclusionPlanets = Flag(value); return true;
                case "SolarOcclusionTerrain": SolarOcclusionTerrain = Flag(value); return true;
                case "SolarTerrainRange": SolarTerrainRange = value; return true;
                case "SolarOcclusionVoxels": SolarOcclusionVoxels = Flag(value); return true;
                case "SolarOcclusionGrids": SolarOcclusionGrids = Flag(value); return true;
                case "SolarOcclusionSamples": SolarOcclusionSamples = (int)value; return true;
                case "EnableHeatSources": EnableHeatSources = Flag(value); return true;
                case "EnableWasteHeat": EnableWasteHeat = Flag(value); return true;
                case "EnablePlanets": EnablePlanets = Flag(value); return true;
                case "EnableFriction": EnableFriction = Flag(value); return true;
                case "EnableDamage": EnableDamage = Flag(value); return true;
                case "EnableCoolantLoops": EnableCoolantLoops = Flag(value); return true;
                case "EnableRoomAir": EnableRoomAir = Flag(value); return true;
                case "EnableHeatPumps": EnableHeatPumps = Flag(value); return true;
                case "ClampConductionOvershoot": ClampConductionOvershoot = Flag(value); return true;
                case "DamageIsPerSecond": DamageIsPerSecond = Flag(value); return true;
                case "Frequency": Frequency = (int)value; return true;
                case "SimulationSpeed": SimulationSpeed = value; return true;
                case "HeatTimeScale": HeatTimeScale = value; return true;
                case "VacuumTemperature": VacuumTemperature = value; return true;
                case "SolarEnergy": SolarEnergy = value; return true;
                case "FrictionAtSpeedsAbove": FrictionAtSpeedsAbove = value; return true;
                case "FrictionScale": FrictionScale = value; return true;
                case "RoomConvectionCoefficient": RoomConvectionCoefficient = value; return true;
                case "RoomAirDensity": RoomAirDensity = value; return true;
                case "HeatPumpCarnotFraction": HeatPumpCarnotFraction = value; return true;
                case "HeatPumpMaxCoefficient": HeatPumpMaxCoefficient = value; return true;
                case "SolarOcclusionInterval": SolarOcclusionInterval = (int)value; return true;
                case "DebugTextOnScreen": DebugTextOnScreen = Flag(value); return true;
                case "DebugSolarRaycast": DebugSolarRaycast = Flag(value); return true;
                case "DebugWindRaycast": DebugWindRaycast = Flag(value); return true;
                case "DebugBlockOverlay":
                    DebugBlockOverlay = (int)value;
                    ThermalDebugView.Set((ThermalDebugView.Mode)DebugBlockOverlay);
                    return true;
                case "EnableTelemetry": EnableTelemetry = Flag(value); Telemetry.SetEnabled(EnableTelemetry); return true;
                case "TelemetrySampleStride": TelemetrySampleStride = (int)value; return true;
                default: return false;
            }
        }

        /// <summary>True when the named setting is a switch rather than a number.</summary>
        public static bool IsFlag(string name)
        {
            return name != null && name != "DebugBlockOverlay"
                && (name.StartsWith("Enable") || name.StartsWith("Debug")
                || name == "SolarSelfShadowing"
                || name == "SolarOcclusionPlanets"
                || name == "SolarOcclusionTerrain"
                || name == "SolarOcclusionVoxels"
                || name == "SolarOcclusionGrids"
                || name == "ClampConductionOvershoot" || name == "DamageIsPerSecond");
        }

        private static float Flag(bool value)
        {
            return value ? 1f : 0f;
        }

        private static bool Flag(float value)
        {
            return value != 0f;
        }

        // ---- file --------------------------------------------------------------------------

        /// <summary>
        /// The active settings, loading the world's config file on first use.
        ///
        /// Load order is not something a mod controls: a grid's game logic can initialise before
        /// the session component does. Whoever asks first triggers the read, so the config file
        /// cannot be bypassed by a world whose grids happen to load early.
        /// </summary>
        public static Settings EnsureLoaded()
        {
            if (Instance != null) return Instance;

            Instance = CanReadWorldStorage() ? Load() : GetDefaults();
            Instance.Apply();
            return Instance;
        }

        /// <summary>
        /// Whether the config file can be read yet. Clients take the server's settings rather than
        /// their own file, and very early in a session the utilities are not there at all.
        /// </summary>
        private static bool CanReadWorldStorage()
        {
            try
            {
                return MyAPIGateway.Utilities != null
                    && MyAPIGateway.Session != null
                    && MyAPIGateway.Session.IsServer;
            }
            catch
            {
                return false;
            }
        }

        public static Settings Load()
        {
            Settings settings = GetDefaults();
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
                {
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
                    string text = reader.ReadToEnd();
                    reader.Close();

                    Settings loaded = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

                    if (loaded.Version != CurrentVersion)
                    {
                        MyLog.Default.Info("[" + Name + "] config version " + loaded.Version
                            + " replaced with " + CurrentVersion);
                        Save(settings);
                    }
                    else
                    {
                        settings = loaded;
                    }
                }
                else
                {
                    Save(settings);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Name + "] failed to load configuration, using defaults\n" + e);
            }

            settings.Clamp();
            return settings;
        }

        public static void Save(Settings settings)
        {
            try
            {
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                writer.Close();
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Name + "] failed to save settings\n" + e);
            }
        }
    }
}
