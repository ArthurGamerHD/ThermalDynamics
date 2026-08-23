using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The packet a server sends a client about the blocks that are about to fail**: what goes in
    /// it, what it weighs, and what a reader does with one it cannot trust.
    ///
    /// <para>
    /// These temperatures are written straight onto a running simulation on the other machine, so
    /// the failure mode is not a dropped update — it is a hull whose blocks were moved to values
    /// nobody sent. That is why a short or unrecognised packet decodes nothing rather than what it
    /// can, and why every one of those paths has a test here.
    /// </para>
    ///
    /// <para>
    /// The selection rule and its budget are checked against a real hull rather than against a
    /// hand-made list, because the claim that matters — the budget keeps the blocks nearest failing
    /// — is about an ordering over blocks with different critical temperatures, and a rig where
    /// every block fails at the same temperature cannot tell that ordering from ordering by kelvin
    /// (`E7`). See [backlog.md](../../docs/backlog.md) `B4`.
    /// </para>
    /// </summary>
    public class HotTailTests
    {
        /// <summary>A hull warmed until some of it is inside the band.</summary>
        private static ThermalSimulation Warm(int blocks = 800, float seconds = 240f)
        {
            ThermalSettings settings = new ThermalSettings().Derive();
            ThermalSimulation simulation = Hulls.Driven(settings, blocks);

            int steps = (int)(seconds / settings.StepSeconds);
            for (int i = 0; i < steps; i++) simulation.StepExact(1, Worlds.Shadow());

            return simulation;
        }

        private static int InBand(ThermalSimulation simulation, float band)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                float critical = nodes[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;

                float floor = critical > band ? critical - band : 0f;
                if (nodes[i].Temperature >= floor) count++;
            }

            return count;
        }

        // ---- what goes in the packet ----------------------------------------------------------

        /// <summary>
        /// The selection is exactly the blocks inside the band — no more, and no fewer.
        ///
        /// Counted independently of the code under test rather than by asking it twice, which would
        /// agree with whatever the rule does (`P4`).
        /// </summary>
        [Fact]
        public void TheSelectionIsEveryBlockInsideTheBandAndNothingElse()
        {
            ThermalSimulation simulation = Warm();
            List<StoredTemperature> tail = new List<StoredTemperature>();

            int reported = simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, tail);
            int expected = InBand(simulation, Incandescence.GlowBandKelvin);

            Assert.Equal(expected, reported);
            Assert.Equal(expected, tail.Count);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < tail.Count; i++)
            {
                ThermalNode node = simulation.Solver.GetNodeAt(tail[i].Position);
                Assert.NotNull(node);

                float critical = node.Thermal.CriticalTemperature;
                Assert.True(critical > 0f, "a block with no critical temperature was selected");
                Assert.True(node.Temperature >= critical - Incandescence.GlowBandKelvin,
                    "a block below the band was selected at " + node.Temperature + " K");
            }

            Assert.NotEmpty(nodes);
        }

        /// <summary>
        /// The band is the one the glow already uses. Two definitions of *about to fail* would drift
        /// apart silently, and the drift would be a player seeing a block glow that the server
        /// never told them about (`P5`, `D3`).
        /// </summary>
        [Fact]
        public void TheBandTheGlowDrawsAndTheBandTheServerSendsAreTheSameNumber()
        {
            ThermalSimulation simulation = Warm();
            List<StoredTemperature> tail = new List<StoredTemperature>();
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, tail);

            int glowing = 0;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                float critical = nodes[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;
                if (nodes[i].Temperature >= Incandescence.GlowStartKelvin(critical)) glowing++;
            }

            Assert.Equal(glowing, tail.Count);
        }

        /// <summary>
        /// A block with no critical temperature is never sent. There is nothing to be wrong about
        /// on a block that cannot fail, and including it would spend the budget on blocks no
        /// readout is asking about.
        /// </summary>
        [Fact]
        public void ABlockThatCannotFailIsNeverSelected()
        {
            ThermalSimulation simulation = Warm();
            List<StoredTemperature> tail = new List<StoredTemperature>();

            // A band wide enough to admit every block that has a critical temperature at all.
            simulation.ExportHotTail(100000f, 0, tail);

            for (int i = 0; i < tail.Count; i++)
            {
                ThermalNode node = simulation.Solver.GetNodeAt(tail[i].Position);
                Assert.True(node.Thermal.CriticalTemperature > 0f);
            }
        }

        // ---- the budget -----------------------------------------------------------------------

        /// <summary>
        /// The budget caps what is sent, and the count returned still names the whole band — so a
        /// caller can tell a tail that fitted from one that was cut (`P2`).
        /// </summary>
        [Fact]
        public void TheBudgetCapsTheSelectionAndTheWholeBandIsStillReported()
        {
            ThermalSimulation simulation = Warm();
            List<StoredTemperature> whole = new List<StoredTemperature>();
            int band = simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, whole);

            Assert.True(band > 4, "the rig needs a band bigger than the budget under test; it was " + band);

            List<StoredTemperature> capped = new List<StoredTemperature>();
            int reported = simulation.ExportHotTail(Incandescence.GlowBandKelvin, 4, capped);

            Assert.Equal(4, capped.Count);
            Assert.Equal(band, reported);
        }

        /// <summary>
        /// What the budget keeps is the blocks nearest their **own** critical temperature, not the
        /// hottest in kelvin — a 400 K block failing at 420 K is in more trouble than a 900 K one
        /// failing at 1,400 K.
        /// </summary>
        [Fact]
        public void TheBudgetKeepsTheBlocksNearestTheirOwnFailure()
        {
            ThermalSimulation simulation = Warm();

            List<StoredTemperature> whole = new List<StoredTemperature>();
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, whole);

            List<StoredTemperature> capped = new List<StoredTemperature>();
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 3, capped);

            float worstDropped = float.NegativeInfinity;
            HashSet<long> kept = new HashSet<long>();
            for (int i = 0; i < capped.Count; i++) kept.Add(GridMath.Key(capped[i].Position));

            for (int i = 0; i < whole.Count; i++)
            {
                if (kept.Contains(GridMath.Key(whole[i].Position))) continue;

                ThermalNode node = simulation.Solver.GetNodeAt(whole[i].Position);
                float margin = node.Temperature - node.Thermal.CriticalTemperature;
                if (margin > worstDropped) worstDropped = margin;
            }

            for (int i = 0; i < capped.Count; i++)
            {
                ThermalNode node = simulation.Solver.GetNodeAt(capped[i].Position);
                float margin = node.Temperature - node.Thermal.CriticalTemperature;
                Assert.True(margin >= worstDropped,
                    "a block " + margin + " K from failing was kept while one at "
                    + worstDropped + " K was dropped");
            }
        }

        /// <summary>A budget larger than the band changes nothing.</summary>
        [Fact]
        public void ABudgetBiggerThanTheBandCutsNothing()
        {
            ThermalSimulation simulation = Warm();

            List<StoredTemperature> whole = new List<StoredTemperature>();
            int band = simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, whole);

            List<StoredTemperature> generous = new List<StoredTemperature>();
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, band + 100, generous);

            Assert.Equal(whole.Count, generous.Count);
        }

        // ---- the wire -------------------------------------------------------------------------

        /// <summary>
        /// A packet survives the round trip: every position exactly, every temperature to inside
        /// the quantum it is packed at.
        /// </summary>
        [Fact]
        public void EveryBlockSurvivesTheRoundTrip()
        {
            ThermalSimulation simulation = Warm();
            List<StoredTemperature> tail = new List<StoredTemperature>();
            simulation.ExportHotTail(Incandescence.GlowBandKelvin, 0, tail);
            Assert.NotEmpty(tail);

            byte[] packet = HotTailCodec.Encode(tail);
            List<StoredTemperature> back = new List<StoredTemperature>();

            Assert.True(HotTailCodec.TryDecode(packet, back));
            Assert.Equal(tail.Count, back.Count);

            for (int i = 0; i < tail.Count; i++)
            {
                Assert.Equal(tail[i].Position, back[i].Position);
                Assert.Equal(tail[i].Temperature, back[i].Temperature,
                    (int)0 + 1);   // one decimal: the tenth-kelvin quantum the codec packs at
            }
        }

        /// <summary>
        /// A packet is ten bytes a block plus a header, which is the figure the bandwidth column of
        /// the drift lab is computed from — so it is pinned rather than assumed.
        /// </summary>
        [Fact]
        public void APacketIsTenBytesABlockPlusAHeader()
        {
            List<StoredTemperature> tail = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 2, 3), 400f),
                new StoredTemperature(new Vector3I(-4, 5, -6), 900f),
            };

            byte[] packet = HotTailCodec.Encode(tail);

            Assert.Equal(HotTailCodec.HeaderSize + 2 * HotTailCodec.RecordSize, packet.Length);
            Assert.Equal(HotTailCodec.SizeOf(2), packet.Length);
        }

        /// <summary>An empty tail is a legal packet, and decodes to nothing rather than failing.</summary>
        [Fact]
        public void AnEmptyTailIsAPacketWithNoRecords()
        {
            byte[] packet = HotTailCodec.Encode(new List<StoredTemperature>());
            List<StoredTemperature> back = new List<StoredTemperature>();

            Assert.Equal(HotTailCodec.HeaderSize, packet.Length);
            Assert.True(HotTailCodec.TryDecode(packet, back));
            Assert.Empty(back);
        }

        /// <summary>
        /// Negative grid coordinates survive, which is most of a real grid: the position key is
        /// signed and a codec that only ever saw a test grid at the origin would not show it.
        /// </summary>
        [Fact]
        public void NegativeCoordinatesSurvive()
        {
            List<StoredTemperature> tail = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(-120, -3, -4096), 500f),
            };

            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.True(HotTailCodec.TryDecode(HotTailCodec.Encode(tail), back));
            Assert.Equal(new Vector3I(-120, -3, -4096), back[0].Position);
        }

        // ---- what a reader refuses ------------------------------------------------------------

        /// <summary>
        /// A packet cut short decodes **nothing**. Half a packet applied is a hull with a few blocks
        /// moved to values that were never sent, which is worse than no update at all.
        /// </summary>
        [Fact]
        public void ATruncatedPacketDecodesNothing()
        {
            List<StoredTemperature> tail = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 1, 1), 400f),
                new StoredTemperature(new Vector3I(2, 2, 2), 500f),
            };

            byte[] packet = HotTailCodec.Encode(tail);
            byte[] cut = new byte[packet.Length - 3];
            for (int i = 0; i < cut.Length; i++) cut[i] = packet[i];

            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.False(HotTailCodec.TryDecode(cut, back));
            Assert.Empty(back);
        }

        /// <summary>A packet from a build that packs differently is refused, not guessed at.</summary>
        [Fact]
        public void APacketWithAnotherVersionMarkerIsRefused()
        {
            byte[] packet = HotTailCodec.Encode(new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 1, 1), 400f),
            });

            packet[0] = 0x01;

            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.False(HotTailCodec.TryDecode(packet, back));
            Assert.Empty(back);
        }

        /// <summary>
        /// A packet claiming more records than it carries is refused. The count is the one field a
        /// corrupt or hostile sender can use to make a reader walk off the end of an array.
        /// </summary>
        [Fact]
        public void APacketThatLiesAboutItsCountIsRefused()
        {
            byte[] packet = HotTailCodec.Encode(new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(1, 1, 1), 400f),
            });

            packet[1] = 0x40;   // the low byte of the count: sixty-four records in a one-record packet

            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.False(HotTailCodec.TryDecode(packet, back));
            Assert.Empty(back);
        }

        /// <summary>A negative count is refused rather than sending the reader backwards.</summary>
        [Fact]
        public void APacketWithANegativeCountIsRefused()
        {
            byte[] packet = HotTailCodec.Encode(new List<StoredTemperature>());
            packet[4] = 0x80;

            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.False(HotTailCodec.TryDecode(packet, back));
            Assert.Empty(back);
        }

        /// <summary>Nothing at all is refused rather than thrown at.</summary>
        [Fact]
        public void NothingAtAllIsRefused()
        {
            List<StoredTemperature> back = new List<StoredTemperature>();
            Assert.False(HotTailCodec.TryDecode(null, back));
            Assert.False(HotTailCodec.TryDecode(new byte[0], back));
            Assert.Empty(back);
        }

        // ---- quantisation ---------------------------------------------------------------------

        /// <summary>
        /// The packing is a tenth of a kelvin, and it rounds rather than truncates — a truncating
        /// pack biases every temperature down, which on this band is biased toward *safe*.
        /// </summary>
        [Fact]
        public void TheTemperatureIsPackedToATenthOfAKelvinAndRounds()
        {
            Assert.Equal(4000, HotTailCodec.Quantise(400f));
            Assert.Equal(4001, HotTailCodec.Quantise(400.06f));
            Assert.Equal(4000, HotTailCodec.Quantise(400.04f));
        }

        /// <summary>
        /// The packing clamps at both ends rather than wrapping. A block thousands of kelvin past
        /// its own critical temperature is past where the exact value carries anything, and a
        /// wrapped one would arrive as ice.
        /// </summary>
        [Fact]
        public void TheTemperatureClampsRatherThanWrapping()
        {
            Assert.Equal(0, HotTailCodec.Quantise(-5f));
            Assert.Equal(ushort.MaxValue, HotTailCodec.Quantise(HotTailCodec.MaxTemperature));
            Assert.Equal(ushort.MaxValue, HotTailCodec.Quantise(1e9f));
        }

        // ---- applying it ----------------------------------------------------------------------

        /// <summary>
        /// Applying a packet moves exactly the blocks it names, and leaves every other block where
        /// it was — the correction is a statement about the band, not about the hull.
        /// </summary>
        [Fact]
        public void ApplyingAPacketMovesTheBlocksItNamesAndNoOthers()
        {
            ThermalSimulation simulation = Warm();
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            float[] before = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) before[i] = nodes[i].Temperature;

            List<StoredTemperature> packet = new List<StoredTemperature>
            {
                new StoredTemperature(nodes[0].Block.Position, 777f),
            };

            Assert.Equal(1, simulation.ImportHotTail(packet));
            Assert.Equal(777f, nodes[0].Temperature, 3);

            for (int i = 1; i < nodes.Count; i++)
            {
                Assert.Equal(before[i], nodes[i].Temperature, 5);
            }
        }

        /// <summary>
        /// A position this grid does not have is ignored, so a client whose grid lost a block since
        /// the packet was built still applies the rest.
        /// </summary>
        [Fact]
        public void APositionThisGridDoesNotHaveIsIgnored()
        {
            ThermalSimulation simulation = Warm();

            List<StoredTemperature> packet = new List<StoredTemperature>
            {
                new StoredTemperature(new Vector3I(9999, 9999, 9999), 777f),
                new StoredTemperature(simulation.Solver.Nodes[0].Block.Position, 555f),
            };

            Assert.Equal(1, simulation.ImportHotTail(packet));
            Assert.Equal(555f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        /// <summary>
        /// A received temperature is floored at the model's own minimum, exactly as loading a save
        /// is. A packet that arrived corrupt cannot put a block below absolute zero and take the
        /// solver with it.
        /// </summary>
        [Fact]
        public void AReceivedTemperatureIsFlooredLikeALoadedOne()
        {
            ThermalSimulation simulation = Warm();

            List<StoredTemperature> packet = new List<StoredTemperature>
            {
                new StoredTemperature(simulation.Solver.Nodes[0].Block.Position, -50f),
            };

            simulation.ImportHotTail(packet);
            Assert.Equal(ThermalConstants.MinimumTemperature, simulation.Solver.Nodes[0].Temperature, 3);
        }

        /// <summary>Nothing at all applies nothing, rather than throwing on a client.</summary>
        [Fact]
        public void ApplyingNothingAppliesNothing()
        {
            ThermalSimulation simulation = Warm();
            Assert.Equal(0, simulation.ImportHotTail(null));
        }

        /// <summary>
        /// **The whole path, end to end**: a server selects, packs, and a second hull built the same
        /// way applies it — and afterwards the two agree about which blocks are past critical.
        ///
        /// This is the claim the protocol exists to make, and it is made against a second simulation
        /// rather than against the first one's own numbers (`P4`).
        /// </summary>
        [Fact]
        public void AfterOnePacketTheTwoHullsAgreeAboutWhatIsPastCritical()
        {
            ThermalSettings settings = new ThermalSettings().Derive();

            // Two thousand blocks, which is the size the drift lab uses and the smallest this rig
            // was found to push past a critical temperature at all.
            ThermalSimulation server = Hulls.Driven(settings, 2000);
            ThermalSimulation client = Hulls.Driven(settings, 2000);

            // Run until blocks are actually past critical rather than for a fixed time: the claim
            // is about a disagreement, so a rig that never produced one would pass this test by
            // having nothing to fix (`E8`).
            int steps = (int)(1800f / settings.StepSeconds);
            int disagreed = 0;

            for (int i = 0; i < steps; i++)
            {
                server.StepExact(1, Worlds.Shadow());
                if (i % 80 != 0) continue;

                disagreed = DisagreeOnCritical(server, client);
                if (disagreed > 0) break;
            }

            Assert.True(disagreed > 0,
                "the rig needs the two hulls to disagree before the packet; they did not");

            List<StoredTemperature> tail = new List<StoredTemperature>();
            server.ExportHotTail(Incandescence.GlowBandKelvin, 0, tail);

            List<StoredTemperature> received = new List<StoredTemperature>();
            Assert.True(HotTailCodec.TryDecode(HotTailCodec.Encode(tail), received));
            client.ImportHotTail(received);

            Assert.Equal(0, DisagreeOnCritical(server, client));
        }

        private static int DisagreeOnCritical(ThermalSimulation server, ThermalSimulation client)
        {
            IList<ThermalNode> mine = server.Solver.Nodes;
            IList<ThermalNode> theirs = client.Solver.Nodes;

            int count = 0;
            for (int i = 0; i < mine.Count && i < theirs.Count; i++)
            {
                float critical = mine[i].Thermal.CriticalTemperature;
                if (critical <= 0f) continue;
                if (mine[i].Temperature > critical != theirs[i].Temperature > critical) count++;
            }

            return count;
        }
    }
}
