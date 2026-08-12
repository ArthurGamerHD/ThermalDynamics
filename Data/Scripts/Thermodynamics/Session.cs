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

        private bool _commandRegistered;

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

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Telemetry.Start();

            ThermalHud.Initialize();
        }

        protected override void UnloadData()
        {
            // The last point at which the mod is still alive and world storage is still writable.
            Telemetry.Finish("world closing");
            Telemetry.Reset();

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
            if (!Telemetry.Enabled)
            {
                Debug.ShowDebugInfo();
                return;
            }

            Telemetry.SessionFrameTime.Begin();

            RegisterCommand();
            Telemetry.FrameTick();

            Debug.ShowDebugInfo();

            Telemetry.SessionFrameTime.End();
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
            if (messageText == null || !messageText.StartsWith(DumpCommand)) return;

            sendToOthers = false;
            Telemetry.Finish("manual dump", true);
            MyAPIGateway.Utilities.ShowMessage(Settings.Name, "telemetry report written to world storage");
        }
    }
}
