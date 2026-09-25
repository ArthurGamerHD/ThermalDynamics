using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game.Components;
using VRage.Utils;

namespace Thermodynamics
{
    public static class SettingsSync
    {
        private static NetSync<Settings> synced;

        private static bool applying;

        public static bool Applying
        {
            get { return applying; }
        }

        public static void Register(MySessionComponentBase session)
        {
            if (synced != null || session == null) return;

            try
            {
                synced = new NetSync<Settings>(
                    session, TransferType.ServerToClient, Settings.EnsureLoaded(), true);

                synced.ValueChangedByNetwork += Received;

                synced.BeforeFetchRequestResponse += Refresh;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] settings sync unavailable\n" + e);
            }
        }

        public static void Publish(Settings settings)
        {
            if (synced == null || settings == null || applying) return;
            if (!IsServer()) return;

            try
            {
                synced.Value = settings;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to publish settings\n" + e);
            }
        }

        private static void Refresh(ulong sender)
        {
            if (synced == null || !IsServer() || Settings.Instance == null) return;

            try
            {
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

                    target.Apply();
                }

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

        public static string Fingerprint()
        {
            Settings settings = Settings.Instance;
            if (settings == null) return "none";

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

        public static bool Ready
        {
            get { return synced != null && synced.Value != null; }
        }

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
