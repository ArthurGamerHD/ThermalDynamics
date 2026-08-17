using System;
using System.Collections.Generic;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;

namespace Thermodynamics
{
    /// <summary>
    /// The settings menu, built on the Rich HUD Framework.
    ///
    /// Every value in the config file is reachable here, because the menu is generated from
    /// <see cref="Settings.Names"/> rather than written out by hand: a setting added to the config
    /// appears in the menu without anyone remembering to add it. A name the layout table below does
    /// not mention still gets a control, in an "Other" category, which is the failure mode worth
    /// having — a stray control rather than a setting that quietly cannot be edited.
    ///
    /// Editing follows the same rule as <c>/thermal set</c>: the config is server side, so on a
    /// multiplayer client every simulation control is disabled and only the client-side
    /// presentation switches can be touched. Nothing here writes to disk until Save is pressed.
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

        private const string Mechanisms = "Mechanisms";
        private const string Solver = "Solver";
        private const string Environment = "Environment";
        private const string HeatPumps = "Heat pumps";
        private const string Presentation = "Presentation";
        private const string TelemetrySection = "Telemetry";
        private const string Other = "Other";

        /// <summary>Categories in the order they are laid out.</summary>
        private static readonly string[] Order =
        {
            Mechanisms, Solver, Environment, HeatPumps, Presentation, TelemetrySection, Other,
        };

