using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class HotTailSyncTests
    {
/// <summary>Hull operation.</summary>
        private static List<StoredTemperature> Hull(int blocks)
        {
/// <summary>List operation.</summary>
            List<StoredTemperature> hull = new List<StoredTemperature>(blocks);

            for (int i = 0; i < blocks; i++)
            {
                hull.Add(new StoredTemperature(new Vector3I(i, i / 7, i / 53), 300f + i * 0.1f));
            }

            return hull;
        }


        [Fact]
/// <summary>AMessageSaysWhichGridItIsAbout operation.</summary>
        public void AMessageSaysWhichGridItIsAbout()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(4);
/// <summary>List operation.</summary>
            List<StoredTemperature> got = new List<StoredTemperature>();

            byte[] message = HotTailMessage.EncodeTemperatures(
                HotTailKind.Band, 1234567890123L, hull, 0, hull.Count);

            long gridId;
            Assert.Equal(HotTailKind.Band, HotTailMessage.TryDecode(message, out gridId, got));
            Assert.Equal(1234567890123L, gridId);
            Assert.Equal(hull.Count, got.Count);
        }

        [Fact]
/// <summary>AHullSnapshotIsNotABandUpdate operation.</summary>
        public void AHullSnapshotIsNotABandUpdate()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(3);
/// <summary>List operation.</summary>
            List<StoredTemperature> got = new List<StoredTemperature>();
            long gridId;

            byte[] snapshot = HotTailMessage.EncodeTemperatures(HotTailKind.HullSnapshot, 7L, hull, 0, 3);
            byte[] band = HotTailMessage.EncodeTemperatures(HotTailKind.Band, 7L, hull, 0, 3);

            Assert.Equal(HotTailKind.HullSnapshot, HotTailMessage.TryDecode(snapshot, out gridId, got));
            Assert.Equal(HotTailKind.Band, HotTailMessage.TryDecode(band, out gridId, got));
            Assert.Equal(snapshot.Length, band.Length);
        }

        [Fact]
/// <summary>ARequestCarriesAGridAndNothingElse operation.</summary>
        public void ARequestCarriesAGridAndNothingElse()
        {
/// <summary>List operation.</summary>
            List<StoredTemperature> got = new List<StoredTemperature>();
            byte[] message = HotTailMessage.EncodeSnapshotRequest(-99L);

            long gridId;
            Assert.Equal(HotTailKind.SnapshotRequest, HotTailMessage.TryDecode(message, out gridId, got));
            Assert.Equal(-99L, gridId);
            Assert.Empty(got);
            Assert.Equal(HotTailMessage.HeaderSize, message.Length);
        }

        [Fact]
/// <summary>NothingUnrecognisedIsApplied operation.</summary>
        public void NothingUnrecognisedIsApplied()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(6);
/// <summary>List operation.</summary>
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
/// <summary>ARequestWithAPayloadIsNotARequest operation.</summary>
        public void ARequestWithAPayloadIsNotARequest()
        {
            byte[] message = new byte[HotTailMessage.HeaderSize + 4];
            byte[] real = HotTailMessage.EncodeSnapshotRequest(3L);
            System.Array.Copy(real, message, real.Length);

            long gridId;
            Assert.Equal(HotTailKind.Unknown,
                HotTailMessage.TryDecode(message, out gridId, new List<StoredTemperature>()));
        }

        [Fact]
/// <summary>AKindThatIsNotTemperaturesIsNotEncodedAsThem operation.</summary>
        public void AKindThatIsNotTemperaturesIsNotEncodedAsThem()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(2);

            Assert.Null(HotTailMessage.EncodeTemperatures(HotTailKind.SnapshotRequest, 1L, hull, 0, 2));
            Assert.Null(HotTailMessage.EncodeTemperatures(HotTailKind.Unknown, 1L, hull, 0, 2));
        }


        [Fact]
/// <summary>EverySliceIsAWholeMessageAndTheSlicesAreTheHull operation.</summary>
        public void EverySliceIsAWholeMessageAndTheSlicesAreTheHull()
        {
            int blocks = HotTailMessage.RecordsPerMessage * 2 + 37;
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(blocks);
/// <summary>List operation.</summary>
            List<StoredTemperature> rebuilt = new List<StoredTemperature>();
/// <summary>List operation.</summary>
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
/// <summary>ASliceOffTheEndIsClippedRatherThanThrown operation.</summary>
        public void ASliceOffTheEndIsClippedRatherThanThrown()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(5);

            Assert.Equal(HotTailCodec.SizeOf(2), HotTailCodec.Encode(hull, 3, 40).Length);
            Assert.Equal(HotTailCodec.SizeOf(0), HotTailCodec.Encode(hull, 9, 4).Length);
            Assert.Equal(HotTailCodec.SizeOf(0), HotTailCodec.Encode(hull, -3, -1).Length);
            Assert.Equal(HotTailCodec.SizeOf(5), HotTailCodec.Encode(hull, -3, 500).Length);
        }

        [Fact]
