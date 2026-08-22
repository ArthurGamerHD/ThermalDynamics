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
    /// The settings menu, built on the Rich HUD Framework.
    ///
    /// Every value in the config file is reachable here: the menu is generated from
    /// <see cref="Settings.Names"/> rather than written by hand, so a setting added to the config
    /// appears without a corresponding menu edit. A name the layout table does not mention still
    /// gets a control, under an "Other" category, so an unlisted setting is still editable.
    ///
    /// Editing follows the same rule as <c>/thermal set</c>: the config is server side, so on a
    /// multiplayer client every simulation control is disabled and only the client-side
    /// presentation switches are editable. Nothing is written to disk until Save is pressed.
    ///
    /// The page is a single column: Save and Reset first, then short titled groups. The framework's
    /// sizes determine this — a tile is a fixed 300x250 box that masks whatever does not fit, and a
    /// group is a fixed-height row scrolling sideways through its tiles.
    /// </summary>
    public static class ThermalSettingsMenu
    {
        /// <summary>How a single setting is presented.</summary>
        private struct Entry
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
        private const string Display = "Display";
        private const string Other = "Other";

        /// <summary>Sections in the order the page reads, top to bottom.</summary>
        private static readonly string[] Order =
        {
            Transfer, Solar, Occlusion, Systems, Solver, Environment, Display, Other,
        };

        /// <summary>
        /// Label, tooltip and slider range per setting. Switches ignore the range. The range bounds
        /// the slider only: the chat command and the mod API accept any value the clamp allows.
        /// </summary>
        private static readonly Dictionary<string, Entry> Layout = new Dictionary<string, Entry>
        {
            { "EnableEnvironment", new Entry(Transfer, "Environment", "Ambient exchange with air, ground and space. Off leaves only internal heat flow.", 0, 1) },
            { "EnableConduction", new Entry(Transfer, "Conduction", "Heat flow between touching blocks.", 0, 1) },
            { "EnableRadiation", new Entry(Transfer, "Radiation", "Radiative exchange with the sky from exposed faces.", 0, 1) },
            { "EnableConvection", new Entry(Transfer, "Convection", "Exchange with atmosphere and with room air.", 0, 1) },
            { "EnableSolarHeat", new Entry(Solar, "Solar heat", "Sunlight on exposed faces, occlusion included.", 0, 1) },
            { "SolarSelfShadowing", new Entry(Solar, "Solar self-shadowing", "A grid shadows itself: a face behind the ship's own structure takes no sunlight. Costs a pass over the grid's cells whenever the sun moves. Off is the cheap model, which lights any face pointing at the sun.", 0, 1) },
            { "SolarOcclusionPlanets", new Entry(Occlusion, "Occlusion: planets", "A planet can shadow the grid — night, and a world's shadow seen from orbit. Analytic, and the cheapest of the three.", 0, 1) },
            { "SolarOcclusionTerrain", new Entry(Occlusion, "Occlusion: terrain", "The planet's own ground can shadow the grid — the mountain to the east at sunrise, the canyon wall. Ground-height lookups, and only near a surface.", 0, 1) },
            { "SolarTerrainRange", new Entry(Occlusion, "Terrain range", "How far along the sun ray the terrain walk looks, in metres. Near ground is what shadows you; far ground almost never does.", 500f, 20000f) },
            { "SolarOcclusionVoxels", new Entry(Occlusion, "Occlusion: asteroids", "Asteroids and other voxels can shadow the grid. Costs a physics raycast per candidate.", 0, 1) },
            { "SolarGridShadows", new Entry(Occlusion, "Grid shadows", "How much work another grid's shadow is worth. None: other grids never shadow this one. Basic: one ray toward the sun, and anything in the way dims the whole grid. Full: the shadow lands on the faces it actually covers, for a walk through the occluder's blocks per face.", 0, 2, true) },
            { "SolarOcclusionSamples", new Entry(Occlusion, "Occlusion samples", "Points across the grid tested for shadow. 1 is a single ray from the middle, all or nothing for the whole ship; more turn a terminator crossing into a ramp and cost their share of the work each.", 1, 9, true) },
            { "EnableHeatSources", new Entry(Systems, "Point heat sources", "Heat from sources registered through the mod API.", 0, 1) },
            { "EnableWasteHeat", new Entry(Systems, "Waste heat", "Power producers, consumers and thrusters turning throughput into heat.", 0, 1) },
            { "EnablePlanets", new Entry(Systems, "Planets", "Per-planet ambient, air and ground temperatures.", 0, 1) },
            { "EnableFriction", new Entry(Systems, "Friction", "Atmospheric heating above the speed threshold.", 0, 1) },
            { "EnableDamage", new Entry(Systems, "Overheat damage", "Blocks above their critical temperature take damage.", 0, 1) },
            { "EnableCoolantLoops", new Entry(Systems, "Coolant loops", "Closed pipe rings acting as one fluid mass.", 0, 1) },
            { "EnableRoomAir", new Entry(Systems, "Room air", "Sealed rooms hold an air mass that carries heat.", 0, 1) },
            { "EnableHeatPumps", new Entry(Systems, "Heat pumps", "The block that moves heat up a gradient for an electrical cost.", 0, 1) },

            // These four had no entry at all, so they fell through to "Other — not yet described"
            // at the bottom of the page, unlabelled and untooltipped. They are the four the field
            // tuning is entirely about: what a step costs and whether it stays stable.
            { "MaxSubsteps", new Entry(Solver, "Substep ceiling", "Most substeps one step may divide itself into. The stability estimate asks for as many as the stiffest block needs; this is the ceiling on granting it, and reaching it is reported as a clamped step.", 1, 64, true) },
            { "MaxSubstepsPerBlock", new Entry(Solver, "Per-block cap", "Most substeps any single block may demand of the whole grid before its heat capacity is floored. 0 leaves every block alone. A handful of light fittings otherwise set the cost of a whole ship. Raise the substep ceiling with it.", 0, 32, true) },
            { "MaxElementVisitsPerStep", new Entry(Solver, "Step budget", "Most element visits one step may make — substeps times links plus four times nodes — before the step is shortened to fit. 0 removes the bound. Trades simulation rate for frame smoothness on very large grids.", 0, 4000000, true) },
            { "ClampEnvironmentOvershoot", new Entry(Solver, "Clamp environment", "Stops radiation or convection carrying a block past ambient in one substep. Leave on.", 0, 1) },

            { "ClampConductionOvershoot", new Entry(Solver, "Clamp conduction", "Stops a step from pushing two blocks past each other's temperature. Leave on.", 0, 1) },
            { "DamageIsPerSecond", new Entry(Solver, "Damage is per second", "Overheat damage scaled to real time rather than to the step.", 0, 1) },
            { "Frequency", new Entry(Solver, "Frequency", "Solver steps per second of simulated time. Higher is finer and costlier.", 1, 60, true) },
            { "SimulationSpeed", new Entry(Solver, "Simulation speed", "Multiplier on how fast heat moves. 1 is the tuned pace.", 0.1f, 10f) },
            { "HeatTimeScale", new Entry(Solver, "Heat time scale", "Seconds of physical time per second of play. The dial that makes heat happen on a human scale.", 1f, 1000f) },

            // From Loops.xml, which the menu never showed. This is the flow rate people ask for.
            { "LoopLargeGridFlowRate", new Entry(Systems, "Flow rate, large", "How fast coolant moves on a large grid with one pump at full speed, m/s. Flow rises with the square root of combined pumping, so four pumps carry twice this, not four times.", 0f, 40f) },
            { "LoopSmallGridFlowRate", new Entry(Systems, "Flow rate, small", "The same for a small grid. Split because it is a balance dial rather than a physical constant: a small-grid pump is a smaller machine driving a shorter ring.", 0f, 40f) },
            { "LoopCoolantMassPerPipe", new Entry(Systems, "Coolant per pipe", "Coolant carried by one pipe block, kg. More is more capacity for the same coupling, so a ring holds heat more steadily and asks less of the integrator.", 1f, 400f) },
            { "LoopSpecificHeat", new Entry(Systems, "Coolant specific heat", "J/(kg K). Water-glycol is about 3400, which is what the shipped fluid is.", 100f, 6000f) },
            { "LoopConductivity", new Entry(Systems, "Coolant conductivity", "How well heat crosses between the fluid and the pipe carrying it, 0..1.", 0f, 1f) },
            { "LoopPipeContactMultiplier", new Entry(Systems, "Pipe contact", "Scales the coupling between the fluid and its own pipe.", 0f, 5f) },
            { "LoopSinkContactMultiplier", new Entry(Systems, "Sink contact", "Scales the coupling through a sink face into whatever is mounted against it. The stiffest path in the mod: a bolt joint carries 167 W/K and a sink face 1,000.", 0f, 5f) },
            { "LoopStagnantTransferFraction", new Entry(Systems, "Stagnant transfer", "What a stopped ring still carries between neighbouring parcels, 0..1. A ring with no pump is a heat buffer rather than a conductor.", 0f, 1f) },

            // From Planets.xml, same argument.
            { "PlanetDayTemperature", new Entry(Environment, "Day temperature", "Air temperature at the equator at noon, K.", 100f, 400f) },
            { "PlanetNightTemperature", new Entry(Environment, "Night temperature", "Air temperature at the equator at midnight, K.", 100f, 400f) },
            { "PlanetPoleTemperatureDrop", new Entry(Environment, "Pole drop", "How much colder a pole is than the equator, K. The least evidenced figure in the climate model.", 0f, 100f) },
            { "PlanetAmbientLapseRate", new Entry(Environment, "Lapse rate", "How much colder the air gets with altitude, K per km. Earth is about 6.5; 4 is a compromise that keeps snow sites from freezing solid.", 0f, 12f) },
            { "PlanetAmbientLagSeconds", new Entry(Environment, "Ambient lag", "Seconds the air takes to chase its target, which is what puts the day's peak after noon. Absolute seconds against a day that is not, so a short-day world wants this smaller.", 0f, 600f) },
            { "PlanetConvectionCoefficient", new Entry(Environment, "Convection coeff", "Convective coefficient at sea level, W/(m2 K), before the atmosphere blend thins it with the air.", 0f, 200f) },
            { "PlanetSolarDecay", new Entry(Environment, "Solar decay", "How much of the sun a full atmosphere absorbs, 0..1.", 0f, 1f) },
            { "PlanetUndergroundTemperature", new Entry(Environment, "Underground temp", "Rock temperature below the damping depth, K.", 100f, 400f) },
            { "PlanetUndergroundDampingDepth", new Entry(Environment, "Damping depth", "Metres over which the day-night swing dies out underground.", 1f, 200f) },
            { "PlanetCoreTemperature", new Entry(Environment, "Core temperature", "Rock temperature the model warms toward below the sea-level deadzone, K. Unreachable in ordinary play at the shipped deadzone.", 300f, 6000f) },
            { "PlanetSealevelDeadzone", new Entry(Environment, "Core deadzone", "Metres below sea level before the rock starts warming toward the core. Shipped at 2 km, which is deeper than SE's voxels go.", 0f, 4000f) },

            { "ClimateGroundInfluence", new Entry(Environment, "Ground influence", "How much the ground a grid is parked on shifts the air above it. 1 applies the full table — snow about 14 K colder than the planet's own figure, desert about 9 K warmer. 0 ignores what the ground is made of.", 0f, 1f) },
            { "ClimateWeatherInfluence", new Entry(Environment, "Weather influence", "How much the weather changes the air around a grid. 1 applies the game's own figures in full — a heavy snowstorm about 18 K colder with a tenth of the sun and twice the wind, a sandstorm 12 K warmer. 0 leaves the weather affecting nothing but the wind.", 0f, 1f) },
            { "VacuumTemperature", new Entry(Environment, "Vacuum temperature", "Sky temperature in space, K. 2.7 is the real background.", 0f, 300f) },
            { "SolarEnergy", new Entry(Solar, "Solar energy", "Irradiance at the planet, W/m2.", 0f, 5000f) },
            { "FrictionAtSpeedsAbove", new Entry(Environment, "Friction above", "Speed at which atmospheric friction starts, m/s.", 0f, 300f) },
            { "FrictionScale", new Entry(Environment, "Friction scale", "Multiplier on friction heating.", 0f, 0.01f) },
            { "RoomConvectionCoefficient", new Entry(Environment, "Room convection", "Convective coefficient between a block and room air, W/(m2 K).", 0f, 50f) },
            { "RoomAirDensity", new Entry(Environment, "Room air density", "Density of room air, kg/m3. 1.225 is sea level.", 0f, 5f) },
            { "SolarOcclusionInterval", new Entry(Occlusion, "Occlusion interval", "Solver steps between sun occlusion raycasts.", 1, 60, true) },

            { "WindRoughnessLength", new Entry(Environment, "Roughness length", "Height at which wind theoretically reaches zero, m — about a tenth of what covers the ground. 0.0002 open water, 0.03 grassland, 0.5 forest. Sets how fast wind picks up as you climb.", 0.0001f, 2f) },
            { "WindGradientHeight", new Entry(Environment, "Gradient height", "Height at which wind stops strengthening, m. Above the boundary layer the ground no longer sets the wind.", 10f, 3000f) },
            { "WindDiurnalAmplitude", new Entry(Environment, "Diurnal swing", "How far the daily cycle moves wind either side of its mean, 0..1. Ground level peaks in the afternoon; above the crossover it peaks before dawn instead.", 0f, 1f) },
            { "WindDiurnalCrossover", new Entry(Environment, "Diurnal crossover", "Height at which the daily cycle vanishes, m. Below it the surface cycle, above it the nocturnal jet.", 0f, 500f) },
            { "WindTerrainInfluence", new Entry(Environment, "Terrain influence", "How much the shape of the ground steers and speeds the wind, 0..1: faster over rises, sheltered behind ridges, channelled along valleys.", 0f, 1f) },
            { "WindSlopeStrength", new Entry(Environment, "Slope winds", "Air running up a mountain by day and draining back down it at night, 0..1. Blows on a still day and is overrun by a real wind. Costs nothing extra.", 0f, 1f) },
            { "WindTerrainRadius", new Entry(Environment, "Terrain radius", "How far out the land around a point is read, m. The size of landform the wind notices.", 50f, 2000f) },

            { "HeatPumpCarnotFraction", new Entry(Systems, "Carnot fraction", "How much of the Carnot limit a pump achieves, 0..1.", 0f, 1f) },
            { "HeatPumpMaxCoefficient", new Entry(Systems, "Max coefficient", "Ceiling on the coefficient of performance.", 0f, 20f) },

            { "DebugTextOnScreen", new Entry(Display, "Crosshair readout", "Everything the simulation knows about the block being looked at. Also makes the solver record per-mechanism watts, which is not free.", 0, 1) },
            { "DebugSolarRaycast", new Entry(Display, "Draw sun ray", "The sun ray from each grid, white when lit and red when occluded.", 0, 1) },
            { "DebugWindRaycast", new Entry(Display, "Draw wind vector", "The relative wind each grid is flying through, drawn from the grid. Green in still air, red once it is fast enough to heat the leading face.", 0, 1) },
            { "DebugWindOverlay", new Entry(Display, "Wind map", "Draws the wind field as arrows: 1 a lattice around you, 2 the whole planet, where the circulation bands are. Ctrl+Shift+W cycles it in play.", 0, WindOverlay.ModeCount - 1, true) },
            { "DebugWindIndicator", new Entry(Display, "Wind indicator", "A needle and a speed beside the crosshair whenever there is wind where you are.", 0, 1) },
            { "DebugBlockOverlay", new Entry(Display, "Block overlay", "The x-ray box overlay. Ctrl+Shift+= cycles it in play.", 0, ThermalDebugView.ModeCount - 1, true) },

            { "RoomOverlayMinKelvin", new Entry(Display, "Room overlay: cold", "Bottom of the room view's colour span, K. Room air lives in a narrow band, so it gets a tighter ramp than blocks do.", 173.15f, 323.15f) },
            { "RoomOverlayMaxKelvin", new Entry(Display, "Room overlay: hot", "Top of the room view's colour span, K.", 273.15f, 423.15f) },
            { "EnableTelemetry", new Entry(Display, "Collect telemetry", "Per-grid and per-block-type data collection. Off for ordinary play.", 0, 1) },
            { "TelemetryPlanetProbes", new Entry(Display, "Wind probes", "Solver steps between planet-wide wind sweeps, or 0 for none. Reads the wind at 72 points around the planet at five heights each, whether or not anything is standing there. Needs telemetry on.", 0, 3600, true) },
            { "TelemetrySampleStride", new Entry(Display, "Sample stride", "Steps between telemetry samples.", 1, 64, true) },
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
        /// The page the menu opens on. Kept for <see cref="Open"/>; the others are reached from
        /// the framework's own page list down the side.
        /// </summary>
        private static ControlPage page;

        /// <summary>
        /// Every setting's control, by setting name, so a change made anywhere — a slider, a
        /// profile, a reset, or the server pushing new values — can be reflected in all of them
        /// rather than only the one that was touched.
        /// </summary>
        private static readonly Dictionary<string, TerminalControlBase> Controls =
            new Dictionary<string, TerminalControlBase>();

        /// <summary>What a fresh install ships with, to mark what has been changed away from.</summary>
        private static Settings shipped;

        /// <summary>
        /// The status page: the one place in this menu where a sentence survives.
        ///
        /// Everything else here is a <see cref="TerminalLabel"/>, which the framework draws as one
        /// centred line and clips at both ends — a paragraph put in one arrives with its beginning
        /// and its end cut off. A <see cref="TextPage"/> wraps and scrolls, so the full warning,
        /// the names of every changed setting and the sync digest live here, and the labels on the
        /// overview stay short enough to read.
        /// </summary>
        private static TextPage statusPage;

        private static TerminalLabel statusLabel;
        private static TerminalLabel profileLabel;
        private static TerminalLabel warningLabel;

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

        public static void Open()
        {
            if (!RichHudClient.Registered)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "Thermodynamics: the settings menu needs the Rich HUD Master mod", 4000, "Red");
                return;
            }

            if (page == null) RichHudTerminal.OpenMenu();
            else RichHudTerminal.OpenToPage(page);
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
            page = null;
            Controls.Clear();
            ThermalDebugPanel.Reset();
            ThermalHud.Reset();
        }

        /// <summary>
        /// Pages, and which sections each one carries.
        ///
        /// One page per job rather than one page for everything. Forty-five settings on a single
        /// scroll is a list to be searched by eye; an administrator arrives wanting the solver, or
        /// the environment, and should be one click from it. The framework lists pages down the
        /// side, so this is navigation the menu previously had and did not use.
        /// </summary>
        /// <summary>
        /// One page of the menu: its name, and the settings on it.
        ///
        /// Named explicitly rather than derived from the mechanism sections, because the split that
        /// matters to someone tuning a world does not follow the code's own categories. The solver
        /// section holds both "what a step costs" and "how fast heat moves", which are different
        /// questions, asked by different people, on different days.
        /// </summary>
        private struct Leaf
        {
            public string Name;
            public string[] Settings;

            public Leaf(string name, params string[] settings)
            {
                Name = name;
                Settings = settings;
            }
        }

        /// <summary>
        /// A folder in the terminal's page list, and the pages inside it.
        ///
        /// The framework renders these as collapsible groups down the side — its own settings menu
        /// is built this way and this mod had never used them. Two levels of navigation is what
        /// turns forty-eight settings from a list to be scrolled into a map to be read once.
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
                new Leaf("Cost limits",
                    "MaxSubsteps", "MaxSubstepsPerBlock", "MaxElementVisitsPerStep",
                    "ClampConductionOvershoot", "ClampEnvironmentOvershoot"),
                new Leaf("Pace",
                    "Frequency", "SimulationSpeed", "HeatTimeScale")),

            // One system to a page, its own switch at the top of it. Four switches used to sit
            // together on a "Mechanisms" page because they were all switches, which is filing by
            // part of speech: switching convection off belongs above the convection dials, where
            // you can see what it governs.
            new Folder("Heat transfer",
                new Leaf("Ambient",
                    "EnableEnvironment", "VacuumTemperature"),
                new Leaf("Conduction",
                    "EnableConduction"),
                new Leaf("Radiation",
                    "EnableRadiation"),
                new Leaf("Convection",
                    "EnableConvection"),
                new Leaf("Solar",
                    "EnableSolarHeat", "SolarEnergy"),
                new Leaf("Occlusion",
                    // Self-shadowing is what a grid does to itself, which is occlusion by any
                    // reading; it sat under Solar because that is where its setting name starts.
                    "SolarSelfShadowing", "SolarGridShadows",
                    "SolarOcclusionPlanets", "SolarOcclusionVoxels",
                    "SolarOcclusionTerrain", "SolarTerrainRange",
                    "SolarOcclusionSamples", "SolarOcclusionInterval")),

            new Folder("Ship systems",
                new Leaf("Coolant loops",
                    "EnableCoolantLoops",
                    "LoopLargeGridFlowRate", "LoopSmallGridFlowRate",
                    "LoopCoolantMassPerPipe", "LoopSpecificHeat", "LoopConductivity",
                    "LoopPipeContactMultiplier", "LoopSinkContactMultiplier",
                    "LoopStagnantTransferFraction"),
                new Leaf("Heat pumps",
                    "EnableHeatPumps", "HeatPumpCarnotFraction", "HeatPumpMaxCoefficient"),
                new Leaf("Room air",
                    "EnableRoomAir", "RoomConvectionCoefficient", "RoomAirDensity"),
                new Leaf("Waste heat",
                    "EnableWasteHeat"),
                new Leaf("Friction",
                    "EnableFriction", "FrictionAtSpeedsAbove", "FrictionScale"),
                new Leaf("Overheat damage",
                    "EnableDamage", "DamageIsPerSecond"),
                new Leaf("Point sources",
                    "EnableHeatSources")),

            new Folder("World",
                new Leaf("Climate",
                    "EnablePlanets", "ClimateGroundInfluence", "ClimateWeatherInfluence",
                    "PlanetDayTemperature", "PlanetNightTemperature", "PlanetPoleTemperatureDrop",
                    "PlanetAmbientLapseRate", "PlanetAmbientLagSeconds",
                    "PlanetConvectionCoefficient", "PlanetSolarDecay"),
                new Leaf("Underground",
                    "PlanetUndergroundTemperature", "PlanetUndergroundDampingDepth",
                    "PlanetCoreTemperature", "PlanetSealevelDeadzone")),
        };

        /// <summary>
        /// A line for a page whose settings do not yet fill it, saying where the rest of that
        /// system's numbers currently live.
        ///
        /// A page with one switch on it looks broken. It is not — it is a system whose remaining
        /// dials are in a definition file the menu does not reach yet, and saying so is better
        /// than leaving a reader to wonder. Each of these disappears as its file is brought in;
        /// see [settings-redesign.md](../../../../docs/settings-redesign.md).
        /// </summary>
        private static readonly Dictionary<string, string> PageNotes = new Dictionary<string, string>
        {
            { "Conduction", "A block's conductivity is its own, from Cubes.xml" },
            { "Radiation", "Emissivity and exposed area are per block, from Cubes.xml" },
            { "Convection", "The coefficient is the planet's, on the Climate page" },

            { "Waste heat", "How much each block wastes is in Cubes.xml" },
            { "Point sources", "Registered by other mods through the API" },

        };

        /// <summary>
        /// The debug page: what this mod draws on your screen, and what it writes to disk.
        ///
        /// Its own page at the root rather than a corner of a display section, because it is the
        /// page someone opens while something is wrong. The first four belong to you whatever the
        /// server says; the telemetry pair belongs to the world.
        /// </summary>
        private static readonly Leaf DebugPage = new Leaf("Debug",
            "DebugTextOnScreen", "DebugBlockOverlay", "DebugSolarRaycast", "DebugWindRaycast",
            "DebugWindOverlay", "DebugWindIndicator",
            "RoomOverlayMinKelvin", "RoomOverlayMaxKelvin",
            "EnableTelemetry", "TelemetrySampleStride", "TelemetryPlanetProbes");

        /// <summary>
        /// Live figures per page, refreshed with everything else.
        ///
        /// Several short labels rather than one long one. A <see cref="TerminalLabel"/> is a single
        /// centred line that clips at both ends rather than wrapping — in game, a sentence of any
        /// length arrives with its beginning and its end cut off — so every figure gets its own
        /// line and every line stays inside about twenty-two characters.
        /// </summary>
        private static readonly Dictionary<string, List<TerminalLabel>> Readouts =
            new Dictionary<string, List<TerminalLabel>>();

        /// <summary>Lines a readout tile holds, which is what a tile fits before it masks.</summary>
        private const int ReadoutLines = 3;

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

            Controls.Clear();
            if (shipped == null) shipped = Settings.GetDefaults();

            RichHudTerminal.Root.Enabled = true;

            // Loose pages first, folders after. In game, a root page added *after* a category
            // draws against the last folder's row rather than on its own line — so the order here
            // is the order the rail can render, not a preference.
            page = BuildOverview(local, editable);
            RichHudTerminal.Root.Add(page);

            statusPage = new TextPage
            {
                Name = "Status",
                HeaderText = "Thermodynamics",
                SubHeaderText = "What this world is set to",
            };
            RichHudTerminal.Root.Add(statusPage);

            // Everything the layout accounts for, so what is left over can be swept onto a final
            // page instead of vanishing.
            HashSet<string> placed = new HashSet<string>();

            // Built before the folders so it lands above them in the rail, for the ordering reason
            // above; it is one page and it is the one people open when something is wrong.
            RichHudTerminal.Root.Add(BuildPage(DebugPage, editable, placed));

            // Anything the tables do not name, worked out before the folders are built so this
            // page can be added while root pages still render correctly. Hidden when empty: an
            // empty page in the rail is a promise of something to find.
            List<string> leftovers = Unplaced();
            if (leftovers.Count > 0)
            {
                RichHudTerminal.Root.Add(
                    BuildPage(new Leaf("Other", leftovers.ToArray()), editable, placed));
            }

            for (int f = 0; f < Folders.Length; f++)
            {
                Folder folder = Folders[f];
                TerminalPageCategory category = new TerminalPageCategory { Name = folder.Name };

                for (int p = 0; p < folder.Pages.Length; p++)
                {
                    category.Add(BuildPage(folder.Pages[p], editable, placed));
                }

                RichHudTerminal.Root.Add(category);
            }

            Refresh();
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
                    string[] settings = Folders[f].Pages[p].Settings;
                    for (int i = 0; i < settings.Length; i++) named.Add(settings[i]);
                }
            }

            for (int i = 0; i < DebugPage.Settings.Length; i++) named.Add(DebugPage.Settings[i]);

            List<string> missing = new List<string>();
            List<string> names = Settings.Names();

            for (int i = 0; i < names.Count; i++)
            {
                if (!named.Contains(names[i])) missing.Add(names[i]);
            }

            return missing;
        }

        /// <summary>
        /// One page: its settings laid out as the framework's fixed tile sizes allow, and — where
        /// the page's settings have a measurable effect — what they are currently costing.
        /// </summary>
        private static ControlPage BuildPage(Leaf leaf, bool editable, HashSet<string> placed)
        {
            ControlPage built = new ControlPage { Name = leaf.Name };

            List<string> members = new List<string>();
            for (int i = 0; i < leaf.Settings.Length; i++)
            {
                string name = leaf.Settings[i];
                if (placed.Contains(name)) continue;

                placed.Add(name);
                members.Add(name);
            }

            if (members.Count > 0) AddSection(built, leaf.Name, members, editable);

            string note;
            if (PageNotes.TryGetValue(leaf.Name, out note))
            {
                ControlTile noteTile = new ControlTile();
                noteTile.Add(new TerminalLabel { Name = "the rest of this system:" });
                noteTile.Add(new TerminalLabel { Name = note });

                ControlCategory elsewhere = new ControlCategory
                {
                    HeaderText = "Elsewhere",
                    SubheaderText = "Dials this menu does not reach yet",
                };
                elsewhere.Add(noteTile);
                built.Add(elsewhere);
            }

            string figures = FiguresFor(leaf.Name);
            if (figures == null) return built;

            List<TerminalLabel> lines = new List<TerminalLabel>();
            ControlTile tile = new ControlTile();

            for (int i = 0; i < ReadoutLines; i++)
            {
                TerminalLabel line = new TerminalLabel { Name = "" };
                lines.Add(line);
                tile.Add(line);
            }

            Readouts[leaf.Name] = lines;

            ControlCategory group = new ControlCategory
            {
                HeaderText = "Right now",
                SubheaderText = figures,
            };
            group.Add(tile);
            built.Add(group);

            return built;
        }

        /// <summary>
        /// What a page's live figures describe, or null for a page that has none. Only pages whose
        /// settings have a measurable effect get one: a readout that never moves is worse than no
        /// readout at all.
        /// </summary>
        private static string FiguresFor(string pageName)
        {
            switch (pageName)
            {
                case "Cost limits":
                    return "What the world's grids are asking for, against what they are granted";
                case "Pace":
                    return "How much heat the world is moving";
                case "Debug":
                    return "What is switched on, and what is being recorded";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The page the menu opens on: what state the world is in, and the two actions that change
        /// all of it at once.
        ///
        /// An administrator's first two questions are "what has been changed here" and "what is
        /// this world set to", and neither was answerable from a wall of sliders — every value was
        /// shown, and none of them said whether it was the shipped one.
        /// </summary>
        private static ControlPage BuildOverview(bool local, bool editable)
        {
            ControlPage overview = new ControlPage { Name = "Overview" };

            statusLabel = new TerminalLabel { Name = "" };
            profileLabel = new TerminalLabel { Name = "" };
            warningLabel = new TerminalLabel { Name = "" };

            ControlTile state = new ControlTile();
            state.Add(profileLabel);
            state.Add(statusLabel);
            state.Add(warningLabel);

            // A third tile of live figures, so the row carries three filled tiles rather than two
            // half-empty ones — the framework gives a category a fixed tall band whatever is in it,
            // and two short tiles in that band is mostly air.
            ControlTile facts = new ControlTile();
            List<TerminalLabel> lines = new List<TerminalLabel>();

            for (int i = 0; i < ReadoutLines; i++)
            {
                TerminalLabel line = new TerminalLabel { Name = "" };
                lines.Add(line);
                facts.Add(line);
            }

            Readouts["Overview"] = lines;

            ControlCategory summary = new ControlCategory
            {
                HeaderText = "This world",
                SubheaderText = editable && !local
                    ? "Your changes are sent to the server and saved there"
                    : "Every change is saved to the config file as you make it",
            };
            summary.Add(state);
            summary.Add(facts);
            overview.Add(summary);

            overview.Add(ProfileCategory(local));
            return overview;
        }

        /// <summary>
        /// The five shipped profiles as buttons, with what each one is for.
        ///
        /// They existed only as a chat command, which meant the menu could show a world tuned by a
        /// profile without ever mentioning that profiles were how you got there.
        /// </summary>
        private static ControlCategory ProfileCategory(bool local)
        {
            ControlCategory group = new ControlCategory
            {
                HeaderText = "Profiles",
                SubheaderText = local
                    ? "A profile sets every world setting, so 'default' is also how you start over"
                    : "Applied by the server; ask an administrator",
            };

            ControlTile tile = new ControlTile();
            int perTile = 0;

            for (int i = 0; i < Core.ThermalProfiles.Names.Length; i++)
            {
                string profile = Core.ThermalProfiles.Names[i];

                TerminalButton button = new TerminalButton
                {
                    Name = profile,
                    ToolTip = Tip(Core.ThermalProfiles.Describe(profile)),
                    Enabled = local,
                };
                button.ControlChangedHandler = (sender, args) => ApplyProfile(profile);

                tile.Add(button);
                perTile++;

                if (perTile == ControlsPerTile)
                {
                    group.Add(tile);
                    tile = new ControlTile();
                    perTile = 0;
                }
            }

            if (perTile > 0) group.Add(tile);
            return group;
        }

        private static void ApplyProfile(string profile)
        {
            if (!Settings.Instance.ApplyProfile(profile))
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "Thermodynamics: no profile called " + profile, 3000, "Red");
                return;
            }

            Refresh();
            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: profile " + profile + " applied (unsaved)", 3000, "White");
        }

        /// <summary>
        /// Brings every label on the page back in step with the settings.
        ///
        /// Called after anything that can move a value from outside a single control — a profile, a
        /// reset, or the server sending new settings — because the framework's controls read their
        /// values through a getter but their *names* are fixed at construction, and the name is
        /// where this menu says what has been changed.
        /// </summary>
        public static void Refresh()
        {
            if (page == null || shipped == null) return;

            try
            {
                int changed = 0;
                List<string> names = Settings.Names();

                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    bool moved = Changed(name);
                    if (moved) changed++;

                    TerminalControlBase control;
                    if (Controls.TryGetValue(name, out control)) control.Name = Label(name, moved);
                }

                // Short enough to survive a single clipped line. The sentences are on the status
                // page, which wraps.
                statusLabel.Name = changed == 0
                    ? "all shipped defaults"
                    : "changed: " + changed + " of " + names.Count;

                profileLabel.Name = "profile: " + (MatchingProfile() ?? "custom");

                string warning = Warning();
                warningLabel.Name = warning.Length == 0 ? "no conflicts" : "! " + WarningShort();

                RefreshStatusPage(changed, names);

                RefreshReadouts();
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to refresh the settings menu\n" + e);
            }
        }

        /// <summary>
        /// Fills each page's live figures from the grids themselves.
        ///
        /// Read from what is running rather than computed from the settings that produced it: the
        /// question a page like "Cost and stability" is asked is not what the ceiling is set to —
        /// that is the slider above — but what the world is doing against it.
        /// </summary>
        private static void RefreshReadouts()
        {
            if (Readouts.Count == 0) return;

            int grids = 0;
            long blocks = 0;
            int floored = 0;
            int granted = 0;
            float demanded = 0f;
            int critical = 0;
            float hottest = float.MinValue;
            float vented = 0f;
            float made = 0f;
            double rate = 1d;

            IList<ThermalGrid> live = ThermalGrid.LiveGrids;
            for (int i = 0; live != null && i < live.Count; i++)
            {
                ThermalGrid thermals = live[i];
                if (thermals == null || thermals.Simulation == null) continue;

                Core.ThermalSolver solver = thermals.Simulation.Solver;

                grids++;
                blocks += thermals.BlockCount;
                floored += solver.FlooredNodes;
                critical += thermals.CriticalBlocks;
                vented += thermals.Simulation.VentedWatts;
                made += thermals.Simulation.HeatGainWatts;

                // Worst rather than mean: a fleet is as starved as its most starved grid, and an
                // average across it hides the one that is actually in trouble.
                if (solver.LastSubsteps > granted) granted = solver.LastSubsteps;
                if (solver.LastRequiredSubsteps > demanded) demanded = solver.LastRequiredSubsteps;
                if (thermals.Simulation.SimulationRate < rate) rate = thermals.Simulation.SimulationRate;

                Core.ThermalNode node = thermals.HottestNode;
                if (node != null && node.Temperature > hottest) hottest = node.Temperature;
            }

            if (grids == 0)
            {
                Fill("Overview", "no grids yet");
                Fill("Cost limits", "no grids yet");
                Fill("Pace", "no grids yet");
            }
            else
            {
                Fill("Cost limits",
                    grids + " grids, " + blocks.ToString("n0") + " blocks",
                    "substeps " + granted + " of " + demanded.ToString("n1") + " asked",
                    floored.ToString("n0") + " floored, "
                        + (100d * rate).ToString("n0") + "% rate");

                Fill("Pace",
                    "hottest " + TemperatureScale.ToCelsiusString(hottest),
                    critical + " over critical",
                    Watts(vented) + " out, " + Watts(made) + " in");

                Fill("Overview",
                    grids + " grids, " + blocks.ToString("n0") + " blocks",
                    "hottest " + TemperatureScale.ToCelsiusString(hottest),
                    Watts(vented) + " out, " + Watts(made) + " in");
            }

            Fill("Debug",
                "overlay " + ThermalDebugView.Describe(ThermalDebugView.Current),
                "telemetry " + (Telemetry.Enabled ? "recording" : "off"),
                "1 sample in " + Telemetry.SampleStride);
        }

        /// <summary>
        /// Fills one page's readout, a line at a time. Extra lines are dropped rather than joined,
        /// because a joined line is a clipped line.
        /// </summary>
        private static void Fill(string pageName, params string[] lines)
        {
            List<TerminalLabel> labels;
            if (!Readouts.TryGetValue(pageName, out labels)) return;

            for (int i = 0; i < labels.Count; i++)
            {
                labels[i].Name = i < lines.Length ? lines[i] : "";
            }
        }

        /// <summary>Watts at a readable magnitude, as the cockpit panel shows them.</summary>
        private static string Watts(float watts)
        {
            return Units.Watts(watts);
        }

        /// <summary>Whether a setting has been moved away from what a fresh install ships with.</summary>
        private static bool Changed(string name)
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
        private static string Label(string name, bool moved)
        {
            string label = EntryFor(name).Label;
            return moved ? "• " + label : label;
        }

        /// <summary>
        /// Which shipped profile this world currently matches, or null when it matches none.
        ///
        /// Only the nine values a profile sets are compared, so a world on the arcade profile with
        /// a different vacuum temperature still reads as arcade — which is what an administrator
        /// means by the question.
        /// </summary>
        private static string MatchingProfile()
        {
            for (int i = 0; i < Core.ThermalProfiles.Names.Length; i++)
            {
                string name = Core.ThermalProfiles.Names[i];

                Core.ThermalSettings bundle = new Core.ThermalSettings();
                if (!Core.ThermalProfiles.Apply(bundle, name)) continue;

                Settings mine = Settings.Instance;
                if (bundle.Frequency != mine.Frequency) continue;
                if (bundle.SimulationSpeed != mine.SimulationSpeed) continue;
                if (bundle.HeatTimeScale != mine.HeatTimeScale) continue;
                if (bundle.MaxSubsteps != mine.MaxSubsteps) continue;
                if (bundle.MaxSubstepsPerBlock != mine.MaxSubstepsPerBlock) continue;
                if (bundle.ClampConductionOvershoot != mine.ClampConductionOvershoot) continue;
                if (bundle.ClampEnvironmentOvershoot != mine.ClampEnvironmentOvershoot) continue;
                if (bundle.EnableRoomAir != mine.EnableRoomAir) continue;
                if (bundle.SolarSelfShadowing != mine.SolarSelfShadowing) continue;

                return name;
            }

            return null;
        }

        /// <summary>
        /// Combinations worth saying out loud, because each one is a setting quietly cancelling
        /// another and none of them is visible from the two controls involved.
        /// </summary>
        /// <summary>
        /// The same conflict as a label, in the space a label has. The sentence is on the status
        /// page; this is only the flag that sends you there.
        /// </summary>
        private static string WarningShort()
        {
            Settings s = Settings.Instance;

            if (s.MaxSubstepsPerBlock > 0 && s.MaxSubsteps < s.MaxSubstepsPerBlock)
            {
                return "substep caps disagree";
            }

            if (s.MaxElementVisitsPerStep <= 0) return "step budget off";
            if (!s.EnableEnvironment) return "environment off";

            return "";
        }

        /// <summary>
        /// Rewrites the status page: what this world is set to, in prose, because it is the only
        /// control here that can hold prose.
        /// </summary>
        private static void RefreshStatusPage(int changed, List<string> names)
        {
            if (statusPage == null) return;

            StringBuilder text = new StringBuilder();

            text.Append("Profile: ").Append(MatchingProfile() ?? "custom").Append('\n');
            text.Append("Settings changed from the shipped defaults: ")
                .Append(changed).Append(" of ").Append(names.Count).Append('\n');
            text.Append("Settings digest: ").Append(SettingsSync.Fingerprint())
                .Append("   (compare with the server's /thermal sync)\n\n");

            string warning = Warning();
            if (warning.Length > 0) text.Append("Worth knowing: ").Append(warning).Append("\n\n");

            if (changed == 0)
            {
                text.Append("Nothing has been moved. This world runs exactly what a fresh install"
                    + " ships with.");
            }
            else
            {
                text.Append("Changed, with the shipped value in brackets:\n");

                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    if (!Changed(name)) continue;

                    text.Append("    ").Append(EntryFor(name).Label)
                        .Append("  ").Append(Settings.Instance.GetValue(name).ToString("n2"))
                        .Append("  (").Append(shipped.GetValue(name).ToString("n2")).Append(")\n");
                }
            }

            statusPage.Text = new RichText(text.ToString());
        }

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
        /// Lays one section out as columns of three controls, side by side across the page.
        ///
        /// The framework's fixed sizes determine this: a tile is a 300x250 box that masks whatever
        /// does not fit, giving three controls per column, and a group is a fixed-height row holding
        /// about three tiles across the page's width. One column per row would waste two thirds of
        /// the width.
        /// </summary>
        private static void AddSection(ControlPage target, string name, List<string> members, bool editable)
        {
            for (int start = 0; start < members.Count; start += ControlsPerGroup)
            {
                // A second row of the same page used to be headed "(cont.)", which tells a reader
                // nothing they cannot already see. It is named for what is in it instead.
                ControlCategory group = new ControlCategory
                {
                    HeaderText = start == 0 ? name : EntryFor(members[start]).Label,
                    SubheaderText = start == 0
                        ? Subheader(name, editable)
                        : "more " + name.ToLower(),
                };

                int groupEnd = Math.Min(members.Count, start + ControlsPerGroup);

                for (int tileStart = start; tileStart < groupEnd; tileStart += ControlsPerTile)
                {
                    ControlTile tile = new ControlTile();

                    int tileEnd = Math.Min(groupEnd, tileStart + ControlsPerTile);
                    for (int i = tileStart; i < tileEnd; i++)
                    {
                        tile.Add(Control(members[i], editable));
                    }

                    group.Add(tile);
                }

                target.Add(group);
            }
        }

        /// <summary>
        /// Controls per tile. A tile is 250 high with 54 of padding at each end and a control is about
        /// 40 with 12 of spacing, so three fit and a fourth is masked.
        /// </summary>
        private const int ControlsPerTile = 3;

        /// <summary>
        /// Controls per group: two columns, which is what the page width shows. A third would be 936
        /// across a page of about 840 and would require sideways scrolling. A section with more than
        /// six controls continues in another group.
        /// </summary>
        private const int ControlsPerGroup = ControlsPerTile * 2;

        private static string Subheader(string section, bool editable)
        {
            if (section != Display && !MyAPIGateway.Session.IsServer)
            {
                // Three states rather than two, because "you may change this" and "the server will
                // decide" are different promises and a player can tell which one they got.
                return editable
                    ? "Server side; your changes are sent to the server"
                    : "Server side; read only here";
            }

            if (!editable)
            {
                return section == Display
                    ? "Client side; yours to change"
                    : "Server side; read only here";
            }

            return SectionNotes.ContainsKey(section) ? SectionNotes[section] : "";
        }

        /// <summary>
        /// One-line description of each section. The controls carry their own descriptions, so this
        /// only names the category.
        /// </summary>
        private static readonly Dictionary<string, string> SectionNotes = new Dictionary<string, string>
        {
            { Transfer, "How heat moves" },
            { Solar, "Sunlight, and self-shadowing" },
            { Occlusion, "What stands between a grid and the sun" },
            { Systems, "Ship parts that make, move or resist heat" },
            { Solver, "Pace and stability" },
            { Environment, "The world the grid sits in" },
            { Display, "What is drawn on your screen, and what is recorded" },
            { Other, "Not yet described" },
        };

        /// <summary>
        /// Restores every setting the player is allowed to change, which on a client is the
        /// presentation switches only, so a client reset cannot alter server-owned world state.
        /// </summary>
        private static void ResetAll()
        {
            Settings fresh = Settings.GetDefaults();
            List<string> names = Settings.Names();

            int changed = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!CanEdit(name)) continue;

                Settings.Instance.SetValue(name, fresh.GetValue(name));
                changed++;
            }

            Settings.Instance.Apply();
            Refresh();

            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: " + changed + " settings back to defaults (unsaved)", 3000, "White");
        }

        private static TerminalControlBase Control(string name, bool editable)
        {
            TerminalControlBase built = BuildControl(name, editable);
            Controls[name] = built;
            return built;
        }

        private static TerminalControlBase BuildControl(string name, bool editable)
        {
            Entry entry = EntryFor(name);
            bool enabled = editable || ClientSide.Contains(name);

            if (Settings.IsFlag(name))
            {
                TerminalCheckbox box = new TerminalCheckbox
                {
                    Name = entry.Label,
                    ToolTip = Tip(entry.Tip),
                    Enabled = enabled,
                    Value = Settings.Instance.GetValue(name) > 0.5f,
                    CustomValueGetter = () => Settings.Instance.GetValue(name) > 0.5f,
                };
                box.ControlChangedHandler = (sender, args) => Write(name, box.Value ? 1f : 0f);
                return box;
            }

            if (name == "DebugBlockOverlay") return OverlayDropdown(entry, enabled);

            // A slider cannot express either end of this mod's ranges. The step budget spans four
            // million, so one pixel is ten thousand visits; the friction scale spans a hundredth,
            // so every pixel is the same number to four decimal places. Both get a field to type
            // the value into instead.
            if (NeedsTyping(entry)) return NumberField(name, entry, enabled);

            if (name == "SolarGridShadows")
            {
                return Dropdown(name, entry, enabled, GridShadowNames);
            }

            TerminalSlider slider = new TerminalSlider
            {
                Name = entry.Label,
                ToolTip = Tip(entry.Tip),
                Enabled = enabled,
                Min = entry.Min,
                Max = entry.Max,
                Value = Settings.Instance.GetValue(name),
                CustomValueGetter = () => Settings.Instance.GetValue(name),
            };

            slider.ValueText = Text(name, slider.Value, entry);
            slider.ControlChangedHandler = (sender, args) =>
            {
                float value = entry.Integer ? (float)Math.Round(slider.Value) : slider.Value;
                Write(name, value);
                slider.ValueText = Text(name, value, entry);
            };

            return slider;
        }

        /// <summary>Names for the grid shadow modes, in value order.</summary>
        private static readonly string[] GridShadowNames = { "none", "basic", "full" };

        /// <summary>
        /// A named choice rather than a slider, since the values are three distinct behaviours rather
        /// than points on a scale.
        /// </summary>
        private static TerminalControlBase Dropdown(string name, Entry entry, bool enabled, string[] labels)
        {
            TerminalDropdown<int> dropdown = new TerminalDropdown<int>
            {
                Name = entry.Label,
                ToolTip = Tip(entry.Tip),
                Enabled = enabled,
            };

            for (int i = 0; i < labels.Length; i++)
            {
                dropdown.List.Add(new RichText(labels[i]), i);
            }

            dropdown.List.SetSelection((int)Settings.Instance.GetValue(name));
            dropdown.ControlChangedHandler = (sender, args) =>
            {
                EntryData<int> selection = dropdown.Value;
                if (selection == null) return;

                Write(name, selection.AssocObject);
            };

            return dropdown;
        }

        private static TerminalControlBase OverlayDropdown(Entry entry, bool enabled)
        {
            TerminalDropdown<int> dropdown = new TerminalDropdown<int>
            {
                Name = entry.Label,
                ToolTip = Tip(entry.Tip),
                Enabled = enabled,
            };

            for (int mode = 0; mode < ThermalDebugView.ModeCount; mode++)
            {
                dropdown.List.Add(
                    new RichText(ThermalDebugView.Describe((ThermalDebugView.Mode)mode)),
                    mode);
            }

            dropdown.List.SetSelection((int)ThermalDebugView.Current);
            dropdown.ControlChangedHandler = (sender, args) =>
            {
                EntryData<int> selection = dropdown.Value;
                if (selection == null) return;

                Write("DebugBlockOverlay", selection.AssocObject);
            };

            return dropdown;
        }

        /// <summary>
        /// The single write-back path for every control. A client that acquired a control it should
        /// not have is rejected here rather than desynchronising from the server.
        /// </summary>
        private static void Write(string name, float value)
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
            Refresh();
        }

        /// <summary>
        /// Whether this machine may offer a control for a setting at all.
        ///
        /// A client owns its presentation switches outright. Everything else is world state, which
        /// a client can now ask the server to change — so the control is offered when this player
        /// is permitted to ask. The server checks again on arrival and its answer is the one that
        /// counts; this only avoids presenting a dial that will be refused.
        /// </summary>
        private static bool CanEdit(string name)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer) return true;
            if (ClientSide.Contains(name)) return true;

            return SettingsRequests.MayAsk;
        }

        /// <summary>
        /// Whether a setting's range is one a slider cannot usefully divide.
        ///
        /// A slider offers something like two hundred distinguishable positions across its range.
        /// Wider than that and a whole position is a meaningless jump — the step budget moves ten
        /// thousand element visits at a time. Finer than a tenth and every position rounds to the
        /// same displayed number, which is the friction scale's problem: its entire range is a
        /// hundredth.
        /// </summary>
        private static bool NeedsTyping(Entry entry)
        {
            return (entry.Max - entry.Min) > 200f || entry.Max <= 0.1f;
        }

        /// <summary>
        /// A setting typed rather than dragged.
        ///
        /// The range is offered in the tooltip rather than enforced here: it is what the slider
        /// would have spanned, not what the setting will accept, and a typed value is checked by
        /// the same clamp the chat command and the mod API go through. Someone who wants a step
        /// budget of nine million can have one, and finds out what it costs.
        /// </summary>
        private static TerminalControlBase NumberField(string name, Entry entry, bool enabled)
        {
            TerminalTextField field = new TerminalTextField
            {
                Name = entry.Label,
                ToolTip = Tip(entry.Tip + "\n\nTyped, because a slider cannot divide this range."
                    + " Usual values run from " + Number(entry.Min, entry)
                    + " to " + Number(entry.Max, entry) + "."),
                Enabled = enabled,
                Value = Number(Settings.Instance.GetValue(name), entry),
                CustomValueGetter = () => Number(Settings.Instance.GetValue(name), entry),
            };

            // Anything that cannot be part of a number never reaches the field, so a typo is
            // refused as it is made rather than on losing focus.
            field.CharFilterFunc = c =>
                (c >= '0' && c <= '9') || c == '.' || c == '-' || c == 'e' || c == 'E' || c == '+';

            field.ControlChangedHandler = (sender, args) =>
            {
                float value;
                if (!float.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out value)
                    && !float.TryParse(field.Value, NumberStyles.Float, CultureInfo.CurrentCulture,
                        out value))
                {
                    // Unreadable: put the setting's own value back rather than guessing at what
                    // was meant. The getter above supplies it on the next draw.
                    field.Value = Number(Settings.Instance.GetValue(name), entry);
                    return;
                }

                Write(name, entry.Integer ? (float)Math.Round(value) : value);
            };

            return field;
        }

        /// <summary>A value as a field shows it: whole for an integer setting, four places at most
        /// otherwise, and never in scientific notation, which nobody wants to retype.</summary>
        private static string Number(float value, Entry entry)
        {
            return entry.Integer
                ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Text(string name, float value, Entry entry)
        {
            if (entry.Integer) return ((int)Math.Round(value)).ToString();
            return value.ToString(entry.Max <= 0.1f ? "n4" : "n2");
        }

        private static ToolTip Tip(string text)
        {
            return new ToolTip { text = new RichText(text) };
        }

        private static string SectionOf(string name)
        {
            Entry entry;
            return Layout.TryGetValue(name, out entry) ? entry.Category : Other;
        }

        /// <summary>
        /// Builds a control for a setting with no layout entry, using its own name as the label and a
        /// wide default range, so an unlisted setting is still editable.
        /// </summary>
        private static Entry EntryFor(string name)
        {
            Entry entry;
            if (Layout.TryGetValue(name, out entry)) return entry;

            return new Entry(Other, name, "Not yet described in the menu's layout table.", 0f, 1000f);
        }
    }
}
