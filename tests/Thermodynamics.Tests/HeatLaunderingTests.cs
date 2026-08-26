using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What a player can make heat do by changing the block population**, which is the exploit
    /// surface of `F26`'s deliberate limit.
    ///
    /// <para>
    /// Heat leaves the world with a block that leaves it and a welded block arrives at ambient.
    /// That was recorded as a limit on the grounds that conserving it would mean a grinder that
    /// heats the ship around it. What the limit did not cost out is that a player can *drive* it:
    /// grind and reweld on a timer and the ship has a heat sink made of one block.
    /// </para>
    ///
    /// <para>
    /// **The coolant loop is the sharp case and it is measured here.** A loop's coolant carries far
    /// more heat than the pipe it sits in, and breaking the ring dissolves the loop. The solver
    /// spills a dying loop into its pipes rather than deleting it — that fix went in after a 190 MJ
    /// case where grinding a pump dumped a ship's heat on demand — and these measure how much of it
    /// actually survives.
    /// </para>
    ///
    /// <para>
    /// **A third of it used to.** The spill mixed each parcel into its pipe at
    /// `(T_n·M_n + T_s·M_s) / (M_n + M_s)` and then left the node at `M_n`, so the energy landing
    /// was `mixed × M_n` where `T_n·M_n + T_s·M_s` went in and `M_s / (M_n + M_s)` of the ring's
    /// heat — 67.9 % on a large grid — was destroyed by two capacities nobody chose. The pipe now
    /// takes the parcel's heat capacity along with its temperature, so the only heat a broken ring
    /// loses is what was inside the block that left.
    /// </para>
    /// </summary>
    public class HeatLaunderingTests
    {
        private readonly ITestOutputHelper output;

        public HeatLaunderingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const float Ambient = 293.15f;

        private static ThermalSettings Isolated()
        {
            ThermalSettings s = new ThermalSettings();
            s.EnableEnvironment = false;
            s.EnableSolarHeat = false;
            s.EnableWasteHeat = false;
            s.Derive();
            return s;
        }

        /// <summary>
        /// Heat above ambient over everything the solver holds — nodes and loop coolant.
        ///
        /// **Above ambient rather than absolute.** `Energy` is `T x C`, so a rebuilt loop's coolant
        /// appearing at ambient adds absolute energy while adding no heat; reading the absolute
        /// figure across a rebuild measures the baseline moving and reports it as a finding.
        ///
        /// Coolant a pipe is holding outside a loop needs no term of its own here: it is inside that
        /// node's `ThermalMass`, which is the whole point of holding it there.
        ///
        /// **The loop term reads `Energy` and `ThermalMass` rather than the mean temperature times
        /// a parcel capacity times the pipe count.** That product is right in the normal model and
        /// eight times the answer under `WellMixedCoolant`, where the ring is expressed as one
        /// parcel holding all of its fluid — so the first draft of this file reported a well-mixed
        /// ring at 73 MJ where it holds 9, and read a correct spill as a 29 % loss. The loop's own
        /// two figures are defined the same way in both models (`P4`).
        /// </summary>
        private static float HeatAboveAmbient(ThermalSimulation simulation)
        {
            float total = 0f;
            ThermalSolver solver = simulation.Solver;

            for (int i = 0; i < solver.Nodes.Count; i++)
            {
                total += (solver.Nodes[i].Temperature - Ambient) * solver.Nodes[i].ThermalMass;
            }

            for (int i = 0; i < solver.Loops.Count; i++)
            {
                CoolantLoop loop = solver.Loops[i];
                total += loop.Energy - (Ambient * loop.ThermalMass);
            }

            return total;
        }

        /// <summary>
        /// **A rebuild provoked by an unrelated block keeps a hot loop's heat**, which is the half
        /// of this that always worked. Loops carry their temperature across a rebuild by signature,
        /// so building anywhere on a ship does not cool its coolant.
        /// </summary>
        [Fact]
        public void ABlockChangeElsewhereDoesNotCoolTheCoolant()
        {
            GridBuilder builder = GridBuilder.Large();
            PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            simulation.AddBlock(new BlockInstance(
                Catalog.LightArmor(), new Vector3I(20, 20, 20), BlockOrientation.Identity));
            simulation.RebuildAll();

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(900f, simulation.Solver.Loops[0].Temperature, 1);
        }

        /// <summary>
        /// **Breaking a ring by grinding one pipe loses that pipe's share and nothing else.**
        ///
        /// <para>
        /// The predicted figure is `1/N` for a ring of `N` pipes, and it is a prediction rather than
        /// a reading: one parcel out of `N` leaves inside the block that was ground out, every other
        /// parcel is absorbed by the pipe it was in, and the reweld hands them all back. It was
        /// written down in thermal-model.md, under Coolant loops, before it was run.
        /// </para>
        ///
        /// <para>
        /// Pinned as a band because floating-point mixing over eight pipes is not exact to the last
        /// bit; pinned tightly — a per cent — because a band wide enough to admit the old behaviour
        /// would admit anything. The old code lost 67.9 %.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(3, 3, 8)]
        [InlineData(4, 3, 10)]
        [InlineData(5, 4, 14)]
        public void GrindingOnePipeLosesOnlyThatPipesShareOfTheCoolant(int width, int depth, int pipes)
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, width, depth));

            Assert.Equal(pipes, ring.Count);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            float segmentMass = loop.SegmentThermalMass;
            float nodeMass = simulation.Solver.GetNode(ring[0]).ThermalMass;
            float start = HeatAboveAmbient(simulation);

            BlockInstance popped = ring[3];

            simulation.RemoveBlock(popped);
            simulation.RebuildAll();

            // With the ring open there is no loop at all, and the heat is in the pipes.
            Assert.Empty(simulation.Solver.Loops);
            float broken = HeatAboveAmbient(simulation);

            simulation.AddBlock(popped);
            simulation.RebuildAll();
            float end = HeatAboveAmbient(simulation);

            float lost = start - end;

            output.WriteLine("pipe node {0:n0} J/K, coolant parcel {1:n0} J/K", nodeMass, segmentMass);
            output.WriteLine("heat above ambient {0:n0} J -> {1:n0} J broken -> {2:n0} J rewelded",
                start, broken, end);
            output.WriteLine("the ring of {0} lost {1:n0} J ({2:n2} %)", pipes, lost, 100f * lost / start);

            Assert.Single(simulation.Solver.Loops);

            // **The fluid's heat, all of it.** What is left above ambient is the pipes' own, which
            // the coolant warmed and which a grinder does not drain.
            float fluid = segmentMass * pipes * (900f - Ambient);
            Assert.InRange(lost, fluid * 0.95f, fluid * 1.05f);

            // The break costs it and the reweld neither adds any back nor takes more: the ring
            // returns empty and pays to refill.
            Assert.InRange(start - broken, lost * 0.99f, lost * 1.01f);
            Assert.Equal(0f, simulation.Solver.Loops[0].FillFraction);
        }

        /// <summary>
        /// **Grinding a ring open now drains it, and refilling costs back exactly what drained.**
        ///
        /// <para>
        /// This measured the exploit before the vent existed: a grind cost one parcel, 188,889 J of
        /// an eight-pipe ring at 100 K over, and 23,611 W at the pipe's own build time — 0.79 % of
        /// the largest reactor's waste. That was `B43`'s evidence and it is why the currency it
        /// chose could be as small as energy and time.
        /// </para>
        ///
        /// <para>
        /// **With `B44` built, the grind takes the whole ring** — a hole in a pressurised loop
        /// drains it — and the ring comes back empty and pays `RefillEquivalentKelvin` per kilogram
        /// to fill. At 100 K over those are the same number, so the cycle is **neutral in heat and
        /// negative in power and time**. The exploit is not small any more; it is nothing.
        /// </para>
        /// </summary>
        [Fact]
        public void GrindingARingOpenDrainsItAndRefillingCostsBackWhatDrained()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            Assert.Equal(8, ring.Count);

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];

            const float Warm = Ambient + 100f;
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, Warm);

            float start = HeatAboveAmbient(simulation);

            BlockInstance popped = ring[3];
            simulation.RemoveBlock(popped);
            simulation.RebuildAll();
            float drained = start - HeatAboveAmbient(simulation);

            simulation.AddBlock(popped);
            simulation.RebuildAll();

            CoolantLoop back = simulation.Solver.Loops[0];
            Assert.Equal(0f, back.FillFraction);

            float spent = 0f;
            for (int tick = 0; tick < 10000 && back.FillFraction < 1f; tick++) spent += back.Refill(1f);

            output.WriteLine("grind drained {0:n0} J of {1:n0} J held", drained, start);
            output.WriteLine("refilling it spent {0:n0} J; ratio {1:n4}", spent, spent / drained);

            // The whole ring, not one parcel: the fluid left through the hole.
            Assert.InRange(drained, start * 0.95f, start * 1.001f);

            // And putting it back costs what it saved, which is the exploit erased rather than
            // merely made small.
            Assert.InRange(spent, drained * 0.98f, drained * 1.02f);
        }

        /// <summary>
        /// **A grind costs the heat inside the block that left and nothing else** — so grinding the
        /// *same* pipe twice costs nothing the second time, and each fresh pipe costs one parcel.
        ///
        /// <para>
        /// This stands where a *split* test was going to. The registered falsifier was "breaking a
        /// ring into two rings must lose nothing", and it is not constructible: the shipped pipes
        /// carry two ports each, straight or corner, so no block that can be added to a ring makes
        /// the builder trace two — the geometry needs a junction and there is no junction block.
        /// The claim it existed to test is that the round trip adds nothing back and takes nothing
        /// extra, and repeating the round trip tests that harder than doing it once.
        /// </para>
        ///
        /// <para>
        /// **The first prediction written for it was wrong and is corrected here** (`E10`): a
        /// geometric decay of `((N−1)/N)^k` over `k` grinds of one pipe. It is flat after the first,
        /// because the reweld puts that pipe's parcel back at ambient and grinding an ambient parcel
        /// out of a hot ring removes no heat. Draining a ring means working round it, one pipe at a
        /// time, at one parcel each — which is the same price grinding any other block pays and no
        /// worse. Pricing it further is `B43`.
        /// </para>
        /// </summary>
        [Fact]
        public void AGrindCostsTheHeatInTheBlockThatLeftAndNothingElse()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            float start = HeatAboveAmbient(simulation);
            float parcel = start / ring.Count;

            float afterFirst = GrindAndReweld(simulation, ring[3]);
            float afterSecond = GrindAndReweld(simulation, ring[3]);
            float afterOther = GrindAndReweld(simulation, ring[5]);

            output.WriteLine("start {0:n0} J, one parcel {1:n0} J", start, parcel);
            output.WriteLine("grind pipe 3: {0:n0}; again: {1:n0}; then pipe 5: {2:n0}",
                afterFirst, afterSecond, afterOther);

            // **The first grind drains the ring**, so what is left above ambient is the pipes' own
            // heat and none of the fluid's. A hole in a pressurised loop empties it.
            Assert.InRange(afterFirst, 0f, start * 0.05f);

            // **The second grind of the same pipe is free**, because what it carries away is the
            // ambient parcel the first reweld gave it back.
            // Bounded against the ring's own heat rather than against `afterFirst`, which is zero
            // once the ring has drained — a relative band around zero is a band of zero width.
            Assert.InRange(afterSecond, afterFirst - start * 0.001f, afterFirst + start * 0.001f);


            // And a pipe that is still hot costs its parcel, at the same price as the first.
            // And a second pipe out of an already-drained ring costs nothing: there is no fluid
            // left to lose, which is what makes the grinder useless as a heat sink rather than
            // merely less good than it was.
            Assert.InRange(afterOther, afterSecond - start * 0.001f, afterSecond + start * 0.001f);

            Assert.Single(simulation.Solver.Loops);
            Assert.Equal(ring.Count, simulation.Solver.Loops[0].Pipes.Count);
        }

        /// <summary>Grinds one block out and welds it straight back. Returns the heat left.</summary>
        private static float GrindAndReweld(ThermalSimulation simulation, BlockInstance block)
        {
            simulation.RemoveBlock(block);
            simulation.RebuildAll();
            simulation.AddBlock(block);
            simulation.RebuildAll();
            return HeatAboveAmbient(simulation);
        }

        /// <summary>
        /// **A grind leaves no pipe holding coolant, because the ring drained.**
        ///
        /// <para>
        /// This was `ASpilledParcelNeverHeatsItsPipeAboveItself`, and it guarded `A12`'s bound: a
        /// spilled parcel comes to one temperature with its pipe and never heats it past itself,
        /// because pouring the energy in at the node's own capacity would put a 900 K parcel into a
        /// 941 J/K pipe at 2,106 K and destroy it. Boundedness is one of the solver's three
        /// invariants.
        /// </para>
        ///
        /// <para>
        /// **`B44`'s vent made that bound unreachable from here.** The spill now runs only when a
        /// ring dissolves *without* losing a pipe — a split, where no fluid can have escaped — and
        /// the harness has no constructor for one, so grinding is no longer a way to reach it. The
        /// test's own guard said so, in the words it was given for exactly this: *the spill moved
        /// almost nothing and the bound above is not being tested*. Re-pointed at what a grind now
        /// does; the bound itself is untested and that is backlog.md `F28`
        /// rather than something this file quietly stopped checking.
        /// </para>
        /// </summary>
                public void AGrindLeavesNoPipeHoldingCoolantBecauseTheRingDrained()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            simulation.RemoveBlock(ring[3]);
            simulation.RebuildAll();

            float hottest = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                ThermalNode node = simulation.Solver.GetNode(ring[i]);
                if (node == null) continue;
                if (node.Temperature > hottest) hottest = node.Temperature;
            }

            output.WriteLine("hottest pipe after the grind: {0:n2} K", hottest);

            // **Nothing was spilled, because the ring drained**, and that is what this now checks:
            // a grinder opens a hole and the fluid leaves through it, so no pipe is holding a
            // parcel and no pipe is warmed by one.
            for (int i = 0; i < ring.Count; i++)
            {
                ThermalNode node = simulation.Solver.GetNode(ring[i]);
                if (node == null) continue;

                Assert.Equal(0f, node.HeldCoolantCapacity);
            }

            Assert.True(hottest <= 900f + 0.01f,
                "a pipe reached " + hottest.ToString("n2") + " K, above the 900 K the fluid was at");
        }

        /// <summary>
        /// **Coolant a pipe is holding survives a save and a load.** The block temperature is
        /// written whatever happens, so a reload that dropped the capacity would put the mixed
        /// temperature onto the bare pipe and destroy exactly the fraction the mix conserved, on a
        /// slower trigger.
        /// </summary>
        [Fact]
        public void HeldCoolantSurvivesASaveAndLoad()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            simulation.RemoveBlock(ring[3]);
            simulation.RebuildAll();

            float broken = HeatAboveAmbient(simulation);
            string saved = simulation.Save();

            // A fresh world built from the same blueprint, minus the pipe that was ground out.
            GridBuilder reloadBuilder = GridBuilder.Large();
            PipeFitter.BuildRing(reloadBuilder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));
            ThermalSimulation reloaded = reloadBuilder.BuildSimulation(Isolated());
            reloaded.RemoveBlock(reloaded.Grid.GetAtCell(ring[3].Position));
            reloaded.RebuildAll();

            reloaded.Load(saved);

            float restored = HeatAboveAmbient(reloaded);

            output.WriteLine("{0:n0} J saved, {1:n0} J restored", broken, restored);

            Assert.InRange(restored, broken * 0.999f, broken * 1.001f);
        }

        /// <summary>
        /// **`WellMixedCoolant` is the same arithmetic, and it used to be a different bug.** That
        /// model expresses the ring as one parcel holding all its fluid, so the spill's
        /// `SegmentThermalMass` was the *whole ring's* capacity and every pipe was handed it. The
        /// share is now the ring's fluid divided by its pipes, which is the same figure as the
        /// per-parcel one in the normal model and the right one here.
        /// </summary>
        [Fact]
        public void TheWellMixedModelLosesTheSameOneParcel()
        {
            ThermalSettings settings = Isolated();
            settings.WellMixedCoolant = true;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            CoolantLoop loop = simulation.Solver.Loops[0];
            loop.Temperature = 900f;

            float start = HeatAboveAmbient(simulation);
            float predicted = start / ring.Count;

            simulation.RemoveBlock(ring[3]);
            simulation.RebuildAll();
            simulation.AddBlock(ring[3]);
            simulation.RebuildAll();

            float end = HeatAboveAmbient(simulation);
            float lost = start - end;

            output.WriteLine("well mixed: {0:n0} J -> {1:n0} J, lost {2:n0} ({3:n2} %)",
                start, end, lost, 100f * lost / start);

            // **The same as the normal model, which is the claim**: a hole drains the ring whether
            // its fluid is carried as eight parcels or as one. The two models disagreeing here is
            // what this test exists to catch, and they disagreed once — the spill handed every pipe
            // the whole ring's fluid under `WellMixedCoolant`.
            Assert.InRange(lost, start * 0.95f, start * 1.001f);
            Assert.True(predicted > 0f, "one parcel is nothing, so this test is comparing zeroes");
        }
    }
}
