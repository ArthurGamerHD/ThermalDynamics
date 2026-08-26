using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Coolant is a consumable, and a ring that is part full costs the integrator nothing extra.**
    ///
    /// <para>
    /// backlog.md `B43` decided that refilling a loop costs energy and time, and `B44` named the
    /// trap in getting there: a loop's substep demand is `SegmentConductance / SegmentThermalMass`,
    /// so scaling the fluid's *capacity* with the fill and leaving its *coupling* alone makes a
    /// 5 %-full ring twenty times stiffer than a full one — on an element that already competes to
    /// be a grid's worst, against a cap of 64.
    /// </para>
    ///
    /// <para>
    /// The design scales both, which makes the ratio invariant and is also the physical answer:
    /// half the fluid touching a wall carries half the heat through it. **That invariance is what
    /// this file exists to hold**, because a consumer of a link's conductance that forgets to scale
    /// it is not a compile error — it is a ring that is quietly stiffer than it should be.
    /// </para>
    /// </summary>
    public class CoolantFillTests
    {
        private readonly ITestOutputHelper output;

        public CoolantFillTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSettings Isolated()
        {
            ThermalSettings s = new ThermalSettings();
            s.EnableEnvironment = false;
            s.EnableSolarHeat = false;
            s.EnableWasteHeat = false;
            s.Derive();
            return s;
        }

        private static ThermalSimulation Ring(out CoolantLoop loop)
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            loop = simulation.Solver.Loops[0];
            return simulation;
        }

        /// <summary>
        /// **The substep demand does not move with the fill**, which is the whole reason both the
        /// capacity and the coupling are scaled rather than only the capacity.
        ///
        /// A tenth full is the interesting case: it is where the cliff would be steepest, and it is
        /// the state a ring is in for most of a refill.
        /// </summary>
        [Theory]
        [InlineData(1.0f)]
        [InlineData(0.5f)]
        [InlineData(0.1f)]
        [InlineData(0.01f)]
        public void TheSubstepDemandIsInvariantInTheFill(float fill)
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(out loop);

            float full = simulation.Solver.RequiredSubsteps(1f);

            loop.FillFraction = fill;
            float part = simulation.Solver.RequiredSubsteps(1f);

            output.WriteLine("full {0:n4} substeps, {1:p0} full {2:n4}", full, fill, part);

            Assert.True(full > 0f, "the full ring demanded nothing, so this test compares two zeroes");
            Assert.InRange(part, full * 0.99f, full * 1.01f);
        }

        /// <summary>
        /// The capacity and the coupling both move with the fill, and by the same factor. This is
        /// the mechanism the test above measures the consequence of; separating them says which of
        /// the two broke when the ratio moves.
        /// </summary>
        [Fact]
        public void BothTheCapacityAndTheCouplingScaleWithTheFill()
        {
            CoolantLoop loop;
            Ring(out loop);

            float massFull = loop.SegmentThermalMass;
            float linkFull = loop.LinkConductance(0);

            Assert.True(linkFull > 0f, "the first link carries nothing, so this checks nothing");

            loop.FillFraction = 0.25f;

            Assert.InRange(loop.SegmentThermalMass, massFull * 0.2475f, massFull * 0.2525f);
            Assert.InRange(loop.LinkConductance(0), linkFull * 0.2475f, linkFull * 0.2525f);
        }

        /// <summary>
        /// **A dry ring is still a ring**: it exists, holds nothing, transports nothing, and can be
        /// refilled. *The loop stops existing* is the shape this area has failed in twice, so zero
        /// is a level rather than a deletion.
        /// </summary>
        [Fact]
        public void ADryRingStillExistsAndCouplesToNothing()
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(out loop);

            loop.FillFraction = 0f;

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(8, loop.PipeCount);
            Assert.Equal(0f, loop.LinkConductance(0));
            Assert.Equal(0f, loop.HeldKilograms);

            // And it comes back, which is the point of it still being there.
            loop.FillFraction = 1f;
            Assert.True(loop.LinkConductance(0) > 0f, "a refilled ring couples to nothing");
        }

        /// <summary>
        /// The fill is a fraction and is held to it, so a caller that computes one from a division
        /// cannot drive the ring past full or below empty.
        /// </summary>
        [Fact]
        public void TheFillIsClampedToItsRange()
        {
            CoolantLoop loop;
            Ring(out loop);

            loop.FillFraction = 4f;
            Assert.Equal(1f, loop.FillFraction);

            loop.FillFraction = -1f;
            Assert.Equal(0f, loop.FillFraction);
        }

        /// <summary>
        /// **The whole of `B43`'s answer, in one assertion: venting and refilling at the break-even
        /// excess is neutral in heat.**
        ///
        /// <para>
        /// The heat a vent removes is the fluid's excess times its capacity. The energy a refill
        /// spends is the heat that same fluid holds at `RefillEquivalentKelvin` — and a pump's
        /// `ConsumerWasteEnergy` is 1, so every joule of it lands back in the ship. At exactly that
        /// excess the two are the same number and the cycle gains nothing, which is the exploit
        /// benefit erased by construction rather than by a threshold chosen to be large enough.
        /// </para>
        ///
        /// <para>
        /// **188,889 J is not a coincidence** — it is what
        /// `GrindAndRewealdIsWorthKilowattsRatherThanMegawatts` measured a grind of one parcel to
        /// remove at 100 K over, taken from the other side of the same arithmetic.
        /// </para>
        /// </summary>
        [Fact]
        public void VentingAndRefillingAtTheBreakEvenExcessIsNeutralInHeat()
        {
            CoolantLoop loop;
            Ring(out loop);

            const float Ambient = 293.15f;
            float excess = loop.Properties.RefillEquivalentKelvin;

            for (int i = 0; i < loop.ParcelCount; i++)
            {
                loop.SetSegmentTemperature(i, Ambient + excess);
            }

            float removed = loop.Vent(Ambient);

            // Refill it all the way back, however many ticks that takes, summing what it spent.
            float spent = 0f;
            for (int tick = 0; tick < 10000 && loop.FillFraction < 1f; tick++)
            {
                spent += loop.Refill(1f) * 1f;
            }

            output.WriteLine("vent removed {0:n0} J; refill spent {1:n0} J; ratio {2:n4}",
                removed, spent, spent / removed);
            output.WriteLine("one parcel of {0} is {1:n0} J", loop.ParcelCount, removed / loop.ParcelCount);

            Assert.Equal(1f, loop.FillFraction);
            Assert.True(removed > 0f, "the vent removed nothing, so this compares two zeroes");

            // Neutral: what came out is what goes back in as heat.
            Assert.InRange(spent, removed * 0.99f, removed * 1.01f);
        }

        /// <summary>
        /// Above the break-even excess venting still pays, and below it costs. That asymmetry is
        /// what keeps the emergency dump useful while making the cycle worthless.
        /// </summary>
        [Theory]
        [InlineData(300f, true)]
        [InlineData(30f, false)]
        public void VentingPaysOnlyWhenTheCoolantIsHotterThanTheRefillIsPricedAt(
            float excess, bool shouldPay)
        {
            CoolantLoop loop;
            Ring(out loop);

            const float Ambient = 293.15f;
            for (int i = 0; i < loop.ParcelCount; i++) loop.SetSegmentTemperature(i, Ambient + excess);

            float removed = loop.Vent(Ambient);

            float spent = 0f;
            for (int tick = 0; tick < 10000 && loop.FillFraction < 1f; tick++) spent += loop.Refill(1f);

            output.WriteLine("{0:n0} K over: removed {1:n0} J, spent {2:n0} J, net {3:n0} J",
                excess, removed, spent, removed - spent);

            Assert.Equal(shouldPay, removed > spent);
        }

        /// <summary>
        /// The fill rate bounds the cycle: a ring comes back at the rate it comes back at, however
        /// hard anybody works. It is the only figure in this feature that was chosen rather than
        /// derived, so it is the one worth pinning against a change nobody meant.
        /// </summary>
        [Fact]
        public void ARingRefillsAtTheRateAndNoFaster()
        {
            CoolantLoop loop;
            Ring(out loop);

            loop.FillFraction = 0f;

            float perSecond = loop.Properties.RefillKilogramsPerSecond;
            loop.Refill(1f);

            output.WriteLine("after 1 s: {0:n1} kg of {1:n0}", loop.HeldKilograms, loop.CapacityKilograms);
            Assert.InRange(loop.HeldKilograms, perSecond * 0.99f, perSecond * 1.01f);

            // And it stops at full rather than overshooting, so the last tick of a refill is priced
            // for what it actually moved.
            float drawn = 0f;
            for (int tick = 0; tick < 10000 && loop.FillFraction < 1f; tick++) drawn += loop.Refill(1f);

            Assert.Equal(1f, loop.FillFraction);
            Assert.Equal(0f, loop.Refill(1f));
            Assert.True(drawn > 0f);
        }

        /// <summary>
        /// A dry ring vents nothing, so a second grinder pass costs a player nothing and gains them
        /// nothing — the same shape as grinding the same pipe twice.
        /// </summary>
        [Fact]
        public void VentingATwiceEmptiedRingRemovesNothingTheSecondTime()
        {
            CoolantLoop loop;
            Ring(out loop);

            for (int i = 0; i < loop.ParcelCount; i++) loop.SetSegmentTemperature(i, 900f);

            Assert.True(loop.Vent(293.15f) > 0f);
            Assert.Equal(0f, loop.Vent(293.15f));
        }

        /// <summary>
        /// **A part-full ring survives a save, and a payload that predates the feature loads full.**
        ///
        /// The second half is `W1`: the format grows by adding a section, so a build that has never
        /// heard of coolant being a consumable writes no fill records and a ring restored from one
        /// is full — which is exactly what it was in that world.
        /// </summary>
        [Fact]
        public void AFillSurvivesASaveAndAPayloadWithoutOneLoadsFull()
        {
            CoolantLoop loop;
            ThermalSimulation simulation = Ring(out loop);

            loop.FillFraction = 0.375f;
            string saved = simulation.Save();

            CoolantLoop reloaded;
            ThermalSimulation second = Ring(out reloaded);
            second.RebuildAll();
            second.Load(saved);

            output.WriteLine("saved {0:n3}, restored {1:n3}", 0.375f, second.Solver.Loops[0].FillFraction);
            Assert.InRange(second.Solver.Loops[0].FillFraction, 0.374f, 0.376f);

            // A full ring writes no record at all, and a ring restored from a payload without one
            // is full rather than empty.
            CoolantLoop fullLoop;
            ThermalSimulation full = Ring(out fullLoop);
            string withoutFills = full.Save();

            CoolantLoop third;
            ThermalSimulation loaded = Ring(out third);
            third.FillFraction = 0.1f;
            loaded.RebuildAll();
            loaded.Load(withoutFills);

            Assert.Equal(1f, loaded.Solver.Loops[0].FillFraction);
        }

        /// <summary>
        /// **A ring refills as the simulation steps, and the pump is charged for it** — so the
        /// heat arrives through the pump's own `ConsumerWasteEnergy` of 1 rather than through a
        /// second path invented for this feature.
        /// </summary>
        [Fact]
        public void SteppingARingRefillsItAndChargesThePump()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];

            Assert.True(loop.HasPump, "the ring has no pump, so nothing can drive a refill");

            loop.FillFraction = 0f;
            Assert.True(loop.RefillDemandWatts > 0f, "an empty ring is asking for nothing");

            BlockInstance pumpBlock = loop.Pumps[0].Block;
            float before = pumpBlock.PowerConsumedWatts;

            for (int i = 0; i < 20; i++) simulation.Update(1f, default(EnvironmentSample));

            output.WriteLine("after 20 s: fill {0:p1}, pump draw {1:n0} W (was {2:n0})",
                loop.FillFraction, pumpBlock.PowerConsumedWatts, before);

            Assert.True(loop.FillFraction > 0f, "stepping the simulation refilled nothing");
            Assert.True(pumpBlock.PowerConsumedWatts > before,
                "the pump was not charged for the refill, so the heat never arrives");
        }

        /// <summary>
        /// **A ring with no pump never refills.** Something has to drive the fluid in, and a
        /// pumpless ring is a loop that holds coolant and circulates none.
        /// </summary>
        [Fact]
        public void ARingWithNoPumpAsksForNothingAndNeverRefills()
        {
            CoolantLoop loop;
            Ring(out loop);

            // A pumpless ring, made by taking the pump back off: `BuildRing` chooses one when it is
            // not told otherwise, which is the right default for every other test here.
            loop.Pumps.Clear();
            loop.HasPump = false;

            loop.FillFraction = 0f;

            Assert.False(loop.HasPump);
            Assert.Equal(0f, loop.RefillDemandWatts);
            Assert.Equal(0f, loop.Refill(10f));
            Assert.Equal(0f, loop.FillFraction);
        }

        /// <summary>
        /// **No power, no fill.** A pump the grid could not supply refills by the share it was
        /// given, which is the rule every other draw in this mod follows.
        /// </summary>
        [Fact]
        public void AnUnderSuppliedPumpRefillsByTheShareItWasGiven()
        {
            CoolantLoop loop;
            Ring(out loop);

            loop.FillFraction = 0f;
            float full = loop.Refill(1f, 1f);

            loop.FillFraction = 0f;
            float half = loop.Refill(1f, 0.5f);

            loop.FillFraction = 0f;
            Assert.Equal(0f, loop.Refill(1f, 0f));
            Assert.Equal(0f, loop.FillFraction);

            output.WriteLine("full supply {0:n0} W, half {1:n0} W", full, half);
            Assert.InRange(half, full * 0.49f, full * 0.51f);
        }

        /// <summary>
        /// **A ring a grinder opened vents, and comes back empty when the pipe is welded back.**
        ///
        /// backlog.md `B44`: a hole in a pressurised loop drains it, so the fluid's heat leaves
        /// with the fluid rather than spilling into the pipes. The ring's signature is what carries
        /// that across the rebuild — welding the pipe back returns the ring to the signature it
        /// had, and to a fill of nothing, which it then pays to restore.
        /// </summary>
        [Fact]
        public void GrindingAPipeVentsTheRingAndItComesBackEmpty()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.ParcelCount; i++) loop.SetSegmentTemperature(i, 900f);

            BlockInstance popped = ring[3];
            simulation.RemoveBlock(popped);
            simulation.RebuildAll();

            Assert.Empty(simulation.Solver.Loops);

            // No pipe is holding the ring's fluid: it drained out of the hole.
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i] == popped) continue;
                ThermalNode node = simulation.Solver.GetNode(ring[i]);
                if (node == null) continue;

                Assert.Equal(0f, node.HeldCoolantCapacity);
            }

            simulation.AddBlock(popped);
            simulation.RebuildAll();

            Assert.Single(simulation.Solver.Loops);
            output.WriteLine("rewelded ring is {0:p0} full", simulation.Solver.Loops[0].FillFraction);
            Assert.Equal(0f, simulation.Solver.Loops[0].FillFraction);
        }

        /// <summary>
        /// Kilograms are what a refill is priced in, so the ring reports them: a full eight-pipe
        /// large-grid ring is eight parcels of the shipped 50 kg.
        /// </summary>
        [Fact]
        public void ARingReportsWhatItHoldsInKilograms()
        {
            CoolantLoop loop;
            Ring(out loop);

            float perPipe = loop.MassPerPipe;

            Assert.Equal(8f * perPipe, loop.CapacityKilograms);
            Assert.Equal(8f * perPipe, loop.HeldKilograms);

            loop.FillFraction = 0.5f;
            Assert.Equal(4f * perPipe, loop.HeldKilograms);
        }

        /// <summary>
        /// **A vented ring refills while the simulation is stepped, and the pump pays for it.**
        ///
        /// <para>
        /// The refill was written onto the frame-paced path alone. That path is the game; it is not
        /// the lane any figure on balance.md is read in — every lab, benchmark and scenario advances
        /// through <see cref="ThermalSimulation.StepExact"/>, and on that path a vented ring stayed
        /// empty for ever and the pump was never charged. So the consumable worked in a session and
        /// did not exist in a measurement, which is the worse half: the exploit `B43` was written to
        /// close was still open in every number the mod is tuned against.
        /// </para>
        ///
        /// <para>
        /// Found by `LoopDialReachTests`, which reported both refill dials as reaching nothing at
        /// all. This asserts the behaviour directly rather than through a sweep, because a sweep
        /// says a dial is connected and this says what it is connected to.
        /// </para>
        /// </summary>
        [Fact]
        public void AVentedRingRefillsAcrossSteppedTimeAndThePumpPaysForIt()
        {
            GridBuilder builder = GridBuilder.Large();
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3);
            PipeFitter.BuildRing(builder, cells, 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];

            Assert.True(loop.HasPump, "the ring needs a pump, because a ring without one never fills");

            loop.Vent(293.15f);
            Assert.Equal(0f, loop.FillFraction);

            float wanted = loop.RefillDemandWatts;
            Assert.True(wanted > 0f, "an empty ring with a pump should be asking for power");

            simulation.StepExact(LabClock.Steps(40), Worlds.Shadow());

            Assert.True(loop.FillFraction > 0f,
                "a vented ring did not refill across stepped time, so the consumable is invisible"
                + " to every lab that measures this mod");

            float drawn = 0f;
            for (int i = 0; i < loop.Pumps.Count; i++)
            {
                if (loop.Pumps[i].Block != null) drawn += loop.Pumps[i].Block.PowerConsumedWatts;
            }

            Assert.True(drawn > 0f,
                "the ring refilled without the pump being charged for it, which is the exploit"
                + " rather than the fix");
        }

        /// <summary>
        /// **A stopped ring carries less across the fluid-to-wall joint than a flowing one**, by
        /// <see cref="LoopThermalProperties.StagnantTransferFraction"/>.
        ///
        /// <para>
        /// That field was authored, documented, clamped, given a slider in the in-game menu and
        /// copied into the running properties, and no line of the simulation multiplied anything by
        /// it — the mod's recurring defect, and one `LoopDialReachTests` now catches by enumeration.
        /// It described the segment-to-segment transport, which <see cref="CoolantLoop.Advect"/>
        /// already stops dead by returning on zero flow, so as written it could only ever be a
        /// no-op. It now scales the leg where a flow dependence is real: fluid-to-wall transfer is
        /// convective, and forced convection against a wall is most of an order of magnitude above
        /// natural convection against the same wall.
        /// </para>
        ///
        /// <para>
        /// **It ships at 1**, so this asserts reach and direction and not a shipped balance figure.
        /// </para>
        /// </summary>
        [Fact]
        public void AStoppedRingCarriesLessAcrossTheWallThanAFlowingOne()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3), 1);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];

            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.StagnantTransferFraction = 0.25f;
            simulation.LoopProperties = properties;
            simulation.RebuildAll();
            loop = simulation.Solver.Loops[0];

            for (int i = 0; i < loop.Pumps.Count; i++) loop.Pumps[i].Enabled = true;
            loop.RefreshFlow();
            Assert.NotEqual(0f, loop.FlowSegmentsPerSecond);
            Assert.Equal(1f, loop.StagnantFactor);
            float flowing = loop.LinkConductance(0);

            for (int i = 0; i < loop.Pumps.Count; i++) loop.Pumps[i].Enabled = false;
            loop.RefreshFlow();
            Assert.Equal(0f, loop.FlowSegmentsPerSecond);
            Assert.Equal(0.25f, loop.StagnantFactor);
            float stopped = loop.LinkConductance(0);

            Assert.True(flowing > 0f, "a flowing ring carried nothing, so nothing below is measured");
            Assert.Equal(flowing * 0.25f, stopped, 3);
        }
    }
}
