using System;
using System.Collections.Generic;
using System.Globalization;
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

                // The menu marks what differs from the shipped defaults, so values arriving from
                // the server have to reach it too — otherwise an open menu shows the old marks.
                ThermalSettingsMenu.Refresh();

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

        /// <summary>
        /// A short digest of every replicated setting, for comparing two machines by eye.
        ///
        /// The whole point of this class is that a client and a server hold the same numbers, and
        /// nothing about it can be tested outside a live session: it is all host code. One string
        /// that must match, read on each side, is the cheapest way to check it — and the cheapest
        /// way to tell whether a report of "the client feels different" is this or something else.
        ///
        /// Order comes from <see cref="Settings.Names"/> and the client-owned switches are left
        /// out, so the digest covers exactly what is replicated and nothing that is allowed to
        /// differ.
        /// </summary>
        public static string Fingerprint()
        {
            Settings settings = Settings.Instance;
            if (settings == null) return "none";

            // FNV-1a over each replicated name and its value, rounded to four decimals so a float
            // that survived a round trip with a last-bit difference still agrees.
            unchecked
            {
                uint hash = 2166136261;
                List<string> names = Settings.Names();

                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    if (Settings.ClientOwned.Contains(name)) continue;

                    string entry = name + "="
                        + settings.GetValue(name).ToString("n4", CultureInfo.InvariantCulture) + ";";

                    for (int c = 0; c < entry.Length; c++)
                    {
                        hash ^= entry[c];
                        hash *= 16777619;
                    }
                }

                return hash.ToString("x8");
            }
        }

        /// <summary>How many settings the digest covers.</summary>
        public static int ReplicatedCount()
        {
            List<string> names = Settings.Names();
            int count = 0;
            for (int i = 0; i < names.Count; i++)
            {
                if (!Settings.ClientOwned.Contains(names[i])) count++;
            }
            return count;
        }

        /// <summary>Whether the property exists and holds something that could be sent.</summary>
        public static bool Ready
        {
            get { return synced != null && synced.Value != null; }
        }

        /// <summary>
        /// Asks the server for the current settings again. A client only fetches automatically
        /// once, when it loads, so this is the way to re-ask after a change that went missing.
        /// </summary>
        public static bool Fetch()
        {
            if (synced == null || IsServer()) return false;

            try
            {
                synced.Fetch();
                return true;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] settings fetch failed\n" + e);
                return false;
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
