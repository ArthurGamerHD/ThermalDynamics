using System;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game.Components;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// Replicates the world's settings from the server to every client.
    ///
    /// <para>
    /// Without this a client ran the simulation on <see cref="Settings.GetDefaults"/>, because
    /// only the server can read the config file — <c>CanReadWorldStorage</c> says so, and its
    /// comment already promised clients "take the server's settings rather than their own file".
    /// Nothing delivered them. A server running the arcade profile and a client running the
    /// shipped defaults integrate different physics from the same inputs, and every temperature
    /// on the client is wrong in a way nothing reports.
    /// </para>
    ///
    /// <para>
    /// One session-scoped property carries the whole settings object rather than a property per
    /// field. Settings change rarely — a profile, a chat command, a slider — and the object is a
    /// few hundred bytes, so the alternative buys nothing and costs one address per field, each of
    /// which has to stay in declaration order across builds.
    /// </para>
    /// </summary>
    public static class SettingsSync
    {
        private static NetSync<Settings> synced;

        /// <summary>
        /// True while a received value is being applied, so applying it cannot bounce back out.
        /// The publish path hangs off <see cref="Settings.Apply"/>, which the receive path calls.
        /// </summary>
        private static bool applying;

        /// <summary>
        /// Declares the property. Called once from the session component, before anything can
        /// change a setting.
        ///
        /// Session-scoped properties take their address from the order they are constructed in, so
        /// this must run on both sides and must stay the first one declared.
        /// </summary>
        public static void Register(MySessionComponentBase session)
        {
            if (synced != null || session == null) return;

            try
            {
                // Server to client only: the config is the server's, and a client editing it would
                // be editing its own copy of someone else's world. Fetch is exempt from that rule
                // inside the API, which is what lets a joining client ask for the current value.
                synced = new NetSync<Settings>(session, TransferType.ServerToClient, null);
                synced.ValueChangedByNetwork += Received;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] settings sync unavailable\n" + e);
            }
        }

        /// <summary>
        /// Publishes the current settings. Called from <see cref="Settings.Apply"/>, which every
        /// path that changes a setting already goes through — the chat commands, the settings
        /// menu, the mod API and a profile change.
        /// </summary>
        public static void Publish(Settings settings)
        {
            if (synced == null || settings == null || applying) return;
            if (!IsServer()) return;

            try
            {
                // A reference type is sent on every assignment rather than compared, which is what
                // this wants: the settings object is mutated in place, so the reference is the
                // same one it was last time and only its contents have moved.
                synced.Value = settings;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to publish settings\n" + e);
            }
        }

        private static void Received(Settings previous, Settings value, ulong sender)
        {
            if (value == null) return;

            try
            {
                applying = true;

                // Replaced rather than merged. A field the server has never written is a field this
                // build does not have, and a partial apply would leave the two simulations
                // disagreeing about exactly the settings nobody thought to copy.
                Settings.Instance = value;
                value.Apply();

                MyLog.Default.Info("[" + Settings.Name + "] settings received from the server");
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to apply received settings\n" + e);
            }
            finally
            {
                applying = false;
            }
        }

        private static bool IsServer()
        {
            try
            {
                return MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer;
            }
            catch
            {
                return false;
            }
        }
    }
}
