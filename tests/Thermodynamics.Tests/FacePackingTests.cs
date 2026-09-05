using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Six face counts in one field, and what the packing may not lose.**
    ///
    /// <para>
    /// A node kept its exposed faces as an `int[6]`: 48 bytes to hold 24 bytes of payload, once per
    /// node (backlog.md `E2`). Ten bits a face holds 1,023 against a real
    /// worst case of about a hundred — the widest vanilla block is ten cells across — so the
    /// packing is lossless for anything the game has, and these say what happens at the edge rather
    /// than leaving it to be discovered.
    /// </para>
    /// </summary>
    public class FacePackingTests
    {
        /// <summary>Every face round-trips, and the six do not bleed into each other.</summary>
        [Fact]
        public void EachFaceHoldsItsOwnCount()
        {
            ThermalNode node = Node();

            for (int f = 0; f < Face.Count; f++)
            {
                node.SetExposedFaces(f, (f + 1) * 7);
            }

            for (int f = 0; f < Face.Count; f++)
            {
                Assert.Equal((f + 1) * 7, node.GetExposedFaces(f));
            }

            // Rewriting one leaves the others alone, which is the failure a shift bug produces.
            node.SetExposedFaces(2, 0);
            Assert.Equal(0, node.GetExposedFaces(2));
            Assert.Equal(7, node.GetExposedFaces(0));
            Assert.Equal(42, node.GetExposedFaces(5));
        }

        /// <summary>
        /// **A count past what the packing holds is clamped, not wrapped.** Wrapping would turn a
        /// fully exposed face into a bare one — a block that stops radiating, which looks like
        /// physics — and the clamp is unreachable for any block the game ships.
        /// </summary>
        [Fact]
        public void ACountPastTheLimitIsClampedRatherThanWrapped()
        {
            ThermalNode node = Node();

            node.SetExposedFaces(0, ThermalNode.MaxExposedPerFace + 1);
            Assert.Equal(ThermalNode.MaxExposedPerFace, node.GetExposedFaces(0));

            node.SetExposedFaces(1, int.MaxValue);
            Assert.Equal(ThermalNode.MaxExposedPerFace, node.GetExposedFaces(1));

            node.SetExposedFaces(2, -4);
            Assert.Equal(0, node.GetExposedFaces(2));

            // And the limit is comfortably past what a block can actually present: the largest
            // vanilla block is ten cells on a side, so a hundred faces one way.
            Assert.True(ThermalNode.MaxExposedPerFace > 500,
                "the packing holds only " + ThermalNode.MaxExposedPerFace + " a face");
        }

        /// <summary>
        /// And the totals derived from it still come out right on a real grid, which is what says
        /// the packing is being read by the code that matters rather than only by these.
        /// </summary>
        [Fact]
        public void AnExposedBlockStillCountsItsOwnFaces()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();

            ThermalNode node = simulation.Solver.Nodes[0];

            int summed = 0;
            for (int f = 0; f < Face.Count; f++) summed += node.GetExposedFaces(f);

            Assert.Equal(Face.Count, summed);
            Assert.Equal(Face.Count, node.TotalExposedFaces);
            Assert.True(node.ExposedArea > 0f);
        }

        /// <summary>
        /// **The array overload is the six single-face writes and the refresh, exactly.**
        ///
        /// <para>
        /// The exposure stage set a face at a time and then called `RefreshExposure` to total them
        /// (performance.md, Pass 9, Iteration 1 measured that at two thirds of the stage); it now
        /// packs all six in one walk and stores once. That is an optimisation, so it is pinned
        /// against the code it replaced rather than against its own intent (`D8`), on inputs that
        /// include both clamps and a zero — a check that only ever exercised counts inside the
        /// range would pass on a packing that dropped the clamp entirely.
        /// </para>
        ///
        /// <para>
        /// Every observable is compared, not only the counts: the two derived floats are what the
        /// solver actually mirrors, and a total that agreed while an area did not would be a
        /// silent change to every radiating block.
        /// </para>
        /// </summary>
        [Fact]
        public void SettingAllSixAtOnceIsTheSixSeparateWritesAndTheRefresh()
        {
            int[][] cases =
            {
                new[] { 0, 0, 0, 0, 0, 0 },
                new[] { 1, 1, 1, 1, 1, 1 },
                new[] { 7, 14, 21, 28, 35, 42 },
                new[] { 100, 0, 3, 0, 0, 1 },
                new[] { ThermalNode.MaxExposedPerFace, 0, 0, 0, 0, 0 },
                new[] { ThermalNode.MaxExposedPerFace + 1, int.MaxValue, -4, 2, 0, 9 },
            };

            foreach (int[] counts in cases)
            {
                ThermalNode reference = Node();
                for (int f = 0; f < Face.Count; f++) reference.SetExposedFaces(f, counts[f]);
                reference.RefreshExposure();

                ThermalNode packed = Node();
                bool expectDirty = false;
                for (int f = 0; f < Face.Count; f++)
                {
                    int clamped = counts[f] < 0 ? 0
                        : counts[f] > ThermalNode.MaxExposedPerFace ? ThermalNode.MaxExposedPerFace
                        : counts[f];
                    if (clamped != packed.GetExposedFaces(f)) expectDirty = true;
                }
                packed.SetExposedFaces(counts);

                string where = "counts " + string.Join(",", counts);
                for (int f = 0; f < Face.Count; f++)
                {
                    Assert.Equal(reference.GetExposedFaces(f), packed.GetExposedFaces(f));
                }

                Assert.Equal(reference.TotalExposedFaces, packed.TotalExposedFaces);
                Assert.True(reference.ExposedArea.Equals(packed.ExposedArea),
                    where + ": area " + reference.ExposedArea + " became " + packed.ExposedArea);
                Assert.True(reference.RadiationCoefficient.Equals(packed.RadiationCoefficient),
                    where + ": radiation coefficient " + reference.RadiationCoefficient
                    + " became " + packed.RadiationCoefficient);
                // Dirty exactly when the write moved a face: the overload deliberately marks
                // nothing on a no-change write, because it is the path the solver refreshes
                // every node through on a room republish (see its own comment, and performance.md
                // Pass 9, Iteration 7). Until the `D4` prologue consumed the build's dirty flags
                // on the rebuild tick, every node arrived here already dirty and the no-change
                // case could not tell a skipped mark from an inherited one.
                Assert.True(expectDirty == packed.StateDirty,
                    where + ": the write " + (expectDirty ? "moved a face" : "changed nothing")
                    + " and left the node " + (packed.StateDirty ? "dirty" : "clean"));
            }
        }

        /// <summary>
        /// The overload rejects an array it cannot fill from, rather than reading past its end or
        /// leaving three faces at whatever they held. The solver's scratch is exactly six long, so
        /// this is unreachable from the simulation and is here to keep it that way.
        /// </summary>
        [Fact]
        public void SettingAllSixFromATooShortArrayIsRejected()
        {
            ThermalNode node = Node();

            Assert.Throws<System.ArgumentException>(() => node.SetExposedFaces(new int[Face.Count - 1]));
            Assert.Throws<System.ArgumentException>(() => node.SetExposedFaces((int[])null));
        }

        /// <summary>
        /// **A node whose faces did not move is not marked for resync.**
        ///
        /// <para>
        /// The solver refreshes exposure over every node whenever the room map republishes, and on
        /// a hull nobody is building the answer is the one it already had. Before Pass 9's seventh
        /// iteration that wrote six identical values and set `StateDirty` on all 126,731 nodes, so
        /// the next `SyncNodeState` mirrored the whole grid — and on a server resent it. This is
        /// the assertion that the skip exists; `TheStageDoesTheSameWorkEitherWay` in
        /// `ExposureSkipTests` is the one that it changes no answer.
        /// </para>
        /// </summary>
        [Fact]
        public void RepeatingTheSameCountsDoesNotMarkTheNodeDirty()
        {
            ThermalNode node = Node();
            int[] counts = { 3, 0, 5, 1, 0, 2 };

            node.SetExposedFaces(counts);
            Assert.True(node.StateDirty, "the first call changed the node and must say so");

            node.StateDirty = false;
            node.SetExposedFaces(counts);
            Assert.False(node.StateDirty, "the same six counts are not a change");

            // A different array holding the same counts is the same counts: the guard is on the
            // values, not on the reference, which is what makes it work at all — the solver passes
            // one reused scratch array for every node.
            node.SetExposedFaces(new[] { 3, 0, 5, 1, 0, 2 });
            Assert.False(node.StateDirty);

            // One face moving is a change, and so is a clamped value that lands somewhere new.
            node.SetExposedFaces(new[] { 3, 0, 5, 1, 0, 3 });
            Assert.True(node.StateDirty, "a face that moved must mark the node");

            node.StateDirty = false;
            node.SetExposedFaces(new[] { 3, 0, 5, 1, 0, ThermalNode.MaxExposedPerFace + 9 });
            Assert.True(node.StateDirty);

            // ...and two counts that clamp to the same value are not two values.
            node.StateDirty = false;
            node.SetExposedFaces(new[] { 3, 0, 5, 1, 0, int.MaxValue });
            Assert.False(node.StateDirty, "both clamp to the ceiling, so nothing moved");
        }

        /// <summary>
        /// The skip compares the total as well as the packing, and this is the case that needs it.
        ///
        /// <para>
        /// `SetExposedFaces(int, int)` writes the packed field without touching the three values it
        /// derives — it is the oracle `D8` pins the array overload against, and nothing under
        /// `Data/Scripts` calls it. If the skip compared only the packing, a node left in that
        /// half-written state would skip and keep an <see cref="ThermalNode.ExposedArea"/> from
        /// before, which is a silently wrong radiating area rather than a slow one. One integer
        /// comparison buys not having to reason about that.
        /// </para>
        /// </summary>
        [Fact]
        public void APackingWrittenWithoutItsDerivedValuesIsNotSkipped()
        {
            ThermalNode node = Node();

            // A consistent starting point: packing and derived both say twenty-four.
            node.SetExposedFaces(new[] { 4, 4, 4, 4, 4, 4 });
            Assert.Equal(24, node.TotalExposedFaces);
            float areaOfTwentyFour = node.ExposedArea;

            // The half-written state: the packing now says forty-two, the derived values still say
            // twenty-four, exactly as the single-face setter leaves it.
            int[] counts = { 7, 7, 7, 7, 7, 7 };
            for (int f = 0; f < Face.Count; f++) node.SetExposedFaces(f, counts[f]);
            Assert.Equal(24, node.TotalExposedFaces);
            Assert.Equal(areaOfTwentyFour, node.ExposedArea);

            node.StateDirty = false;
            node.SetExposedFaces(counts);

            Assert.True(node.StateDirty, "the derived values were stale, so this was a change");
            Assert.Equal(42, node.TotalExposedFaces);
            Assert.True(node.ExposedArea > areaOfTwentyFour);
            Assert.True(node.RadiationCoefficient > 0f);
        }

        private static ThermalNode Node()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.RebuildAll();
            return simulation.Solver.Nodes[0];
        }
    }
}
