using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>Fidelity of shadows cast by other grids.</summary>
    public enum GridShadowMode
    {
        /// <summary>Other grids do not shadow this one at all.</summary>
        None = 0,

        /// <summary>One ray towards the sun; anything in the way dims the whole grid.</summary>
        Basic = 1,

        /// <summary>The shadow falls only on the faces it covers.</summary>
        Full = 2,
    }

    /// <summary>
    /// The world's configuration file: the serialised shape, the defaults, and the write-through into
    /// the <see cref="Core.ThermalSettings"/> instance every grid already holds. Every field is
    /// reachable by name, booleans as 0 and 1. See configuration.md.
    /// </summary>
    [ProtoContract]
    public class Settings
    {
        public const string Filename = "ThermodynamicsConfig.cfg";
        public const string Name = "Thermodynamics";

        /// <summary>
        /// Incremented whenever the file's shape changes. A file at a different version is replaced
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
        /// Whether a grid shadows itself, so a face behind the grid's own structure takes no
        /// sunlight. Costs a pass over the grid's cells each time the sun moves appreciably. When
        /// off, any face pointing at the sun is lit.
        /// </summary>
        [ProtoMember(58)] public bool SolarSelfShadowing = true;

        /// <summary>
        /// Whether a planet can shadow a grid, covering both night and a planet's shadow seen from
        /// orbit. Computed analytically from an angle against the planet's radius, with no raycast.
        /// </summary>
        [ProtoMember(59)] public bool SolarOcclusionPlanets = true;

        /// <summary>
        /// Whether planetary terrain can shadow a grid, such as a ridge at sunrise or a cliff a base
        /// is parked against. Costs a short walk of ground-height lookups, and only for grids near a
        /// surface.
        /// </summary>
        [ProtoMember(74)] public bool SolarOcclusionTerrain = true;

        /// <summary>
        /// Distance the terrain walk follows the sun ray, in metres. Deliberately short: nearby
        /// terrain accounts for almost all real shadowing, and every metre costs height lookups.
        /// </summary>
        [ProtoMember(75)] public float SolarTerrainRange = 4000f;

        /// <summary>
        /// Whether asteroids and other voxels can shadow a grid. Costs a physics raycast per
        /// candidate voxel per sample.
        /// </summary>
        [ProtoMember(71)] public bool SolarOcclusionVoxels = true;

        /// <summary>
        /// Fidelity of shadows cast by other grids: none, one ray, or projected geometry.
        /// <see cref="GridShadowMode.Full"/> needs <see cref="SolarSelfShadowing"/>, whose pass it
        /// rides on. See configuration.md, External shadow.
        /// </summary>
        [ProtoMember(77)] public int SolarGridShadows = (int)GridShadowMode.Full;

        /// <summary>
        /// Points across a grid tested for occlusion, 1..9. One is a single ray from the centre,
        /// giving an all-or-nothing result for the whole grid. More points spread through the hull
        /// turn a terminator crossing into a ramp, at proportional cost.
        /// </summary>
        [ProtoMember(73)] public int SolarOcclusionSamples = 1;
        [ProtoMember(15)] public bool EnableHeatSources = true;
        [ProtoMember(16)] public bool EnableWasteHeat = true;
        [ProtoMember(17)] public bool EnablePlanets = true;
        [ProtoMember(18)] public bool EnableFriction = true;
        [ProtoMember(19)] public bool EnableDamage = true;
        [ProtoMember(20)] public bool EnableCoolantLoops = true;

        /// <summary>
        /// The cheap rung of coolant transport: one well-mixed body of fluid instead of parcels
        /// carried round the ring.
        ///
        /// <para>
        /// Off — the default — is the realistic form: the fluid is a ring of parcels, so a stopped
        /// pump leaves the coolant at the radiator cold and the coolant at the reactor hot, and
        /// where a sink sits round the loop matters. On collapses the ring to a single temperature,
        /// which is cheaper and makes a loop's layout stop mattering.
        /// See configuration.md, Coolant loops, and thermal-model.md.
        /// </para>
        /// </summary>
        [ProtoMember(126)] public bool WellMixedCoolant = false;
        [ProtoMember(21)] public bool EnableRoomAir = true;
        [ProtoMember(22)] public bool EnableHeatPumps = true;

        /// <summary>Heat as something that can hurt a player, not only a block.</summary>
        [ProtoMember(23)] public bool EnableSuitDamage = true;

        // ---- solver ------------------------------------------------------------------------

        [ProtoMember(30)] public bool ClampConductionOvershoot = true;

        /// <summary>
        /// Clamp radiation and convection so a node cannot overshoot ambient in one substep. See the
        /// core setting of the same name: it bounds the integrator when <see cref="MaxSubsteps"/>
        /// refuses the substeps the stability estimate demanded.
        /// </summary>
        [ProtoMember(37)] public bool ClampEnvironmentOvershoot = true;
        [ProtoMember(31)] public bool DamageIsPerSecond = true;

        /// <summary>
        /// Solver steps per simulated second. Four is a quarter-second step, which is the basis every
        /// substep figure in the documentation is quoted on.
        /// </summary>
        [ProtoMember(32)] public int Frequency = 4;
        [ProtoMember(33)] public float SimulationSpeed = 1f;
        [ProtoMember(34)] public float HeatTimeScale = 225f;

        /// <summary>
        /// Most element visits one solver step may make — substeps times its links plus its weighted
        /// nodes. Zero removes the bound. A config predating the rename from
        /// <c>MaxLinkVisitsPerStep</c> takes the default deliberately, since the old number was in a
        /// different unit. See configuration.md, Solver.
        /// </summary>
        [ProtoMember(35)] public int MaxElementVisitsPerStep = 2000000;

        /// <summary>
        /// Most substeps one solver step may divide itself into. See the core setting of the same
        /// name: the ceiling on the stability estimate.
        /// </summary>
        [ProtoMember(36)] public int MaxSubsteps = 64;

        /// <summary>
        /// Most substeps any single block may demand of the whole grid before its integration capacity
        /// is floored. Zero leaves every block's real capacity in place. See stiffness.md.
        /// </summary>
        [ProtoMember(84)] public int MaxSubstepsPerBlock = 0;

        // ---- environment -------------------------------------------------------------------

        [ProtoMember(40)] public float VacuumTemperature = 2.7f;
        [ProtoMember(41)] public float SolarEnergy = 1000f;
        [ProtoMember(42)] public float FrictionAtSpeedsAbove = 50f;
        [ProtoMember(43)] public float FrictionScale = 0.001f;
        [ProtoMember(44)] public float RoomConvectionCoefficient = 8f;
        [ProtoMember(45)] public float RoomAirDensity = 1.225f;

        /// <summary>Solver steps between solar occlusion raycasts.</summary>
        [ProtoMember(46)] public int SolarOcclusionInterval = 12;

        /// <summary>
        /// Weight given to the surface material under a grid when offsetting the air above it, 0..1.
        ///
        /// 1 applies the full table: a snowfield about 14 K below the planet's own figure, a desert
        /// about 9 K above. 0 ignores the surface material.
        /// </summary>
        [ProtoMember(82)] public float ClimateGroundInfluence = 1f;

        /// <summary>
        /// Weight given to the weather over a grid when offsetting the air around it, 0..1.
        ///
        /// 1 applies the game's authored figures in full: a heavy snowstorm about 18 K colder with a
        /// tenth of the sunlight and twice the wind, a sandstorm about 12 K warmer. 0 skips the
        /// weather offset for one float compare per grid per step; weather still scales the wind.
        /// </summary>
        [ProtoMember(83)] public float ClimateWeatherInfluence = 1f;

        // ---- coolant loops -----------------------------------------------------------------
        // Loops.xml as world settings. A value still equal to the shipped one is left alone rather
        // than written over the definition; move one and it wins from then on.

        /// <summary>Coolant carried by one pipe block, kg. More is more capacity for the same coupling.</summary>
        [ProtoMember(85)] public float LoopCoolantMassPerPipe = 50f;

        /// <summary>
        /// How well heat crosses between the coolant and the wall it touches, W/(m²·K).
        ///
        /// **Replaces `LoopConductivity`, which was a 0…1 quality against a reference conductivity**
        /// and gave the game a second conduction pace ([backlog.md](../../docs/backlog.md) `C20`).
        /// A new member number rather than a reused one, so a world saved before this reads its old
        /// quality out of 86 and is migrated rather than silently reinterpreted as a coefficient of
        /// one.
        /// </summary>
        [ProtoMember(125)] public float LoopHeatTransferCoefficient = 160f;

        /// <summary>
        /// **Retired.** The 0…1 quality this used to be, kept so a world saved with it moved can be
        /// migrated into <see cref="LoopHeatTransferCoefficient"/>. Negative means "not present",
        /// which is what a world saved after the change writes.
        /// </summary>
        [ProtoMember(86)] public float LegacyLoopConductivity = -1f;

        /// <summary>Coolant's specific heat, J/(kg K). Water-glycol is about 3400.</summary>
        [ProtoMember(87)] public float LoopSpecificHeat = 3400f;

        /// <summary>Scales the coupling between the fluid and the pipe carrying it.</summary>
        [ProtoMember(88)] public float LoopPipeContactMultiplier = 1f;

        /// <summary>Scales the coupling through a sink face into whatever is mounted against it.</summary>
        [ProtoMember(89)] public float LoopSinkContactMultiplier = 1f;

        /// <summary>How fast coolant moves on a large grid with one pump at full speed, m/s.</summary>
        [ProtoMember(90)] public float LoopLargeGridFlowRate = 10f;

        /// <summary>The same for a small grid, split because it is a balance dial rather than a constant.</summary>
        [ProtoMember(91)] public float LoopSmallGridFlowRate = 10f;

        /// <summary>What a stopped ring still carries between neighbouring parcels, 0..1.</summary>
        [ProtoMember(92)] public float LoopStagnantTransferFraction = 1f;

        // ---- planet climate ----------------------------------------------------------------
        //
        // From Planets.xml, and the same argument. One entry ships, so these address it; a world
        // with several authored planet types still reads them from the file, and only a value
        // moved from its shipped figure reaches across all of them.

        [ProtoMember(93)] public float PlanetDayTemperature = 294.261f;
        [ProtoMember(94)] public float PlanetNightTemperature = 283.15f;
        [ProtoMember(95)] public float PlanetPoleTemperatureDrop = 40f;
        [ProtoMember(96)] public float PlanetAmbientLapseRate = 4f;
        [ProtoMember(97)] public float PlanetAmbientLagSeconds = 45f;
        [ProtoMember(98)] public float PlanetConvectionCoefficient = 50f;

        /// <summary>Heat transfer coefficient for a grid buried in rock, W/(m^2 K).</summary>
        [ProtoMember(120)] public float PlanetUndergroundConvectionCoefficient = 2f;
        [ProtoMember(99)] public float PlanetSolarDecay = 0.5f;
        [ProtoMember(100)] public float PlanetUndergroundTemperature = 280f;
        [ProtoMember(101)] public float PlanetUndergroundDampingDepth = 20f;
        [ProtoMember(102)] public float PlanetCoreTemperature = 3000f;
        [ProtoMember(103)] public float PlanetSealevelDeadzone = 2000f;

        // ---- wind --------------------------------------------------------------------------

        /// <summary>
        /// Roughness length z0, m: about a tenth of the height of whatever covers the ground. 0.0002
        /// open water, 0.03 grassland, 0.1 scattered obstacles, 0.5 forest. Sets how fast wind
        /// strengthens with height near the surface.
        /// </summary>
        [ProtoMember(104)] public float WindRoughnessLength = 0.03f;

        /// <summary>
        /// Height at which wind stops strengthening, m — the top of the boundary layer. Above it the
        /// wind is set by the pressure field rather than by the ground.
        /// </summary>
        [ProtoMember(105)] public float WindGradientHeight = 600f;

        /// <summary>
        /// How far the daily cycle swings wind either side of its mean, 0..1. Surface wind peaks in
        /// the afternoon; wind above the crossover height peaks before dawn instead.
        /// </summary>
        [ProtoMember(106)] public float WindDiurnalAmplitude = 0.35f;

        /// <summary>
        /// Height at which the daily cycle vanishes, m. Below it the surface cycle, above it the
        /// nocturnal jet, fully reversed by twice this height.
        /// </summary>
        [ProtoMember(107)] public float WindDiurnalCrossover = 80f;

        /// <summary>
        /// How much the shape of the ground affects wind, 0..1: speed-up over rises, shelter behind
        /// ridges, and steering along valleys. 0 leaves the wind ignorant of terrain.
        /// </summary>
        [ProtoMember(108)] public float WindTerrainInfluence = 1f;

        /// <summary>
        /// How far out the terrain around a point is read when deciding all three, m. The scale of
        /// landform the wind is allowed to notice.
        /// </summary>
        [ProtoMember(109)] public float WindTerrainRadius = 300f;

        /// <summary>
        /// How much slope wind blows, 0..1: air running up a mountain by day and draining back down
        /// it at night. A thermal effect rather than a mechanical one — it blows on a still day, and
        /// a real wind overruns it. Costs nothing extra: the terrain it needs is already read.
        /// </summary>
        [ProtoMember(111)] public float WindSlopeStrength = 1f;

        // ---- heat pumps --------------------------------------------------------------------

        /// <summary>How much of the Carnot limit a heat pump achieves, 0..1.</summary>
        [ProtoMember(47)] public float HeatPumpCarnotFraction = 0.4f;

        /// <summary>Ceiling on a heat pump's coefficient of performance.</summary>
        [ProtoMember(48)] public float HeatPumpMaxCoefficient = 8f;

        // ---- the suit ----------------------------------------------------------------------

        /// <summary>How well a room reaches the occupant through a sealed suit, W/K.</summary>
        [ProtoMember(115)] public float SuitConductance = 2.5f;

        /// <summary>Heat capacity of occupant and suit together, J/K, before the clock divides it.</summary>
        [ProtoMember(116)] public float SuitHeatCapacity = 240000f;

        /// <summary>Heat the suit can move, either way, W.</summary>
        [ProtoMember(117)] public float SuitCoolingWatts = 500f;

        /// <summary>Interior temperature above which the occupant is hurt, K.</summary>
        [ProtoMember(118)] public float SuitCriticalTemperature = 315.15f;

        /// <summary>Hit points a second per kelvin above the suit's critical temperature.</summary>
        [ProtoMember(119)] public float SuitDamagePerKelvin = 1f;

        // ---- presentation ------------------------------------------------------------------

        /// <summary>Crosshair readout for the block being looked at. Client side.</summary>
        [ProtoMember(50)] public bool DebugTextOnScreen = false;

        /// <summary>Draws the sun ray from each grid, white when lit and red when occluded.</summary>
        [ProtoMember(51)] public bool DebugSolarRaycast = false;

        /// <summary>Draws the relative wind vector.</summary>
        [ProtoMember(52)] public bool DebugWindRaycast = false;

        /// <summary>
        /// Value the block overlay starts a session showing, as a
        /// <see cref="ThermalDebugView.Mode"/>: 0 off, 1 temperature, 2 solar watts, 3 exposed
        /// faces, 4 friction watts, 5 rooms. Ctrl+Shift+= cycles it in play. Client side.
        /// </summary>
        [ProtoMember(57)] public int DebugBlockOverlay = 0;

        /// <summary>
        /// Boxes the block overlay may draw in one frame. Each is eighteen billboards, so a capital
        /// ship drawn whole is over half a million a frame and the client stops rendering at rate.
        /// Beyond the budget the overlay draws the part of the grid nearest the camera; the radius
        /// it uses is fitted per frame. Client side.
        /// </summary>
        [ProtoMember(114)] public int DebugOverlayMaxBoxes = 12000;

        /// <summary>
        /// Value the wind map starts a session showing, as a <see cref="WindOverlay.Mode"/>: 0 off,
        /// 1 the lattice around the player, 2 the whole globe. Ctrl+Shift+W cycles it in play.
        /// Client side.
        /// </summary>
        [ProtoMember(112)] public int DebugWindOverlay = 0;

        /// <summary>
        /// The wind needle and speed under the crosshair. Client side, and the one entry here on by
        /// default: it is a readout for playing rather than a diagnostic.
        /// See configuration.md, The wind indicator.
        /// </summary>
        [ProtoMember(113)] public bool DebugWindIndicator = true;

        /// <summary>
        /// Blocks glow as they heat, from the Draper point up. Client side, and on by default: it
        /// is what a hot block looks like rather than a diagnostic, and it is the mod's answer to
        /// *a player should learn their ship is overheating without looking at an instrument*.
        /// See document-of-intent.md, Natural feedback.
        /// </summary>
        [ProtoMember(121)] public bool HeatGlow = true;

        /// <summary>
        /// A cue in the cockpit as a block comes up on its rating and as it passes it. Client side,
        /// heard only by the player at the controls, and on by default for the same reason.
        /// </summary>
        [ProtoMember(122)] public bool HeatWarningSound = true;

        /// <summary>
        /// Bottom of the room overlay's colour span, K. Separate from the block ramp because room
        /// air spans a few tens of degrees, over which the block ramp gives one shade.
        /// </summary>
        [ProtoMember(78)] public float RoomOverlayMinKelvin = 253.15f;   // -20 C

        /// <summary>Top of the room overlay's colour span, K.</summary>
        [ProtoMember(79)] public float RoomOverlayMaxKelvin = 323.15f;   //  50 C

        // Retired ProtoMember numbers, left unused so an older config file or peer message cannot
        // land on a different field: 72 and 76 were the switches SolarGridShadows replaced, 53-56
        // the block-colouring debug modes, and 60-70 the thermal vision overlay.

        // ---- multiplayer -------------------------------------------------------------------

        /// <summary>
        /// Whether the server states block temperatures to its clients.
        ///
        /// Off leaves every client re-simulating from its own inputs, which is what the mod did
        /// before this existed and is measurably wrong about which side of critical a block is on
        /// for longer than the damage event lasts. It is a switch because the correction costs
        /// bandwidth and a server operator is the one who pays it.
        /// See configuration.md, Replicating temperatures.
        /// </summary>
        [ProtoMember(123)] public bool EnableTemperatureSync = true;

        /// <summary>
        /// Seconds between band updates, once a client has been told the whole hull.
        /// See <see cref="Core.HotTailSchedule.DefaultIntervalSeconds"/> for why five.
        /// </summary>
        [ProtoMember(124)] public float TemperatureSyncInterval = 5f;

        // ---- telemetry ---------------------------------------------------------------------

        [ProtoMember(80)] public bool EnableTelemetry = false;
        [ProtoMember(81)] public int TelemetrySampleStride = 4;

        /// <summary>
        /// Solver steps between planet-wide wind and climate sweeps, or 0 for none. Needs
        /// <see cref="EnableTelemetry"/>; 360 is a sweep a minute at the shipped clock.
        /// See environment.md, Measuring it.
        /// </summary>
        [ProtoMember(110)] public int TelemetryPlanetProbes = 0;

        /// <summary>
        /// A fresh configuration. Defaults live on the field initialisers, not here: a config written
        /// before a setting existed carries no element for it, and a reader that finds nothing must
        /// leave the field alone. See known-issues.md, A guard has to test what it claims to test.
        /// </summary>
        public static Settings GetDefaults()
        {
            Settings s = new Settings();
            s.Version = CurrentVersion;
            s.Clamp();
            return s;
        }

        /// <summary>
        /// Carries a world saved with the old fluid-coupling dial onto the new one.
        ///
        /// <para>
        /// `LoopConductivity` was a 0…1 quality that the loop multiplied by 200 W/(m·K) and divided
        /// by half a cell; on a large grid that came to 160 W/(m²·K), which is what
        /// <see cref="LoopHeatTransferCoefficient"/> now holds directly
        /// ([backlog.md](../../docs/backlog.md) `C20`). A world that moved the old dial gets the
        /// same *relative* change on the new one, and the legacy field is spent so a later load
        /// does not apply it twice.
        /// </para>
        ///
        /// <para>
        /// A world saved after the change carries −1 there and is left alone, which is also what a
        /// new world has.
        /// </para>
        /// </summary>
        private void MigrateLoopCoupling()
        {
            if (LegacyLoopConductivity < 0f) return;

            LoopHeatTransferCoefficient = LegacyLoopConductivity * ShippedLoopCoefficient;
            LegacyLoopConductivity = -1f;
        }

        /// <summary>What the old quality of one came to on a large grid, W/(m²·K).</summary>
        private const float ShippedLoopCoefficient = 160f;

        private void Clamp()
        {
            MigrateLoopCoupling();

            if (Frequency < 1) Frequency = 1;

            // A zero or negative interval would stop the band without stopping the join packet,
            // which is a half-working protocol that reports as working. EnableTemperatureSync is
            // the way to turn it off.
            if (TemperatureSyncInterval < 0.5f) TemperatureSyncInterval = 0.5f;
            if (SimulationSpeed <= 0f) SimulationSpeed = 1f;
            if (HeatTimeScale <= 0f) HeatTimeScale = 1f;
            if (MaxElementVisitsPerStep < 0) MaxElementVisitsPerStep = 0;
            if (MaxSubsteps < 1) MaxSubsteps = 1;
            if (MaxSubstepsPerBlock < 0) MaxSubstepsPerBlock = 0;
            if (TelemetrySampleStride < 1) TelemetrySampleStride = 1;
            if (SolarOcclusionInterval < 1) SolarOcclusionInterval = 1;
            if (SolarTerrainRange < 0f) SolarTerrainRange = 0f;
            if (ClimateGroundInfluence < 0f) ClimateGroundInfluence = 0f;
            if (ClimateGroundInfluence > 1f) ClimateGroundInfluence = 1f;
            if (ClimateWeatherInfluence < 0f) ClimateWeatherInfluence = 0f;
            if (ClimateWeatherInfluence > 1f) ClimateWeatherInfluence = 1f;
            if (SolarGridShadows < 0) SolarGridShadows = 0;
            if (SolarGridShadows > (int)GridShadowMode.Full) SolarGridShadows = (int)GridShadowMode.Full;
            if (SolarOcclusionSamples < 1) SolarOcclusionSamples = 1;
            if (SolarOcclusionSamples > Core.SolarOcclusionSampler.MaxSamples)
                SolarOcclusionSamples = Core.SolarOcclusionSampler.MaxSamples;
            if (RoomOverlayMaxKelvin <= RoomOverlayMinKelvin)
                RoomOverlayMaxKelvin = RoomOverlayMinKelvin + 1f;
            if (DebugOverlayMaxBoxes < 0) DebugOverlayMaxBoxes = 0;
            if (DebugBlockOverlay < 0) DebugBlockOverlay = 0;
            if (DebugBlockOverlay >= ThermalDebugView.ModeCount)
                DebugBlockOverlay = ThermalDebugView.ModeCount - 1;
            if (DebugWindOverlay < 0) DebugWindOverlay = 0;
            if (DebugWindOverlay >= WindOverlay.ModeCount)
                DebugWindOverlay = WindOverlay.ModeCount - 1;
            if (RoomConvectionCoefficient < 0f) RoomConvectionCoefficient = 0f;
            if (RoomAirDensity < 0f) RoomAirDensity = 0f;
            if (HeatPumpCarnotFraction < 0f) HeatPumpCarnotFraction = 0f;
            if (HeatPumpCarnotFraction > 1f) HeatPumpCarnotFraction = 1f;
            if (HeatPumpMaxCoefficient < 0f) HeatPumpMaxCoefficient = 0f;
            if (SuitConductance < 0f) SuitConductance = 0f;
            if (SuitHeatCapacity <= 0f) SuitHeatCapacity = 1f;
            if (SuitCoolingWatts < 0f) SuitCoolingWatts = 0f;
            if (SuitCriticalTemperature < 0f) SuitCriticalTemperature = 0f;
            if (SuitDamagePerKelvin < 0f) SuitDamagePerKelvin = 0f;
            if (WindRoughnessLength <= 0f) WindRoughnessLength = 0.0002f;
            if (WindGradientHeight < Core.WindProfile.ReferenceHeight)
                WindGradientHeight = Core.WindProfile.ReferenceHeight;
            if (WindDiurnalAmplitude < 0f) WindDiurnalAmplitude = 0f;
            if (WindDiurnalAmplitude > 1f) WindDiurnalAmplitude = 1f;
            if (WindDiurnalCrossover < 0f) WindDiurnalCrossover = 0f;
            if (WindTerrainInfluence < 0f) WindTerrainInfluence = 0f;
            if (WindTerrainInfluence > 1f) WindTerrainInfluence = 1f;
            if (WindTerrainRadius < 0f) WindTerrainRadius = 0f;
            if (WindSlopeStrength < 0f) WindSlopeStrength = 0f;
            if (WindSlopeStrength > 1f) WindSlopeStrength = 1f;
            if (TelemetryPlanetProbes < 0) TelemetryPlanetProbes = 0;
        }

        // ---- conversion --------------------------------------------------------------------

        [XmlIgnore]
        private Core.ThermalSettings core;

        /// <summary>
        /// The same configuration in the form the simulation consumes.
        ///
        /// Built once and thereafter written through, never replaced: every grid holds a reference
        /// to this instance, so replacing it would leave existing grids on the old values.
        /// </summary>
        public Core.ThermalSettings ToCore()
        {
            if (core == null) core = new Core.ThermalSettings();
            Apply();
            return core;
        }

        /// <summary>
        /// Returns every world setting to the value a fresh install ships, in one action.
        ///
        /// The four presentation switches are left alone: what is drawn on a player's own screen is
        /// theirs, not the world's. This is the menu's only bulk action, and the reason it needs no
        /// Reset button beside every control.
        /// </summary>
        public void RestoreDefaults()
        {
            Settings shipped = GetDefaults();
            List<string> names = Names();

            for (int i = 0; i < names.Count; i++)
            {
                string setting = names[i];
                if (ClientOwned.Contains(setting)) continue;

                SetValue(setting, shipped.GetValue(setting));
            }

            Apply();
        }

        /// <summary>
        /// The world's own loop and planet values, snapshotted so <see cref="Apply"/> can tell when one
        /// has actually moved.
        ///
        /// Those nineteen are laid over the definitions when a block model is cached, so a change to
        /// one reaches a running world only by dropping that cache and rebuilding every grid — which
        /// is as expensive as a world load and must not run on every slider tick.
        /// </summary>
        private string definitionState;

        /// <summary>
        /// The loop and planet values as one string, for comparison. Their exact figures do not
        /// matter here; only whether any of them changed since the last <see cref="Apply"/>.
        /// </summary>
        private string DefinitionState()
        {
            StringBuilder state = new StringBuilder();
            List<string> names = Names();

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!name.StartsWith("Loop", StringComparison.Ordinal)
                    && !name.StartsWith("Planet", StringComparison.Ordinal)) continue;

                state.Append(name).Append('=')
                     .Append(GetValue(name).ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }

            return state.ToString();
        }

        /// <summary>
        /// Rebuilds every live grid's view of the definitions, after a loop or planet value moved.
        /// As expensive as a world load per grid, which is why <see cref="Apply"/> reaches it only
        /// when <see cref="DefinitionState"/> says something changed.
        /// </summary>
        private static void RebuildGrids()
        {
            try
            {
                ThermalBlockCatalog.Clear();

                IList<ThermalGrid> grids = ThermalGrid.LiveGrids;
                for (int i = 0; grids != null && i < grids.Count; i++)
                {
                    if (grids[i] != null) grids[i].RefreshDefinitions();
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Name + "] failed to rebuild grids after a definition change\n" + e);
            }
        }

        /// <summary>
        /// Pushes the current values into the simulation's settings and derives them, so every grid
        /// picks the change up on its next step.
        /// </summary>
        public void Apply()
        {
            Clamp();

            // Every path that changes a setting ends here — the chat commands, the settings menu,
            // the mod API and a profile change — so this is the one place a server has to publish
            // from, and the one place that knows the file is now behind. A client applying a value
            // it just received is guarded inside the sync and never marks the file dirty.
            SettingsSync.Publish(this);
            if (this == Instance && !SettingsSync.Applying) SavePending = true;
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
            core.WellMixedCoolant = WellMixedCoolant;
            core.EnableRoomAir = EnableRoomAir;
            core.EnableHeatPumps = EnableHeatPumps;

            core.ClampConductionOvershoot = ClampConductionOvershoot;
            core.ClampEnvironmentOvershoot = ClampEnvironmentOvershoot;
            core.DamageIsPerSecond = DamageIsPerSecond;
            core.Frequency = Frequency;
            core.SimulationSpeed = SimulationSpeed;
            core.HeatTimeScale = HeatTimeScale;
            core.MaxElementVisitsPerStep = MaxElementVisitsPerStep;
            core.MaxSubsteps = MaxSubsteps;
            core.MaxSubstepsPerBlock = MaxSubstepsPerBlock;

            core.VacuumTemperature = VacuumTemperature;
            core.SolarEnergy = SolarEnergy;
            core.FrictionAtSpeedsAbove = FrictionAtSpeedsAbove;
            core.FrictionScale = FrictionScale;
            core.RoomConvectionCoefficient = RoomConvectionCoefficient;
            core.RoomAirDensity = RoomAirDensity;
            core.HeatPumpCarnotFraction = HeatPumpCarnotFraction;
            core.HeatPumpMaxCoefficient = HeatPumpMaxCoefficient;
            core.EnableSuitDamage = EnableSuitDamage;
            core.SuitConductance = SuitConductance;
            core.SuitHeatCapacity = SuitHeatCapacity;
            core.SuitCoolingWatts = SuitCoolingWatts;
            core.SuitCriticalTemperature = SuitCriticalTemperature;
            core.SuitDamagePerKelvin = SuitDamagePerKelvin;

            core.Derive();

            // After the derive, because two of the problems are about derived quantities, and after
            // Clamp above for the same reason the block check runs before its clamp: what is worth
            // telling an administrator is what the clamp could not fix. See ThermalValidation.
            Core.ThermalValidation.Check(core);

            Telemetry.SampleStride = TelemetrySampleStride;

            // The loop and planet values are laid over the definitions at cache time, so moving one
            // reaches nothing already built until the cache is dropped. Only on a real change: this
            // runs on every set, every slider tick and every settings sync.
            string state = DefinitionState();
            bool moved = definitionState != null && definitionState != state;
            definitionState = state;
            if (moved) RebuildGrids();
        }

        /// <summary>Solver steps per real second. Used by readouts that report rates.</summary>
        [XmlIgnore]
        public float StepsPerSecond
        {
            get { return core == null ? Frequency * SimulationSpeed : core.StepsPerSecond; }
        }

        // ---- access by name ----------------------------------------------------------------

        /// <summary>
        /// The settings a client owns for itself; everything else in <see cref="Names"/> is world
        /// state. One list, because the menu and the replication have to agree on the answer.
        /// </summary>
        public static readonly HashSet<string> ClientOwned = new HashSet<string>
        {
            "DebugTextOnScreen", "DebugSolarRaycast", "DebugWindRaycast", "DebugBlockOverlay",
            "DebugWindOverlay", "DebugWindIndicator", "DebugOverlayMaxBoxes",
            "HeatGlow", "HeatWarningSound",
        };

        /// <summary>
        /// Every setting a player or mod may change at runtime, in display order. Booleans are 0
        /// and 1.
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
                "ClampConductionOvershoot", "ClampEnvironmentOvershoot", "DamageIsPerSecond",
                "Frequency", "SimulationSpeed", "HeatTimeScale", "MaxElementVisitsPerStep",
                "MaxSubsteps", "MaxSubstepsPerBlock",
                "VacuumTemperature", "SolarEnergy", "FrictionAtSpeedsAbove", "FrictionScale",
                "RoomConvectionCoefficient", "RoomAirDensity", "SolarOcclusionInterval",
                "ClimateGroundInfluence", "ClimateWeatherInfluence",
                "HeatPumpCarnotFraction", "HeatPumpMaxCoefficient",
                "EnableSuitDamage", "SuitConductance", "SuitHeatCapacity", "SuitCoolingWatts",
                "SuitCriticalTemperature", "SuitDamagePerKelvin",
                "WindRoughnessLength", "WindGradientHeight",
                "WindDiurnalAmplitude", "WindDiurnalCrossover",
                "WindTerrainInfluence", "WindTerrainRadius", "WindSlopeStrength",
                "DebugTextOnScreen", "DebugSolarRaycast", "DebugWindRaycast",
                "DebugBlockOverlay", "DebugWindOverlay", "DebugWindIndicator",
                "DebugOverlayMaxBoxes",
                "HeatGlow", "HeatWarningSound",
                "RoomOverlayMinKelvin", "RoomOverlayMaxKelvin",
                "EnableTemperatureSync", "TemperatureSyncInterval",
                "EnableTelemetry", "TelemetrySampleStride", "TelemetryPlanetProbes",

                "LoopLargeGridFlowRate", "LoopSmallGridFlowRate", "LoopCoolantMassPerPipe",
                "LoopSpecificHeat", "LoopHeatTransferCoefficient", "LoopPipeContactMultiplier",
                "LoopSinkContactMultiplier", "LoopStagnantTransferFraction",

                "PlanetDayTemperature", "PlanetNightTemperature", "PlanetPoleTemperatureDrop",
                "PlanetAmbientLapseRate", "PlanetAmbientLagSeconds", "PlanetConvectionCoefficient",
                "PlanetUndergroundConvectionCoefficient",
                "PlanetSolarDecay", "PlanetUndergroundTemperature",
                "PlanetUndergroundDampingDepth", "PlanetCoreTemperature",
                "PlanetSealevelDeadzone",
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
                case "ClampEnvironmentOvershoot": return Flag(ClampEnvironmentOvershoot);
                case "DamageIsPerSecond": return Flag(DamageIsPerSecond);
                case "Frequency": return Frequency;
                case "SimulationSpeed": return SimulationSpeed;
                case "HeatTimeScale": return HeatTimeScale;
                case "MaxElementVisitsPerStep": return MaxElementVisitsPerStep;
                case "MaxSubsteps": return MaxSubsteps;
                case "MaxSubstepsPerBlock": return MaxSubstepsPerBlock;
                case "VacuumTemperature": return VacuumTemperature;
                case "SolarEnergy": return SolarEnergy;
                case "FrictionAtSpeedsAbove": return FrictionAtSpeedsAbove;
                case "FrictionScale": return FrictionScale;
                case "RoomConvectionCoefficient": return RoomConvectionCoefficient;
                case "RoomAirDensity": return RoomAirDensity;
                case "HeatPumpCarnotFraction": return HeatPumpCarnotFraction;
                case "HeatPumpMaxCoefficient": return HeatPumpMaxCoefficient;
                case "EnableSuitDamage": return EnableSuitDamage ? 1f : 0f;
                case "SuitConductance": return SuitConductance;
                case "SuitHeatCapacity": return SuitHeatCapacity;
                case "SuitCoolingWatts": return SuitCoolingWatts;
                case "SuitCriticalTemperature": return SuitCriticalTemperature;
                case "SuitDamagePerKelvin": return SuitDamagePerKelvin;
                case "WindRoughnessLength": return WindRoughnessLength;
                case "WindGradientHeight": return WindGradientHeight;
                case "WindDiurnalAmplitude": return WindDiurnalAmplitude;
                case "WindDiurnalCrossover": return WindDiurnalCrossover;
                case "WindTerrainInfluence": return WindTerrainInfluence;
                case "WindTerrainRadius": return WindTerrainRadius;
                case "WindSlopeStrength": return WindSlopeStrength;
                case "SolarOcclusionInterval": return SolarOcclusionInterval;
                case "ClimateGroundInfluence": return ClimateGroundInfluence;
                case "ClimateWeatherInfluence": return ClimateWeatherInfluence;
                case "DebugTextOnScreen": return Flag(DebugTextOnScreen);
                case "DebugSolarRaycast": return Flag(DebugSolarRaycast);
                case "DebugWindRaycast": return Flag(DebugWindRaycast);
                case "DebugBlockOverlay": return DebugBlockOverlay;
                case "DebugOverlayMaxBoxes": return DebugOverlayMaxBoxes;
                case "DebugWindOverlay": return DebugWindOverlay;
                case "DebugWindIndicator": return Flag(DebugWindIndicator);
                case "HeatGlow": return Flag(HeatGlow);
                case "HeatWarningSound": return Flag(HeatWarningSound);
                case "RoomOverlayMinKelvin": return RoomOverlayMinKelvin;
                case "RoomOverlayMaxKelvin": return RoomOverlayMaxKelvin;
                case "EnableTemperatureSync": return Flag(EnableTemperatureSync);
                case "TemperatureSyncInterval": return TemperatureSyncInterval;
                case "EnableTelemetry": return Flag(EnableTelemetry);
                case "TelemetrySampleStride": return TelemetrySampleStride;
                case "TelemetryPlanetProbes": return TelemetryPlanetProbes;

                case "LoopCoolantMassPerPipe": return LoopCoolantMassPerPipe;
                case "LoopHeatTransferCoefficient": return LoopHeatTransferCoefficient;
                case "LoopSpecificHeat": return LoopSpecificHeat;
                case "LoopPipeContactMultiplier": return LoopPipeContactMultiplier;
                case "LoopSinkContactMultiplier": return LoopSinkContactMultiplier;
                case "LoopLargeGridFlowRate": return LoopLargeGridFlowRate;
                case "LoopSmallGridFlowRate": return LoopSmallGridFlowRate;
                case "LoopStagnantTransferFraction": return LoopStagnantTransferFraction;

                case "PlanetDayTemperature": return PlanetDayTemperature;
                case "PlanetNightTemperature": return PlanetNightTemperature;
                case "PlanetPoleTemperatureDrop": return PlanetPoleTemperatureDrop;
                case "PlanetAmbientLapseRate": return PlanetAmbientLapseRate;
                case "PlanetAmbientLagSeconds": return PlanetAmbientLagSeconds;
                case "PlanetConvectionCoefficient": return PlanetConvectionCoefficient;
                case "PlanetUndergroundConvectionCoefficient": return PlanetUndergroundConvectionCoefficient;
                case "PlanetSolarDecay": return PlanetSolarDecay;
                case "PlanetUndergroundTemperature": return PlanetUndergroundTemperature;
                case "PlanetUndergroundDampingDepth": return PlanetUndergroundDampingDepth;
                case "PlanetCoreTemperature": return PlanetCoreTemperature;
                case "PlanetSealevelDeadzone": return PlanetSealevelDeadzone;
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
                case "ClampEnvironmentOvershoot": ClampEnvironmentOvershoot = Flag(value); return true;
                case "DamageIsPerSecond": DamageIsPerSecond = Flag(value); return true;
                case "Frequency": Frequency = (int)value; return true;
                case "SimulationSpeed": SimulationSpeed = value; return true;
                case "HeatTimeScale": HeatTimeScale = value; return true;
                case "MaxElementVisitsPerStep": MaxElementVisitsPerStep = (int)value; return true;
                case "MaxSubsteps": MaxSubsteps = (int)value; return true;
                case "MaxSubstepsPerBlock": MaxSubstepsPerBlock = (int)value; return true;
                case "VacuumTemperature": VacuumTemperature = value; return true;
                case "SolarEnergy": SolarEnergy = value; return true;
                case "FrictionAtSpeedsAbove": FrictionAtSpeedsAbove = value; return true;
                case "FrictionScale": FrictionScale = value; return true;
                case "RoomConvectionCoefficient": RoomConvectionCoefficient = value; return true;
                case "RoomAirDensity": RoomAirDensity = value; return true;
                case "HeatPumpCarnotFraction": HeatPumpCarnotFraction = value; return true;
                case "HeatPumpMaxCoefficient": HeatPumpMaxCoefficient = value; return true;
                case "EnableSuitDamage": EnableSuitDamage = value != 0f; return true;
                case "SuitConductance": SuitConductance = value; return true;
                case "SuitHeatCapacity": SuitHeatCapacity = value; return true;
                case "SuitCoolingWatts": SuitCoolingWatts = value; return true;
                case "SuitCriticalTemperature": SuitCriticalTemperature = value; return true;
                case "SuitDamagePerKelvin": SuitDamagePerKelvin = value; return true;
                case "WindRoughnessLength": WindRoughnessLength = value; return true;
                case "WindGradientHeight": WindGradientHeight = value; return true;
                case "WindDiurnalAmplitude": WindDiurnalAmplitude = value; return true;
                case "WindDiurnalCrossover": WindDiurnalCrossover = value; return true;
                case "WindTerrainInfluence": WindTerrainInfluence = value; return true;
                case "WindTerrainRadius": WindTerrainRadius = value; return true;
                case "WindSlopeStrength": WindSlopeStrength = value; return true;
                case "SolarOcclusionInterval": SolarOcclusionInterval = (int)value; return true;
                case "ClimateGroundInfluence": ClimateGroundInfluence = value; return true;
                case "ClimateWeatherInfluence": ClimateWeatherInfluence = value; return true;
                case "DebugTextOnScreen": DebugTextOnScreen = Flag(value); return true;
                case "DebugSolarRaycast": DebugSolarRaycast = Flag(value); return true;
                case "DebugWindRaycast": DebugWindRaycast = Flag(value); return true;
                case "RoomOverlayMinKelvin": RoomOverlayMinKelvin = value; return true;
                case "RoomOverlayMaxKelvin": RoomOverlayMaxKelvin = value; return true;
                case "DebugBlockOverlay":
                    DebugBlockOverlay = (int)value;
                    ThermalDebugView.Set((ThermalDebugView.Mode)DebugBlockOverlay);
                    return true;
                case "DebugWindOverlay":
                    DebugWindOverlay = (int)value;
                    WindOverlay.Set((WindOverlay.Mode)DebugWindOverlay);
                    return true;
                case "DebugOverlayMaxBoxes": DebugOverlayMaxBoxes = (int)value; return true;
                case "DebugWindIndicator": DebugWindIndicator = Flag(value); return true;
                case "HeatGlow": HeatGlow = Flag(value); return true;
                case "HeatWarningSound": HeatWarningSound = Flag(value); return true;
                case "EnableTemperatureSync": EnableTemperatureSync = Flag(value); return true;
                case "TemperatureSyncInterval": TemperatureSyncInterval = value; return true;
                case "EnableTelemetry": EnableTelemetry = Flag(value); Telemetry.SetEnabled(EnableTelemetry); return true;
                case "TelemetrySampleStride": TelemetrySampleStride = (int)value; return true;
                case "TelemetryPlanetProbes": TelemetryPlanetProbes = (int)value; return true;

                case "LoopCoolantMassPerPipe": LoopCoolantMassPerPipe = value; return true;
                case "LoopHeatTransferCoefficient": LoopHeatTransferCoefficient = value; return true;
                case "LoopSpecificHeat": LoopSpecificHeat = value; return true;
                case "LoopPipeContactMultiplier": LoopPipeContactMultiplier = value; return true;
                case "LoopSinkContactMultiplier": LoopSinkContactMultiplier = value; return true;
                case "LoopLargeGridFlowRate": LoopLargeGridFlowRate = value; return true;
                case "LoopSmallGridFlowRate": LoopSmallGridFlowRate = value; return true;
                case "LoopStagnantTransferFraction": LoopStagnantTransferFraction = value; return true;

                case "PlanetDayTemperature": PlanetDayTemperature = value; return true;
                case "PlanetNightTemperature": PlanetNightTemperature = value; return true;
                case "PlanetPoleTemperatureDrop": PlanetPoleTemperatureDrop = value; return true;
                case "PlanetAmbientLapseRate": PlanetAmbientLapseRate = value; return true;
                case "PlanetAmbientLagSeconds": PlanetAmbientLagSeconds = value; return true;
                case "PlanetConvectionCoefficient": PlanetConvectionCoefficient = value; return true;
                case "PlanetUndergroundConvectionCoefficient": PlanetUndergroundConvectionCoefficient = value; return true;
                case "PlanetSolarDecay": PlanetSolarDecay = value; return true;
                case "PlanetUndergroundTemperature": PlanetUndergroundTemperature = value; return true;
                case "PlanetUndergroundDampingDepth": PlanetUndergroundDampingDepth = value; return true;
                case "PlanetCoreTemperature": PlanetCoreTemperature = value; return true;
                case "PlanetSealevelDeadzone": PlanetSealevelDeadzone = value; return true;
                default: return false;
            }
        }

        /// <summary>True when the named setting is a switch rather than a number.</summary>
        public static bool IsFlag(string name)
        {
            return name != null && name != "DebugBlockOverlay" && name != "DebugWindOverlay"
                && name != "DebugOverlayMaxBoxes"
                && (name.StartsWith("Enable") || name.StartsWith("Debug")
                || name == "SolarSelfShadowing"
                || name == "SolarOcclusionPlanets"
                || name == "SolarOcclusionTerrain"
                || name == "SolarOcclusionVoxels"
                || name == "SolarOcclusionTerrain"
                || name == "ClampConductionOvershoot" || name == "ClampEnvironmentOvershoot"
                || name == "DamageIsPerSecond");
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
        /// A mod cannot control load order — a grid's game logic can initialise before the session
        /// component — so the first caller triggers the read and the config cannot be bypassed by a
        /// world whose grids load early.
        /// </summary>
        public static Settings EnsureLoaded()
        {
            if (Instance != null) return Instance;

            Instance = CanReadWorldStorage() ? Load() : GetDefaults();
            Instance.Apply();
            return Instance;
        }

        /// <summary>
        /// Whether the config file can be read yet. False on clients, which take the server's
        /// settings rather than their own file, and early in a session before the file utilities
        /// exist.
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

                    // Renamed when the step budget stopped counting links alone. The old element
                    // deserialises into nothing, and that is intended — the two settings are in
                    // different units — but a world that had tuned it deserves to be told rather
                    // than left wondering why its value stopped applying.
                    if (text.IndexOf("MaxLinkVisitsPerStep", StringComparison.Ordinal) >= 0)
                    {
                        MyLog.Default.Info("[" + Name + "] MaxLinkVisitsPerStep is now"
                            + " MaxElementVisitsPerStep and counts nodes as well as links;"
                            + " the old value was not carried over. Default "
                            + settings.MaxElementVisitsPerStep + " is in force.");
                    }

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

        /// <summary>
        /// True when a setting has changed since the file was last written. Every change saves itself;
        /// <see cref="FlushPending"/> defers the write a moment so dragging a slider is one write.
        /// </summary>
        public static bool SavePending;

        /// <summary>
        /// Writes the config file when a change is waiting and the world can be written to.
        /// Called once a second from the session; cheap when nothing has changed.
        /// </summary>
        public static void FlushPending()
        {
            if (!SavePending || Instance == null) return;

            try
            {
                if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

                SavePending = false;
                Save(Instance);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Name + "] deferred save failed\n" + e);
            }
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
