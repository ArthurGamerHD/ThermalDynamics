using System;
using System.Collections.Generic;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using VRage.Utils;

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
    ///
    /// The page is one column read from the top: Save and Reset first, then short titled groups of
    /// three controls each. That shape is forced by the framework as much as chosen — a tile is a
    /// fixed 300x250 box that masks whatever does not fit inside it, and a group is a fixed-height
    /// row that scrolls sideways through its tiles. One tile per group, three controls per tile, is
    /// the only arrangement in which everything is both visible and in one vertical line.
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
        /// Label, tooltip and slider range per setting. Switches ignore the range. A range is a
        /// judgement about what is worth dragging to, not a limit: the chat command and the mod API
        /// still take any value the clamp accepts.
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

            { "ClampConductionOvershoot", new Entry(Solver, "Clamp conduction overshoot", "Stops a step from pushing two blocks past each other's temperature. Leave on.", 0, 1) },
            { "DamageIsPerSecond", new Entry(Solver, "Damage is per second", "Overheat damage scaled to real time rather than to the step.", 0, 1) },
            { "Frequency", new Entry(Solver, "Frequency", "Solver steps per second of simulated time. Higher is finer and costlier.", 1, 60, true) },
            { "SimulationSpeed", new Entry(Solver, "Simulation speed", "Multiplier on how fast heat moves. 1 is the tuned pace.", 0.1f, 10f) },
            { "HeatTimeScale", new Entry(Solver, "Heat time scale", "Seconds of physical time per second of play. The dial that makes heat happen on a human scale.", 1f, 1000f) },

            { "VacuumTemperature", new Entry(Environment, "Vacuum temperature", "Sky temperature in space, K. 2.7 is the real background.", 0f, 300f) },
            { "SolarEnergy", new Entry(Solar, "Solar energy", "Irradiance at the planet, W/m2.", 0f, 5000f) },
            { "FrictionAtSpeedsAbove", new Entry(Environment, "Friction above", "Speed at which atmospheric friction starts, m/s.", 0f, 300f) },
            { "FrictionScale", new Entry(Environment, "Friction scale", "Multiplier on friction heating.", 0f, 0.01f) },
            { "RoomConvectionCoefficient", new Entry(Environment, "Room convection", "Convective coefficient between a block and room air, W/(m2 K).", 0f, 50f) },
            { "RoomAirDensity", new Entry(Environment, "Room air density", "Density of room air, kg/m3. 1.225 is sea level.", 0f, 5f) },
            { "SolarOcclusionInterval", new Entry(Occlusion, "Occlusion interval", "Solver steps between sun occlusion raycasts.", 1, 60, true) },

            { "HeatPumpCarnotFraction", new Entry(Systems, "Carnot fraction", "How much of the Carnot limit a pump achieves, 0..1.", 0f, 1f) },
            { "HeatPumpMaxCoefficient", new Entry(Systems, "Max coefficient", "Ceiling on the coefficient of performance.", 0f, 20f) },

            { "DebugTextOnScreen", new Entry(Display, "Crosshair readout", "Everything the simulation knows about the block being looked at. Also makes the solver record per-mechanism watts, which is not free.", 0, 1) },
            { "DebugSolarRaycast", new Entry(Display, "Draw sun ray", "The sun ray from each grid, white when lit and red when occluded.", 0, 1) },
            { "DebugWindRaycast", new Entry(Display, "Draw wind vector", "The relative wind vector.", 0, 1) },
            { "DebugBlockOverlay", new Entry(Display, "Block overlay", "The x-ray box overlay. Ctrl+Shift+= cycles it in play.", 0, ThermalDebugView.ModeCount - 1, true) },

            { "RoomOverlayMinKelvin", new Entry(Display, "Room overlay: cold", "Bottom of the room view's colour span, K. Room air lives in a narrow band, so it gets a tighter ramp than blocks do.", 173.15f, 323.15f) },
            { "RoomOverlayMaxKelvin", new Entry(Display, "Room overlay: hot", "Top of the room view's colour span, K.", 273.15f, 423.15f) },
            { "EnableTelemetry", new Entry(Display, "Collect telemetry", "Per-grid and per-block-type data collection. Off for ordinary play.", 0, 1) },
            { "TelemetrySampleStride", new Entry(Display, "Sample stride", "Steps between telemetry samples.", 1, 64, true) },
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

            // Registration is a handshake with a separate mod that may not be installed. It never
            // reports failure — it just never answers — so the request is logged, and so is the
            // answer, to tell "no Rich HUD Master" apart from "menu failed to build".
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
            ThermalDebugPanel.Reset();
            ThermalHud.Reset();
        }

        private static void Build()
        {
            bool editable = MyAPIGateway.Session == null || MyAPIGateway.Session.IsServer;

            page = new ControlPage { Name = "Settings" };

            RichHudTerminal.Root.Enabled = true;
            RichHudTerminal.Root.Add(page);

            // Save and reset first, at the top, so they are found without reading the page. There
            // is one of each for the whole file: a Save per section invites the question of what
            // the other Saves did, and the answer was always "the same thing".
            page.Add(Actions(editable));

            List<string> names = Settings.Names();
            List<string> section = new List<string>();

            for (int i = 0; i < Order.Length; i++)
            {
                section.Clear();

                for (int n = 0; n < names.Count; n++)
                {
                    if (SectionOf(names[n]) == Order[i]) section.Add(names[n]);
                }

                AddSection(Order[i], section, editable);
            }
        }

        /// <summary>
        /// Lays one section out: its controls in columns of three, side by side across the page.
        ///
        /// The framework's sizes decide this. A tile is a fixed 300x250 box that masks whatever does
        /// not fit, so three controls is a column; a group is a fixed-height row that holds tiles
        /// across the page's width, which is about three of them. One column per row wastes two
        /// thirds of the width and turns forty settings into a very long scroll, which is what the
        /// last arrangement did.
        /// </summary>
        private static void AddSection(string name, List<string> members, bool editable)
        {
            for (int start = 0; start < members.Count; start += ControlsPerGroup)
            {
                ControlCategory group = new ControlCategory
                {
                    HeaderText = start == 0 ? name : name + " (cont.)",
                    SubheaderText = start == 0 ? Subheader(name, editable) : "",
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

                page.Add(group);
            }
        }

        /// <summary>
        /// Controls per tile. The tile is 250 high with 54 of padding at each end, and a control is
        /// about 40 with 12 of spacing, so three fit and a fourth is masked away.
        /// </summary>
        private const int ControlsPerTile = 3;

        /// <summary>
        /// Controls per group: two columns, which is what the page is wide enough to show. A third
        /// would be 936 across a page of about 840 and would have to be scrolled to sideways, which
        /// is worse than another row. A section with more than six continues in another group.
        /// </summary>
        private const int ControlsPerGroup = ControlsPerTile * 2;

        private static string Subheader(string section, bool editable)
        {
            if (!editable)
            {
                return section == Display
                    ? "Client side; yours to change"
                    : "Server side; read only here";
            }

            return SectionNotes.ContainsKey(section) ? SectionNotes[section] : "";
        }

        /// <summary>
        /// What each section is for, in one line. The controls carry their own descriptions, so
        /// this only has to say what kind of thing is below it.
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
        /// One Save and one Reset, for the whole file.
        ///
        /// Nothing here writes to disk until Save is pressed, so a session can be experimented with
        /// and abandoned by not pressing it — which is also why Reset does not save: it puts the
        /// values back and leaves the file alone until you say otherwise.
        /// </summary>
        private static ControlCategory Actions(bool editable)
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
                Name = "Reset everything to defaults",
                ToolTip = Tip("Puts every setting back to what a fresh install ships with. Applies at once; not written to the config file until you press Save."),
                Enabled = true,
            };
            defaults.ControlChangedHandler = (sender, args) => ResetAll();
            tile.Add(defaults);

            ControlCategory group = new ControlCategory
            {
                HeaderText = "Thermodynamics",
                SubheaderText = editable
                    ? "Changes apply at once. Save writes them to the config file."
                    : "Server side; a client may change presentation only.",
            };

            group.Add(tile);
            return group;
        }

        /// <summary>
        /// Puts back every setting the player is allowed to change — which on a client is the
        /// presentation switches and nothing else, so a reset there cannot quietly ask the server
        /// for a world it has no say over.
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

            MyAPIGateway.Utilities.ShowNotification(
                "Thermodynamics: " + changed + " settings back to defaults (unsaved)", 3000, "White");
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
        /// A named choice rather than a slider. A setting whose values are three different
        /// behaviours reads as nonsense on a scale from 0 to 2, however well the tooltip explains
        /// it.
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

        private static string SectionOf(string name)
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
