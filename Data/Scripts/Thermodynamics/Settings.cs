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
        public const int CurrentVersion = 5;

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
        [ProtoMember(15)] public bool EnableHeatSources;
        [ProtoMember(16)] public bool EnableWasteHeat;
        [ProtoMember(17)] public bool EnablePlanets;
        [ProtoMember(18)] public bool EnableFriction;
        [ProtoMember(19)] public bool EnableDamage;
        [ProtoMember(20)] public bool EnableCoolantLoops;
        [ProtoMember(21)] public bool EnableRoomAir;

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

        // ---- presentation ------------------------------------------------------------------

        /// <summary>Crosshair readout for the block being looked at. Client side.</summary>
        [ProtoMember(50)] public bool DebugTextOnScreen;

        /// <summary>Draws the sun ray from each grid, white when lit and red when occluded.</summary>
        [ProtoMember(51)] public bool DebugSolarRaycast;

        /// <summary>Draws the relative wind vector.</summary>
        [ProtoMember(52)] public bool DebugWindRaycast;

        /// <summary>
        /// Recolours every block by temperature by writing real block paint.
        ///
        /// Destructive: it overwrites players' colour schemes permanently and cannot be undone by
        /// switching it off. Off by default, and the thermal vision overlay does the same job
        /// without touching the grid.
        /// </summary>
        [ProtoMember(53)] public bool DebugTemperatureBlockColors;

        [ProtoMember(54)] public bool DebugSolarRadiationBlockColors;
        [ProtoMember(55)] public bool DebugExposedSurfaceBlockColors;
        [ProtoMember(56)] public bool DebugFrictionColors;

        /// <summary>
        /// Thermal vision: a non-destructive heat overlay drawn over whatever the player is
        /// looking at. See <see cref="ThermalVision"/>.
        /// </summary>
        [ProtoMember(60)] public bool EnableThermalVision;

        /// <summary>Thermal vision draws in greyscale rather than the heat ramp.</summary>
        [ProtoMember(61)] public bool ThermalVisionGreyscale;

        /// <summary>Metres out to which thermal vision draws anything at all.</summary>
        [ProtoMember(62)] public float ThermalVisionRange;

        /// <summary>
        /// Metres out to which grids are drawn block by block. Past this a grid is one body at the
        /// temperature of its hottest block, which is what a distant ship looks like on a real
        /// sensor and what keeps a fleet from costing a billboard per block.
        /// </summary>
        [ProtoMember(63)] public float ThermalVisionDetailRange;

        /// <summary>
        /// How much of the visible-light image is removed, 0..1.
        ///
        /// 1 is a thermal camera: the ordinary view is gone and what is left is only what the mod
        /// draws. Lower values leave some of it showing through, which makes it a tinted visor
        /// rather than a sensor — available, but not the intent.
        /// </summary>
        [ProtoMember(64)] public float ThermalVisionDimming;

        /// <summary>Bottom of the sensor's span, K. Anything colder clips to black.</summary>
        [ProtoMember(65)] public float ThermalVisionMinKelvin;

        /// <summary>Top of the sensor's span, K. Anything hotter clips to white.</summary>
        [ProtoMember(66)] public float ThermalVisionMaxKelvin;

        /// <summary>Brightness of drawn bodies, 0..1.</summary>
        [ProtoMember(67)] public float ThermalVisionIntensity;

        /// <summary>Temperature a character is drawn at, K. Bodies are not simulated.</summary>
        [ProtoMember(70)] public float ThermalVisionBodyTemperature;

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
                EnableHeatSources = true,
                EnableWasteHeat = true,
                EnablePlanets = true,
                EnableFriction = true,
                EnableDamage = true,
                EnableCoolantLoops = true,
                EnableRoomAir = true,

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

                // Presentation defaults to off. A fresh install should look like the game, not
                // like a debugger: the previous defaults repainted every grid in the world.
                DebugTextOnScreen = false,
                DebugSolarRaycast = false,
                DebugWindRaycast = false,
                DebugTemperatureBlockColors = false,
                DebugSolarRadiationBlockColors = false,
                DebugExposedSurfaceBlockColors = false,
                DebugFrictionColors = false,

                EnableThermalVision = true,
                ThermalVisionGreyscale = false,
                ThermalVisionRange = 400f,
                ThermalVisionDetailRange = 80f,
                ThermalVisionDimming = 1f,
                ThermalVisionMinKelvin = 240f,
                ThermalVisionMaxKelvin = 500f,
                ThermalVisionIntensity = 0.9f,
                ThermalVisionBodyTemperature = 310f,

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
            if (ThermalVisionRange < 1f) ThermalVisionRange = 1f;
            if (ThermalVisionDetailRange < 1f) ThermalVisionDetailRange = 1f;
            if (ThermalVisionDetailRange > ThermalVisionRange) ThermalVisionDetailRange = ThermalVisionRange;
            if (ThermalVisionDimming < 0f) ThermalVisionDimming = 0f;
            if (ThermalVisionDimming > 1f) ThermalVisionDimming = 1f;
            if (ThermalVisionIntensity < 0f) ThermalVisionIntensity = 0f;
            if (ThermalVisionBodyTemperature < 0f) ThermalVisionBodyTemperature = 0f;
            if (ThermalVisionMinKelvin < 0f) ThermalVisionMinKelvin = 0f;
            if (ThermalVisionMaxKelvin <= ThermalVisionMinKelvin)
                ThermalVisionMaxKelvin = ThermalVisionMinKelvin + 1f;
            if (RoomConvectionCoefficient < 0f) RoomConvectionCoefficient = 0f;
            if (RoomAirDensity < 0f) RoomAirDensity = 0f;
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
            core.EnableHeatSources = EnableHeatSources;
            core.EnableWasteHeat = EnableWasteHeat;
            core.EnablePlanets = EnablePlanets;
            core.EnableFriction = EnableFriction;
            core.EnableDamage = EnableDamage;
            core.EnableCoolantLoops = EnableCoolantLoops;
            core.EnableRoomAir = EnableRoomAir;

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
                "EnableSolarHeat", "EnableHeatSources", "EnableWasteHeat", "EnablePlanets",
                "EnableFriction", "EnableDamage", "EnableCoolantLoops", "EnableRoomAir",
                "ClampConductionOvershoot", "DamageIsPerSecond",
                "Frequency", "SimulationSpeed", "HeatTimeScale",
                "VacuumTemperature", "SolarEnergy", "FrictionAtSpeedsAbove", "FrictionScale",
                "RoomConvectionCoefficient", "RoomAirDensity", "SolarOcclusionInterval",
                "DebugTextOnScreen", "DebugSolarRaycast", "DebugWindRaycast",
                "DebugTemperatureBlockColors", "DebugSolarRadiationBlockColors",
                "DebugExposedSurfaceBlockColors", "DebugFrictionColors",
                "EnableThermalVision", "ThermalVisionGreyscale", "ThermalVisionRange",
                "ThermalVisionDetailRange", "ThermalVisionDimming",
                "ThermalVisionMinKelvin", "ThermalVisionMaxKelvin",
                "ThermalVisionIntensity",
                "ThermalVisionBodyTemperature",
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
                case "EnableHeatSources": return Flag(EnableHeatSources);
                case "EnableWasteHeat": return Flag(EnableWasteHeat);
                case "EnablePlanets": return Flag(EnablePlanets);
                case "EnableFriction": return Flag(EnableFriction);
                case "EnableDamage": return Flag(EnableDamage);
                case "EnableCoolantLoops": return Flag(EnableCoolantLoops);
                case "EnableRoomAir": return Flag(EnableRoomAir);
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
                case "SolarOcclusionInterval": return SolarOcclusionInterval;
                case "DebugTextOnScreen": return Flag(DebugTextOnScreen);
                case "DebugSolarRaycast": return Flag(DebugSolarRaycast);
                case "DebugWindRaycast": return Flag(DebugWindRaycast);
                case "DebugTemperatureBlockColors": return Flag(DebugTemperatureBlockColors);
                case "DebugSolarRadiationBlockColors": return Flag(DebugSolarRadiationBlockColors);
                case "DebugExposedSurfaceBlockColors": return Flag(DebugExposedSurfaceBlockColors);
                case "DebugFrictionColors": return Flag(DebugFrictionColors);
                case "EnableThermalVision": return Flag(EnableThermalVision);
                case "ThermalVisionGreyscale": return Flag(ThermalVisionGreyscale);
                case "ThermalVisionRange": return ThermalVisionRange;
                case "ThermalVisionDetailRange": return ThermalVisionDetailRange;
                case "ThermalVisionDimming": return ThermalVisionDimming;
                case "ThermalVisionMinKelvin": return ThermalVisionMinKelvin;
                case "ThermalVisionMaxKelvin": return ThermalVisionMaxKelvin;
                case "ThermalVisionIntensity": return ThermalVisionIntensity;
                case "ThermalVisionBodyTemperature": return ThermalVisionBodyTemperature;
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
                case "EnableHeatSources": EnableHeatSources = Flag(value); return true;
                case "EnableWasteHeat": EnableWasteHeat = Flag(value); return true;
                case "EnablePlanets": EnablePlanets = Flag(value); return true;
                case "EnableFriction": EnableFriction = Flag(value); return true;
                case "EnableDamage": EnableDamage = Flag(value); return true;
                case "EnableCoolantLoops": EnableCoolantLoops = Flag(value); return true;
                case "EnableRoomAir": EnableRoomAir = Flag(value); return true;
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
                case "SolarOcclusionInterval": SolarOcclusionInterval = (int)value; return true;
                case "DebugTextOnScreen": DebugTextOnScreen = Flag(value); return true;
                case "DebugSolarRaycast": DebugSolarRaycast = Flag(value); return true;
                case "DebugWindRaycast": DebugWindRaycast = Flag(value); return true;
                case "DebugTemperatureBlockColors": DebugTemperatureBlockColors = Flag(value); return true;
                case "DebugSolarRadiationBlockColors": DebugSolarRadiationBlockColors = Flag(value); return true;
                case "DebugExposedSurfaceBlockColors": DebugExposedSurfaceBlockColors = Flag(value); return true;
                case "DebugFrictionColors": DebugFrictionColors = Flag(value); return true;
                case "EnableThermalVision": EnableThermalVision = Flag(value); return true;
                case "ThermalVisionGreyscale": ThermalVisionGreyscale = Flag(value); return true;
                case "ThermalVisionRange": ThermalVisionRange = value; return true;
                case "ThermalVisionDetailRange": ThermalVisionDetailRange = value; return true;
                case "ThermalVisionDimming": ThermalVisionDimming = value; return true;
                case "ThermalVisionMinKelvin": ThermalVisionMinKelvin = value; return true;
                case "ThermalVisionMaxKelvin": ThermalVisionMaxKelvin = value; return true;
                case "ThermalVisionIntensity": ThermalVisionIntensity = value; return true;
                case "ThermalVisionBodyTemperature": ThermalVisionBodyTemperature = value; return true;
                case "EnableTelemetry": EnableTelemetry = Flag(value); Telemetry.SetEnabled(EnableTelemetry); return true;
                case "TelemetrySampleStride": TelemetrySampleStride = (int)value; return true;
                default: return false;
            }
        }

        /// <summary>True when the named setting is a switch rather than a number.</summary>
        public static bool IsFlag(string name)
        {
            return name != null && (name.StartsWith("Enable") || name.StartsWith("Debug")
                || name == "ClampConductionOvershoot" || name == "DamageIsPerSecond"
                || name == "ThermalVisionGreyscale");
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