/// <summary>ASliceHoldsTheSameRecordsAsAWholePacketOfThoseRecords operation.</summary>
        public void ASliceHoldsTheSameRecordsAsAWholePacketOfThoseRecords()
        {
/// <summary>Hull operation.</summary>
            List<StoredTemperature> hull = Hull(20);
            List<StoredTemperature> middle = hull.GetRange(6, 7);

            byte[] sliced = HotTailCodec.Encode(hull, 6, 7);
            byte[] whole = HotTailCodec.Encode(middle);

            Assert.Equal(whole.Length, sliced.Length);
            for (int i = 0; i < whole.Length; i++) Assert.Equal(whole[i], sliced[i]);
        }

        [Fact]
/// <summary>AnEmptySelectionIsOneMessage operation.</summary>
        public void AnEmptySelectionIsOneMessage()
        {
            Assert.Equal(1, HotTailMessage.Messages(0));
            Assert.Equal(1, HotTailCodec.Packets(0, 2048));
            Assert.Equal(1, HotTailCodec.Packets(2048, 2048));
            Assert.Equal(2, HotTailCodec.Packets(2049, 2048));
            Assert.Equal(1, HotTailCodec.Packets(5, 0));
        }


        [Fact]
/// <summary>NothingIsDueBeforeTheIntervalAndTheBandIsDueAfterIt operation.</summary>
        public void NothingIsDueBeforeTheIntervalAndTheBandIsDueAfterIt()
        {
/// <summary>HotTailSchedule operation.</summary>
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.IntervalSeconds = 5f;

            schedule.Advance(4.9f);
            Assert.Equal(HotTailSend.Nothing, schedule.Next());

            schedule.Advance(0.2f);
            Assert.Equal(HotTailSend.Band, schedule.Next());

            Assert.Equal(HotTailSend.Nothing, schedule.Next());
        }

        [Fact]
