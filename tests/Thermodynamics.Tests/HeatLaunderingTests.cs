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
    /// **A third of it used to** (backlog.md `A12`). The spill mixed each parcel into its pipe at
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
            float predicted = start / pipes;

            output.WriteLine("pipe node {0:n0} J/K, coolant parcel {1:n0} J/K", nodeMass, segmentMass);
            output.WriteLine("heat above ambient {0:n0} J -> {1:n0} J broken -> {2:n0} J rewelded",
                start, broken, end);
            output.WriteLine("lost {0:n0} J ({1:n2} %); one parcel of {2} predicts {3:n0} J ({4:n2} %)",
                lost, 100f * lost / start, pipes, predicted, 100f / pipes);

            Assert.Single(simulation.Solver.Loops);

            // The whole claim: one parcel's worth, not the mass ratio's worth.
            Assert.InRange(lost, predicted * 0.99f, predicted * 1.01f);

            // And the break itself costs the same — the reweld adds nothing back and takes nothing
            // more, which is what makes grind-and-reweld useless as a heat sink rather than merely
            // less good than it was.
            Assert.InRange(start - broken, predicted * 0.99f, predicted * 1.01f);
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

            Assert.InRange(afterFirst, (start - parcel) * 0.99f, (start - parcel) * 1.01f);

            // **The second grind of the same pipe is free**, because what it carries away is the
            // ambient parcel the first reweld gave it back.
            Assert.InRange(afterSecond, afterFirst * 0.999f, afterFirst * 1.001f);

            // And a pipe that is still hot costs its parcel, at the same price as the first.
            Assert.InRange(afterOther, (afterSecond - parcel) * 0.99f, (afterSecond - parcel) * 1.01f);

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
        /// **No pipe ends hotter than the parcel it absorbed.** Boundedness is one of the solver's
        /// three invariants, and it is the reason the fix is a mixture rather than the other
        /// conserving option — pouring the parcel's energy into the node at the node's own capacity,
        /// which puts a 900 K parcel into a 941 J/K pipe at 2,106 K and destroys it.
        /// </summary>
        [Fact]
        public void ASpilledParcelNeverHeatsItsPipeAboveItself()
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

            output.WriteLine("hottest pipe after the spill: {0:n2} K", hottest);

            Assert.True(hottest <= 900f + 0.01f,
                "a pipe reached " + hottest.ToString("n2") + " K, above the 900 K parcel it absorbed");

            // And it is genuinely warm, or the assertion above is passing on a spill that did
            // nothing: a 941 J/K pipe taking a 1,889 J/K parcel at 900 K lands near 700 K.
            Assert.True(hottest > 500f,
                "the hottest pipe is only " + hottest.ToString("n2") + " K, so the spill moved "
                + "almost nothing and the bound above is not being tested");
        }

        /// <summary>
        /// **Coolant a pipe is holding survives a save and a load.** The block temperature is
        /// written whatever happens, so a reload that dropped the capacity would put the mixed
        /// temperature onto the bare pipe and destroy exactly the fraction the mix conserved — `A12`
        /// again, on a slower trigger.
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

            Assert.InRange(lost, predicted * 0.99f, predicted * 1.01f);
        }
    }
}
