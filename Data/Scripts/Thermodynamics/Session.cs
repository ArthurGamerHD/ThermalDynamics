using System;
using System.Collections.Generic;
using System.Text;
using Draygo.BlockExtensionsAPI;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Input;
using VRage.Utils;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation)]
	public class Session : MySessionComponentBase
	{
        public const ushort ModID = 30323;

        /// <summary>
        /// The running session component, for the few things that need the mod's own context —
        /// reading a file out of the mod folder needs the mod's entry in the world's mod list, and
        /// only a component knows its own.
        /// </summary>
        public static Session Instance;
        public static DefinitionExtensionsAPI Definitions;

        /// <summary>Chat command that writes a telemetry report without closing the world.</summary>
        private const string DumpCommand = "/thermaldump";

        /// <summary>
        /// Runtime control: <c>/thermal status</c>, <c>/thermal telemetry on|off</c>,
        /// <c>/thermal stride n</c>, <c>/thermal dump</c>.
        /// </summary>
        private const string Command = "/thermal";

        private bool _commandRegistered;
        private long _frame;

        public Session()
        {
            MyLog.Default.Info($"[{Settings.Name}] Setup Definition Extention API");
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {
            MyLog.Default.Info($"[{Settings.Name}] Definition Extention API - Done");
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Instance = this;

            NetworkAPI.Init(ModID, Settings.Name);
            NetworkAPI.LogNetworkTraffic = true;

            // The config file controls whether telemetry collects. Grids may have initialised before
            // this ran, so the load is not performed here: whichever caller touches the settings
            // first performs it.
            Settings.EnsureLoaded();

            // After the load, so the property is seeded with real settings rather than null — a
            // null value is never transmitted, and a client's fetch would get nothing back.
            // Session-scoped properties are addressed by the order they are constructed in, so
            // this stays first among them and stays on both sides.
            SettingsSync.Register(this);

            Telemetry.Start();

            ThermalTerminal.Register();

            // Its own channel and the engine's verified sender, because the shared one cannot say
            // who really sent a packet — see SettingsRequests.
            SettingsRequests.Register();

            // Published from Init so a mod loading after this one still finds it: a late consumer
            // requests the table and it is re-sent.
            ThermalApi.Register();

            // The overlay is client state rather than grid state: the config supplies only the view
            // a session opens on, and the keybind changes it from there.
            ThermalDebugView.Current = (ThermalDebugView.Mode)Settings.Instance.DebugBlockOverlay;
            WindOverlay.Current = (WindOverlay.Mode)Settings.Instance.DebugWindOverlay;

            // Registration with Rich HUD Master is asynchronous and may never complete, so this only
            // requests it; the menu builds itself when the framework responds.
            ThermalSettingsMenu.Initialize();
        }

        protected override void UnloadData()
        {
            // The last point at which the mod is still live and world storage is still writable.
            // The fault summary goes first and is not conditional on collection: a world that ran
            // with telemetry off produces no report, and the faults it hit are the only thing worth
            // saying about it.
            Telemetry.LogFaultSummary();
            Telemetry.Finish("world closing");
            Telemetry.Reset();

            // Definition and shape caches are keyed by definition and outlive a single grid, so they
            // must be dropped with the session or a second world inherits them.
            ThermalBlockCatalog.Clear();
            ThermalCoolantShapes.Clear();
            ThermalHeatPumpShapes.Clear();
            ThermalBridges.Clear();
            ThermalGrid.ResetEnvironmentCaches();
            ThermalHeatSources.Clear();
            ThermalApi.Unregister();
            ThermalTerminal.Unregister();
            SettingsRequests.Unregister();
            Instance = null;

            if (_commandRegistered && MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                _commandRegistered = false;
            }

            Definitions?.UnloadData();
            base.UnloadData();
        }

        /// <summary>Frames between deferred config writes; one second at 60 fps.</summary>
        private const int SaveFlushFrames = 60;

        private int framesSinceSaveCheck;

        public override void Simulate()
        {
            // Settings save themselves as they change. The write is deferred to here so that
            // dragging a slider across its range is one file write rather than one per step of it.
            if (++framesSinceSaveCheck >= SaveFlushFrames)
            {
                framesSinceSaveCheck = 0;
                Settings.FlushPending();
            }

            _frame++;

            if (!Telemetry.Enabled)
            {
                Tick();
                return;
            }

            Telemetry.SessionFrameTime.Begin();

            Telemetry.FrameTick();
            Tick();

            Telemetry.SessionFrameTime.End();
        }

        /// <summary>
        /// The session's own per-frame work. Cross-grid conduction runs on the ten-frame cadence its
        /// exchange is scaled to.
        /// </summary>
        private void Tick()
        {
            RegisterCommand();
            PollKeys();

            // Every grid every frame, each doing its share of the step it is part way through.
            ThermalGridScheduler.Tick();

            // The planet-wide wind sweep. Once per frame at most, never per grid, and it returns
            // immediately unless telemetry and the probe interval are both on.
            PlanetProbes.Step(ThermalGrid.TickSeconds);
            ThermalHeatSourceDebug.Update(ThermalGrid.TickSeconds);

            if (_frame % 10 == 0)
            {
                ThermalBridges.Update(ThermalGrid.TickSeconds);
                ThermalTerminal.Update();
            }

            Debug.ShowDebugInfo();
        }

        public override void Draw()
        {
            ThermalHud.Draw();
            ThermalDebugView.Draw();
            WindOverlay.Draw();
            ThermalDebugPanel.Update();
        }

        /// <summary>
        /// Ctrl+Shift+= cycles the block overlay, Ctrl+Shift+W the wind map, Ctrl+Shift+S the settings
        /// menu. Hard-coded rather than rebindable, because a mod cannot add an entry to the game's
        /// binding list; the typing checks keep the chords out of text a player is entering.
        /// </summary>
        private void PollKeys()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Input == null || MyAPIGateway.Gui == null) return;
            if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible) return;
            if (!MyAPIGateway.Input.IsAnyCtrlKeyPressed()) return;
            if (!MyAPIGateway.Input.IsAnyShiftKeyPressed()) return;

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemPlus))
            {
                ThermalDebugView.Cycle();
                return;
            }

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.S))
            {
                ThermalSettingsMenu.Open();
                return;
            }

            // The wind map, on its own key rather than as another view of the block overlay: it
            // draws the world rather than a grid, and it is the one a player wants up *while*
            // flying through what it describes.
            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.W))
            {
                WindOverlay.Cycle();
                return;
            }

            // The performance panel. A key rather than a chat command because it is something a
            // player flicks on to check a suspicion and off again, and because it has to be
            // reachable while flying.
            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.M))
            {
                bool shown = ThermalHud.TogglePerformancePanel();
                MyAPIGateway.Utilities.ShowNotification(
                    "Thermal performance panel " + (shown ? "on" : "off"), 2000);
            }
        }

        /// <summary>
        /// Attaches the chat hook. <c>MyAPIGateway.Utilities</c> is not reliably available from Init,
        /// so this runs on the first simulated frame.
        /// </summary>
        private void RegisterCommand()
        {
            if (_commandRegistered || MyAPIGateway.Utilities == null) return;

            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            _commandRegistered = true;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText == null) return;

            string text = messageText.Trim();

            if (text.StartsWith(DumpCommand))
            {
                sendToOthers = false;
                Dump();
                return;
            }

            if (!text.StartsWith(Command)) return;
            sendToOthers = false;

            string argument = text.Length > Command.Length ? text.Substring(Command.Length).Trim() : "";
            RunCommand(argument);
        }

        /// <summary>
        /// Runtime telemetry control: turn collection on or off and take a report at any point,
        /// without reloading the world.
        /// </summary>
        private void RunCommand(string argument)
        {
            string lowered = argument.ToLower();

            if (lowered == "telemetry on")
            {
                Telemetry.SetEnabled(true);
                Reply("telemetry collection ON (stride " + Telemetry.SampleStride + ")");
                return;
            }

            if (lowered == "telemetry off")
            {
                Telemetry.SetEnabled(false);
                Reply("telemetry collection OFF");
                return;
            }

            if (lowered == "dump")
            {
                Dump();
                return;
            }

            if (lowered == "menu")
            {
                ThermalSettingsMenu.Open();
                return;
            }

            if (lowered == "heat" || lowered.StartsWith("heat "))
            {
                Reply(ThermalHeatSourceDebug.Run(argument.Length > 4
                    ? argument.Substring(4).Trim() : ""));
                return;
            }

            if (lowered == "overlay")
            {
                ThermalDebugView.Cycle();
                Reply("block overlay: " + ThermalDebugView.Describe(ThermalDebugView.Current));
                return;
            }

            if (lowered == "wind")
            {
                WindOverlay.Cycle();
                Reply("wind map: " + WindOverlay.Describe(WindOverlay.Current));
                return;
            }

            if (lowered == "settings" || lowered == "list")
            {
                ListSettings();
                return;
            }

            if (lowered == "sync")
            {
                ReportSync();
                return;
            }

            if (lowered == "sync fetch")
            {
                Reply(SettingsSync.Fetch()
                    ? "asked the server for the settings again"
                    : "nothing to fetch: this is the server");
                return;
            }

            if (lowered.StartsWith("set "))
            {
                RunSet(argument.Substring(4).Trim());
                return;
            }

            if (lowered == "profile" || lowered == "profiles")
            {
                Reply("profiles: " + string.Join(" | ", Core.ThermalProfiles.Names));
                for (int i = 0; i < Core.ThermalProfiles.Names.Length; i++)
                {
                    string name = Core.ThermalProfiles.Names[i];
                    Reply("  " + name + " — " + Core.ThermalProfiles.Describe(name));
                }
                return;
            }

            if (lowered.StartsWith("profile "))
            {
                string wanted = argument.Substring(8).Trim();
                if (!Settings.Instance.ApplyProfile(wanted))
                {
                    Reply("unknown profile '" + wanted + "'. One of: "
                        + string.Join(" | ", Core.ThermalProfiles.Names));
                    return;
                }

                Reply("profile " + wanted.ToLowerInvariant() + " — "
                    + Core.ThermalProfiles.Describe(wanted));
                Reply("  Frequency " + Settings.Instance.Frequency
                    + ", HeatTimeScale " + Settings.Instance.HeatTimeScale.ToString("n0")
                    + ", MaxSubsteps " + Settings.Instance.MaxSubsteps
                    + ". /thermal save to keep it.");
                return;
            }

            if (lowered == "save")
            {
                Settings.Save(Settings.Instance);
                Reply("settings written to world storage");
                return;
            }

            if (lowered.StartsWith("stride "))
            {
                int stride;
                if (int.TryParse(lowered.Substring(7).Trim(), out stride) && stride > 0)
                {
                    Telemetry.SampleStride = stride;
                    Reply("telemetry sample stride " + stride);
                }
                else
                {
                    Reply("stride must be a positive whole number");
                }
                return;
            }

            if (lowered == "status" || lowered.Length == 0)
            {
                Reply("telemetry " + (Telemetry.Enabled ? "ON" : "OFF")
                    + ", stride " + Telemetry.SampleStride
                    + ", grids " + ThermalGrid.LiveGrids.Count
                    + ", block models " + ThermalBlockCatalog.ModelCount
                    + ", bridges " + ThermalBridges.Count);
                return;
            }

            Reply("commands: status | settings | set <name> <value> | profile [name] | save"
                + " | sync [fetch] | overlay | menu | telemetry on | telemetry off"
                + " | stride <n> | dump");
        }

        /// <summary>
        /// Changes one setting for the running session.
        ///
        /// Every setting is live: the value is written into the settings object every grid already
        /// holds and is picked up on the next step. Nothing is written to disk unless
        /// <c>/thermal save</c> is issued.
        /// </summary>
        private void RunSet(string argument)
        {
            int space = argument.IndexOf(' ');
            if (space <= 0)
            {
                Reply("usage: /thermal set <name> <value>   (switches take 0 or 1)");
                return;
            }

            string name = Resolve(argument.Substring(0, space).Trim());
            string text = argument.Substring(space + 1).Trim().ToLower();

            if (name == null)
            {
                Reply("no such setting; /thermal settings lists them");
                return;
            }

            float value;
            if (text == "on" || text == "true") value = 1f;
            else if (text == "off" || text == "false") value = 0f;
            else if (!float.TryParse(text, out value))
            {
                Reply("could not read '" + text + "' as a number");
                return;
            }

            // A client owns its own presentation switches and sets them locally; everything else
            // is world state and goes to the server as a request, which answers whether it was
            // allowed. The server's own path is unchanged.
            if (SettingsRequests.MustAsk && !Settings.ClientOwned.Contains(name))
            {
                SettingsRequests.Send(name, value);
                Reply("asked the server to set " + name);
                return;
            }

            Settings.Instance.SetValue(name, value);
            Settings.Instance.Apply();

            Reply(name + " = " + Format(name, Settings.Instance.GetValue(name)) + " (unsaved)");
        }

        /// <summary>Matches a setting name case-insensitively, for chat entry.</summary>
        private static string Resolve(string name)
        {
            List<string> names = Settings.Names();
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return names[i];
            }
            return null;
        }

        private void ListSettings()
        {
            List<string> names = Settings.Names();
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < names.Count; i++)
            {
                text.Append(names[i]).Append(" = ")
                    .Append(Format(names[i], Settings.Instance.GetValue(names[i])))
                    .Append('\n');
            }

            MyAPIGateway.Utilities.ShowMissionScreen(
                Settings.Name, "Settings", "", text.ToString(), null, "Close");
        }

        private static string Format(string name, float value)
        {
            if (Settings.IsFlag(name)) return value != 0f ? "on" : "off";
            return value.ToString("0.####");
        }

        private void Dump()
        {
            if (!Telemetry.Enabled)
            {
                Reply("telemetry is off; /thermal telemetry on first");
                return;
            }

            Telemetry.Finish("manual dump", true);
            Reply("telemetry report written to world storage");
        }

        /// <summary>
        /// Reports whether this machine's settings match the server's, as a digest to compare by
        /// eye with the same command run on the other side.
        ///
        /// Replication is host code and cannot be tested outside a live session, so this is how it
        /// gets checked: run it on the server, run it on a client, compare one string.
        /// </summary>
        private static void ReportSync()
        {
            bool server = MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer;

            Reply((server ? "server" : "client")
                + " | settings digest " + SettingsSync.Fingerprint()
                + " over " + SettingsSync.ReplicatedCount() + " values"
                + (SettingsSync.Ready ? "" : " | NOT SYNCED: nothing to send or receive"));

            // The few figures most likely to differ, so a mismatch says which way it went without
            // needing the config file open.
            Reply("  Frequency " + Settings.Instance.Frequency
                + " | HeatTimeScale " + Settings.Instance.HeatTimeScale.ToString("n0")
                + " | MaxSubsteps " + Settings.Instance.MaxSubsteps
                + " | MaxSubstepsPerBlock " + Settings.Instance.MaxSubstepsPerBlock
                + " | MaxElementVisits " + Settings.Instance.MaxElementVisitsPerStep.ToString("n0"));

            if (!server) Reply("  digests differ? run /thermal sync fetch, then this again");
        }

        private static void Reply(string message)
        {
            MyAPIGateway.Utilities.ShowMessage(Settings.Name, message);
        }
    }
}
