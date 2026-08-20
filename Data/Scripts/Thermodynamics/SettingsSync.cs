using System;
using System.Collections.Generic;
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
                // Seeded with the settings already in hand, and never with null: a null value is
                // never transmitted, because there is nothing to encode. A property left at null
                // answers a joining client's fetch with silence, and since the server only
                // publishes when a setting *changes*, a world where nobody touches the config
                // would leave every client on the shipped defaults for the whole session — which
                // is the failure this class exists to fix, reintroduced one layer up.
                //
                // Server to client only: the config is the server's, and a client editing it would
                // be editing its own copy of someone else's world. Fetch is exempt from that rule
                // inside the API, which is what lets a joining client ask at all.
                synced = new NetSync<Settings>(
                    session, TransferType.ServerToClient, Settings.EnsureLoaded(), true);

                synced.ValueChangedByNetwork += Received;

                // Answered from the live object rather than from whatever was last assigned, so a
                // fetch cannot hand out a stale copy if some path ever mutates the settings
                // without going through Apply.
                synced.BeforeFetchRequestResponse += Refresh;
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

        /// <summary>
        /// Points the property at the current settings before a fetch is answered. Server side
        /// only; a client answering a fetch would be handing out its own copy.
        /// </summary>
        private static void Refresh(ulong sender)
        {
            if (synced == null || !IsServer() || Settings.Instance == null) return;

            try
            {
                // SetValue rather than Value: this is a read being served, not a change being
                // announced, and broadcasting here would send the settings to everyone every time
                // one player joined.
                synced.SetValue(Settings.Instance, SyncType.None);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to refresh settings for fetch\n" + e);
            }
        }

        private static void Received(Settings previous, Settings value, ulong sender)
        {
            if (value == null) return;

            try
            {
                applying = true;

                // Copied into the live object rather than swapped for it, and this is not a
                // preference. A grid takes its core settings once, at construction —
                // `new ThermalSimulation(Settings.Instance.ToCore(), Model)` — and notices later
                // changes only through that object's Revision. Replacing Settings.Instance leaves
                // every grid already on the client holding the settings it was born with, which
                // is a subtler version of the divergence this class exists to close.
                //
                // Names() is the same list the settings menu copies through: every setting a
                // player or mod may change at runtime. It includes the four presentation switches,
                // which a client owns for itself, so those are skipped — a server has no business
                // deciding which overlay is on someone else's screen.
                Settings target = Settings.Instance;
                if (target == null)
                {
                    Settings.Instance = value;
                    value.Apply();
                }
                else
                {
                    List<string> names = Settings.Names();
                    for (int i = 0; i < names.Count; i++)
                    {
                        string name = names[i];
                        if (Settings.ClientOwned.Contains(name)) continue;

                        target.SetValue(name, value.GetValue(name));
                    }

                    // Bumps the revision the simulations watch, so every grid picks the new values
                    // up on its next step without being rebuilt.
                    target.Apply();
                }

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
