using System;
using System.Collections.Generic;
using System.Text;
using Draygo.API;
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
        public static DefinitionExtensionsAPI Definitions;

        /// <summary>
        /// Typed into chat to write a telemetry report without closing the world.
        /// </summary>
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
            NetworkAPI.Init(ModID, Settings.Name);
            NetworkAPI.LogNetworkTraffic = true;

            // The config file is what turns telemetry on for a test session and off again for
            // ordinary play. Grids may have initialised before this ran, so the load is shared
            // rather than done here: whoever touches the settings first performs it.
            Settings.EnsureLoaded();

            Telemetry.Start();

            ThermalHud.Initialize();
            ThermalTerminal.Register();

            // The API is published from Init so a mod that loads after this one still finds it:
            // late consumers ask for the table and get it re-sent.
            ThermalApi.Register();

            // The overlay is client state, not grid state: the config only says which view a
            // session opens on, and the keybind takes it from there.
            ThermalDebugView.Current = (ThermalDebugView.Mode)Settings.Instance.DebugBlockOverlay;
        }

        protected override void UnloadData()
        {
            // The last point at which the mod is still alive and world storage is still writable.
            Telemetry.Finish("world closing");
            Telemetry.Reset();

            // Definition and shape caches are keyed by definition and outlive a single grid, so
            // they have to be dropped when the session does or a second world inherits them.
            ThermalBlockCatalog.Clear();
            ThermalCoolantShapes.Clear();
            ThermalHeatPumpShapes.Clear();
            ThermalBridges.Clear();
            ThermalGrid.ResetEnvironmentCaches();
            ThermalHeatSources.Clear();
            ThermalApi.Unregister();
            ThermalTerminal.Unregister();

            if (_commandRegistered && MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                _commandRegistered = false;
            }

            Definitions?.UnloadData();
            base.UnloadData();
        }

        public override void Simulate()
        {
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
        /// The session's own per-frame work. Cross-grid conduction runs on the same ten-frame
        /// cadence the grids step on, because that is the interval its exchange is scaled to.
        /// </summary>
        private void Tick()
        {
            RegisterCommand();
            PollOverlayKey();

            if (_frame % 10 == 0)
            {
                ThermalBridges.Update(ThermalGrid.TickSeconds);
            }

            Debug.ShowDebugInfo();
        }

        public override void Draw()
        {
            ThermalHud.Draw();
            ThermalDebugView.Draw();
        }

        /// <summary>
        /// Ctrl+Shift+V cycles the block overlay through its views.
        ///
        /// Chosen over a control the player can rebind because a mod cannot add one: the game's
        /// binding list is fixed. The chat and terminal typing checks are what keep the V out of a
        /// name the player is halfway through entering.
        /// </summary>
        private void PollOverlayKey()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Input == null || MyAPIGateway.Gui == null) return;
            if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible) return;

            if (!MyAPIGateway.Input.IsNewKeyPressed(MyKeys.V)) return;
            if (!MyAPIGateway.Input.IsAnyCtrlKeyPressed()) return;
            if (!MyAPIGateway.Input.IsAnyShiftKeyPressed()) return;

            ThermalDebugView.Cycle();
        }

        /// <summary>
        /// MyAPIGateway.Utilities is not dependable from Init, so the chat hook is attached on the
        /// first simulated frame instead.
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
        /// The whole point of the runtime switch: turn collection on for a test, off again
        /// afterwards, and take a report at any point, without reloading the world.
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

            if (lowered == "overlay")
            {
                ThermalDebugView.Cycle();
                Reply("block overlay: " + ThermalDebugView.Describe(ThermalDebugView.Current));
                return;
            }

            if (lowered == "settings" || lowered == "list")
            {
                ListSettings();
                return;
            }

            if (lowered.StartsWith("set "))
            {
                RunSet(argument.Substring(4).Trim());
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

            Reply("commands: status | settings | set <name> <value> | save | overlay"
                + " | telemetry on | telemetry off | stride <n> | dump");
        }

        /// <summary>
        /// Changes one setting for the running session.
        ///
        /// Every switch in the mod is live: the value is written into the settings object every
        /// grid already holds, and the simulation picks it up on its next step. Nothing is written
        /// to disk unless <c>/thermal save</c> asks for it, so an experiment cannot outlive the
        /// session by accident.
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

            if (!MyAPIGateway.Session.IsServer)
            {
                Reply("settings are server side; ask an administrator");
                return;
            }

            Settings.Instance.SetValue(name, value);
            Settings.Instance.Apply();

            Reply(name + " = " + Format(name, Settings.Instance.GetValue(name)) + " (unsaved)");
        }

        /// <summary>Matches a setting name without regard to case, so players can type it.</summary>
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

        private static void Reply(string message)
        {
            MyAPIGateway.Utilities.ShowMessage(Settings.Name, message);
        }
    }
}
