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
    /// How much work another grid's shadow is worth.
    /// </summary>
    public enum GridShadowMode
    {
        /// <summary>Other grids do not shadow this one at all.</summary>
        None = 0,

        /// <summary>One ray toward the sun; anything in the way dims the whole grid.</summary>
        Basic = 1,

        /// <summary>The shadow lands on the faces it covers, and only those.</summary>
        Full = 2,
    }

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

        [ProtoMember(10)] public bool EnableEnvironment = true;
        [ProtoMember(11)] public bool EnableConduction = true;
        [ProtoMember(12)] public bool EnableRadiation = true;
        [ProtoMember(13)] public bool EnableConvection = true;
        [ProtoMember(14)] public bool EnableSolarHeat = true;

        /// <summary>
        /// Whether a grid shadows itself: a face behind the ship's own structure takes no sunlight.
        /// Costs a pass over the grid's cells each time the sun moves appreciably. Off is the cheap
        /// model, which lights any face pointing at the sun.
        /// </summary>
        [ProtoMember(58)] public bool SolarSelfShadowing = true;

        /// <summary>
        /// Whether a planet can shadow a grid — night, and the shadow of a world seen from orbit.
        /// Analytic: an angle against the planet's radius, no raycast, so it is the cheap one.
        /// </summary>
        [ProtoMember(59)] public bool SolarOcclusionPlanets = true;

        /// <summary>
        /// Whether asteroids and other voxels can shadow a grid. Costs a physics raycast per
        /// candidate voxel per sample.
        /// </summary>
        /// <summary>
        /// Whether the planet's own terrain can shadow a grid: the mountain to the east at sunrise,
        /// the canyon wall, the cliff a base is parked against. Costs a short walk of ground-height
        /// lookups, and only for grids near a surface.
        /// </summary>
        [ProtoMember(74)] public bool SolarOcclusionTerrain = true;

        /// <summary>
        /// How far the terrain walk looks along the sun ray, in metres. Far ground shadows almost
        /// nothing — the cliff two hundred metres away is what matters — so this is short by
        /// design, and every metre of it costs lookups.
        /// </summary>
        [ProtoMember(75)] public float SolarTerrainRange = 4000f;

        [ProtoMember(71)] public bool SolarOcclusionVoxels = true;

        /// <summary>
        /// How much work other grids' shadows are worth: none, one ray, or the real geometry.
        ///
        /// <see cref="GridShadowMode.Basic"/> is the original test — one ray toward the sun, and if
        /// another grid is in the way the whole grid dims by a sample's share. It costs a ray
        /// against that grid's blocks per sample and nothing else.
        ///
        /// <see cref="GridShadowMode.Full"/> projects the shadow onto the faces it actually covers,
        /// so a station overhead darkens the hull beneath it and leaves the rest in the sun. It
        /// costs a walk through the occluder's blocks per face of this grid, on the shadow pass
        /// rather than per step, and needs <see cref="SolarSelfShadowing"/> for the pass it rides
        /// on.
        /// </summary>
        [ProtoMember(77)] public int SolarGridShadows = (int)GridShadowMode.Full;

        /// <summary>
        /// How many points across a grid are tested, 1..9. One is a single ray from the middle,
        /// which is all or nothing for the whole ship. More points spread through the hull turn a
        /// terminator crossing into a ramp, and cost their own share of the work each.
        /// </summary>
        [ProtoMember(73)] public int SolarOcclusionSamples = 1;
        [ProtoMember(15)] public bool EnableHeatSources = true;
        [ProtoMember(16)] public bool EnableWasteHeat = true;
        [ProtoMember(17)] public bool EnablePlanets = true;
        [ProtoMember(18)] public bool EnableFriction = true;
        [ProtoMember(19)] public bool EnableDamage = true;
        [ProtoMember(20)] public bool EnableCoolantLoops = true;
        [ProtoMember(21)] public bool EnableRoomAir = true;
        [ProtoMember(22)] public bool EnableHeatPumps = true;

        // ---- solver ------------------------------------------------------------------------

        [ProtoMember(30)] public bool ClampConductionOvershoot = true;
        [ProtoMember(31)] public bool DamageIsPerSecond = true;
        [ProtoMember(32)] public int Frequency = 4;
        [ProtoMember(33)] public float SimulationSpeed = 1f;
        [ProtoMember(34)] public float HeatTimeScale = 225f;

        // ---- environment -------------------------------------------------------------------

        [ProtoMember(40)] public float VacuumTemperature = 2.7f;
        [ProtoMember(41)] public float SolarEnergy = 1000f;
        [ProtoMember(42)] public float FrictionAtSpeedsAbove = 50f;
        [ProtoMember(43)] public float FrictionScale = 0.001f;
        [ProtoMember(44)] public float RoomConvectionCoefficient = 8f;
        [ProtoMember(45)] public float RoomAirDensity = 1.225f;

        /// <summary>Solver steps between solar occlusion raycasts.</summary>
        [ProtoMember(46)] public int SolarOcclusionInterval = 12;

        // ---- heat pumps --------------------------------------------------------------------

        /// <summary>How much of the Carnot limit a heat pump achieves, 0..1.</summary>
        [ProtoMember(47)] public float HeatPumpCarnotFraction = 0.4f;

        /// <summary>Ceiling on a heat pump's coefficient of performance.</summary>
        [ProtoMember(48)] public float HeatPumpMaxCoefficient = 8f;

        // ---- presentation ------------------------------------------------------------------

        /// <summary>Crosshair readout for the block being looked at. Client side.</summary>
        [ProtoMember(50)] public bool DebugTextOnScreen = false;

        /// <summary>Draws the sun ray from each grid, white when lit and red when occluded.</summary>
        [ProtoMember(51)] public bool DebugSolarRaycast = false;

        /// <summary>Draws the relative wind vector.</summary>
        [ProtoMember(52)] public bool DebugWindRaycast = false;

        /// <summary>
        /// Which value the block overlay starts a session showing, as a
        /// <see cref="ThermalDebugView.Mode"/>: 0 off, 1 temperature, 2 solar watts, 3 exposed
        /// faces, 4 friction watts, 5 rooms. Ctrl+Shift+= cycles it in play, client side.
        /// </summary>
        [ProtoMember(57)] public int DebugBlockOverlay = 0;

        /// <summary>
        /// Bottom of the room overlay's colour span, K. Room air lives inside a few tens of degrees
        /// of comfortable, so it gets its own span: on the block ramp every room on a ship is the
        /// same shade.
        /// </summary>
        [ProtoMember(78)] public float RoomOverlayMinKelvin = 253.15f;   // -20 C

        /// <summary>Top of the room overlay's colour span, K.</summary>
        [ProtoMember(79)] public float RoomOverlayMaxKelvin = 323.15f;   //  50 C

        // ProtoMember numbers 72 and 76 were the two switches SolarGridShadows replaced.
        // ProtoMember numbers 53-56 were the block-colouring debug modes, which wrote real block
        // paint and have been replaced by the overlay above. 60-70 were the thermal vision
        // overlay. Both stay unused so an older config or an older peer's message does not land on
        // a different field.

        // ---- telemetry ---------------------------------------------------------------------

        [ProtoMember(80)] public bool EnableTelemetry = false;
        [ProtoMember(81)] public int TelemetrySampleStride = 4;

        /// <summary>
        /// A fresh configuration.
        ///
        /// The values themselves live on the fields, which is not a style choice: a config file
        /// written before a setting existed has no element for it, and the XML reader leaves what
        /// it finds. With the defaults here instead, every setting added after a world's file was
        /// written loaded as false or zero in that world — silently, and with no way to tell it
        /// apart from someone having switched it off.
        /// </summary>
        public static Settings GetDefaults()
        {
            Settings s = new Settings();
            s.Version = CurrentVersion;
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
            if (SolarGridShadows < 0) SolarGridShadows = 0;
            if (SolarGridShadows > (int)GridShadowMode.Full) SolarGridShadows = (int)GridShadowMode.Full;
            if (SolarOcclusionSamples < 1) SolarOcclusionSamples = 1;
            if (SolarOcclusionSamples > Core.SolarOcclusionSampler.MaxSamples)
                SolarOcclusionSamples = Core.SolarOcclusionSampler.MaxSamples;
            if (RoomOverlayMaxKelvin <= RoomOverlayMinKelvin)
                RoomOverlayMaxKelvin = RoomOverlayMinKelvin + 1f;
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
                "SolarOcclusionVoxels", "SolarGridShadows",
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
                "DebugBlockOverlay", "RoomOverlayMinKelvin", "RoomOverlayMaxKelvin",
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
                case "SolarGridShadows": return SolarGridShadows;
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
                case "RoomOverlayMinKelvin": return RoomOverlayMinKelvin;
                case "RoomOverlayMaxKelvin": return RoomOverlayMaxKelvin;
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
                case "SolarGridShadows": SolarGridShadows = (int)value; return true;
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
                case "RoomOverlayMinKelvin": RoomOverlayMinKelvin = value; return true;
                case "RoomOverlayMaxKelvin": RoomOverlayMaxKelvin = value; return true;
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
                || name == "SolarOcclusionTerrain"
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
