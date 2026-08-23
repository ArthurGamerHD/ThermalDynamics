using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The protocol around the packet**: which grid a message is about, when a server sends one,
    /// when a client asks for one, and what is dropped when nobody is there any more.
    ///
    /// <para>
    /// `HotTailTests` covers the packet itself. This covers everything the transport decides —
    /// and it is here rather than in a session because a session is the one place none of it can
    /// be checked. Registration and addressing are host code; the policy is not, so the policy
    /// lives in `Core` where it can be run (`C5`).
    /// </para>
    ///
    /// <para>
    /// Two of these are about failures that are silent by construction. A message with no grid on
    /// it would be applied to whichever hull received it, because block positions are only an
    /// identity inside a grid. And a ledger that is never pruned costs one entry per grid that has
    /// ever existed per player who has ever joined — invisible in a session with one player, and a
    /// leak on the servers this mod is for. See [backlog.md](../../docs/backlog.md) `B30`.
    /// </para>
    /// </summary>
    public class HotTailSyncTests
    {
        private static List<StoredTemperature> Hull(int blocks)
        {
            List<StoredTemperature> hull = new List<StoredTemperature>(blocks);

            for (int i = 0; i < blocks; i++)
            {
                hull.Add(new StoredTemperature(new Vector3I(i, i / 7, i / 53), 300f + i * 0.1f));
            }

            return hull;
        }

        // ---- the envelope ----------------------------------------------------------------------

        [Fact]
        public void AMessageSaysWhichGridItIsAbout()
        {
            // The whole reason the envelope exists. Block positions repeat on every grid in the
            // world, so a packet without a grid id is one that could be applied to the wrong hull —
            // and every record of it would land, because every position is a legal position.
            List<StoredTemperature> hull = Hull(4);
            List<StoredTemperature> got = new List<StoredTemperature>();

            byte[] message = HotTailMessage.EncodeTemperatures(
                HotTailKind.Band, 1234567890123L, hull, 0, hull.Count);

            long gridId;
            Assert.Equal(HotTailKind.Band, HotTailMessage.TryDecode(message, out gridId, got));
            Assert.Equal(1234567890123L, gridId);
            Assert.Equal(hull.Count, got.Count);
        }

        [Fact]
        public void AHullSnapshotIsNotABandUpdate()
        {
            // They carry identical records, and a client has to tell them apart: one ends the
            // asking and the other does not. A band update taken for a snapshot is a client that
            // stops asking for the thing measured to be what actually fixes it.
            List<StoredTemperature> hull = Hull(3);
            List<StoredTemperature> got = new List<StoredTemperature>();
            long gridId;

            byte[] snapshot = HotTailMessage.EncodeTemperatures(HotTailKind.HullSnapshot, 7L, hull, 0, 3);
            byte[] band = HotTailMessage.EncodeTemperatures(HotTailKind.Band, 7L, hull, 0, 3);

            Assert.Equal(HotTailKind.HullSnapshot, HotTailMessage.TryDecode(snapshot, out gridId, got));
            Assert.Equal(HotTailKind.Band, HotTailMessage.TryDecode(band, out gridId, got));
            Assert.Equal(snapshot.Length, band.Length);
        }

        [Fact]
        public void ARequestCarriesAGridAndNothingElse()
        {
            List<StoredTemperature> got = new List<StoredTemperature>();
            byte[] message = HotTailMessage.EncodeSnapshotRequest(-99L);

            long gridId;
            Assert.Equal(HotTailKind.SnapshotRequest, HotTailMessage.TryDecode(message, out gridId, got));
            Assert.Equal(-99L, gridId);
            Assert.Empty(got);
            Assert.Equal(HotTailMessage.HeaderSize, message.Length);
        }

        [Fact]
        public void NothingUnrecognisedIsApplied()
        {
            // These bytes are written onto a live simulation, so a reader that decoded what it
            // could would be a hull moved to values nobody sent.
            List<StoredTemperature> hull = Hull(6);
            List<StoredTemperature> got = new List<StoredTemperature>();
            long gridId;

            byte[] good = HotTailMessage.EncodeTemperatures(HotTailKind.Band, 5L, hull, 0, 6);

            byte[] wrongMarker = (byte[])good.Clone();
            wrongMarker[0] = 0x00;
            Assert.Equal(HotTailKind.Unknown, HotTailMessage.TryDecode(wrongMarker, out gridId, got));
            Assert.Empty(got);

            byte[] wrongKind = (byte[])good.Clone();
            wrongKind[1] = 0x7F;
            Assert.Equal(HotTailKind.Unknown, HotTailMessage.TryDecode(wrongKind, out gridId, got));
            Assert.Empty(got);

            byte[] truncated = new byte[good.Length - 1];
            System.Array.Copy(good, truncated, truncated.Length);
            Assert.Equal(HotTailKind.Unknown, HotTailMessage.TryDecode(truncated, out gridId, got));
            Assert.Empty(got);

            Assert.Equal(HotTailKind.Unknown, HotTailMessage.TryDecode(null, out gridId, got));
            Assert.Equal(HotTailKind.Unknown, HotTailMessage.TryDecode(new byte[3], out gridId, got));
        }

        [Fact]
        public void ARequestWithAPayloadIsNotARequest()
        {
            // A message some other build wrote. Reading the part that looks familiar is how a
            // reader acts on something it did not understand.
            byte[] message = new byte[HotTailMessage.HeaderSize + 4];
            byte[] real = HotTailMessage.EncodeSnapshotRequest(3L);
            System.Array.Copy(real, message, real.Length);

            long gridId;
            Assert.Equal(HotTailKind.Unknown,
                HotTailMessage.TryDecode(message, out gridId, new List<StoredTemperature>()));
        }

        [Fact]
        public void AKindThatIsNotTemperaturesIsNotEncodedAsThem()
        {
            List<StoredTemperature> hull = Hull(2);

            Assert.Null(HotTailMessage.EncodeTemperatures(HotTailKind.SnapshotRequest, 1L, hull, 0, 2));
            Assert.Null(HotTailMessage.EncodeTemperatures(HotTailKind.Unknown, 1L, hull, 0, 2));
        }

        // ---- slicing a hull too large for one message -------------------------------------------

        [Fact]
        public void EverySliceIsAWholeMessageAndTheSlicesAreTheHull()
        {
            // A million-block station does not fit in one message and there is no reassembly: each
            // slice is complete, applied on arrival, and a lost one costs its own records rather
            // than the hull.
            int blocks = HotTailMessage.RecordsPerMessage * 2 + 37;
            List<StoredTemperature> hull = Hull(blocks);
            List<StoredTemperature> rebuilt = new List<StoredTemperature>();
            List<StoredTemperature> got = new List<StoredTemperature>();

            int messages = 0;
            for (int offset = 0; offset < blocks; offset += HotTailMessage.RecordsPerMessage)
            {
                int count = blocks - offset;
                if (count > HotTailMessage.RecordsPerMessage) count = HotTailMessage.RecordsPerMessage;

                byte[] message = HotTailMessage.EncodeTemperatures(
                    HotTailKind.HullSnapshot, 42L, hull, offset, count);

                long gridId;
                Assert.Equal(HotTailKind.HullSnapshot,
                    HotTailMessage.TryDecode(message, out gridId, got));
                Assert.Equal(42L, gridId);
                Assert.Equal(count, got.Count);
                Assert.Equal(HotTailMessage.SizeOf(count), message.Length);

                rebuilt.AddRange(got);
                messages++;
            }

            Assert.Equal(HotTailMessage.Messages(blocks), messages);
            Assert.Equal(blocks, rebuilt.Count);

            for (int i = 0; i < blocks; i++)
            {
                Assert.Equal(hull[i].Position, rebuilt[i].Position);
                Assert.Equal(hull[i].Temperature, rebuilt[i].Temperature, 1);
            }
        }

        [Fact]
        public void ASliceOffTheEndIsClippedRatherThanThrown()
        {
            // The whitelist does not admit the out-of-range exception types, so bounds are checked
            // rather than caught — see development.md. An overrun here would be a mod that stops
            // loading in a session and builds clean here.
            List<StoredTemperature> hull = Hull(5);

            Assert.Equal(HotTailCodec.SizeOf(2), HotTailCodec.Encode(hull, 3, 40).Length);
            Assert.Equal(HotTailCodec.SizeOf(0), HotTailCodec.Encode(hull, 9, 4).Length);
            Assert.Equal(HotTailCodec.SizeOf(0), HotTailCodec.Encode(hull, -3, -1).Length);
            Assert.Equal(HotTailCodec.SizeOf(5), HotTailCodec.Encode(hull, -3, 500).Length);
        }

        [Fact]
        public void ASliceHoldsTheSameRecordsAsAWholePacketOfThoseRecords()
        {
            List<StoredTemperature> hull = Hull(20);
            List<StoredTemperature> middle = hull.GetRange(6, 7);

            byte[] sliced = HotTailCodec.Encode(hull, 6, 7);
            byte[] whole = HotTailCodec.Encode(middle);

            Assert.Equal(whole.Length, sliced.Length);
            for (int i = 0; i < whole.Length; i++) Assert.Equal(whole[i], sliced[i]);
        }

        [Fact]
        public void AnEmptySelectionIsOneMessage()
        {
            // Not zero: the count is what a caller loops on, and a zero would be a hull nobody ever
            // sends anything about.
            Assert.Equal(1, HotTailMessage.Messages(0));
            Assert.Equal(1, HotTailCodec.Packets(0, 2048));
            Assert.Equal(1, HotTailCodec.Packets(2048, 2048));
            Assert.Equal(2, HotTailCodec.Packets(2049, 2048));
            Assert.Equal(1, HotTailCodec.Packets(5, 0));
        }

        // ---- when the server sends --------------------------------------------------------------

        [Fact]
        public void NothingIsDueBeforeTheIntervalAndTheBandIsDueAfterIt()
        {
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.IntervalSeconds = 5f;

            schedule.Advance(4.9f);
            Assert.Equal(HotTailSend.Nothing, schedule.Next());

            schedule.Advance(0.2f);
            Assert.Equal(HotTailSend.Band, schedule.Next());

            // And the interval restarts, rather than firing every pass once it is overdue.
            Assert.Equal(HotTailSend.Nothing, schedule.Next());
        }

        [Fact]
        public void TheHullOutranksTheBandAndResetsTheInterval()
        {
            // A snapshot states every block a band update would have carried, so a band sent behind
            // it is bytes for values the client already holds.
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.IntervalSeconds = 5f;

            schedule.Advance(60f);
            Assert.True(schedule.RequestSnapshot());

            Assert.Equal(HotTailSend.Snapshot, schedule.Next());
            Assert.Equal(HotTailSend.Nothing, schedule.Next());

            schedule.Advance(5f);
            Assert.Equal(HotTailSend.Band, schedule.Next());
        }

        [Fact]
        public void AskingTwiceInsideTheCooldownIsRefused()
        {
            // The snapshot is the one large thing this protocol sends — 94 KB on a 9,430-node hull.
            // A client asking in a loop would otherwise be a client that makes the server transmit
            // a hull per frame.
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.SnapshotCooldownSeconds = 5f;

            Assert.True(schedule.RequestSnapshot());

            // Outstanding, so a repeat before it has even been served is not a second snapshot.
            Assert.False(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());

            Assert.False(schedule.RequestSnapshot());
            schedule.Advance(4f);
            Assert.False(schedule.RequestSnapshot());

            schedule.Advance(2f);
            Assert.True(schedule.RequestSnapshot());
        }

        [Fact]
        public void AnHonestRetryIsAnsweredAndAFloodIsNot()
        {
            // The cooldown is shorter than the retry, so a client whose request or answer was lost
            // is served on its next attempt rather than punished for the loss. If this ever
            // inverted, a lossy link would become a client that can never be corrected.
            Assert.True(HotTailSchedule.DefaultSnapshotCooldownSeconds < HotTailSchedule.RetrySeconds);

            HotTailSchedule schedule = new HotTailSchedule();
            Assert.True(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());

            schedule.Advance(HotTailSchedule.RetrySeconds);
            Assert.True(schedule.RequestSnapshot());
        }

        [Fact]
        public void AnIntervalOfZeroStopsTheBandAndNotTheHull()
        {
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.IntervalSeconds = 0f;

            schedule.Advance(600f);
            Assert.Equal(HotTailSend.Nothing, schedule.Next());

            Assert.True(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());
        }

        // ---- when the client asks ---------------------------------------------------------------

        [Fact]
        public void AClientAsksAtOnceAndThenBacksOff()
        {
            HotTailRequest request = new HotTailRequest();
            request.RetrySeconds = 8f;
            request.MaxRetrySeconds = 32f;

            Assert.True(request.ShouldAsk());
            Assert.False(request.ShouldAsk());

            request.Advance(7.9f);
            Assert.False(request.ShouldAsk());

            request.Advance(0.2f);
            Assert.True(request.ShouldAsk());

            request.Advance(8f);
            Assert.False(request.ShouldAsk());
            request.Advance(8f);
            Assert.True(request.ShouldAsk());

            // Doubling, and capped: a grid nobody will ever answer for costs a header a minute
            // rather than a header every eight seconds forever.
            request.Advance(31f);
            Assert.False(request.ShouldAsk());
            request.Advance(1f);
            Assert.True(request.ShouldAsk());

            request.Advance(31f);
            Assert.False(request.ShouldAsk());
            request.Advance(1f);
            Assert.True(request.ShouldAsk());
        }

        [Fact]
        public void AClientStopsAskingWhenItHasTheHull()
        {
            HotTailRequest request = new HotTailRequest();
            Assert.True(request.ShouldAsk());

            request.Answer();
            request.Advance(600f);

            Assert.False(request.ShouldAsk());
            Assert.True(request.Answered);
            Assert.Equal(1, request.Attempts);
        }

        [Fact]
        public void AClientNeverGivesUpOnAGridItStillHas()
        {
            // Giving up would leave this machine on a stale hull for the rest of the session, which
            // is exactly the defect the protocol exists to remove.
            HotTailRequest request = new HotTailRequest();

            for (int i = 0; i < 200; i++)
            {
                request.ShouldAsk();
                request.Advance(120f);
            }

            Assert.True(request.ShouldAsk());
        }

        // ---- the ledgers ------------------------------------------------------------------------

        [Fact]
        public void WhatIsOwedToAPlayerWhoLeftIsDropped()
        {
            HotTailServerState state = new HotTailServerState();

            state.Next(1UL, 100L);
            state.Next(1UL, 200L);
            state.Next(2UL, 100L);
            Assert.Equal(3, state.Tracked);

            state.Forget(new HashSet<ulong> { 1UL }, new HashSet<long> { 100L, 200L });
            Assert.Equal(1, state.Clients);
            Assert.Equal(2, state.Tracked);

            state.Forget(new HashSet<ulong> { 1UL }, new HashSet<long> { 200L });
            Assert.Equal(1, state.Tracked);

            state.Forget(new HashSet<ulong>(), new HashSet<long>());
            Assert.Equal(0, state.Clients);
            Assert.Equal(0, state.Tracked);
        }

        [Fact]
        public void AGridThatIsGoneIsForgottenAndAGridThatComesBackIsAskedAboutAgain()
        {
            HotTailClientState client = new HotTailClientState();

            Assert.True(client.ShouldAsk(9L));
            client.Answered(9L);
            Assert.True(client.HasHull(9L));
            Assert.Equal(0, client.Waiting);

            // Streamed out.
            client.Forget(new HashSet<long>());
            Assert.Equal(0, client.Tracked);

            // And back: this machine rebuilt it from whatever the engine had, which is the stale
            // state the protocol is about.
            Assert.False(client.HasHull(9L));
            Assert.True(client.ShouldAsk(9L));
            Assert.Equal(1, client.Waiting);
        }

        [Fact]
        public void TheLedgerAdvancesEverySchedulesClockIncludingOneNothingAskedAbout()
        {
            // A grid skipped by a pass — out of range, or still building — keeps its place in the
            // interval instead of restarting it, so a client that comes back into range is stated
            // on the first pass rather than after another interval.
            HotTailServerState state = new HotTailServerState();
            state.IntervalSeconds = 5f;

            Assert.Equal(HotTailSend.Nothing, state.Next(1UL, 100L));

            state.Advance(6f);
            Assert.Equal(HotTailSend.Band, state.Next(1UL, 100L));
        }

        [Fact]
        public void AnIntervalChangedMidSessionReachesASchedulePlacedBeforeIt()
        {
            HotTailServerState state = new HotTailServerState();
            state.IntervalSeconds = 60f;
            state.Next(1UL, 100L);

            state.Advance(10f);
            Assert.Equal(HotTailSend.Nothing, state.Next(1UL, 100L));

            state.IntervalSeconds = 5f;
            state.Advance(6f);
            Assert.Equal(HotTailSend.Band, state.Next(1UL, 100L));
        }

        [Fact]
        public void ARefusedRequestLeavesNothingOutstanding()
        {
            HotTailServerState state = new HotTailServerState();

            Assert.True(state.Request(3UL, 55L));
            Assert.True(state.Wants(3UL, 55L));

            Assert.False(state.Request(3UL, 55L));
            Assert.Equal(HotTailSend.Snapshot, state.Next(3UL, 55L));
            Assert.False(state.Wants(3UL, 55L));
        }

        // ---- against a real hull -----------------------------------------------------------------

        [Fact]
        public void TheWholeHullAndTheBandAreTheSameCallWithADifferentBand()
        {
            // The mod states the whole hull with a band wide enough to hold everything, which is
            // the call the join packet was measured with. Two definitions of "the whole hull" would
            // drift apart silently, and the measured protocol would stop being the shipped one.
            ThermalSettings settings = new ThermalSettings().Derive();
            ThermalSimulation simulation = Hulls.Driven(settings, 400);

            int steps = (int)(240f / settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, Worlds.Shadow());

            List<StoredTemperature> hull = new List<StoredTemperature>();
            List<StoredTemperature> band = new List<StoredTemperature>();

            simulation.ExportHotTail(float.MaxValue, 0, hull);
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, band);

            Assert.True(hull.Count > 0);
            Assert.True(band.Count <= hull.Count);

            // And the hull is every node the solver has that can fail at all, which is what makes
            // it a statement rather than a selection.
            int ratable = 0;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Thermal.CriticalTemperature > 0f) ratable++;
            }

            Assert.Equal(ratable, hull.Count);
        }

        [Fact]
        public void AHullTravelsThroughTheWireAndArrivesOnTheOtherMachine()
        {
            // The end to end shape of one join: the server states its hull, the bytes are cut into
            // messages, and a client that was somewhere else entirely takes the server's answer.
            ThermalSettings settings = new ThermalSettings().Derive();

            ThermalSimulation server = Hulls.Driven(settings, 300);
            ThermalSimulation client = Hulls.Driven(settings, 300);

            int steps = (int)(300f / settings.StepSeconds);
            for (int i = 0; i < steps; i++) server.StepExact(1, Worlds.Shadow());

            List<StoredTemperature> hull = new List<StoredTemperature>();
            server.ExportHotTail(float.MaxValue, 0, hull);

            List<StoredTemperature> got = new List<StoredTemperature>();
            int applied = 0;

            for (int offset = 0; offset < hull.Count; offset += HotTailMessage.RecordsPerMessage)
            {
                int count = hull.Count - offset;
                if (count > HotTailMessage.RecordsPerMessage) count = HotTailMessage.RecordsPerMessage;

                byte[] message = HotTailMessage.EncodeTemperatures(
                    HotTailKind.HullSnapshot, 77L, hull, offset, count);

                long gridId;
                Assert.Equal(HotTailKind.HullSnapshot,
                    HotTailMessage.TryDecode(message, out gridId, got));

                applied += client.ImportHotTail(got);
            }

            Assert.True(applied > 0, "the join packet moved nothing, so the client was already right");

            // Within the wire's own resolution, which is a tenth of a kelvin, and no further: a
            // tighter claim here would be this test asserting a precision the packet does not have.
            IList<ThermalNode> theirs = server.Solver.Nodes;
            IList<ThermalNode> ours = client.Solver.Nodes;

            float worst = 0f;
            for (int i = 0; i < theirs.Count; i++)
            {
                if (theirs[i].Thermal.CriticalTemperature <= 0f) continue;

                ThermalNode mine = ours.Count > i ? ours[i] : null;
                Assert.NotNull(mine);

                float error = System.Math.Abs(theirs[i].Temperature - mine.Temperature);
                if (error > worst) worst = error;
            }

            Assert.True(worst <= HotTailCodec.TemperatureStep,
                "the worst block was " + worst + " K out after the whole hull was stated");
        }
    }
}