        /// <summary>
        /// Label, tooltip and slider range per setting. Switches ignore the range. A range is a
        /// judgement about what is worth dragging to, not a limit: the chat command and the mod API
        /// still take any value the clamp accepts.
        /// </summary>
        private static readonly Dictionary<string, Entry> Layout = new Dictionary<string, Entry>
        {
            { "EnableEnvironment", new Entry(Mechanisms, "Environment", "Ambient exchange with air, ground and space. Off leaves only internal heat flow.", 0, 1) },
            { "EnableConduction", new Entry(Mechanisms, "Conduction", "Heat flow between touching blocks.", 0, 1) },
            { "EnableRadiation", new Entry(Mechanisms, "Radiation", "Radiative exchange with the sky from exposed faces.", 0, 1) },
            { "EnableConvection", new Entry(Mechanisms, "Convection", "Exchange with atmosphere and with room air.", 0, 1) },
            { "EnableSolarHeat", new Entry(Mechanisms, "Solar heat", "Sunlight on exposed faces, occlusion included.", 0, 1) },
            { "EnableHeatSources", new Entry(Mechanisms, "Point heat sources", "Heat from sources registered through the mod API.", 0, 1) },
            { "EnableWasteHeat", new Entry(Mechanisms, "Waste heat", "Power producers, consumers and thrusters turning throughput into heat.", 0, 1) },
            { "EnablePlanets", new Entry(Mechanisms, "Planets", "Per-planet ambient, air and ground temperatures.", 0, 1) },
            { "EnableFriction", new Entry(Mechanisms, "Friction", "Atmospheric heating above the speed threshold.", 0, 1) },
            { "EnableDamage", new Entry(Mechanisms, "Overheat damage", "Blocks above their critical temperature take damage.", 0, 1) },
            { "EnableCoolantLoops", new Entry(Mechanisms, "Coolant loops", "Closed pipe rings acting as one fluid mass.", 0, 1) },
            { "EnableRoomAir", new Entry(Mechanisms, "Room air", "Sealed rooms hold an air mass that carries heat.", 0, 1) },
            { "EnableHeatPumps", new Entry(Mechanisms, "Heat pumps", "The block that moves heat up a gradient for an electrical cost.", 0, 1) },

            { "ClampConductionOvershoot", new Entry(Solver, "Clamp conduction overshoot", "Stops a step from pushing two blocks past each other's temperature. Leave on.", 0, 1) },
            { "DamageIsPerSecond", new Entry(Solver, "Damage is per second", "Overheat damage scaled to real time rather than to the step.", 0, 1) },
            { "Frequency", new Entry(Solver, "Frequency", "Solver steps per second of simulated time. Higher is finer and costlier.", 1, 60, true) },
            { "SimulationSpeed", new Entry(Solver, "Simulation speed", "Multiplier on how fast heat moves. 1 is the tuned pace.", 0.1f, 10f) },
            { "HeatTimeScale", new Entry(Solver, "Heat time scale", "Seconds of physical time per second of play. The dial that makes heat happen on a human scale.", 1f, 1000f) },

            { "VacuumTemperature", new Entry(Environment, "Vacuum temperature", "Sky temperature in space, K. 2.7 is the real background.", 0f, 300f) },
            { "SolarEnergy", new Entry(Environment, "Solar energy", "Irradiance at the planet, W/m2.", 0f, 5000f) },
            { "FrictionAtSpeedsAbove", new Entry(Environment, "Friction above", "Speed at which atmospheric friction starts, m/s.", 0f, 300f) },
            { "FrictionScale", new Entry(Environment, "Friction scale", "Multiplier on friction heating.", 0f, 0.01f) },
            { "RoomConvectionCoefficient", new Entry(Environment, "Room convection", "Convective coefficient between a block and room air, W/(m2 K).", 0f, 50f) },
            { "RoomAirDensity", new Entry(Environment, "Room air density", "Density of room air, kg/m3. 1.225 is sea level.", 0f, 5f) },
            { "SolarOcclusionInterval", new Entry(Environment, "Occlusion interval", "Solver steps between sun occlusion raycasts.", 1, 60, true) },

            { "HeatPumpCarnotFraction", new Entry(HeatPumps, "Carnot fraction", "How much of the Carnot limit a pump achieves, 0..1.", 0f, 1f) },
            { "HeatPumpMaxCoefficient", new Entry(HeatPumps, "Max coefficient", "Ceiling on the coefficient of performance.", 0f, 20f) },

            { "DebugTextOnScreen", new Entry(Presentation, "Crosshair readout", "Everything the simulation knows about the block being looked at. Also makes the solver record per-mechanism watts, which is not free.", 0, 1) },
            { "DebugSolarRaycast", new Entry(Presentation, "Draw sun ray", "The sun ray from each grid, white when lit and red when occluded.", 0, 1) },
            { "DebugWindRaycast", new Entry(Presentation, "Draw wind vector", "The relative wind vector.", 0, 1) },
            { "DebugBlockOverlay", new Entry(Presentation, "Block overlay", "The x-ray box overlay. Ctrl+Shift+= cycles it in play.", 0, ThermalDebugView.ModeCount - 1, true) },

            { "EnableTelemetry", new Entry(TelemetrySection, "Collect telemetry", "Per-grid and per-block-type data collection. Off for ordinary play.", 0, 1) },
            { "TelemetrySampleStride", new Entry(TelemetrySection, "Sample stride", "Steps between telemetry samples.", 1, 64, true) },
        };

        /// <summary>
        /// The settings a client may change for itself. Everything else is world state and belongs
        /// to the server, exactly as <c>/thermal set</c> has it.
        /// </summary>
        private static readonly HashSet<string> ClientSide = new HashSet<string>
        {
            "DebugTextOnScreen", "DebugSolarRaycast", "DebugWindRaycast", "DebugBlockOverlay",
        };

        private static bool initialised;
        private static ControlPage page;

        /// <summary>
        /// Asks Rich HUD Master to register this mod. The framework answers on its own schedule —
        /// possibly never, if the player does not have it installed — so the menu is built from the
        /// callback rather than here.
        /// </summary>
        public static void Initialize()
        {
            if (initialised) return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated) return;

            initialised = true;
            RichHudClient.Init(Settings.Name, Build, Reset);
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

        private static void Reset()
        {
            page = null;
        }

