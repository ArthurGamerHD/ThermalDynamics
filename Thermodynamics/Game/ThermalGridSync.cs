using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public static class ThermalGridSync
    {
        public const ushort ChannelId = Session.ModID + 2;

        public const int PassFrames = 30;

        private static bool registered;
        private static int frames;

        private static readonly HotTailServerState Owed = new HotTailServerState();

        private static readonly HotTailClientState Asked = new HotTailClientState();

        private static readonly List<IMyPlayer> Players = new List<IMyPlayer>();
        private static readonly List<StoredTemperature> Selection = new List<StoredTemperature>();
        private static readonly List<StoredTemperature> Received = new List<StoredTemperature>();
        private static readonly List<ulong> WantSnapshot = new List<ulong>();
        private static readonly List<ulong> WantBand = new List<ulong>();
        private static readonly HashSet<ulong> Present = new HashSet<ulong>();
        private static readonly HashSet<long> LiveIds = new HashSet<long>();


        public static int SnapshotsSent;

        public static int BandsSent;

        public static long BytesSent;

        public static int RequestsRefused;

        public static int RequestsSent;

        public static int MessagesApplied;

        public static int BlocksApplied;

        public static int MessagesDropped;

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
                MyLog.Default.Info("[" + Settings.Name + "] temperature sync unavailable\n" + e);
            }
        }

        public static void Unregister()
        {
            Owed.Clear();
            Asked.Clear();
            SnapshotsSent = 0;
            BandsSent = 0;
            BytesSent = 0;
            RequestsRefused = 0;
            RequestsSent = 0;
            MessagesApplied = 0;
            BlocksApplied = 0;
            MessagesDropped = 0;

            if (!registered) return;
            registered = false;

            try
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, Handle);
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to unregister temperature sync\n" + e);
            }
        }

        public static void Tick()
        {
            if (!registered) return;
            if (++frames < PassFrames) return;

            float seconds = frames / 60f;
            frames = 0;

            try
            {
                if (!Settings.Instance.EnableTemperatureSync) return;
                if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive) return;

                if (MyAPIGateway.Multiplayer.IsServer) Serve(seconds);
                else Ask(seconds);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGridSync.Tick", e);
            }
        }


        private static void Serve(float seconds)
        {
            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players);

            Present.Clear();
            for (int p = 0; p < Players.Count; p++)
            {
                if (Players[p] != null) Present.Add(Players[p].SteamUserId);
            }

            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;

            LiveIds.Clear();
            for (int g = 0; g < grids.Count; g++)
            {
                if (grids[g] != null && grids[g].Grid != null) LiveIds.Add(grids[g].Grid.EntityId);
            }

            Owed.IntervalSeconds = Settings.Instance.TemperatureSyncInterval;
            Owed.Forget(Present, LiveIds);
            Owed.Advance(seconds);

            double range = SyncRange();

            for (int g = 0; g < grids.Count; g++)
            {
                ThermalGrid thermals = grids[g];
                if (thermals == null || !thermals.Started || thermals.Grid == null) continue;

                long gridId = thermals.Grid.EntityId;
                Vector3D at = thermals.Grid.PositionComp.GetPosition();

                WantSnapshot.Clear();
                WantBand.Clear();

                for (int p = 0; p < Players.Count; p++)
                {
                    IMyPlayer player = Players[p];
                    if (player == null) continue;

                    ulong client = player.SteamUserId;

                    if (client == MyAPIGateway.Multiplayer.MyId) continue;

                    if (Vector3D.DistanceSquared(player.GetPosition(), at) > range * range) continue;

                    HotTailSend send = Owed.Next(client, gridId);
                    if (send == HotTailSend.Snapshot) WantSnapshot.Add(client);
                    else if (send == HotTailSend.Band) WantBand.Add(client);
                }

                if (WantSnapshot.Count > 0)
                {
                    Export(thermals, HotTailKind.HullSnapshot);
                    Send(HotTailKind.HullSnapshot, gridId, WantSnapshot);
                }

                if (WantBand.Count > 0)
                {
                    Export(thermals, HotTailKind.Band);

                    if (Selection.Count > 0) Send(HotTailKind.Band, gridId, WantBand);
                }
            }

            WantSnapshot.Clear();
            WantBand.Clear();
            Selection.Clear();
            Players.Clear();
            Present.Clear();
            LiveIds.Clear();
        }

        private static void Export(ThermalGrid thermals, HotTailKind kind)
        {
            float band = kind == HotTailKind.HullSnapshot
                ? float.MaxValue
                : Incandescence.GlowBandKelvin;

            thermals.Simulation.ExportHotTail(band, 0, Selection);
        }

        private static void Send(HotTailKind kind, long gridId, List<ulong> recipients)
        {
            int per = HotTailMessage.RecordsPerMessage;

            for (int offset = 0; offset < Selection.Count; offset += per)
            {
                int count = Selection.Count - offset;
                if (count > per) count = per;

                byte[] payload = HotTailMessage.EncodeTemperatures(kind, gridId, Selection, offset, count);
                if (payload == null) return;

                for (int r = 0; r < recipients.Count; r++)
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, payload, recipients[r], true);
                    BytesSent += payload.Length;
                }

                if (kind == HotTailKind.HullSnapshot) SnapshotsSent += recipients.Count;
                else BandsSent += recipients.Count;
            }
        }

        private static double SyncRange()
        {
            try
            {
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.SessionSettings != null)
                {
                    int sync = MyAPIGateway.Session.SessionSettings.SyncDistance;
                    if (sync > 0) return sync;
                }
            }
            catch
            {
            }

            return 10000d;
        }


        private static void Ask(float seconds)
        {
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;

            LiveIds.Clear();
            for (int g = 0; g < grids.Count; g++)
            {
                ThermalGrid thermals = grids[g];
                if (thermals != null && thermals.Started && thermals.Grid != null)
                {
                    LiveIds.Add(thermals.Grid.EntityId);
                }
            }

            Asked.Forget(LiveIds);
            Asked.Advance(seconds);

            foreach (long gridId in LiveIds)
            {
                if (!Asked.ShouldAsk(gridId)) continue;

                byte[] payload = HotTailMessage.EncodeSnapshotRequest(gridId);
                MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, payload, true);
                RequestsSent++;
                BytesSent += payload.Length;
            }

            LiveIds.Clear();
        }


        private static void Handle(ushort channel, byte[] payload, ulong sender, bool fromServer)
        {
            try
            {
                long gridId;
                HotTailKind kind = HotTailMessage.TryDecode(payload, out gridId, Received);

                if (kind == HotTailKind.SnapshotRequest)
                {
                    if (MyAPIGateway.Multiplayer.IsServer) Requested(sender, gridId);
                    else MessagesDropped++;
                    return;
                }

                if (kind == HotTailKind.Unknown)
                {
                    MessagesDropped++;
                    return;
                }

                if (!fromServer)
                {
                    MessagesDropped++;
                    return;
                }

                Apply(kind, gridId);
            }
            catch (Exception e)
            {
                MessagesDropped++;
                Telemetry.Exception("ThermalGridSync.Handle", e);
            }
        }

        private static void Requested(ulong client, long gridId)
        {
            if (!Settings.Instance.EnableTemperatureSync) return;
            if (client == MyAPIGateway.Multiplayer.MyId) return;

            ThermalGrid thermals = Find(gridId);
            if (thermals == null)
            {
                MessagesDropped++;
                return;
            }

            Owed.IntervalSeconds = Settings.Instance.TemperatureSyncInterval;

            if (!Owed.Request(client, gridId))
            {
                RequestsRefused++;
                return;
            }

            if (Owed.Next(client, gridId) != HotTailSend.Snapshot) return;

            Export(thermals, HotTailKind.HullSnapshot);

            WantSnapshot.Clear();
            WantSnapshot.Add(client);
            Send(HotTailKind.HullSnapshot, gridId, WantSnapshot);
            WantSnapshot.Clear();
            Selection.Clear();
        }

        private static void Apply(HotTailKind kind, long gridId)
        {
            ThermalGrid thermals = Find(gridId);
            if (thermals == null)
            {
                MessagesDropped++;
                return;
            }

            MessagesApplied++;
            BlocksApplied += thermals.Simulation.ImportHotTail(Received);

            if (kind == HotTailKind.HullSnapshot) Asked.Answered(gridId);
        }

        private static ThermalGrid Find(long gridId)
        {
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;

            for (int g = 0; g < grids.Count; g++)
            {
                ThermalGrid thermals = grids[g];
                if (thermals == null || !thermals.Started || thermals.Grid == null) continue;
                if (thermals.Grid.EntityId == gridId) return thermals;
            }

            return null;
        }

        public static string Report()
        {
            if (!registered) return "temperature sync is not registered";

            if (!Settings.Instance.EnableTemperatureSync)
            {
                return "temperature sync OFF (EnableTemperatureSync)";
            }

            return "temperatures: sent " + SnapshotsSent + " hull + " + BandsSent + " band in "
                + (BytesSent / 1024) + " KB to " + Owed.Clients + " clients over "
                + Owed.Tracked + " pairs | asked " + RequestsSent
                + ", refused " + RequestsRefused + ", waiting on " + Asked.Waiting
                + " of " + Asked.Tracked
                + " | applied " + MessagesApplied + " messages, " + BlocksApplied + " blocks"
                + ", dropped " + MessagesDropped;
        }
    }
}
