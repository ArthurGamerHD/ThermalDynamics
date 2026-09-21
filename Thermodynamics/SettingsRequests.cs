using System;
using System.Text;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class SettingsRequest
    {
        [ProtoMember(1)] public string Name;

        [ProtoMember(2)] public float Value;

        [ProtoMember(3)] public string Result;

        [ProtoMember(4)] public bool IsReply;
    }

    public static class SettingsRequests
    {
        public const ushort ChannelId = Session.ModID + 1;

        private const MyPromoteLevel Required = MyPromoteLevel.SpaceMaster;

        private static bool registered;

/// <summary>Registers the API and message handler.</summary>
        public static void Register()
        {
            if (registered) return;

            try
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, Handle);
                registered = true;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] settings requests unavailable\n" + e);
            }
        }

/// <summary>Unregisters the API and cleans resources.</summary>
        public static void Unregister()
        {
            if (!registered) return;
            registered = false;

            try
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, Handle);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to unregister settings requests\n" + e);
            }
        }

        public static bool MustAsk
        {
            get
            {
                try
                {
                    return MyAPIGateway.Session != null && !MyAPIGateway.Session.IsServer;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool MayAsk
        {
            get
            {
                try
                {
                    if (MyAPIGateway.Session == null) return false;
                    if (MyAPIGateway.Session.IsServer) return true;

                    return MyAPIGateway.Session.PromoteLevel >= Required;
                }
                catch
                {
                    return false;
                }
            }
        }

/// <summary>Send operation.</summary>
        public static void Send(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                SettingsRequest request = new SettingsRequest { Name = name, Value = value };
                byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(request);

                MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, payload, true);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to send a settings request\n" + e);
            }
        }

/// <summary>Handle operation.</summary>
        private static void Handle(ushort channel, byte[] payload, ulong sender, bool fromServer)
        {
            try
            {
                SettingsRequest request =
                    MyAPIGateway.Utilities.SerializeFromBinary<SettingsRequest>(payload);
                if (request == null) return;

                if (request.IsReply)
                {
                    if (!fromServer) return;

                    MyAPIGateway.Utilities.ShowMessage(Settings.Name, request.Result);
                    return;
                }

                if (!MyAPIGateway.Session.IsServer) return;

                Apply(request, sender);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] bad settings request\n" + e);
            }
        }

/// <summary>Applies the .</summary>
        private static void Apply(SettingsRequest request, ulong sender)
        {
            MyPromoteLevel level = MyAPIGateway.Session.GetUserPromoteLevel(sender);

            if (level < Required)
            {
                Reply(sender, "refused: changing world settings needs " + Required + " or above");
                MyLog.Default.Info("[" + Settings.Name + "] refused a settings change from "
                    + sender + " at " + level);
                return;
            }

            if (Settings.ClientOwned.Contains(request.Name))
            {
                Reply(sender, request.Name + " is yours to set, not the world's");
                return;
            }

            if (!Settings.Instance.SetValue(request.Name, request.Value))
            {
                Reply(sender, "no setting called " + request.Name);
                return;
            }

            Settings.Instance.Apply();

/// <summary>StringBuilder operation.</summary>
            StringBuilder message = new StringBuilder();
            message.Append(request.Name).Append(" = ")
                .Append(Settings.Instance.GetValue(request.Name).ToString("n4"))
                .Append(" (unsaved)");

            Reply(sender, message.ToString());
            MyLog.Default.Info("[" + Settings.Name + "] " + sender + " set " + message);
        }

/// <summary>Reply operation.</summary>
        private static void Reply(ulong recipient, string text)
        {
            try
            {
                SettingsRequest reply = new SettingsRequest { Result = text, IsReply = true };
                byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(reply);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, payload, recipient, true);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to answer a settings request\n" + e);
            }
        }
    }
}
