using Thermodynamics.Presentation;
using System;
using Sandbox.ModAPI;
using VRage.Input;
using RichHudFramework;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;

namespace Thermodynamics
{
    public static partial class ThermalVisionProbe
    {
        private static MyKeys visionKey = MyKeys.T;
        private static bool visionKeyLoaded;
        private const string VisionKeyFile = "ThermalVisionKey.txt";
        private static void LoadVisionKey()
        {
            if(visionKeyLoaded) return;
            visionKeyLoaded=true;
            try
            {
                if(MyAPIGateway.Utilities.FileExistsInLocalStorage(VisionKeyFile,typeof(ThermalVisionProbe)))
                    using(var reader=MyAPIGateway.Utilities.ReadFileInLocalStorage(VisionKeyFile,typeof(ThermalVisionProbe)))
                    { MyKeys key; if(Enum.TryParse(reader.ReadToEnd().Trim(),true,out key) && key>=MyKeys.A && key<=MyKeys.Z) visionKey=key; }
            }
            catch(Exception) { RecordEvent("vision key: could not read local preference; using default"); }
        }
        private static string BindVisionKey(string value)
        {
            LoadVisionKey(); MyKeys key;
            if(!Enum.TryParse(value.Trim(),true,out key) || key<MyKeys.A || key>MyKeys.Z)
                return "Use /thermal vision bind <A-Z>; binding is Ctrl+Shift+letter";
            if(key==MyKeys.S || key==MyKeys.W || key==MyKeys.M) return "S, W and M are already assigned to thermal tools";
            try { using(var writer=MyAPIGateway.Utilities.WriteFileInLocalStorage(VisionKeyFile,typeof(ThermalVisionProbe))) writer.Write(key.ToString()); }
            catch(Exception) { return "Could not save thermal vision key preference"; }
            visionKey=key;
            return "Grey thermal toggle: Ctrl+Shift+"+key;
        }
        public static void PollVisionKey()
        {
            if(MyAPIGateway.Utilities==null || MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Input==null || MyAPIGateway.Gui==null) return;
            if(MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible || BindManager.IsChatOpen) return;
            LoadVisionKey();
            if(MyAPIGateway.Input.IsAnyCtrlKeyPressed() && MyAPIGateway.Input.IsAnyShiftKeyPressed() && MyAPIGateway.Input.IsNewKeyPressed(visionKey))
                MyAPIGateway.Utilities.ShowNotification(Run(State.Current==Presentation.ThermalVisionState.Mode.WhiteHot?"off":"grey"),1800);
        }
    }
}