        private static void Build()
        {
            bool editable = MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer;

            page = new ControlPage { Name = "Settings" };

            RichHudTerminal.Root.Enabled = true;
            RichHudTerminal.Root.Add(page);

            List<string> names = Settings.Names();

            for (int i = 0; i < Order.Length; i++)
            {
                string category = Order[i];

                ControlCategory group = new ControlCategory
                {
                    HeaderText = category,
                    SubheaderText = Subheader(category, editable),
                };

                ControlTile tile = new ControlTile();
                int inTile = 0;
                bool any = false;

                for (int n = 0; n < names.Count; n++)
                {
                    string name = names[n];
                    if (CategoryOf(name) != category) continue;

                    // A tile is a column. Splitting keeps a long category readable rather than
                    // running one column off the bottom of the page.
                    if (inTile == TileSize)
                    {
                        group.Add(tile);
                        tile = new ControlTile();
                        inTile = 0;
                    }

                    tile.Add(Control(name, editable));
                    inTile++;
                    any = true;
                }

                if (!any) continue;

                group.Add(tile);
                group.Add(Actions(category, editable));
                page.Add(group);
            }
        }

        /// <summary>Controls per column.</summary>
        private const int TileSize = 7;

        private static string Subheader(string category, bool editable)
        {
            if (!editable)
            {
                return category == Presentation
                    ? "Client side; yours to change."
                    : "Server side; read only from a client.";
            }

            return "Changes apply immediately. Save writes them to the config file.";
        }

        /// <summary>
        /// Save and defaults, repeated on every category so neither is ever a scroll away from the
        /// control that was just changed.
        /// </summary>
        private static ControlTile Actions(string category, bool editable)
        {
            ControlTile tile = new ControlTile();

            TerminalButton save = new TerminalButton
            {
                Name = "Save to config file",
                ToolTip = Tip("Writes every current value to the world's config file."),
                Enabled = editable,
            };
            save.ControlChangedHandler = (sender, args) =>
            {
                Settings.Save(Settings.Instance);
                MyAPIGateway.Utilities.ShowNotification("Thermodynamics: settings saved", 2000, "White");
            };
            tile.Add(save);

            TerminalButton defaults = new TerminalButton
            {
                Name = "Reset " + category.ToLower() + " to defaults",
                ToolTip = Tip("Puts this category back to the values a fresh install ships with. Not saved until you press Save."),
                Enabled = editable || category == Presentation,
            };
            defaults.ControlChangedHandler = (sender, args) => ResetCategory(category);
            tile.Add(defaults);

            return tile;
        }

        private static void ResetCategory(string category)
        {
            Settings fresh = Settings.GetDefaults();
            List<string> names = Settings.Names();

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (CategoryOf(name) != category) continue;
                if (!CanEdit(name)) continue;

                Settings.Instance.SetValue(name, fresh.GetValue(name));
            }

            Settings.Instance.Apply();
        }

        private static TerminalControlBase Control(string name, bool editable)
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
        /// The one place a control writes back. A client that got a control it should not have —
        /// through a framework quirk or a change to the layout table — is stopped here rather than
        /// silently desynchronising itself from the server.
        /// </summary>
        private static void Write(string name, float value)
        {
            if (!CanEdit(name)) return;

            Settings.Instance.SetValue(name, value);
            Settings.Instance.Apply();
        }

        private static bool CanEdit(string name)
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer) return true;
            return ClientSide.Contains(name);
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

        private static string CategoryOf(string name)
        {
            Entry entry;
            return Layout.TryGetValue(name, out entry) ? entry.Category : Other;
        }

        /// <summary>
        /// A setting with no layout entry still gets a control: its own name as the label and a
        /// range wide enough to be useful, so the menu degrades to something usable rather than
        /// dropping the setting.
        /// </summary>
        private static Entry EntryFor(string name)
        {
            Entry entry;
            if (Layout.TryGetValue(name, out entry)) return entry;

            return new Entry(Other, name, "Not yet described in the menu's layout table.", 0f, 1000f);
        }
    }
}