/// <summary>TheHullOutranksTheBandAndResetsTheInterval operation.</summary>
        public void TheHullOutranksTheBandAndResetsTheInterval()
        {
/// <summary>HotTailSchedule operation.</summary>
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
/// <summary>AskingTwiceInsideTheCooldownIsRefused operation.</summary>
        public void AskingTwiceInsideTheCooldownIsRefused()
        {
/// <summary>HotTailSchedule operation.</summary>
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.SnapshotCooldownSeconds = 5f;

            Assert.True(schedule.RequestSnapshot());

            Assert.False(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());

            Assert.False(schedule.RequestSnapshot());
            schedule.Advance(4f);
            Assert.False(schedule.RequestSnapshot());

            schedule.Advance(2f);
            Assert.True(schedule.RequestSnapshot());
        }

        [Fact]
/// <summary>AnHonestRetryIsAnsweredAndAFloodIsNot operation.</summary>
        public void AnHonestRetryIsAnsweredAndAFloodIsNot()
        {
            Assert.True(HotTailSchedule.DefaultSnapshotCooldownSeconds < HotTailSchedule.RetrySeconds);

/// <summary>HotTailSchedule operation.</summary>
            HotTailSchedule schedule = new HotTailSchedule();
            Assert.True(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());

            schedule.Advance(HotTailSchedule.RetrySeconds);
            Assert.True(schedule.RequestSnapshot());
        }

        [Fact]
/// <summary>AnIntervalOfZeroStopsTheBandAndNotTheHull operation.</summary>
        public void AnIntervalOfZeroStopsTheBandAndNotTheHull()
        {
/// <summary>HotTailSchedule operation.</summary>
            HotTailSchedule schedule = new HotTailSchedule();
            schedule.IntervalSeconds = 0f;

            schedule.Advance(600f);
            Assert.Equal(HotTailSend.Nothing, schedule.Next());

            Assert.True(schedule.RequestSnapshot());
            Assert.Equal(HotTailSend.Snapshot, schedule.Next());
        }


        [Fact]
/// <summary>AClientAsksAtOnceAndThenBacksOff operation.</summary>
        public void AClientAsksAtOnceAndThenBacksOff()
        {
/// <summary>HotTailRequest operation.</summary>
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
/// <summary>AClientStopsAskingWhenItHasTheHull operation.</summary>
        public void AClientStopsAskingWhenItHasTheHull()
        {
/// <summary>HotTailRequest operation.</summary>
            HotTailRequest request = new HotTailRequest();
            Assert.True(request.ShouldAsk());

            request.Answer();
            request.Advance(600f);

            Assert.False(request.ShouldAsk());
            Assert.True(request.Answered);
            Assert.Equal(1, request.Attempts);
        }

        [Fact]
/// <summary>AClientNeverGivesUpOnAGridItStillHas operation.</summary>
        public void AClientNeverGivesUpOnAGridItStillHas()
        {
/// <summary>HotTailRequest operation.</summary>
            HotTailRequest request = new HotTailRequest();

            for (int i = 0; i < 200; i++)
            {
                request.ShouldAsk();
                request.Advance(120f);
            }

            Assert.True(request.ShouldAsk());
        }


        [Fact]
/// <summary>WhatIsOwedToAPlayerWhoLeftIsDropped operation.</summary>
        public void WhatIsOwedToAPlayerWhoLeftIsDropped()
        {
/// <summary>HotTailServerState operation.</summary>
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
/// <summary>AGridThatIsGoneIsForgottenAndAGridThatComesBackIsAskedAboutAgain operation.</summary>
        public void AGridThatIsGoneIsForgottenAndAGridThatComesBackIsAskedAboutAgain()
        {
/// <summary>HotTailClientState operation.</summary>
            HotTailClientState client = new HotTailClientState();

            Assert.True(client.ShouldAsk(9L));
            client.Answered(9L);
            Assert.True(client.HasHull(9L));
            Assert.Equal(0, client.Waiting);

            client.Forget(new HashSet<long>());
            Assert.Equal(0, client.Tracked);

            Assert.False(client.HasHull(9L));
            Assert.True(client.ShouldAsk(9L));
            Assert.Equal(1, client.Waiting);
        }

        [Fact]
/// <summary>TheLedgerAdvancesEverySchedulesClockIncludingOneNothingAskedAbout operation.</summary>
        public void TheLedgerAdvancesEverySchedulesClockIncludingOneNothingAskedAbout()
        {
/// <summary>HotTailServerState operation.</summary>
            HotTailServerState state = new HotTailServerState();
            state.IntervalSeconds = 5f;

            Assert.Equal(HotTailSend.Nothing, state.Next(1UL, 100L));

            state.Advance(6f);
            Assert.Equal(HotTailSend.Band, state.Next(1UL, 100L));
        }

        [Fact]
/// <summary>AnIntervalChangedMidSessionReachesASchedulePlacedBeforeIt operation.</summary>
        public void AnIntervalChangedMidSessionReachesASchedulePlacedBeforeIt()
        {
/// <summary>HotTailServerState operation.</summary>
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
/// <summary>ARefusedRequestLeavesNothingOutstanding operation.</summary>
        public void ARefusedRequestLeavesNothingOutstanding()
        {
/// <summary>HotTailServerState operation.</summary>
            HotTailServerState state = new HotTailServerState();

            Assert.True(state.Request(3UL, 55L));
            Assert.True(state.Wants(3UL, 55L));

            Assert.False(state.Request(3UL, 55L));
            Assert.Equal(HotTailSend.Snapshot, state.Next(3UL, 55L));
            Assert.False(state.Wants(3UL, 55L));
        }


        [Fact]
/// <summary>TheWholeHullAndTheBandAreTheSameCallWithADifferentBand operation.</summary>
        public void TheWholeHullAndTheBandAreTheSameCallWithADifferentBand()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings().Derive();
            ThermalSimulation simulation = Hulls.Driven(settings, 400);

            int steps = (int)(240f / settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, Worlds.Shadow());

/// <summary>List operation.</summary>
            List<StoredTemperature> hull = new List<StoredTemperature>();
/// <summary>List operation.</summary>
            List<StoredTemperature> band = new List<StoredTemperature>();

            simulation.ExportHotTail(float.MaxValue, 0, hull);
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, band);

            Assert.True(hull.Count > 0);
            Assert.True(band.Count <= hull.Count);

            int ratable = 0;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Thermal.CriticalTemperature > 0f) ratable++;
            }

            Assert.Equal(ratable, hull.Count);
        }

        [Fact]
/// <summary>AHullTravelsThroughTheWireAndArrivesOnTheOtherMachine operation.</summary>
        public void AHullTravelsThroughTheWireAndArrivesOnTheOtherMachine()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings().Derive();

            ThermalSimulation server = Hulls.Driven(settings, 300);
            ThermalSimulation client = Hulls.Driven(settings, 300);

            int steps = (int)(300f / settings.StepSeconds);
            for (int i = 0; i < steps; i++) server.StepExact(1, Worlds.Shadow());

/// <summary>List operation.</summary>
            List<StoredTemperature> hull = new List<StoredTemperature>();
            server.ExportHotTail(float.MaxValue, 0, hull);

/// <summary>List operation.</summary>
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
