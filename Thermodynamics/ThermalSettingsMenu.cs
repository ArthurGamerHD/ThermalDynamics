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
    public static class ThermalSettingsMenu
    {
        internal struct Entry
        {
            public string Category;
            public string Label;
            public string Tip;
            public float Min;
            public float Max;
            public bool Integer;

/// <summary>Entry operation.</summary>
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

        private static readonly Dictionary<string, Entry> Layout = new Dictionary<string, Entry>
        {
/// <summary>Entry operation.</summary>
            { "EnableEnvironment", new Entry(Transfer, "Ambient exchange", "Ambient exchange with air, ground and space; off leaves only internal heat flow.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableConduction", new Entry(Transfer, "Conduction", "Heat flow between touching blocks.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableRadiation", new Entry(Transfer, "Radiation", "Radiative exchange with the sky from exposed faces.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableConvection", new Entry(Transfer, "Convection", "Exchange with atmosphere and with room air.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableSolarHeat", new Entry(Solar, "Solar heat", "Sunlight on exposed faces, occlusion included.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "SolarTerrainRange", new Entry(Occlusion, "Terrain shadow range (m)", "How far along the sun ray the terrain walk looks, in metres; near ground is what shadows you.", 500f, 20000f) },
/// <summary>Entry operation.</summary>
            { "SolarOcclusionSamples", new Entry(Occlusion, "Shadow samples across the grid", "Points across the grid tested for shadow; more turn a terminator crossing into a ramp, and cost their share each.", 1, 9, true) },
/// <summary>Entry operation.</summary>
            { "EnableHeatSources", new Entry(Systems, "Point heat sources", "Heat from sources registered through the mod API.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableWasteHeat", new Entry(Systems, "Waste heat", "Power producers, consumers and thrusters turning throughput into heat.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnablePlanets", new Entry(Systems, "Planets", "Per-planet ambient, air and ground temperatures.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableFriction", new Entry(Aero, "Friction heating", "Atmospheric heating above the speed threshold: the share of the air's work on the hull that lands in the surface.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableWind", new Entry(Environment, "Wind", "The wind field and everything that shapes it; off is no wind anywhere, though a grid still feels its own motion.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableDamage", new Entry(Systems, "Overheat damage", "Blocks above their critical temperature take damage.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableCoolantLoops", new Entry(Systems, "Coolant loops", "Closed pipe rings acting as one fluid mass.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableRoomAir", new Entry(Systems, "Room air", "Sealed rooms hold an air mass that carries heat.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableHeatPumps", new Entry(Systems, "Heat pumps", "The block that moves heat up a gradient for an electrical cost.", 0, 1) },

/// <summary>Entry operation.</summary>
            { "WellMixedCoolant", new Entry(Systems, "Well-mixed coolant", "The cheaper transport rung: a loop's fluid as one well-mixed mass rather than parcels travelling round the ring.", 0, 1) },

/// <summary>Entry operation.</summary>
            { "EnableSuitDamage", new Entry(Suit, "Suit damage", "Heat as something that can hurt a player; off leaves the suit unsimulated and costs nothing.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "SuitConductance", new Entry(Suit, "Suit conductance (W/K)", "How well the outside reaches the occupant through a sealed suit, W/K.", 0f, 20f) },
/// <summary>Entry operation.</summary>
            { "SuitHeatCapacity", new Entry(Suit, "Suit heat capacity (J/K)", "Heat capacity of the occupant and suit together, J/K — about eighty kilograms of mostly water.", 10000f, 1000000f) },
/// <summary>Entry operation.</summary>
            { "SuitCoolingWatts", new Entry(Suit, "Suit cooling (W)", "Heat the suit can move either way, W, cooling a player in a hot room and warming one in a cold one.", 0f, 5000f) },
/// <summary>Entry operation.</summary>
            { "SuitCriticalTemperature", new Entry(Suit, "Hurts above (K)", "Interior temperature at which the occupant starts being hurt, K; 315.15 is 42 C.", 300f, 350f) },
/// <summary>Entry operation.</summary>
            { "SuitDamagePerKelvin", new Entry(Suit, "Damage per kelvin (hp/s)", "Hit points a second, per kelvin above the temperature that hurts.", 0f, 10f) },

/// <summary>Entry operation.</summary>
            { "FloorBlocksWhenOverBudget", new Entry(Solver, "Floor stiff blocks when over budget", "Over budget, floor the heat capacity of the blocks demanding most of it rather than shortening the step for everyone.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "ParallelGrids", new Entry(Solver, "Solve in parallel", "Solve a frame's grids across the engine's own workers: 10x on a 242-grid fleet, and nothing on one ship.", 0, 1) },

/// <summary>Entry operation.</summary>
            { "ShowEnvironmentReadout", new Entry(Display, "Environment readout", "One line, bottom centre: the air temperature around your ship, and one word for the ship against its own rating.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugOverlayMaxBoxes", new Entry(Display, "Overlay box budget per frame", "Boxes the block overlay may draw in one frame; past it it draws the part of the grid nearest the camera.", 0f, 50000f, true) },

/// <summary>Entry operation.</summary>
            { "PlanetUndergroundConvectionCoefficient", new Entry(Environment, "Buried convection (W/m2 K)", "Convective coefficient for a grid buried in rock, W/(m2 K), crossed over the first five metres of burial.", 0f, 50f) },

/// <summary>Entry operation.</summary>
            { "EnableTemperatureSync", new Entry(Multiplayer, "Replicate temperatures", "The server tells each client what its blocks are really at; off leaves every client guessing.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "TemperatureSyncInterval", new Entry(Multiplayer, "Update interval (s)", "Seconds between updates about the blocks near failing; the whole ship is stated once regardless.", 0.5f, 60f) },

/// <summary>Entry operation.</summary>
            { "MaxSubsteps", new Entry(Solver, "Substep ceiling per step", "Most substeps one step may divide itself into; reaching it is reported as a clamped step.", 1, 64, true) },
/// <summary>Entry operation.</summary>
            { "MaxSubstepsPerBlock", new Entry(Solver, "Substep cap per block", "Most substeps one block may demand before its heat capacity is floored; 0 leaves every block alone.", 0, 32, true) },
/// <summary>Entry operation.</summary>
            { "MaxElementVisitsPerStep", new Entry(Solver, "Work budget per step (visits)", "Most element visits one step may make before it is shortened to fit; 0 removes the bound.", 0, 8000000, true) },

/// <summary>Entry operation.</summary>
            { "DamageIsPerSecond", new Entry(Solver, "Overheat damage is per second", "Overheat damage scaled to real time rather than to the step.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "Frequency", new Entry(Solver, "Solver steps per simulated second", "Solver steps per second of simulated time. Higher is finer and costlier.", 1, 60, true) },
/// <summary>Entry operation.</summary>
            { "HeatTimeScale", new Entry(Solver, "Heat pace (physics seconds per second)", "Seconds of physical time per second of play: the dial that puts heat on a human scale.", 1f, 1000f) },

/// <summary>Entry operation.</summary>
            { "LoopRefillEquivalentKelvin", new Entry(Systems, "Refill price (K above ambient)", "What a coolant refill is priced at: the temperature at which venting and refilling exactly breaks even.", 0f, 600f) },
/// <summary>Entry operation.</summary>
            { "LoopRefillKilogramsPerSecond", new Entry(Systems, "Refill rate (kg/s)", "How fast a vented coolant loop comes back, kg/s — venting is instant and refilling is not.", 0f, 200f) },
/// <summary>Entry operation.</summary>
            { "LoopCoolantKilogramsPerCubicMetre", new Entry(Systems, "Coolant density (kg/m3)", "Coolant per cubic metre of the cell a pipe occupies, kg/m3; more is more capacity for the same coupling.", 0f, 200f) },
/// <summary>Entry operation.</summary>
            { "LoopSpecificHeat", new Entry(Systems, "Coolant specific heat (J/kg K)", "J/(kg K). Water-glycol is about 3400, which is what the shipped fluid is.", 100f, 6000f) },
/// <summary>Entry operation.</summary>
            { "LoopHeatTransferCoefficient", new Entry(Systems, "Fluid-to-wall transfer (W/m2 K)", "How well heat crosses between the fluid and the wall it touches while the pump is running, W/(m2 K).", 0f, 2000f) },
/// <summary>Entry operation.</summary>
            { "LoopStagnantTransferFraction", new Entry(Systems, "Transfer with the pump stopped (0..1)", "What a stopped ring still carries across the fluid-to-wall joint, as a share of the coefficient above.", 0f, 1f) },

/// <summary>Entry operation.</summary>
            { "PlanetDayTemperature", new Entry(Environment, "Day temperature (K)", "Air temperature at the equator at noon, K.", 100f, 400f) },
/// <summary>Entry operation.</summary>
            { "PlanetNightTemperature", new Entry(Environment, "Night temperature (K)", "Air temperature at the equator at midnight, K.", 100f, 400f) },
/// <summary>Entry operation.</summary>
            { "PlanetPoleTemperatureDrop", new Entry(Environment, "Colder at the poles (K)", "How much colder a pole is than the equator, K.", 0f, 100f) },
/// <summary>Entry operation.</summary>
            { "PlanetAmbientLapseRate", new Entry(Environment, "Colder with altitude (K/km)", "How much colder the air gets with altitude, K per km; Earth is about 6.5.", 0f, 12f) },
/// <summary>Entry operation.</summary>
            { "PlanetAmbientLagSeconds", new Entry(Environment, "Air lag behind the sun (s)", "Seconds the air takes to chase its target, which is what puts the day's peak after noon.", 0f, 600f) },
/// <summary>Entry operation.</summary>
            { "PlanetConvectionCoefficient", new Entry(Environment, "Air convection at sea level (W/m2 K)", "Convective coefficient at sea level, W/(m2 K), before the atmosphere blend thins it.", 0f, 200f) },
/// <summary>Entry operation.</summary>
            { "PlanetSolarDecay", new Entry(Environment, "Sunlight absorbed by air (0..1)", "How much of the sun a full atmosphere absorbs, 0..1.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "PlanetUndergroundTemperature", new Entry(Environment, "Underground temperature (K)", "Rock temperature below the damping depth, K.", 100f, 400f) },
/// <summary>Entry operation.</summary>
            { "PlanetUndergroundDampingDepth", new Entry(Environment, "Day-night reach underground (m)", "Metres over which the day-night swing dies out underground.", 1f, 200f) },
/// <summary>Entry operation.</summary>
            { "PlanetCoreTemperature", new Entry(Environment, "Core temperature (K)", "Rock temperature the model warms toward below the sea-level deadzone, K.", 300f, 6000f) },
/// <summary>Entry operation.</summary>
            { "PlanetSealevelDeadzone", new Entry(Environment, "Depth before core warming (m)", "Metres below sea level before the rock starts warming toward the core.", 0f, 4000f) },

/// <summary>Entry operation.</summary>
            { "ClimateGroundInfluence", new Entry(Environment, "Ground shifts air temperature (0..1)", "How much the ground a grid is parked on shifts the air above it; 0 ignores what the ground is made of.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "ClimateWeatherInfluence", new Entry(Environment, "Weather shifts air temperature (0..1)", "How much the weather changes the air around a grid; 1 applies the game's own figures in full.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "VacuumTemperature", new Entry(Environment, "Vacuum temperature (K)", "Sky temperature in space, K. 2.7 is the real background.", 0f, 300f) },
/// <summary>Entry operation.</summary>
            { "SolarEnergy", new Entry(Solar, "Sunlight at the planet (W/m2)", "Irradiance at the planet, W/m2.", 0f, 5000f) },
/// <summary>Entry operation.</summary>
            { "FrictionAtSpeedsAbove", new Entry(Aero, "Aero term floor (m/s)", "Relative airspeed below which the whole aerodynamic term - heating, drag and lift - is off. 0 applies it at every speed; the cooling-to-heating crossover emerges on its own.", 0f, 300f) },
/// <summary>Entry operation.</summary>
            { "FrictionScale", new Entry(Aero, "Friction heating scale", "Multiplier on the v3 heating term, so moving it retunes temperatures and leaves handling alone.", 0f, 0.01f) },

/// <summary>Entry operation.</summary>
            { "EnableDrag", new Entry(Aero, "Apply drag", "Takes the drag the friction term already computes out of the ship's motion; off, so an aerodynamics mod is not doubled.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DragCoefficient", new Entry(Aero, "Drag coefficient", "The coefficient a hull is treated as having; 0.5 is measured, and authored rather than read off the shape.", 0f, 2f) },
/// <summary>Entry operation.</summary>
            { "EnableShapeDrag", new Entry(Aero, "Correct area for hull shape", "Corrects the projected area for which way the hull actually faces, which moves temperatures as well as handling.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "EnableLift", new Entry(Aero, "Lift", "Applies the aerodynamic force across the airflow rather than along it; needs Apply drag AND Hull shape both on, and is small on real ships.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "LiftCoefficient", new Entry(Aero, "Lift coefficient", "How much of the computed transverse force is applied; 1 is the model's own answer.", 0f, 2f) },
/// <summary>Entry operation.</summary>
            { "EnableWindwardShielding", new Entry(Aero, "Shelter blocks behind others", "A block behind another is sheltered from the wind, for heat and for drag, at a second sliced pass over the hull.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "RoomConvectionCoefficient", new Entry(Environment, "Room air convection (W/m2 K)", "Convective coefficient between a block and room air, W/(m2 K).", 0f, 50f) },
/// <summary>Entry operation.</summary>
            { "RoomAirDensity", new Entry(Environment, "Room air density (kg/m3)", "Density of room air, kg/m3. 1.225 is sea level.", 0f, 5f) },
/// <summary>Entry operation.</summary>
            { "SolarOcclusionInterval", new Entry(Occlusion, "Shadow recheck interval (steps)", "Solver steps between sun occlusion raycasts.", 1, 60, true) },

/// <summary>Entry operation.</summary>
            { "WindRoughnessLength", new Entry(Environment, "Ground roughness (m)", "Height at which wind theoretically reaches zero, m: 0.0002 open water, 0.03 grassland, 0.5 forest.", 0.0001f, 2f) },
/// <summary>Entry operation.</summary>
            { "WindGradientHeight", new Entry(Environment, "Wind stops rising above (m)", "Height at which wind stops strengthening, m, above which the ground no longer sets it.", 10f, 3000f) },
/// <summary>Entry operation.</summary>
            { "WindDiurnalAmplitude", new Entry(Environment, "Daily wind swing (0..1)", "How far the daily cycle moves wind either side of its mean, 0..1.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "WindDiurnalCrossover", new Entry(Environment, "Daily cycle vanishes at (m)", "Height at which the daily cycle vanishes, m: the surface cycle below, the nocturnal jet above.", 0f, 500f) },
/// <summary>Entry operation.</summary>
            { "WindTerrainInfluence", new Entry(Environment, "Terrain steers the wind (0..1)", "How much the shape of the ground steers and speeds the wind, 0..1.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "WindSlopeStrength", new Entry(Environment, "Slope winds (0..1)", "Air running up a mountain by day and draining back down it at night, 0..1.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "WindTerrainRadius", new Entry(Environment, "Terrain read radius (m)", "How far out the land around a point is read, m: the size of landform the wind notices.", 50f, 2000f) },

/// <summary>Entry operation.</summary>
            { "HeatPumpCarnotFraction", new Entry(Systems, "Pump efficiency (share of Carnot)", "How much of the Carnot limit a pump achieves, 0..1.", 0f, 1f) },
/// <summary>Entry operation.</summary>
            { "HeatPumpMaxCoefficient", new Entry(Systems, "Pump coefficient ceiling", "Ceiling on the coefficient of performance.", 0f, 20f) },

/// <summary>Entry operation.</summary>
            { "HeatGlow", new Entry(Display, "Blocks glow when hot", "A block glows over the last 100 K before its own critical temperature, in the colour a body that hot really is.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "HeatWarningSound", new Entry(Display, "Overheat cue", "A cue in the cockpit as a block comes up on its rating and as it passes it, heard only at the controls.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "HeatTerminalPanel", new Entry(Display, "Terminal readout", "The thermal panel in a block's terminal detail pane.", 0, 1) },

/// <summary>Entry operation.</summary>
            { "DebugTextOnScreen", new Entry(Display, "Crosshair readout", "Everything the simulation knows about the block being looked at; also records per-mechanism watts.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugSolarRaycast", new Entry(Display, "Draw sun ray", "The sun ray from each grid, white when lit and red when occluded.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugWindRaycast", new Entry(Display, "Draw wind vector", "The relative wind each grid is flying through, drawn from the grid.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugAeroOverlay", new Entry(Display, "Aero debug view", "The centre of mass, the drag, lift and wind vectors on your ship, and the name of whichever switch is stopping a force from applying.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugWindOverlay", new Entry(Display, "Wind map", "Draws the wind field as arrows: 1 a lattice around you, 2 the whole planet. Ctrl+Shift+W cycles it.", 0, WindOverlay.ModeCount - 1, true) },
/// <summary>Entry operation.</summary>
            { "DebugWindIndicator", new Entry(Display, "Wind indicator", "A needle and a speed beside the crosshair whenever there is wind where you are.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "DebugBlockOverlay", new Entry(Display, "Block overlay", "The x-ray box overlay. Ctrl+Shift+= cycles it in play.", 0, ThermalDebugView.ModeCount - 1, true) },

/// <summary>Entry operation.</summary>
            { "RoomOverlayMinKelvin", new Entry(Display, "Room overlay cold end (K)", "Bottom of the room view's colour span, K, which is a tighter ramp than blocks get.", 173.15f, 323.15f) },
/// <summary>Entry operation.</summary>
            { "RoomOverlayMaxKelvin", new Entry(Display, "Room overlay hot end (K)", "Top of the room view's colour span, K.", 273.15f, 423.15f) },
/// <summary>Entry operation.</summary>
            { "EnableTelemetry", new Entry(Display, "Collect telemetry", "Per-grid and per-block-type data collection. Off for ordinary play.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "TelemetryPlanetProbes", new Entry(Display, "Wind probe interval (steps)", "Solver steps between planet-wide wind sweeps, or 0 for none; needs telemetry on.", 0, 3600, true) },
/// <summary>Entry operation.</summary>
            { "EnableTopSpeed", new Entry(Aero, "Mass sets top speed", "Raises the world's speed cap and holds each ship under a cruise speed that falls with its mass, by a force rather than a limit.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "SpeedLimit", new Entry(Aero, "World speed limit (m/s)", "The ceiling no ship passes whatever its mass or its boost, written into the world's own environment definition.", 20f, 1000f) },
/// <summary>Entry operation.</summary>
            { "EnableSpeedBoost", new Entry(Aero, "Allow boosting past cruise", "Lets thrust push a ship past its cruise speed, up to the boost speed ceiling, and drags it back toward cruise. Off, a ship cannot pass its cruise speed at all.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "LargeGridMinCruise", new Entry(Aero, "Large: cruise, light (m/s)", "Cruise speed of a large grid at or below the light mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMidCruise", new Entry(Aero, "Large: cruise, middle (m/s)", "Cruise speed of a large grid at the middle mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMaxCruise", new Entry(Aero, "Large: cruise, heavy (m/s)", "Cruise speed of a large grid at or above the heavy mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMinMass", new Entry(Aero, "Large: light mass (kg)", "Mass below which a large grid holds its light cruise speed.", 0f, 2000000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMidMass", new Entry(Aero, "Large: middle mass (kg)", "The middle point of the large-grid curve, where the middle cruise speed applies.", 0f, 20000000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMaxMass", new Entry(Aero, "Large: heavy mass (kg)", "Mass above which a large grid holds its heavy cruise speed.", 0f, 40000000f) },
/// <summary>Entry operation.</summary>
            { "LargeGridResistance", new Entry(Aero, "Large: resistance (x)", "How hard a large grid is held to its cruise speed. Higher is a firmer hold.", 0f, 10f) },
/// <summary>Entry operation.</summary>
            { "LargeGridMaxBoostSpeed", new Entry(Aero, "Large: boost speed ceiling (m/s)", "The fastest a boosting large grid may travel. It is a speed, not a force: the resistance drags the ship back toward cruise and this is the speed the engine clamps it to on the way. Inert above the world speed limit.", 0f, 1000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMinCruise", new Entry(Aero, "Small: cruise, light (m/s)", "Cruise speed of a small grid at or below the light mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMidCruise", new Entry(Aero, "Small: cruise, middle (m/s)", "Cruise speed of a small grid at the middle mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMaxCruise", new Entry(Aero, "Small: cruise, heavy (m/s)", "Cruise speed of a small grid at or above the heavy mass below.", 10f, 1000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMinMass", new Entry(Aero, "Small: light mass (kg)", "Mass below which a small grid holds its light cruise speed.", 0f, 100000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMidMass", new Entry(Aero, "Small: middle mass (kg)", "The middle point of the small-grid curve, where the middle cruise speed applies.", 0f, 1000000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMaxMass", new Entry(Aero, "Small: heavy mass (kg)", "Mass above which a small grid holds its heavy cruise speed.", 0f, 2000000f) },
/// <summary>Entry operation.</summary>
            { "SmallGridResistance", new Entry(Aero, "Small: resistance (x)", "How hard a small grid is held to its cruise speed. Higher is a firmer hold.", 0f, 10f) },
/// <summary>Entry operation.</summary>
            { "SmallGridMaxBoostSpeed", new Entry(Aero, "Small: boost speed ceiling (m/s)", "The fastest a boosting small grid may travel. It is a speed, not a force: the resistance drags the ship back toward cruise and this is the speed the engine clamps it to on the way. Inert above the world speed limit.", 0f, 1000f) },
/// <summary>Entry operation.</summary>
            { "ShadowDetail", new Entry(Solar, "Shadow detail", "How much work a shadow is worth: none, planets only, the world around the ship, or everything including other grids.", 0, 3, true) },
/// <summary>Entry operation.</summary>
            { "ClampOvershoot", new Entry(Solver, "Clamp overshoot", "Stops a substep carrying a block past a neighbour's temperature or past ambient. Leave on.", 0, 1) },
/// <summary>Entry operation.</summary>
            { "LoopFlowRate", new Entry(Systems, "Coolant flow (m/s)", "How fast coolant moves with one pump at full speed; flow rises with the square root of combined pumping.", 0f, 40f) },
/// <summary>Entry operation.</summary>
            { "LoopContactMultiplier", new Entry(Systems, "Coolant coupling (x)", "Scales the coupling between the fluid and the metal it touches, at the pipe wall and the sink face alike.", 0f, 5f) },
/// <summary>Entry operation.</summary>
            { "TelemetrySampleStride", new Entry(Display, "Telemetry sample stride (steps)", "Steps between telemetry samples.", 1, 64, true) },
        };

        private static HashSet<string> ClientSide
        {
            get { return Settings.ClientOwned; }
        }

        private static bool initialised;

        private static ThermalSettingsWindow window;

        private static Settings shipped;

/// <summary>Initialize operation.</summary>
        public static void Initialize()
        {
            if (initialised) return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated) return;

            initialised = true;

            MyLog.Default.Info("[" + Settings.Name + "] requesting Rich HUD registration");
            RichHudClient.Init(Settings.Name, OnRegistered, OnReset);
        }

/// <summary>Tick operation.</summary>
        public static void Tick()
        {
            if (window == null) return;

            if (++framesSinceStatistics < StatisticsFrames) return;
            framesSinceStatistics = 0;

            if (!window.IsOpen) return;

            Refresh();
        }

        private const int StatisticsFrames = 30;

        private static int framesSinceStatistics;

/// <summary>Open operation.</summary>
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

/// <summary>Toggle operation.</summary>
        public static void Toggle()
        {
            if (window != null && window.IsOpen)
            {
                window.Hide();
                return;
            }

            Open();
        }

/// <summary>OnRegistered operation.</summary>
        private static void OnRegistered()
        {
            MyLog.Default.Info("[" + Settings.Name + "] Rich HUD registered; building menu and readout");

            Build();
            ThermalDebugPanel.Build();
            ThermalHud.Build();
            ThermalVisionProbe.Build();
        }

/// <summary>OnReset operation.</summary>
        private static void OnReset()
        {
            window = null;
            ThermalDebugPanel.Reset();
            ThermalHud.Reset();
            ThermalVisionProbe.ResetHud();
        }

        private struct Leaf
        {
            public string Name;
            public string[] Settings;

            public string[] Advanced;

/// <summary>Leaf operation.</summary>
            public Leaf(string name, params string[] settings)
            {
                Name = name;
                Settings = settings;
                Advanced = Empty;
            }

/// <summary>Then operation.</summary>
            public Leaf Then(params string[] advanced)
            {
                Leaf copy = this;
                copy.Advanced = advanced;
                return copy;
            }

            private static readonly string[] Empty = new string[0];
        }

        private struct Folder
        {
            public string Name;
            public Leaf[] Pages;

/// <summary>Folder operation.</summary>
            public Folder(string name, params Leaf[] pages)
            {
                Name = name;
                Pages = pages;
            }
        }

        private static readonly Folder[] Folders =
        {
/// <summary>Folder operation.</summary>
            new Folder("Solver",
/// <summary>Leaf operation.</summary>
                new Leaf("Cost limits",
                    "MaxSubsteps", "MaxElementVisitsPerStep")
                    .Then("MaxSubstepsPerBlock", "FloorBlocksWhenOverBudget",
                        "ClampOvershoot", "ParallelGrids"),
/// <summary>Leaf operation.</summary>
                new Leaf("Pace",
                    "Frequency", "HeatTimeScale")),

/// <summary>Folder operation.</summary>
            new Folder("Heat transfer",
/// <summary>Leaf operation.</summary>
                new Leaf("Ambient exchange",
                    "EnableEnvironment", "EnableConduction", "EnableRadiation", "EnableConvection",
                    "VacuumTemperature"),

/// <summary>Leaf operation.</summary>
                new Leaf("Sunlight",
                    "EnableSolarHeat", "SolarEnergy", "ShadowDetail")
                    .Then("SolarTerrainRange", "SolarOcclusionSamples", "SolarOcclusionInterval")),

/// <summary>Folder operation.</summary>
            new Folder("Ship systems",
/// <summary>Leaf operation.</summary>
                new Leaf("Coolant loops",
                    "EnableCoolantLoops", "LoopFlowRate", "LoopHeatTransferCoefficient",
                    "LoopRefillEquivalentKelvin", "LoopRefillKilogramsPerSecond")
                    .Then("WellMixedCoolant", "LoopCoolantKilogramsPerCubicMetre",
                        "LoopSpecificHeat", "LoopContactMultiplier",
                        "LoopStagnantTransferFraction"),
/// <summary>Leaf operation.</summary>
                new Leaf("Heat pumps",
                    "EnableHeatPumps")
                    .Then("HeatPumpCarnotFraction", "HeatPumpMaxCoefficient"),
/// <summary>Leaf operation.</summary>
                new Leaf("Room air",
                    "EnableRoomAir")
                    .Then("RoomConvectionCoefficient", "RoomAirDensity"),

/// <summary>Leaf operation.</summary>
                new Leaf("Heat made and damage",
                    "EnableWasteHeat", "EnableHeatSources", "EnableDamage", "DamageIsPerSecond"),

/// <summary>Leaf operation.</summary>
                new Leaf("Suit",
                    "EnableSuitDamage", "SuitCriticalTemperature", "SuitCoolingWatts")
                    .Then("SuitConductance", "SuitHeatCapacity", "SuitDamagePerKelvin")),

/// <summary>Folder operation.</summary>
            new Folder("Aerodynamics",
/// <summary>Leaf operation.</summary>
                new Leaf("Friction heating",
                    "EnableFriction")
                    .Then("FrictionAtSpeedsAbove", "FrictionScale"),
/// <summary>Leaf operation.</summary>
                new Leaf("Top speed",
                    "EnableTopSpeed", "SpeedLimit", "EnableSpeedBoost")
                    .Then("LargeGridMinCruise", "LargeGridMidCruise", "LargeGridMaxCruise",
                        "LargeGridMinMass", "LargeGridMidMass", "LargeGridMaxMass",
                        "LargeGridResistance", "LargeGridMaxBoostSpeed",
                        "SmallGridMinCruise", "SmallGridMidCruise", "SmallGridMaxCruise",
                        "SmallGridMinMass", "SmallGridMidMass", "SmallGridMaxMass",
                        "SmallGridResistance", "SmallGridMaxBoostSpeed"),
/// <summary>Leaf operation.</summary>
                new Leaf("Drag and lift",
                    "EnableDrag", "DragCoefficient", "EnableLift")
                    .Then("EnableShapeDrag", "EnableWindwardShielding", "LiftCoefficient")),

/// <summary>Folder operation.</summary>
            new Folder("Multiplayer",
/// <summary>Leaf operation.</summary>
                new Leaf("Temperatures",
                    "EnableTemperatureSync", "TemperatureSyncInterval")),

/// <summary>Folder operation.</summary>
            new Folder("World",
/// <summary>Leaf operation.</summary>
                new Leaf("Climate",
                    "EnablePlanets", "PlanetDayTemperature", "PlanetNightTemperature",
                    "ClimateGroundInfluence", "ClimateWeatherInfluence")
                    .Then("PlanetPoleTemperatureDrop", "PlanetAmbientLapseRate",
                        "PlanetAmbientLagSeconds", "PlanetConvectionCoefficient",
                        "PlanetSolarDecay"),
/// <summary>Leaf operation.</summary>
                new Leaf("Underground",
                    "PlanetUndergroundTemperature", "PlanetUndergroundDampingDepth")
                    .Then("PlanetUndergroundConvectionCoefficient", "PlanetCoreTemperature",
                        "PlanetSealevelDeadzone"),

/// <summary>Leaf operation.</summary>
                new Leaf("Wind",
                    "EnableWind", "WindGradientHeight", "WindTerrainInfluence")
                    .Then("WindRoughnessLength", "WindDiurnalAmplitude", "WindDiurnalCrossover",
                        "WindTerrainRadius", "WindSlopeStrength")),
        };

        private static readonly Dictionary<string, string> PageNotes = new Dictionary<string, string>
        {
            { "Ambient exchange", "Conductivity, emissivity and exposed area are per block, from Cubes.xml" },
            { "Heat made and damage", "How much each block wastes is in Cubes.xml; point sources come through the API" },
        };

/// <summary>Leaf operation.</summary>
        private static readonly Leaf DebugPage = new Leaf("Debug",
                "HeatGlow", "HeatWarningSound", "HeatTerminalPanel", "ShowEnvironmentReadout",
                "DebugBlockOverlay", "DebugTextOnScreen", "DebugWindOverlay", "DebugWindIndicator")
            .Then("DebugSolarRaycast", "DebugWindRaycast", "DebugAeroOverlay", "DebugOverlayMaxBoxes",
                "RoomOverlayMinKelvin", "RoomOverlayMaxKelvin",
                "EnableTelemetry", "TelemetrySampleStride", "TelemetryPlanetProbes");

/// <summary>Builds the API method table.</summary>
        private static void Build()
        {
            bool editable = MyAPIGateway.Session == null
                || MyAPIGateway.Session.IsServer
                || SettingsRequests.MayAsk;

            bool local = MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer;

            if (shipped == null) shipped = Settings.GetDefaults();

/// <summary>ThermalSettingsWindow operation.</summary>
            window = new ThermalSettingsWindow(HudMain.HighDpiRoot);

            window.AddStatisticsPage();

/// <summary>HashSet operation.</summary>
            HashSet<string> placed = new HashSet<string>();

            AddPage(DebugPage, editable, placed, false);
            window.AddDefaultsPage(local, RestoreDefaults);

/// <summary>Unplaced operation.</summary>
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

/// <summary>Builds the API method table.</summary>
        private static void BuildTerminalEntry()
        {
            RichHudTerminal.Root.Enabled = true;

            TerminalButton button = new TerminalButton
            {
                Name = "Open the settings",
                ToolTip = new ToolTip
                {
/// <summary>RichText operation.</summary>
                    text = new RichText("Closes this menu and opens the Thermodynamics settings"
                        + " window, which is where every setting this mod has lives."
                        + "\n\nCtrl+Shift+S opens the same window at any time, and closes it"
                        + " again; so does /thermal menu."),
                },
            };

            button.ControlChangedHandler = (sender, args) =>
            {
                RichHudTerminal.CloseMenu();
                Open();
            };

/// <summary>ControlTile operation.</summary>
            ControlTile tile = new ControlTile();
            tile.Add(button);

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

/// <summary>Adds a page.</summary>
        private static void AddPage(Leaf leaf, bool editable, HashSet<string> placed, bool indented)
        {
/// <summary>Take operation.</summary>
            List<string> members = Take(leaf.Settings, placed);
/// <summary>Take operation.</summary>
            List<string> advanced = Take(leaf.Advanced, placed);

            string note;
            PageNotes.TryGetValue(leaf.Name, out note);

/// <summary>List operation.</summary>
            List<string> both = new List<string>(members);
            both.AddRange(advanced);

            window.AddPage(leaf.Name, Subheader(leaf.Name, both, editable), members, advanced,
                editable, note, indented);
        }

/// <summary>Take operation.</summary>
        private static List<string> Take(string[] names, HashSet<string> placed)
        {
/// <summary>List operation.</summary>
            List<string> taken = new List<string>();

            for (int i = 0; i < names.Length; i++)
            {
                if (placed.Contains(names[i])) continue;

                placed.Add(names[i]);
                taken.Add(names[i]);
            }

            return taken;
        }

/// <summary>Unplaced operation.</summary>
        private static List<string> Unplaced()
        {
/// <summary>HashSet operation.</summary>
            HashSet<string> named = new HashSet<string>();

            for (int f = 0; f < Folders.Length; f++)
            {
                for (int p = 0; p < Folders[f].Pages.Length; p++)
                {
                    Leaf page = Folders[f].Pages[p];

                    for (int i = 0; i < page.Settings.Length; i++) named.Add(page.Settings[i]);
                    for (int i = 0; i < page.Advanced.Length; i++) named.Add(page.Advanced[i]);
                }
            }

            for (int i = 0; i < DebugPage.Settings.Length; i++) named.Add(DebugPage.Settings[i]);
            for (int i = 0; i < DebugPage.Advanced.Length; i++) named.Add(DebugPage.Advanced[i]);

/// <summary>List operation.</summary>
            List<string> missing = new List<string>();
            List<string> names = Settings.Names();

            for (int i = 0; i < names.Count; i++)
            {
                if (!named.Contains(names[i])) missing.Add(names[i]);
            }

            return missing;
        }

/// <summary>RestoreDefaults operation.</summary>
        private static void RestoreDefaults()
        {
            Settings.Instance.RestoreDefaults();
            Refresh();

            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: every world setting back to its shipped value", 3000, "White");
        }

/// <summary>Refresh operation.</summary>
        public static void Refresh()
        {
            Refresh(true);
        }

/// <summary>Refresh operation.</summary>
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

/// <summary>Watts operation.</summary>
        private static string Watts(float watts)
        {
            return Units.Watts(watts);
        }

/// <summary>Changed operation.</summary>
        internal static bool Changed(string name)
        {
            float mine = Settings.Instance.GetValue(name);
            float theirs = shipped.GetValue(name);

            float difference = mine - theirs;
            if (difference < 0f) difference = -difference;

            float scale = theirs < 0f ? -theirs : theirs;
            return difference > 0.0001f * (scale < 1f ? 1f : scale);
        }

/// <summary>Label operation.</summary>
        internal static string Label(string name, bool moved)
        {
/// <summary>EntryFor operation.</summary>
            string label = EntryFor(name).Label;
            return moved ? "• " + label : label;
        }

/// <summary>StatisticsText operation.</summary>
        private static string StatisticsText(int changed, List<string> names)
        {
/// <summary>StringBuilder operation.</summary>
            StringBuilder text = new StringBuilder();

            WriteWorld(text, changed, names);
            WriteLive(text);
            WriteChanged(text, names);

            return text.ToString();
        }

/// <summary>WriteWorld operation.</summary>
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

/// <summary>Warning operation.</summary>
            string warning = Warning();
            if (warning.Length > 0) text.Append("\nWorth knowing: ").Append(warning).Append('\n');

            text.Append("\nCompare the digest with the server's /thermal sync to tell a settings"
                + " disagreement from a simulation one.\n\n");
        }

/// <summary>WriteLive operation.</summary>
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

/// <summary>WriteFrameTime operation.</summary>
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

/// <summary>WriteChanged operation.</summary>
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

/// <summary>Row operation.</summary>
        private static void Row(StringBuilder text, string label, string value)
        {
            text.Append("    ").Append(label);

            for (int i = label.Length; i < RowLabelWidth; i++) text.Append(' ');

            text.Append("  ").Append(value).Append('\n');
        }

        private const int RowLabelWidth = 26;

/// <summary>Warning operation.</summary>
        private static string Warning()
        {
            Settings s = Settings.Instance;

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

/// <summary>Subheader operation.</summary>
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
                return editable
                    ? "World settings; your changes are sent to the server"
                    : "World settings; read only on a client";
            }

            string note;
            return SectionNotes.TryGetValue(section, out note) ? note : "";
        }

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

        internal static readonly string[] ShadowDetailNames =
        {
            "none", "planets", "the world", "everything",
        };

/// <summary>OverlayNames operation.</summary>
        internal static string[] OverlayNames()
        {
            string[] names = new string[ThermalDebugView.ModeCount];

            for (int mode = 0; mode < names.Length; mode++)
            {
                names[mode] = ThermalDebugView.Describe((ThermalDebugView.Mode)mode);
            }

            return names;
        }

/// <summary>TipFor operation.</summary>
        internal static ToolTip TipFor(string name, Entry entry)
        {
            string text = name == "DebugBlockOverlay"
                ? entry.Tip
                : entry.Tip + FidelityEnds.Sentence(name);

            if (NeedsTyping(entry))
            {
/// <summary>Number operation.</summary>
                text += "\n\nTyped; usual values run from " + Number(entry.Min, entry) + " to "
/// <summary>Number operation.</summary>
                    + Number(entry.Max, entry) + ".";
            }

            return new ToolTip { text = new RichText(text) };
        }

/// <summary>MayOffer operation.</summary>
        internal static bool MayOffer(string name, bool editable)
        {
            return editable || ClientSide.Contains(name);
        }

/// <summary>Write operation.</summary>
        internal static void Write(string name, float value)
        {
            if (!CanEdit(name)) return;

            if (SettingsRequests.MustAsk && !Settings.ClientOwned.Contains(name))
            {
                SettingsRequests.Send(name, value);
                return;
            }

            Settings.Instance.SetValue(name, value);
            Settings.Instance.Apply();
            Refresh(false);
        }

/// <summary>CanEdit operation.</summary>
        private static bool CanEdit(string name)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer) return true;
            if (ClientSide.Contains(name)) return true;

            return SettingsRequests.MayAsk;
        }

/// <summary>NeedsTyping operation.</summary>
        internal static bool NeedsTyping(Entry entry)
        {
            return (entry.Max - entry.Min) > 200f || entry.Max <= 0.1f;
        }

/// <summary>Number operation.</summary>
        internal static string Number(float value, Entry entry)
        {
            return entry.Integer
                ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.####", CultureInfo.InvariantCulture);
        }

/// <summary>ValueText operation.</summary>
        internal static string ValueText(float value, Entry entry)
        {
            if (entry.Integer) return ((int)Math.Round(value)).ToString();
            return value.ToString(entry.Max <= 0.1f ? "n4" : "n2");
        }

/// <summary>TryParse operation.</summary>
        internal static bool TryParse(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

/// <summary>EntryFor operation.</summary>
        internal static Entry EntryFor(string name)
        {
            Entry entry;
            if (Layout.TryGetValue(name, out entry)) return entry;

            return new Entry(Other, name, "Not yet described in the menu's layout table.", 0f, 1000f);
        }
    }
}
