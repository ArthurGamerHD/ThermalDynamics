using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// What the settings menu holds: the pages, the settings on each of them, and what each control
    /// writes. Generated from <see cref="Settings.Names"/>, so a setting added to the config appears
    /// without a menu edit. No Save button: every change saves itself. One Defaults button, on its
    /// own page, for starting over. Every figure the menu reports is on the Statistics page, which
    /// is the one page that holds sentences.
    ///
    /// <para>
    /// **The menu is drawn by <see cref="ThermalSettingsWindow"/> rather than by the Rich HUD
    /// terminal**, which put a fixed bordered panel around every control and offered the client no
    /// way to turn it off. This file decides what the menu says; that one decides what it looks
    /// like.
    /// </para>
    /// See configuration.md, The settings menu.
    /// </summary>
    public static class ThermalSettingsMenu
    {
        /// <summary>How a single setting is presented.</summary>
        internal struct Entry
        {
            public string Category;
            public string Label;
            public string Tip;
            public float Min;
            public float Max;
            public bool Integer;

            public Entry(string category, string label, string tip, float min, float max, bool integer = false)
            {
                Category = category;
                Label = label;
                Tip = tip;
                Min = min;
                Max = max;
                Integer = integer;
            }
        }

        private const string Transfer = "Heat transfer";
        private const string Solar = "Solar";
        private const string Occlusion = "Solar occlusion";
        private const string Systems = "Ship systems";
        private const string Solver = "Solver";
        private const string Environment = "Environment";
        private const string Aero = "Aerodynamics";
        private const string Suit = "Suit";
        private const string Display = "Display";
        private const string Multiplayer = "Multiplayer";
        private const string Other = "Other";

        /// <summary>
        /// Label, tooltip and slider range per setting. Switches ignore the range. The range bounds
        /// the slider only: the chat command and the mod API accept any value the clamp allows.
        /// </summary>
        private static readonly Dictionary<string, Entry> Layout = new Dictionary<string, Entry>
        {
            { "EnableEnvironment", new Entry(Transfer, "Ambient exchange", "Ambient exchange with air, ground and space; off leaves only internal heat flow.", 0, 1) },
            { "EnableConduction", new Entry(Transfer, "Conduction", "Heat flow between touching blocks.", 0, 1) },
            { "EnableRadiation", new Entry(Transfer, "Radiation", "Radiative exchange with the sky from exposed faces.", 0, 1) },
            { "EnableConvection", new Entry(Transfer, "Convection", "Exchange with atmosphere and with room air.", 0, 1) },
            { "EnableSolarHeat", new Entry(Solar, "Solar heat", "Sunlight on exposed faces, occlusion included.", 0, 1) },
            { "SolarTerrainRange", new Entry(Occlusion, "Terrain shadow range (m)", "How far along the sun ray the terrain walk looks, in metres; near ground is what shadows you.", 500f, 20000f) },
            { "SolarOcclusionSamples", new Entry(Occlusion, "Shadow samples across the grid", "Points across the grid tested for shadow; more turn a terminator crossing into a ramp, and cost their share each.", 1, 9, true) },
            { "EnableHeatSources", new Entry(Systems, "Point heat sources", "Heat from sources registered through the mod API.", 0, 1) },
            { "EnableWasteHeat", new Entry(Systems, "Waste heat", "Power producers, consumers and thrusters turning throughput into heat.", 0, 1) },
            { "EnablePlanets", new Entry(Systems, "Planets", "Per-planet ambient, air and ground temperatures.", 0, 1) },
            { "EnableFriction", new Entry(Aero, "Friction heating", "Atmospheric heating above the speed threshold: the share of the air's work on the hull that lands in the surface.", 0, 1) },
            { "EnableWind", new Entry(Environment, "Wind", "The wind field and everything that shapes it; off is no wind anywhere, though a grid still feels its own motion.", 0, 1) },
            { "EnableDamage", new Entry(Systems, "Overheat damage", "Blocks above their critical temperature take damage.", 0, 1) },
            { "EnableCoolantLoops", new Entry(Systems, "Coolant loops", "Closed pipe rings acting as one fluid mass.", 0, 1) },
            { "EnableRoomAir", new Entry(Systems, "Room air", "Sealed rooms hold an air mass that carries heat.", 0, 1) },
            { "EnableHeatPumps", new Entry(Systems, "Heat pumps", "The block that moves heat up a gradient for an electrical cost.", 0, 1) },

            // **The last eleven settings with no layout entry.** They fell through to a page
            // called *Other* that said it did not describe them, with the setting's own name as its
            // label and a 0..1000 slider whatever the setting was. A suit's heat capacity is
            // 240,000.
            // **Documented as a rung and reachable by nothing.** `configuration.md`'s ladder
            // lists coolant transport as off, well-mixed, then parcels round a ring — and the
            // middle rung was in the config file, copied into the core and read by the solver, but
            // absent from `Names()`, so no menu, no chat command and no API could set it. Only
            // hand-editing a world's XML could.
            { "WellMixedCoolant", new Entry(Systems, "Well-mixed coolant", "The cheaper transport rung: a loop's fluid as one well-mixed mass rather than parcels travelling round the ring.", 0, 1) },

            { "EnableSuitDamage", new Entry(Suit, "Suit damage", "Heat as something that can hurt a player; off leaves the suit unsimulated and costs nothing.", 0, 1) },
            { "SuitConductance", new Entry(Suit, "Suit conductance (W/K)", "How well the outside reaches the occupant through a sealed suit, W/K.", 0f, 20f) },
            { "SuitHeatCapacity", new Entry(Suit, "Suit heat capacity (J/K)", "Heat capacity of the occupant and suit together, J/K — about eighty kilograms of mostly water.", 10000f, 1000000f) },
            { "SuitCoolingWatts", new Entry(Suit, "Suit cooling (W)", "Heat the suit can move either way, W, cooling a player in a hot room and warming one in a cold one.", 0f, 5000f) },
            { "SuitCriticalTemperature", new Entry(Suit, "Hurts above (K)", "Interior temperature at which the occupant starts being hurt, K; 315.15 is 42 C.", 300f, 350f) },
            { "SuitDamagePerKelvin", new Entry(Suit, "Damage per kelvin (hp/s)", "Hit points a second, per kelvin above the temperature that hurts.", 0f, 10f) },

            { "FloorBlocksWhenOverBudget", new Entry(Solver, "Floor stiff blocks when over budget", "Over budget, floor the heat capacity of the blocks demanding most of it rather than shortening the step for everyone.", 0, 1) },
            { "ParallelGrids", new Entry(Solver, "Solve in parallel", "Solve a frame's grids across the engine's own workers: 10x on a 242-grid fleet, and nothing on one ship.", 0, 1) },

            { "ShowEnvironmentReadout", new Entry(Display, "Environment readout", "One line, bottom centre: the air temperature around your ship, and one word for the ship against its own rating.", 0, 1) },
            { "DebugOverlayMaxBoxes", new Entry(Display, "Overlay box budget per frame", "Boxes the block overlay may draw in one frame; past it it draws the part of the grid nearest the camera.", 0f, 50000f, true) },

            { "PlanetUndergroundConvectionCoefficient", new Entry(Environment, "Buried convection (W/m2 K)", "Convective coefficient for a grid buried in rock, W/(m2 K), crossed over the first five metres of burial.", 0f, 50f) },

            { "EnableTemperatureSync", new Entry(Multiplayer, "Replicate temperatures", "The server tells each client what its blocks are really at; off leaves every client guessing.", 0, 1) },
            { "TemperatureSyncInterval", new Entry(Multiplayer, "Update interval (s)", "Seconds between updates about the blocks near failing; the whole ship is stated once regardless.", 0.5f, 60f) },

            // These four had no entry at all, so they fell through to "Other — not yet described"
            // at the bottom of the page, unlabelled and untooltipped. They are the four the field
            // tuning is entirely about: what a step costs and whether it stays stable.
            { "MaxSubsteps", new Entry(Solver, "Substep ceiling per step", "Most substeps one step may divide itself into; reaching it is reported as a clamped step.", 1, 64, true) },
            { "MaxSubstepsPerBlock", new Entry(Solver, "Substep cap per block", "Most substeps one block may demand before its heat capacity is floored; 0 leaves every block alone.", 0, 32, true) },
            { "MaxElementVisitsPerStep", new Entry(Solver, "Work budget per step (visits)", "Most element visits one step may make before it is shortened to fit; 0 removes the bound.", 0, 8000000, true) },

            { "DamageIsPerSecond", new Entry(Solver, "Overheat damage is per second", "Overheat damage scaled to real time rather than to the step.", 0, 1) },
            { "Frequency", new Entry(Solver, "Solver steps per simulated second", "Solver steps per second of simulated time. Higher is finer and costlier.", 1, 60, true) },
            { "HeatTimeScale", new Entry(Solver, "Heat pace (physics seconds per second)", "Seconds of physical time per second of play: the dial that puts heat on a human scale.", 1f, 1000f) },

            // From Loops.xml, which the menu never showed. This is the flow rate people ask for.
            { "LoopRefillEquivalentKelvin", new Entry(Systems, "Refill price (K above ambient)", "What a coolant refill is priced at: the temperature at which venting and refilling exactly breaks even.", 0f, 600f) },
            { "LoopRefillKilogramsPerSecond", new Entry(Systems, "Refill rate (kg/s)", "How fast a vented coolant loop comes back, kg/s — venting is instant and refilling is not.", 0f, 200f) },
            { "LoopCoolantKilogramsPerCubicMetre", new Entry(Systems, "Coolant density (kg/m3)", "Coolant per cubic metre of the cell a pipe occupies, kg/m3; more is more capacity for the same coupling.", 0f, 200f) },
            { "LoopSpecificHeat", new Entry(Systems, "Coolant specific heat (J/kg K)", "J/(kg K). Water-glycol is about 3400, which is what the shipped fluid is.", 100f, 6000f) },
            { "LoopHeatTransferCoefficient", new Entry(Systems, "Fluid-to-wall transfer (W/m2 K)", "How well heat crosses between the fluid and the wall it touches while the pump is running, W/(m2 K).", 0f, 2000f) },
            { "LoopStagnantTransferFraction", new Entry(Systems, "Transfer with the pump stopped (0..1)", "What a stopped ring still carries across the fluid-to-wall joint, as a share of the coefficient above.", 0f, 1f) },

            // From Planets.xml, same argument.
            { "PlanetDayTemperature", new Entry(Environment, "Day temperature (K)", "Air temperature at the equator at noon, K.", 100f, 400f) },
            { "PlanetNightTemperature", new Entry(Environment, "Night temperature (K)", "Air temperature at the equator at midnight, K.", 100f, 400f) },
            { "PlanetPoleTemperatureDrop", new Entry(Environment, "Colder at the poles (K)", "How much colder a pole is than the equator, K.", 0f, 100f) },
            { "PlanetAmbientLapseRate", new Entry(Environment, "Colder with altitude (K/km)", "How much colder the air gets with altitude, K per km; Earth is about 6.5.", 0f, 12f) },
            { "PlanetAmbientLagSeconds", new Entry(Environment, "Air lag behind the sun (s)", "Seconds the air takes to chase its target, which is what puts the day's peak after noon.", 0f, 600f) },
            { "PlanetConvectionCoefficient", new Entry(Environment, "Air convection at sea level (W/m2 K)", "Convective coefficient at sea level, W/(m2 K), before the atmosphere blend thins it.", 0f, 200f) },
            { "PlanetSolarDecay", new Entry(Environment, "Sunlight absorbed by air (0..1)", "How much of the sun a full atmosphere absorbs, 0..1.", 0f, 1f) },
            { "PlanetUndergroundTemperature", new Entry(Environment, "Underground temperature (K)", "Rock temperature below the damping depth, K.", 100f, 400f) },
            { "PlanetUndergroundDampingDepth", new Entry(Environment, "Day-night reach underground (m)", "Metres over which the day-night swing dies out underground.", 1f, 200f) },
            { "PlanetCoreTemperature", new Entry(Environment, "Core temperature (K)", "Rock temperature the model warms toward below the sea-level deadzone, K.", 300f, 6000f) },
            { "PlanetSealevelDeadzone", new Entry(Environment, "Depth before core warming (m)", "Metres below sea level before the rock starts warming toward the core.", 0f, 4000f) },

            { "ClimateGroundInfluence", new Entry(Environment, "Ground shifts air temperature (0..1)", "How much the ground a grid is parked on shifts the air above it; 0 ignores what the ground is made of.", 0f, 1f) },
            { "ClimateWeatherInfluence", new Entry(Environment, "Weather shifts air temperature (0..1)", "How much the weather changes the air around a grid; 1 applies the game's own figures in full.", 0f, 1f) },
            { "VacuumTemperature", new Entry(Environment, "Vacuum temperature (K)", "Sky temperature in space, K. 2.7 is the real background.", 0f, 300f) },
            { "SolarEnergy", new Entry(Solar, "Sunlight at the planet (W/m2)", "Irradiance at the planet, W/m2.", 0f, 5000f) },
            { "FrictionAtSpeedsAbove", new Entry(Aero, "Heating starts above (m/s)", "Relative airspeed at which atmospheric heating starts, m/s.", 0f, 300f) },
            { "FrictionScale", new Entry(Aero, "Friction heating scale", "Multiplier on the v3 heating term, so moving it retunes temperatures and leaves handling alone.", 0f, 0.01f) },

            // The drag three had no entry at all, so they fell through to "Other" unlabelled and
            // untooltipped: a switch called EnableDrag, a bare number and a second switch, on a
            // page that says it does not describe them. They are the force half of the same term
            // friction already computes.
            { "EnableDrag", new Entry(Aero, "Apply drag", "Takes the drag the friction term already computes out of the ship's motion; off, so an aerodynamics mod is not doubled.", 0, 1) },
            { "DragCoefficient", new Entry(Aero, "Drag coefficient", "The coefficient a hull is treated as having; 0.5 is measured, and authored rather than read off the shape.", 0f, 2f) },
            { "EnableShapeDrag", new Entry(Aero, "Correct area for hull shape", "Corrects the projected area for which way the hull actually faces, which moves temperatures as well as handling.", 0, 1) },
            { "EnableLift", new Entry(Aero, "Lift", "Applies the aerodynamic force across the airflow rather than along it; needs Hull shape, and is small on real ships.", 0, 1) },
            { "LiftCoefficient", new Entry(Aero, "Lift coefficient", "How much of the computed transverse force is applied; 1 is the model's own answer.", 0f, 2f) },
            { "EnableWindwardShielding", new Entry(Aero, "Shelter blocks behind others", "A block behind another is sheltered from the wind, for heat and for drag, at a second sliced pass over the hull.", 0, 1) },
            { "RoomConvectionCoefficient", new Entry(Environment, "Room air convection (W/m2 K)", "Convective coefficient between a block and room air, W/(m2 K).", 0f, 50f) },
            { "RoomAirDensity", new Entry(Environment, "Room air density (kg/m3)", "Density of room air, kg/m3. 1.225 is sea level.", 0f, 5f) },
            { "SolarOcclusionInterval", new Entry(Occlusion, "Shadow recheck interval (steps)", "Solver steps between sun occlusion raycasts.", 1, 60, true) },

            { "WindRoughnessLength", new Entry(Environment, "Ground roughness (m)", "Height at which wind theoretically reaches zero, m: 0.0002 open water, 0.03 grassland, 0.5 forest.", 0.0001f, 2f) },
            { "WindGradientHeight", new Entry(Environment, "Wind stops rising above (m)", "Height at which wind stops strengthening, m, above which the ground no longer sets it.", 10f, 3000f) },
            { "WindDiurnalAmplitude", new Entry(Environment, "Daily wind swing (0..1)", "How far the daily cycle moves wind either side of its mean, 0..1.", 0f, 1f) },
            { "WindDiurnalCrossover", new Entry(Environment, "Daily cycle vanishes at (m)", "Height at which the daily cycle vanishes, m: the surface cycle below, the nocturnal jet above.", 0f, 500f) },
            { "WindTerrainInfluence", new Entry(Environment, "Terrain steers the wind (0..1)", "How much the shape of the ground steers and speeds the wind, 0..1.", 0f, 1f) },
            { "WindSlopeStrength", new Entry(Environment, "Slope winds (0..1)", "Air running up a mountain by day and draining back down it at night, 0..1.", 0f, 1f) },
            { "WindTerrainRadius", new Entry(Environment, "Terrain read radius (m)", "How far out the land around a point is read, m: the size of landform the wind notices.", 50f, 2000f) },

            { "HeatPumpCarnotFraction", new Entry(Systems, "Pump efficiency (share of Carnot)", "How much of the Carnot limit a pump achieves, 0..1.", 0f, 1f) },
            { "HeatPumpMaxCoefficient", new Entry(Systems, "Pump coefficient ceiling", "Ceiling on the coefficient of performance.", 0f, 20f) },

            { "HeatGlow", new Entry(Display, "Blocks glow when hot", "A block glows over the last 100 K before its own critical temperature, in the colour a body that hot really is.", 0, 1) },
            { "HeatWarningSound", new Entry(Display, "Overheat cue", "A cue in the cockpit as a block comes up on its rating and as it passes it, heard only at the controls.", 0, 1) },
            { "HeatTerminalPanel", new Entry(Display, "Terminal readout", "The thermal panel in a block's terminal detail pane.", 0, 1) },

            { "DebugTextOnScreen", new Entry(Display, "Crosshair readout", "Everything the simulation knows about the block being looked at; also records per-mechanism watts.", 0, 1) },
            { "DebugSolarRaycast", new Entry(Display, "Draw sun ray", "The sun ray from each grid, white when lit and red when occluded.", 0, 1) },
            { "DebugWindRaycast", new Entry(Display, "Draw wind vector", "The relative wind each grid is flying through, drawn from the grid.", 0, 1) },
            { "DebugWindOverlay", new Entry(Display, "Wind map", "Draws the wind field as arrows: 1 a lattice around you, 2 the whole planet. Ctrl+Shift+W cycles it.", 0, WindOverlay.ModeCount - 1, true) },
            { "DebugWindIndicator", new Entry(Display, "Wind indicator", "A needle and a speed beside the crosshair whenever there is wind where you are.", 0, 1) },
            { "DebugBlockOverlay", new Entry(Display, "Block overlay", "The x-ray box overlay. Ctrl+Shift+= cycles it in play.", 0, ThermalDebugView.ModeCount - 1, true) },

            { "RoomOverlayMinKelvin", new Entry(Display, "Room overlay cold end (K)", "Bottom of the room view's colour span, K, which is a tighter ramp than blocks get.", 173.15f, 323.15f) },
            { "RoomOverlayMaxKelvin", new Entry(Display, "Room overlay hot end (K)", "Top of the room view's colour span, K.", 273.15f, 423.15f) },
            { "EnableTelemetry", new Entry(Display, "Collect telemetry", "Per-grid and per-block-type data collection. Off for ordinary play.", 0, 1) },
            { "TelemetryPlanetProbes", new Entry(Display, "Wind probe interval (steps)", "Solver steps between planet-wide wind sweeps, or 0 for none; needs telemetry on.", 0, 3600, true) },
            { "EnableTopSpeed", new Entry(Aero, "Mass sets top speed", "Raises the world's speed cap and holds each ship under a cruise speed that falls with its mass, by a force rather than a limit.", 0, 1) },
            { "SpeedLimit", new Entry(Aero, "World speed limit (m/s)", "The ceiling no ship passes whatever its mass or its boost, written into the world's own environment definition.", 20f, 1000f) },
            { "EnableSpeedBoost", new Entry(Aero, "Allow boosting past cruise", "Lets thrust push a ship past its cruise speed and drags it back, rather than stopping it there.", 0, 1) },
            { "LargeGridMinCruise", new Entry(Aero, "Large: cruise, light (m/s)", "Cruise speed of a large grid at or below the light mass below.", 10f, 1000f) },
            { "LargeGridMidCruise", new Entry(Aero, "Large: cruise, middle (m/s)", "Cruise speed of a large grid at the middle mass below.", 10f, 1000f) },
            { "LargeGridMaxCruise", new Entry(Aero, "Large: cruise, heavy (m/s)", "Cruise speed of a large grid at or above the heavy mass below.", 10f, 1000f) },
            { "LargeGridMinMass", new Entry(Aero, "Large: light mass (kg)", "Mass below which a large grid holds its light cruise speed.", 0f, 2000000f) },
            { "LargeGridMidMass", new Entry(Aero, "Large: middle mass (kg)", "The middle point of the large-grid curve, where the middle cruise speed applies.", 0f, 20000000f) },
            { "LargeGridMaxMass", new Entry(Aero, "Large: heavy mass (kg)", "Mass above which a large grid holds its heavy cruise speed.", 0f, 40000000f) },
            { "LargeGridResistance", new Entry(Aero, "Large: resistance (x)", "How hard a large grid is held to its cruise speed. Higher is a firmer hold.", 0f, 10f) },
            { "LargeGridMaxBoostSpeed", new Entry(Aero, "Large: boost ceiling (N)", "Ceiling on the force that drags a boosting large grid back to its cruise speed.", 0f, 1000f) },
            { "SmallGridMinCruise", new Entry(Aero, "Small: cruise, light (m/s)", "Cruise speed of a small grid at or below the light mass below.", 10f, 1000f) },
            { "SmallGridMidCruise", new Entry(Aero, "Small: cruise, middle (m/s)", "Cruise speed of a small grid at the middle mass below.", 10f, 1000f) },
            { "SmallGridMaxCruise", new Entry(Aero, "Small: cruise, heavy (m/s)", "Cruise speed of a small grid at or above the heavy mass below.", 10f, 1000f) },
            { "SmallGridMinMass", new Entry(Aero, "Small: light mass (kg)", "Mass below which a small grid holds its light cruise speed.", 0f, 100000f) },
            { "SmallGridMidMass", new Entry(Aero, "Small: middle mass (kg)", "The middle point of the small-grid curve, where the middle cruise speed applies.", 0f, 1000000f) },
            { "SmallGridMaxMass", new Entry(Aero, "Small: heavy mass (kg)", "Mass above which a small grid holds its heavy cruise speed.", 0f, 2000000f) },
            { "SmallGridResistance", new Entry(Aero, "Small: resistance (x)", "How hard a small grid is held to its cruise speed. Higher is a firmer hold.", 0f, 10f) },
            { "SmallGridMaxBoostSpeed", new Entry(Aero, "Small: boost ceiling (N)", "Ceiling on the force that drags a boosting small grid back to its cruise speed.", 0f, 1000f) },
            { "ShadowDetail", new Entry(Solar, "Shadow detail", "How much work a shadow is worth: none, planets only, the world around the ship, or everything including other grids.", 0, 3, true) },
            { "ClampOvershoot", new Entry(Solver, "Clamp overshoot", "Stops a substep carrying a block past a neighbour's temperature or past ambient. Leave on.", 0, 1) },
            { "LoopFlowRate", new Entry(Systems, "Coolant flow (m/s)", "How fast coolant moves with one pump at full speed; flow rises with the square root of combined pumping.", 0f, 40f) },
            { "LoopContactMultiplier", new Entry(Systems, "Coolant coupling (x)", "Scales the coupling between the fluid and the metal it touches, at the pipe wall and the sink face alike.", 0f, 5f) },
            { "TelemetrySampleStride", new Entry(Display, "Telemetry sample stride (steps)", "Steps between telemetry samples.", 1, 64, true) },
        };

        /// <summary>
        /// The settings a client may change for itself. Everything else is world state owned by the
        /// server, matching <c>/thermal set</c>.
        /// </summary>
        private static HashSet<string> ClientSide
        {
            get { return Settings.ClientOwned; }
        }

        private static bool initialised;

        /// <summary>
        /// The window itself, built once the framework answers. Null until then, and null again if
        /// it resets, which is what <see cref="Open"/> tests before offering to show it.
        /// </summary>
        private static ThermalSettingsWindow window;

        /// <summary>What a fresh install ships with, to mark what has been changed away from.</summary>
        private static Settings shipped;

        /// <summary>
        /// Requests registration with Rich HUD Master. The framework responds on its own schedule, or
        /// never when it is not installed, so the menu is built from the callback rather than here.
        /// </summary>
        public static void Initialize()
        {
            if (initialised) return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated) return;

            initialised = true;

            // Registration is a handshake with a separate mod that may not be installed and reports
            // no failure, only silence. Both the request and the response are logged, to distinguish
            // a missing framework from a menu that failed to build.
            MyLog.Default.Info("[" + Settings.Name + "] requesting Rich HUD registration");
            RichHudClient.Init(Settings.Name, OnRegistered, OnReset);
        }

        /// <summary>
        /// Keeps the statistics page in step with the world while somebody is looking at it.
        ///
        /// <para>
        /// **The live figures used to be written once and never again.** <see cref="Refresh"/> was
        /// called from the settings sync and from nowhere else, so a category headed "Right now"
        /// held whatever the world was doing at the moment the menu was built. A readout that does
        /// not move is worse than no readout: it reads as a measurement.
        /// </para>
        ///
        /// <para>
        /// Twice a second, and only while the window is open — the refresh walks every live grid
        /// and builds a page of text, which is not cheap enough to do behind a closed menu.
        /// </para>
        /// </summary>
        public static void Tick()
        {
            if (window == null) return;

            if (++framesSinceStatistics < StatisticsFrames) return;
            framesSinceStatistics = 0;

            if (!window.IsOpen) return;

            Refresh();
        }

        /// <summary>Frames between statistics refreshes: about twice a second at sixty.</summary>
        private const int StatisticsFrames = 30;

        private static int framesSinceStatistics;

        public static void Open()
        {
            if (window == null)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "Thermodynamics: the settings menu needs the Rich HUD Master mod", 4000, "Red");
                return;
            }

            window.Show();
        }

        /// <summary>
        /// What the keystroke does: the same key closes the window it opened, which is what a
        /// player expects of a key that opened something and what the close button and Escape do
        /// anyway. `/thermal menu` opens rather than toggles, because a command that closed the
        /// menu would have to be typed into a chat box the menu is covering.
        /// </summary>
        public static void Toggle()
        {
            if (window != null && window.IsOpen)
            {
                window.Hide();
                return;
            }

            Open();
        }

        /// <summary>
        /// One registration serves the whole mod, so the callback fans out to everything built on
        /// the framework rather than each part registering its own client.
        /// </summary>
        private static void OnRegistered()
        {
            MyLog.Default.Info("[" + Settings.Name + "] Rich HUD registered; building menu and readout");

            Build();
            ThermalDebugPanel.Build();
            ThermalHud.Build();
        }

        private static void OnReset()
        {
            window = null;
            ThermalDebugPanel.Reset();
            ThermalHud.Reset();
        }

        /// <summary>
        /// One page of the menu: its name, and the settings on it. Named explicitly rather than
        /// derived from the mechanism sections, because the split that matters to someone tuning a
        /// world does not follow the code's own categories.
        /// </summary>
        private struct Leaf
        {
            public string Name;
            public string[] Settings;

            /// <summary>
            /// The settings that go below the page's **Advanced** line: the ones a world tunes once
            /// or never, kept on the page that owns them rather than moved somewhere else.
            ///
            /// <para>
            /// **Two tiers rather than two pages.** Every setting is still exactly one page away
            /// and still named by this table, which is what `EverySettingIsOnAMenuPageThatNamesIt`
            /// asks; what changes is that a reader opening *Coolant loops* sees the five dials that
            /// answer "my ship is too hot" before the five that describe the fluid.
            /// </para>
            /// </summary>
            public string[] Advanced;

            public Leaf(string name, params string[] settings)
            {
                Name = name;
                Settings = settings;
                Advanced = Empty;
            }

            /// <summary>The same page, with the settings that sit below its Advanced line.</summary>
            public Leaf Then(params string[] advanced)
            {
                Leaf copy = this;
                copy.Advanced = advanced;
                return copy;
            }

            private static readonly string[] Empty = new string[0];
        }

        /// <summary>
        /// A folder in the terminal's page list, and the pages inside it.
        ///
        /// The framework renders these as collapsible groups down the side — its own settings menu
        /// is built this way and this mod had never used them. Two levels of navigation is what
        /// turns a hundred settings from a list to be scrolled into a map to be read once.
        /// </summary>
        private struct Folder
        {
            public string Name;
            public Leaf[] Pages;

            public Folder(string name, params Leaf[] pages)
            {
                Name = name;
                Pages = pages;
            }
        }

        /// <summary>
        /// The menu, as it appears down the side. A setting named nowhere here still gets a
        /// control: whatever is left over lands on a final page, so a setting added to the config
        /// and forgotten here is reachable rather than invisible.
        /// </summary>
        private static readonly Folder[] Folders =
        {
            new Folder("Solver",
                // Threading folded in: whether a fleet is solved across cores is a cost decision
                // like the three above it, and it was a page holding one switch.
                new Leaf("Cost limits",
                    "MaxSubsteps", "MaxElementVisitsPerStep")
                    .Then("MaxSubstepsPerBlock", "FloorBlocksWhenOverBudget",
                        "ClampOvershoot", "ParallelGrids"),
                new Leaf("Pace",
                    "Frequency", "HeatTimeScale")),

            // **Four pages became two.** Conduction, Radiation and Convection were a page each
            // holding a single switch, because a block's conductivity, emissivity and area are its
            // own and live in Cubes.xml — so each page said "the rest of this is elsewhere" and had
            // nothing else to say. They sit with the ambient switch that governs them all. This is
            // not the *Mechanisms* page the docs argue against: that one held switches away from
            // the dials they govern, and these have no dials to be away from.
            new Folder("Heat transfer",
                new Leaf("Ambient exchange",
                    "EnableEnvironment", "EnableConduction", "EnableRadiation", "EnableConvection",
                    "VacuumTemperature"),

                // Solar and Occlusion were two pages about one thing, and eight of their dials
                // asked the same question five times over. See ShadowDetail.
                new Leaf("Sunlight",
                    "EnableSolarHeat", "SolarEnergy", "ShadowDetail")
                    .Then("SolarTerrainRange", "SolarOcclusionSamples", "SolarOcclusionInterval")),

            new Folder("Ship systems",
                new Leaf("Coolant loops",
                    "EnableCoolantLoops", "LoopFlowRate", "LoopHeatTransferCoefficient",
                    "LoopRefillEquivalentKelvin", "LoopRefillKilogramsPerSecond")
                    .Then("WellMixedCoolant", "LoopCoolantKilogramsPerCubicMetre",
                        "LoopSpecificHeat", "LoopContactMultiplier",
                        "LoopStagnantTransferFraction"),
                new Leaf("Heat pumps",
                    "EnableHeatPumps")
                    .Then("HeatPumpCarnotFraction", "HeatPumpMaxCoefficient"),
                new Leaf("Room air",
                    "EnableRoomAir")
                    .Then("RoomConvectionCoefficient", "RoomAirDensity"),

                // Waste heat, Point sources and Overheat damage were three pages carrying four
                // switches between them: where heat comes from and what it does when there is too
                // much of it.
                new Leaf("Heat made and damage",
                    "EnableWasteHeat", "EnableHeatSources", "EnableDamage", "DamageIsPerSecond"),

                // The occupant rather than the ship, and the only page here that is not a block.
                // It sits with the ship's systems because that is what a player is inside of.
                new Leaf("Suit",
                    "EnableSuitDamage", "SuitCriticalTemperature", "SuitCoolingWatts")
                    .Then("SuitConductance", "SuitHeatCapacity", "SuitDamagePerKelvin")),

            // Its own folder rather than a corner of Ship systems, because the two halves of
            // the same term were filed apart: the heating was a ship system and the force it
            // implies was on no page at all. `DragForce` derives one from the other, so a world
            // tuning either wants to see both.
            new Folder("Aerodynamics",
                new Leaf("Friction heating",
                    "EnableFriction")
                    .Then("FrictionAtSpeedsAbove", "FrictionScale"),
                // RelativeTopSpeed's configuration, absorbed. The three that answer "how fast can
                // my ship go" are above the line; the twelve that draw the curve are below it.
                new Leaf("Top speed",
                    "EnableTopSpeed", "SpeedLimit", "EnableSpeedBoost")
                    .Then("LargeGridMinCruise", "LargeGridMidCruise", "LargeGridMaxCruise",
                        "LargeGridMinMass", "LargeGridMidMass", "LargeGridMaxMass",
                        "LargeGridResistance", "LargeGridMaxBoostSpeed",
                        "SmallGridMinCruise", "SmallGridMidCruise", "SmallGridMaxCruise",
                        "SmallGridMinMass", "SmallGridMidMass", "SmallGridMaxMass",
                        "SmallGridResistance", "SmallGridMaxBoostSpeed"),
                new Leaf("Drag and lift",
                    "EnableDrag", "DragCoefficient", "EnableLift")
                    .Then("EnableShapeDrag", "EnableWindwardShielding", "LiftCoefficient")),

            new Folder("Multiplayer",
                new Leaf("Temperatures",
                    "EnableTemperatureSync", "TemperatureSyncInterval")),

            new Folder("World",
                new Leaf("Climate",
                    "EnablePlanets", "PlanetDayTemperature", "PlanetNightTemperature",
                    "ClimateGroundInfluence", "ClimateWeatherInfluence")
                    .Then("PlanetPoleTemperatureDrop", "PlanetAmbientLapseRate",
                        "PlanetAmbientLagSeconds", "PlanetConvectionCoefficient",
                        "PlanetSolarDecay"),
                new Leaf("Underground",
                    "PlanetUndergroundTemperature", "PlanetUndergroundDampingDepth")
                    .Then("PlanetUndergroundConvectionCoefficient", "PlanetCoreTemperature",
                        "PlanetSealevelDeadzone"),

                // Its own switch at the top of it, like every other system's page. The wind dials
                // used to fall through to the leftovers page, which is where a mechanism with no
                // switch ends up (`C7`).
                new Leaf("Wind",
                    "EnableWind", "WindGradientHeight", "WindTerrainInfluence")
                    .Then("WindRoughnessLength", "WindDiurnalAmplitude", "WindDiurnalCrossover",
                        "WindTerrainRadius", "WindSlopeStrength")),
        };

        /// <summary>
        /// A line for a page whose settings do not yet fill it, saying where the rest of that system's
        /// numbers live — a page with one switch on it otherwise looks broken. Each disappears as its
        /// definition file is brought in. See configuration.md, Where the settings surface is going.
        /// </summary>
        private static readonly Dictionary<string, string> PageNotes = new Dictionary<string, string>
        {
            { "Ambient exchange", "Conductivity, emissivity and exposed area are per block, from Cubes.xml" },
            { "Heat made and damage", "How much each block wastes is in Cubes.xml; point sources come through the API" },
        };

        /// <summary>
        /// The debug page: what this mod draws on your screen, and what it writes to disk.
        ///
        /// Its own page at the root rather than a corner of a display section, because it is the
        /// page someone opens while something is wrong. The first four belong to you whatever the
        /// server says; the telemetry pair belongs to the world.
        /// </summary>
        private static readonly Leaf DebugPage = new Leaf("Debug",
                "HeatGlow", "HeatWarningSound", "HeatTerminalPanel", "ShowEnvironmentReadout",
                "DebugBlockOverlay", "DebugTextOnScreen", "DebugWindOverlay", "DebugWindIndicator")
            .Then("DebugSolarRaycast", "DebugWindRaycast", "DebugOverlayMaxBoxes",
                "RoomOverlayMinKelvin", "RoomOverlayMaxKelvin",
                "EnableTelemetry", "TelemetrySampleStride", "TelemetryPlanetProbes");

        /// <summary>
        /// Builds the window: the pages down the rail in the order they are read, and every setting
        /// on the page it belongs to.
        /// </summary>
        private static void Build()
        {
            // A client permitted to ask counts as able to edit: its controls send a request to the
            // server rather than writing locally. Deciding this from IsServer alone is what kept
            // the whole page greyed out on a client, including for an administrator who could
            // change the same settings from chat.
            bool editable = MyAPIGateway.Session == null
                || MyAPIGateway.Session.IsServer
                || SettingsRequests.MayAsk;

            // Writing the file and resetting every value are still the server's alone: one is a
            // disk write on a machine the client is not sitting at, and the other would be forty
            // separate requests.
            bool local = MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer;

            if (shipped == null) shipped = Settings.GetDefaults();

            window = new ThermalSettingsWindow(HudMain.HighDpiRoot);

            // The page the window opens on, and the only one that holds sentences rather than
            // dials: what the world is set to, and what it is doing.
            window.AddStatisticsPage();

            // Everything the layout accounts for, so what is left over can be swept onto a page of
            // its own instead of vanishing.
            HashSet<string> placed = new HashSet<string>();

            // The page someone opens when something is wrong, and the one bulk action. Both sit at
            // the top of the rail rather than inside a folder, because neither is a system.
            AddPage(DebugPage, editable, placed, false);
            window.AddDefaultsPage(local, RestoreDefaults);

            // Anything the tables do not name. Absent when empty: an empty page in the rail is a
            // promise of something to find.
            List<string> leftovers = Unplaced();
            if (leftovers.Count > 0)
            {
                AddPage(new Leaf("Other", leftovers.ToArray()), editable, placed, false);
            }

            for (int f = 0; f < Folders.Length; f++)
            {
                Folder folder = Folders[f];
                window.AddFolder(folder.Name);

                for (int p = 0; p < folder.Pages.Length; p++)
                {
                    AddPage(folder.Pages[p], editable, placed, true);
                }
            }

            window.OpenToFirst();
            BuildTerminalEntry();
            Refresh();
        }

        /// <summary>
        /// The mod's one page in the Rich HUD terminal: a button that opens the window, and the
        /// keystroke that opens it without coming here at all.
        ///
        /// <para>
        /// **The settings are not on it, and cannot be.** The terminal takes a control nowhere but
        /// inside a tile, which is a fixed bordered box drawn inside Rich HUD Master with no
        /// accessor for its background, its border or the scroll bar under its row — which is why
        /// the settings moved into a window of this mod's own. What the terminal is still good for
        /// is being the place a player looks: a mod absent from that list reads as a mod with
        /// nothing to configure.
        /// </para>
        /// </summary>
        private static void BuildTerminalEntry()
        {
            RichHudTerminal.Root.Enabled = true;

            TerminalButton button = new TerminalButton
            {
                Name = "Open the settings",
                ToolTip = new ToolTip
                {
                    text = new RichText("Closes this menu and opens the Thermodynamics settings"
                        + " window, which is where every setting this mod has lives."
                        + "\n\nCtrl+Shift+S opens the same window at any time, and closes it"
                        + " again; so does /thermal menu."),
                },
            };

            // Closes the terminal first: the window would otherwise open underneath the menu that
            // asked for it.
            button.ControlChangedHandler = (sender, args) =>
            {
                RichHudTerminal.CloseMenu();
                Open();
            };

            ControlTile tile = new ControlTile();
            tile.Add(button);

            // A label is one line that clips rather than wrapping, so the keystroke is said in the
            // fewest characters that still say it. The tooltip above carries the long form.
            tile.Add(new TerminalLabel { Name = "or press Ctrl+Shift+S" });

            ControlCategory group = new ControlCategory
            {
                HeaderText = "Thermodynamics",
                SubheaderText = "Its settings are in a window of this mod's own",
            };
            group.Add(tile);

            ControlPage page = new ControlPage { Name = "Settings" };
            page.Add(group);

            RichHudTerminal.Root.Add(page);
        }

        /// <summary>
        /// One page: its settings, the line saying who owns them, and — where the menu does not
        /// reach a system's numbers yet — a line saying where the rest of them live.
        /// </summary>
        private static void AddPage(Leaf leaf, bool editable, HashSet<string> placed, bool indented)
        {
            List<string> members = Take(leaf.Settings, placed);
            List<string> advanced = Take(leaf.Advanced, placed);

            string note;
            PageNotes.TryGetValue(leaf.Name, out note);

            // The subheader says who owns the page's settings, which is a property of all of them
            // rather than of the tier they are drawn in.
            List<string> both = new List<string>(members);
            both.AddRange(advanced);

            window.AddPage(leaf.Name, Subheader(leaf.Name, both, editable), members, advanced,
                editable, note, indented);
        }

        /// <summary>
        /// The settings of one tier that no earlier page has already taken, marking them taken. A
        /// setting named twice belongs to the first page that names it, which is what keeps the
        /// leftovers page honest.
        /// </summary>
        private static List<string> Take(string[] names, HashSet<string> placed)
        {
            List<string> taken = new List<string>();

            for (int i = 0; i < names.Length; i++)
            {
                if (placed.Contains(names[i])) continue;

                placed.Add(names[i]);
                taken.Add(names[i]);
            }

            return taken;
        }

        /// <summary>
        /// Settings the page tables do not mention. Empty in a healthy build; not empty the moment
        /// someone adds a setting to the config and forgets this file.
        /// </summary>
        private static List<string> Unplaced()
        {
            HashSet<string> named = new HashSet<string>();

            for (int f = 0; f < Folders.Length; f++)
            {
                for (int p = 0; p < Folders[f].Pages.Length; p++)
                {
                    Leaf page = Folders[f].Pages[p];

                    // Both tiers: a setting below a page's Advanced line is on that page, and
                    // counting only the first tier would sweep every one of them onto Other.
                    for (int i = 0; i < page.Settings.Length; i++) named.Add(page.Settings[i]);
                    for (int i = 0; i < page.Advanced.Length; i++) named.Add(page.Advanced[i]);
                }
            }

            for (int i = 0; i < DebugPage.Settings.Length; i++) named.Add(DebugPage.Settings[i]);
            for (int i = 0; i < DebugPage.Advanced.Length; i++) named.Add(DebugPage.Advanced[i]);

            List<string> missing = new List<string>();
            List<string> names = Settings.Names();

            for (int i = 0; i < names.Count; i++)
            {
                if (!named.Contains(names[i])) missing.Add(names[i]);
            }

            return missing;
        }

        private static void RestoreDefaults()
        {
            Settings.Instance.RestoreDefaults();
            Refresh();

            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: every world setting back to its shipped value", 3000, "White");
        }

        /// <summary>
        /// Brings every label back in step with the settings, after anything that moves a value from
        /// outside one control. The framework's controls read a value through a getter but fix their
        /// *name* at construction, and the name is where this menu marks what has changed.
        /// </summary>
        public static void Refresh()
        {
            Refresh(true);
        }

        /// <summary>
        /// Brings the controls back in step, and the statistics page with them when asked.
        ///
        /// **A write asks for the controls only.** Rebuilding the statistics text walks every live
        /// grid, and a settings write is already the most expensive thing the menu does; the page
        /// refreshes itself twice a second from <see cref="Tick"/> regardless, so a figure is at
        /// most half a second stale rather than costing a fleet walk per slider tick.
        /// </summary>
        public static void Refresh(bool statistics)
        {
            if (window == null || shipped == null) return;

            try
            {
                int changed = 0;
                List<string> names = Settings.Names();

                for (int i = 0; i < names.Count; i++)
                {
                    if (Changed(names[i])) changed++;
                }

                window.Refresh();
                if (statistics) window.SetStatistics(StatisticsText(changed, names));
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to refresh the settings menu\n" + e);
            }
        }

        /// <summary>Watts at a readable magnitude, as the cockpit panel shows them.</summary>
        private static string Watts(float watts)
        {
            return Units.Watts(watts);
        }

        /// <summary>Whether a setting has been moved away from what a fresh install ships with.</summary>
        internal static bool Changed(string name)
        {
            float mine = Settings.Instance.GetValue(name);
            float theirs = shipped.GetValue(name);

            float difference = mine - theirs;
            if (difference < 0f) difference = -difference;

            // A relative tolerance, because these span switches at 0 and 1 and a heat time scale in
            // the tens of thousands.
            float scale = theirs < 0f ? -theirs : theirs;
            return difference > 0.0001f * (scale < 1f ? 1f : scale);
        }

        /// <summary>The label a control carries: its name, dotted when it has been changed.</summary>
        internal static string Label(string name, bool moved)
        {
            string label = EntryFor(name).Label;
            return moved ? "• " + label : label;
        }

        /// <summary>
        /// Rewrites the statistics page: what this world is set to, and what it is doing.
        ///
        /// <para>
        /// Everything the menu can report is here because this is the only page that can hold a
        /// sentence. The figures are read from the running grids rather than computed from the
        /// settings that produced them — the question is not what the ceiling is set to, which is
        /// the slider that set it, but what the world is doing against it.
        /// </para>
        /// </summary>
        private static string StatisticsText(int changed, List<string> names)
        {
            StringBuilder text = new StringBuilder();

            WriteWorld(text, changed, names);
            WriteLive(text);
            WriteChanged(text, names);

            return text.ToString();
        }

        /// <summary>General information: what this world is running and who may change it.</summary>
        private static void WriteWorld(StringBuilder text, int changed, List<string> names)
        {
            bool server = MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer;

            text.Append("THIS WORLD\n");
            Row(text, "Mod version", Settings.Name + ", config v" + Settings.CurrentVersion);
            Row(text, "Settings changed", changed + " of " + names.Count
                + (changed == 0 ? "  (all shipped defaults)" : ""));
            Row(text, "Settings digest", SettingsSync.Fingerprint());
            Row(text, "This machine", server
                ? "server; changes are saved here"
                : SettingsRequests.MayAsk
                    ? "client; changes are sent to the server"
                    : "client; world settings are read only");

            string warning = Warning();
            if (warning.Length > 0) text.Append("\nWorth knowing: ").Append(warning).Append('\n');

            text.Append("\nCompare the digest with the server's /thermal sync to tell a settings"
                + " disagreement from a simulation one.\n\n");
        }

        /// <summary>
        /// Performance data, read off the grids that are actually running.
        ///
        /// <para>
        /// The worst grid rather than the mean wherever a fleet has to be reduced to one number: a
        /// world is as starved as its most starved grid, and an average across it hides the one
        /// that is in trouble.
        /// </para>
        /// </summary>
        private static void WriteLive(StringBuilder text)
        {
            int grids = 0, floored = 0, granted = 0, critical = 0, links = 0, clamped = 0;
            long blocks = 0, nodes = 0, steps = 0, visits = 0;
            float demanded = 0f, hottest = float.MinValue;
            float vented = 0f, made = 0f, ambient = 0f, friction = 0f;
            double rate = 1d;
            int budget = int.MaxValue;

            IList<ThermalGrid> live = ThermalGrid.LiveGrids;
            for (int i = 0; live != null && i < live.Count; i++)
            {
                ThermalGrid thermals = live[i];
                if (thermals == null || thermals.Simulation == null) continue;

                ThermalSimulation simulation = thermals.Simulation;
                Core.ThermalSolver solver = simulation.Solver;

                grids++;
                blocks += thermals.BlockCount;
                nodes += solver.Nodes.Count;
                links += solver.LinkCount;
                floored += solver.FlooredNodes;
                critical += thermals.CriticalBlocks;
                steps += simulation.StepsCompleted;

                vented += simulation.VentedWatts;
                made += simulation.HeatGainWatts;
                ambient += simulation.EnvironmentWatts;
                friction += simulation.FrictionWatts;

                if (solver.LastSubsteps > granted) granted = solver.LastSubsteps;
                if (solver.LastRequiredSubsteps > demanded) demanded = solver.LastRequiredSubsteps;
                if (solver.LastStepWasClamped) clamped++;
                if (simulation.SubstepCost > visits) visits = simulation.SubstepCost;
                if (simulation.SubstepBudget < budget) budget = simulation.SubstepBudget;
                if (simulation.SimulationRate < rate) rate = simulation.SimulationRate;

                Core.ThermalNode node = thermals.HottestNode;
                if (node != null && node.Temperature > hottest) hottest = node.Temperature;
            }

            if (grids == 0)
            {
                text.Append("WHAT IS RUNNING\n");
                text.Append("    No grids are being simulated yet, so there is nothing to report"
                    + " here. This fills in as soon as a grid loads.\n\n");
                WriteFrameTime(text);
                return;
            }

            text.Append("WHAT IS RUNNING\n");
            Row(text, "Grids simulated", grids.ToString("n0"));
            Row(text, "Blocks", blocks.ToString("n0"));
            Row(text, "Solver nodes", nodes.ToString("n0"));
            Row(text, "Links between them", links.ToString("n0"));
            Row(text, "Hottest block", TemperatureScale.ToCelsiusString(hottest));
            Row(text, "Blocks over critical", critical.ToString("n0")
                + (critical == 0 ? "  (nothing is failing)" : "  (taking damage)"));

            text.Append("\nENERGY\n");
            Row(text, "Heat being made", Watts(made));
            Row(text, "Vented to the world", Watts(vented));
            Row(text, "Ambient exchange", Watts(ambient));
            Row(text, "Aerodynamic friction", Watts(friction));

            text.Append("\nSOLVER, WORST GRID\n");
            Row(text, "Substeps granted", granted + " of " + demanded.ToString("n1") + " asked for");
            Row(text, "Steps shortened", clamped == 0
                ? "none"
                : clamped + " of " + grids + " grids, to fit the budget");
            Row(text, "Blocks floored by the cap", floored.ToString("n0"));
            Row(text, "Element visits a step", visits.ToString("n0")
                + " against a budget of "
                + (budget == int.MaxValue ? "unbounded" : budget.ToString("n0") + " substeps"));
            Row(text, "Simulation rate", (100d * rate).ToString("n0") + "%"
                + (rate > 0.999d
                    ? "  (heat is keeping up with real time)"
                    : "  (heat is running slow; the step is being shortened)"));
            Row(text, "Steps completed", steps.ToString("n0"));

            text.Append('\n');
            WriteFrameTime(text);
        }

        /// <summary>
        /// What the mod costs the frame, which only telemetry measures.
        ///
        /// <para>
        /// Reported as absent rather than as nought when it is switched off (`E8`): the timer is
        /// only wound in <see cref="Session.Simulate"/>'s telemetry branch, so a zero here would be
        /// a reading of an instrument that never ran.
        /// </para>
        /// </summary>
        private static void WriteFrameTime(StringBuilder text)
        {
            text.Append("FRAME COST\n");

            if (!Telemetry.Enabled)
            {
                text.Append("    Not measured. The frame timer runs only while telemetry is"
                    + " recording — turn Collect telemetry on, on the Debug page, and this"
                    + " fills in.\n\n");
                return;
            }

            TimingStat frame = Telemetry.SessionFrameTime;
            if (frame.Calls == 0)
            {
                text.Append("    Telemetry is on but no frame has been timed yet.\n\n");
                return;
            }

            Row(text, "This mod, per frame", frame.LastMilliseconds.ToString("n3") + " ms");
            Row(text, "Mean over the session", frame.MeanMilliseconds.ToString("n3") + " ms");
            Row(text, "Worst frame", frame.MaxMilliseconds.ToString("n3") + " ms");
            Row(text, "Frames timed", frame.Calls.ToString("n0"));
            Row(text, "Telemetry sampling", "1 frame in " + Telemetry.SampleStride);
            text.Append("\n    A frame is 16.7 ms at 60 updates a second, so that is the figure"
                + " these are a share of.\n\n");
        }

        /// <summary>Every setting moved away from what a fresh install ships, with the shipped value.</summary>
        private static void WriteChanged(StringBuilder text, List<string> names)
        {
            text.Append("CHANGED FROM THE SHIPPED DEFAULTS\n");

            int written = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!Changed(name)) continue;

                written++;
                Row(text, EntryFor(name).Label,
                    Settings.Instance.GetValue(name).ToString("n2")
                        + "   was " + shipped.GetValue(name).ToString("n2"));
            }

            if (written == 0)
            {
                text.Append("    Nothing has been moved. This world runs exactly what a fresh"
                    + " install ships with.\n");
            }
        }

        /// <summary>
        /// One `label  value` line. Padded rather than tabbed: the page renders a proportional
        /// font, so this lines up approximately and is still readable where it does not.
        /// </summary>
        private static void Row(StringBuilder text, string label, string value)
        {
            text.Append("    ").Append(label);

            for (int i = label.Length; i < RowLabelWidth; i++) text.Append(' ');

            text.Append("  ").Append(value).Append('\n');
        }

        /// <summary>Characters a statistics row gives its label before the value starts.</summary>
        private const int RowLabelWidth = 26;

        private static string Warning()
        {
            Settings s = Settings.Instance;

            // The one the field tuning ran into: the per-block cap raises what a step asks for,
            // and MaxSubsteps refuses above its own ceiling, so the two have to move together.
            if (s.MaxSubstepsPerBlock > 0 && s.MaxSubsteps < s.MaxSubstepsPerBlock)
            {
                return "MaxSubsteps (" + s.MaxSubsteps + ") refuses what MaxSubstepsPerBlock ("
                    + s.MaxSubstepsPerBlock + ") asks for. Raise MaxSubsteps to at least that.";
            }

            if (s.MaxElementVisitsPerStep <= 0)
            {
                return "The step budget is off. A very large grid can spend a whole frame in one step.";
            }

            if (!s.EnableEnvironment)
            {
                return "The environment is off: nothing radiates, convects or takes sunlight.";
            }

            return "";
        }

        /// <summary>
        /// A section's one line: who owns these settings, or what the section is for where that is
        /// the same for everyone.
        ///
        /// <para>
        /// **Decided from the settings on the page rather than from its name.** This used to
        /// compare the page's name against the `Display` *category* constant, which no page is
        /// called — so on a multiplayer client every page claimed to be server side, the Debug page
        /// included, whose switches are the client's own and always were.
        /// </para>
        /// </summary>
        private static string Subheader(string section, List<string> members, bool editable)
        {
            bool clientOwned = members.Count > 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (ClientSide.Contains(members[i])) continue;

                clientOwned = false;
                break;
            }

            if (clientOwned) return "Yours to change; it reaches nobody else's screen";

            if (MyAPIGateway.Session != null && !MyAPIGateway.Session.IsServer)
            {
                // Three states rather than two, because "you may change this" and "the server will
                // decide" are different promises and a player can tell which one they got.
                return editable
                    ? "World settings; your changes are sent to the server"
                    : "World settings; read only on a client";
            }

            string note;
            return SectionNotes.TryGetValue(section, out note) ? note : "";
        }

        /// <summary>
        /// What each section is for, in one line. Keyed by page name, which is what a section is
        /// headed; the controls carry their own descriptions, so this only says what the page is.
        /// </summary>
        private static readonly Dictionary<string, string> SectionNotes = new Dictionary<string, string>
        {
            { "Cost limits", "What a step may spend before it is shortened, and across how many cores" },
            { "Pace", "How fast heat moves, and how finely" },

            { "Ambient exchange", "What a grid trades with the world it sits in" },
            { "Sunlight", "Sunlight on the hull, and what stands between it and the sun" },

            { "Coolant loops", "Closed pipe rings acting as one fluid mass" },
            { "Heat pumps", "Moving heat up a gradient for an electrical cost" },
            { "Room air", "The air a sealed room holds" },
            { "Heat made and damage", "Where heat comes from, and what too much of it does" },
            { "Suit", "The occupant, and what the ship does to them" },

            { "Friction heating", "Air heating a hull at speed" },
            { "Drag and lift", "The same air pushing back on the motion" },
            { "Top speed", "How fast a ship of this mass may go, and the cap over all of them" },

            { "Temperatures", "What the server tells a client about its own blocks" },

            { "Climate", "Air and ground temperature over a planet" },
            { "Underground", "Rock temperature, and how deep the day reaches" },
            { "Wind", "The wind field, and everything that shapes it" },

            { "Debug", "What this mod draws on your screen, and what it records" },
            { "Other", "Settings this menu's layout table does not describe yet" },
        };

        /// <summary>The shadow levels, in cost order, as the chooser lists them.</summary>
        internal static readonly string[] ShadowDetailNames =
        {
            "none", "planets", "the world", "everything",
        };

        /// <summary>
        /// The block overlay's views, named as the overlay describes them itself, so the window
        /// offers a named choice rather than a slider over three distinct behaviours.
        /// </summary>
        internal static string[] OverlayNames()
        {
            string[] names = new string[ThermalDebugView.ModeCount];

            for (int mode = 0; mode < names.Length; mode++)
            {
                names[mode] = ThermalDebugView.Describe((ThermalDebugView.Mode)mode);
            }

            return names;
        }

        /// <summary>
        /// What a control says on hover: what the setting does, which end of it is the faithful
        /// one, and — where the value is typed — what the usual range is.
        ///
        /// The block overlay is the exception on the faithful end: it is a view chooser rather
        /// than a dial, so there is no end of it to name.
        /// </summary>
        internal static ToolTip TipFor(string name, Entry entry)
        {
            string text = name == "DebugBlockOverlay"
                ? entry.Tip
                : entry.Tip + FidelityEnds.Sentence(name);

            if (NeedsTyping(entry))
            {
                text += "\n\nTyped; usual values run from " + Number(entry.Min, entry) + " to "
                    + Number(entry.Max, entry) + ".";
            }

            return new ToolTip { text = new RichText(text) };
        }

        /// <summary>
        /// Whether this machine may offer a working control for a setting. A client's own switches
        /// are always its own; everything else depends on whether the server takes requests.
        /// </summary>
        internal static bool MayOffer(string name, bool editable)
        {
            return editable || ClientSide.Contains(name);
        }

        /// <summary>
        /// The single write-back path for every control. A client that acquired a control it should
        /// not have is rejected here rather than desynchronising from the server.
        /// </summary>
        internal static void Write(string name, float value)
        {
            if (!CanEdit(name)) return;

            // World state travels to the server, which owns it; a client's own switches are set
            // here. Without this a client's slider would move its local copy and be overwritten by
            // the next value the server sent, which looks exactly like the control not working.
            if (SettingsRequests.MustAsk && !Settings.ClientOwned.Contains(name))
            {
                SettingsRequests.Send(name, value);
                return;
            }

            Settings.Instance.SetValue(name, value);
            Settings.Instance.Apply();
            Refresh(false);
        }

        /// <summary>
        /// Whether this machine may offer a control for a setting at all. The server checks again on
        /// arrival and its answer is the one that counts; this only avoids offering a dial that will
        /// be refused. See configuration.md, Changing settings from a client.
        /// </summary>
        private static bool CanEdit(string name)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer) return true;
            if (ClientSide.Contains(name)) return true;

            return SettingsRequests.MayAsk;
        }

        /// <summary>
        /// Whether a setting's range is one a slider cannot usefully divide — about two hundred
        /// positions, so a four-million span jumps and a hundredth-wide one never moves.
        /// </summary>
        internal static bool NeedsTyping(Entry entry)
        {
            return (entry.Max - entry.Min) > 200f || entry.Max <= 0.1f;
        }

        /// <summary>A value as a field shows it: whole for an integer setting, four places at most
        /// otherwise, and never in scientific notation, which nobody wants to retype.</summary>
        internal static string Number(float value, Entry entry)
        {
            return entry.Integer
                ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>A value as the column beside a slider reads it.</summary>
        internal static string ValueText(float value, Entry entry)
        {
            if (entry.Integer) return ((int)Math.Round(value)).ToString();
            return value.ToString(entry.Max <= 0.1f ? "n4" : "n2");
        }

        /// <summary>
        /// A typed number, in whichever of the player's own format and the invariant one reads it.
        /// False for anything unreadable, which is what half a number looks like while it is still
        /// being typed.
        /// </summary>
        internal static bool TryParse(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>
        /// Builds a control for a setting with no layout entry, using its own name as the label and a
        /// wide default range, so an unlisted setting is still editable.
        /// </summary>
        internal static Entry EntryFor(string name)
        {
            Entry entry;
            if (Layout.TryGetValue(name, out entry)) return entry;

            return new Entry(Other, name, "Not yet described in the menu's layout table.", 0f, 1000f);
        }
    }
}
