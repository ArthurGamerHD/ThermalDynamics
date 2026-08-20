using System;
using System.Text;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// One client asking the server to change a world setting, and the server's answer.
    /// </summary>
    [ProtoContract]
    public class SettingsRequest
    {
        /// <summary>Setting name, as <see cref="Settings.Names"/> spells it.</summary>
        [ProtoMember(1)] public string Name;

        /// <summary>Value to set. Switches are 0 and 1, as everywhere else.</summary>
        [ProtoMember(2)] public float Value;

        /// <summary>Set on the reply: what happened, for the requester to read.</summary>
        [ProtoMember(3)] public string Result;

        /// <summary>True on a reply, so one message type serves both directions.</summary>
        [ProtoMember(4)] public bool IsReply;
    }

    /// <summary>
    /// Lets an admin change the world's settings from a multiplayer client.
    ///
    /// <para>
    /// **This deliberately does not use the mod's <see cref="SENetworkAPI"/> channel.** That API
    /// registers the game's *non-secure* message handler, so every "who sent this" it reports —
    /// including the steam id given to a network command — is a field the sender wrote, and a
    /// modified client can put anything there. Its own documentation says not to gate admin
    /// actions on it. The engine's secure handler supplies a sender the transport verified and a
    /// flag saying whether a message came from the server, which is exactly what a permission
    /// check needs, so this uses that directly on a channel of its own.
    /// </para>
    ///
    /// <para>
    /// The reply path exists because a refusal is otherwise silent: the settings themselves
    /// replicate on change, so an accepted request is visible as the value moving, and a rejected
    /// one would look identical to a lost packet.
    /// </para>
    /// </summary>
    public static class SettingsRequests
    {
        /// <summary>
        /// A channel of this mod's own, one above the shared one the network API uses. Kept
        /// separate rather than multiplexed: the two use different engine handlers, and a single
        /// id registered with both delivers every packet twice.
        /// </summary>
        public const ushort ChannelId = Session.ModID + 1;

        /// <summary>
        /// Lowest promote level allowed to change a world setting.
        ///
        /// Space master rather than admin: on most servers that is the level given to people
        /// trusted with the world's own state, and every setting here is world state. The offline
        /// and single-player case never reaches this — a lone player is the server.
        /// </summary>
        private const MyPromoteLevel Required = MyPromoteLevel.SpaceMaster;

        private static bool registered;

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

        /// <summary>
        /// Whether this machine has to ask rather than simply set. True only on a multiplayer
        /// client; the server, and a single player who is the server, change settings directly.
        /// </summary>
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

        /// <summary>
        /// Whether this player is likely to be allowed to change world settings, for deciding
        /// whether to grey a control out. The server checks again on arrival and its answer is the
        /// one that counts — this only avoids offering a control that will be refused.
        /// </summary>
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

        /// <summary>Asks the server to change one setting.</summary>
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

        private static void Handle(ushort channel, byte[] payload, ulong sender, bool fromServer)
        {
            try
            {
                SettingsRequest request =
                    MyAPIGateway.Utilities.SerializeFromBinary<SettingsRequest>(payload);
                if (request == null) return;

                if (request.IsReply)
                {
                    // Only the server answers, and a reply that did not come from it is a client
                    // telling this machine what it wants it to believe.
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

        private static void Apply(SettingsRequest request, ulong sender)
        {
            // The sender the engine supplies, not one out of the payload. This is the whole reason
            // the request does not travel on the shared channel.
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

            // Applying publishes the whole settings object to every client, so the change is its
            // own confirmation everywhere else. The requester is told separately because a value
            // that lands where it already was looks like nothing happened.
            Settings.Instance.Apply();

            StringBuilder message = new StringBuilder();
            message.Append(request.Name).Append(" = ")
                .Append(Settings.Instance.GetValue(request.Name).ToString("n4"))
                .Append(" (unsaved)");

            Reply(sender, message.ToString());
            MyLog.Default.Info("[" + Settings.Name + "] " + sender + " set " + message);
        }

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
