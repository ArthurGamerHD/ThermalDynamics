using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Replicates block temperatures from the server to its clients: the whole hull once, when a
    /// client has built the grid, then the near-critical band on an interval.
    ///
    /// <para>
    /// **The defect this closes.** Temperatures were not replicated at all. A client re-simulates
    /// from its own inputs and a client joining mid-session starts from whatever the world was last
    /// saved at, so for 150 to 305 s it shows a block on the wrong side of its own critical
    /// temperature — against a damage event that runs a median 37 s from the load to the first
    /// block lost. The readout was wrong about the one thing it is for, for longer than the thing
    /// lasted. See known-issues.md and
    /// backlog.md `B30`.
    /// </para>
    ///
    /// <para>
    /// **The protocol was measured before it was built**, by `ClientDriftLab`, and both halves of
    /// it are load-bearing: the band alone leaves 145 s of 560 s misread whatever the interval,
    /// because a corrected block conducts to neighbours that are still stale; the hull once and
    /// then the band takes it to nothing. <see cref="HotTailSchedule"/> is that decision,
    /// <see cref="HotTailMessage"/> is the wire, and this class is the session it happens in.
    /// </para>
    ///
    /// <para>
    /// **On its own secure channel, like <see cref="SettingsRequests"/> and for the same reason.**
    /// The shared channel's sender id is a field the sender wrote; the engine's secure handler
    /// supplies one the transport verified and says whether a message came from the server. Both
    /// matter here: a client must not be able to write temperatures onto another client's
    /// simulation, and the server must not serve a snapshot to a player id somebody else named.
    /// </para>
    ///
    /// <para>
    /// **What none of this is tested by.** Registration, addressing and the send itself are host
    /// code that cannot run outside a session; what can be tested is the policy, and it is —
    /// `HotTailSyncTests` covers the schedule, the backoff and the wire. What is left is checked by
    /// eye through <c>/thermal sync</c>, which prints this class's counters on both machines.
    /// </para>
    /// </summary>
    public static class ThermalGridSync
    {
        /// <summary>
        /// A channel of this mod's own, two above the shared one. Separate from
        /// <see cref="SettingsRequests"/>'s: one id registered with two handlers delivers every
        /// message twice.
        /// </summary>
        public const ushort ChannelId = Session.ModID + 2;

        /// <summary>
        /// Frames between passes. Half a second at 60 fps.
        ///
        /// The pass costs a walk over the live grids and, for each one that is due, a walk over its
        /// nodes. It is deliberately coarser than the frame and finer than the interval it serves,
        /// so the shipped five seconds is delivered to within half a second without a per-frame
        /// cost for something that happens every tenth pass.
        /// </summary>
        public const int PassFrames = 30;

        private static bool registered;
        private static int frames;

        /// <summary>What the server still owes each client about each grid. Server side.</summary>
        private static readonly HotTailServerState Owed = new HotTailServerState();

        /// <summary>Which grids this machine is still waiting to be told about. Client side.</summary>
        private static readonly HotTailClientState Asked = new HotTailClientState();

        private static readonly List<IMyPlayer> Players = new List<IMyPlayer>();
        private static readonly List<StoredTemperature> Selection = new List<StoredTemperature>();
        private static readonly List<StoredTemperature> Received = new List<StoredTemperature>();
        private static readonly List<ulong> WantSnapshot = new List<ulong>();
        private static readonly List<ulong> WantBand = new List<ulong>();
        private static readonly HashSet<ulong> Present = new HashSet<ulong>();
        private static readonly HashSet<long> LiveIds = new HashSet<long>();

        // ---- counters, for /thermal sync -------------------------------------------------------

        /// <summary>Hull snapshots this machine has sent, in messages rather than in hulls.</summary>
        public static int SnapshotsSent;

        /// <summary>Band updates this machine has sent.</summary>
        public static int BandsSent;

        /// <summary>Bytes this machine has put on the wire, envelope included.</summary>
        public static long BytesSent;

        /// <summary>Snapshot requests refused: asked for twice inside the cooldown.</summary>
        public static int RequestsRefused;

        /// <summary>Requests this machine has sent, repeats included.</summary>
        public static int RequestsSent;

        /// <summary>Messages this machine has applied to a live simulation.</summary>
        public static int MessagesApplied;

        /// <summary>Blocks those messages moved. A block already right is not counted, and is not written.</summary>
        public static int BlocksApplied;

        /// <summary>Messages dropped: unreadable, not from the server, or about a grid not here.</summary>
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

        /// <summary>
        /// One frame of the session. Does its work every <see cref="PassFrames"/>th call and
        /// nothing at all in single player, where there is no second machine to disagree with.
        /// </summary>
        public static void Tick()
        {
            if (!registered) return;
            if (++frames < PassFrames) return;

            // Real seconds, which are simulated seconds now that the world carries no time
            // multiplier: the dial that scaled these was `SimulationSpeed`, and it only ever
            // multiplied `Frequency`.
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

        // ---- the server ------------------------------------------------------------------------

        /// <summary>
        /// States every live grid to every client near it that is owed something.
        ///
        /// **Near it**, because a grid outside a client's sync distance is a grid that client has
        /// not built and cannot draw: sending its hull would be bandwidth spent on records the
        /// receiver drops. A client that arrives asks, which is the whole handshake.
        /// </summary>
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

                    // The host of a listen server is the server: its simulation is the one being
                    // replicated, and sending it to itself would be a machine correcting itself
                    // with its own answer.
                    if (client == MyAPIGateway.Multiplayer.MyId) continue;

                    // Out of range is not a reason to forget what is owed, only to defer it: the
                    // schedule keeps counting, so a client that flies back into range is stated on
                    // the first pass rather than after another interval.
                    if (Vector3D.DistanceSquared(player.GetPosition(), at) > range * range) continue;

                    HotTailSend send = Owed.Next(client, gridId);
                    if (send == HotTailSend.Snapshot) WantSnapshot.Add(client);
                    else if (send == HotTailSend.Band) WantBand.Add(client);
                }

                // Selected once per grid rather than once per recipient: the answer is the same for
                // everyone, and the selection is a walk over every node.
                if (WantSnapshot.Count > 0)
                {
                    Export(thermals, HotTailKind.HullSnapshot);
                    Send(HotTailKind.HullSnapshot, gridId, WantSnapshot);
                }

                if (WantBand.Count > 0)
                {
                    Export(thermals, HotTailKind.Band);

                    // An empty band is not sent. A hull with nothing near failing has nothing to
                    // correct, and the alternative is a header per grid per client per interval for
                    // the whole population of a server, forever.
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

        /// <summary>
        /// Fills the selection with what one kind of message carries.
        ///
        /// **The whole hull is the same call with a band wide enough to hold everything**, which is
        /// how the lab measured the join packet — stating it twice would let the shipped protocol
        /// and the measured one drift apart silently (`P5`).
        /// </summary>
        private static void Export(ThermalGrid thermals, HotTailKind kind)
        {
            float band = kind == HotTailKind.HullSnapshot
                ? float.MaxValue
                : Incandescence.GlowBandKelvin;

            // No budget. Capping an update is measured to make the misreading worse rather than
            // cheaper, because the blocks a cap drops are exactly the ones the correction is for;
            // what a packet too large for the transport gets instead is more packets.
            thermals.Simulation.ExportHotTail(band, 0, Selection);
        }

        /// <summary>
        /// Puts the current selection on the wire, in as many messages as it takes, to each
        /// recipient.
        /// </summary>
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
                    // Reliable: a dropped correction is a client left wrong about a block until the
                    // next interval, which is the defect this exists to remove.
                    MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, payload, recipients[r], true);
                    BytesSent += payload.Length;
                }

                if (kind == HotTailKind.HullSnapshot) SnapshotsSent += recipients.Count;
                else BandsSent += recipients.Count;
            }
        }

        /// <summary>
        /// How far a client is expected to have built a grid, in metres.
        ///
        /// The world's own sync distance, because that is the distance the engine itself streams
        /// entities over: beyond it there is no grid on the client to correct.
        /// </summary>
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
                // A world with no session settings is a world with no clients either.
            }

            return 10000d;
        }

        // ---- the client ------------------------------------------------------------------------

        /// <summary>
        /// Asks the server to state each grid this machine has built and has not been told about.
        ///
        /// **The client asks rather than the server offering**, because only this machine knows
        /// when it has finished building a grid, and a snapshot that arrives first is a snapshot
        /// every record of which is dropped for naming a block that does not exist yet.
        /// </summary>
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

        // ---- arrival ---------------------------------------------------------------------------

        private static void Handle(ushort channel, byte[] payload, ulong sender, bool fromServer)
        {
            try
            {
                long gridId;
                HotTailKind kind = HotTailMessage.TryDecode(payload, out gridId, Received);

                if (kind == HotTailKind.SnapshotRequest)
                {
                    // Only the server serves. A request that reaches a client is a client telling
                    // another client what to transmit.
                    if (MyAPIGateway.Multiplayer.IsServer) Requested(sender, gridId);
                    else MessagesDropped++;
                    return;
                }

                if (kind == HotTailKind.Unknown)
                {
                    MessagesDropped++;
                    return;
                }

                // Temperatures are the server's answer. One that did not come from it is a client
                // writing onto this machine's simulation.
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

        /// <summary>Serves one client's request, or refuses it. Server side.</summary>
        private static void Requested(ulong client, long gridId)
        {
            if (!Settings.Instance.EnableTemperatureSync) return;
            if (client == MyAPIGateway.Multiplayer.MyId) return;

            ThermalGrid thermals = Find(gridId);
            if (thermals == null)
            {
                // A grid this server does not have, or one that has not built itself yet. Neither
                // is answerable, and the client's own backoff is what asks again.
                MessagesDropped++;
                return;
            }

            Owed.IntervalSeconds = Settings.Instance.TemperatureSyncInterval;

            if (!Owed.Request(client, gridId))
            {
                RequestsRefused++;
                return;
            }

            // Served here rather than on the next pass, because the wait a client is in is exactly
            // the wait this exists to end.
            if (Owed.Next(client, gridId) != HotTailSend.Snapshot) return;

            Export(thermals, HotTailKind.HullSnapshot);

            WantSnapshot.Clear();
            WantSnapshot.Add(client);
            Send(HotTailKind.HullSnapshot, gridId, WantSnapshot);
            WantSnapshot.Clear();
            Selection.Clear();
        }

        /// <summary>Writes the server's answer onto this machine's simulation. Client side.</summary>
        private static void Apply(HotTailKind kind, long gridId)
        {
            ThermalGrid thermals = Find(gridId);
            if (thermals == null)
            {
                // A grid that has streamed out, or one still building. The records name blocks this
                // machine does not have, and applying them would write nothing anyway.
                MessagesDropped++;
                return;
            }

            MessagesApplied++;
            BlocksApplied += thermals.Simulation.ImportHotTail(Received);

            if (kind == HotTailKind.HullSnapshot) Asked.Answered(gridId);
        }

        /// <summary>The live grid with this entity id, or null.</summary>
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

        /// <summary>
        /// One line of what this machine has sent and applied, for comparing the two sides by eye.
        ///
        /// Registration and addressing cannot be tested outside a session, so this is how they get
        /// checked: run <c>/thermal sync</c> on the server and on a client, and the counters say
        /// which half is not moving.
        /// </summary>
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
