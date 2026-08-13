using Draygo.API;
using Draygo.BlockExtensionsAPI;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game;
using VRage.Game.Components;
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
            ThermalBridges.Clear();
            ThermalGrid.ResetEnvironmentCaches();

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

            if (_frame % 10 == 0)
            {
                ThermalBridges.Update(ThermalGrid.TickSeconds);
            }

            Debug.ShowDebugInfo();
        }

        public override void Draw()
        {
            ThermalHud.Draw();
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

            Reply("commands: status | telemetry on | telemetry off | stride <n> | dump");
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
