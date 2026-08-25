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
    /// already spills a dying loop into its pipes rather than deleting it — that fix went in after a
    /// 190 MJ case where grinding a pump dumped a ship's heat on demand — and this pins how much of
    /// it actually survives.
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
                total += (loop.Temperature - Ambient) * loop.SegmentThermalMass * loop.Pipes.Count;
            }

            return total;
        }

        /// <summary>
        /// **A rebuild provoked by an unrelated block keeps a hot loop's heat**, which is the half
        /// of this that works. Loops carry their temperature across a rebuild by signature, so
        /// building anywhere on a ship does not cool its coolant.
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
        /// **Breaking the ring destroys about two thirds of the coolant's heat**, and the fraction
        /// is arithmetic rather than a tuning choice.
        ///
        /// <para>
        /// `SpillDissolvedLoops` mixes each parcel into the pipe it was in —
        /// `mixed = (T_n M_n + T_s M_s) / (M_n + M_s)` — and then assigns that temperature to the
        /// node, whose capacity is still `M_n`. So the energy that lands is `mixed x M_n` where the
        /// energy that went in was `T_n M_n + T_s M_s`, and **the surviving fraction is
        /// `M_n / (M_n + M_s)`**. A pipe node holds about 910 J/K against the parcel's 1,889, so
        /// about a third survives — which is what this measures.
        /// </para>
        ///
        /// <para>
        /// Whether a severed ring's coolant *should* keep its heat is a design question and not
        /// this test's business: grinding a sealed pipe would vent the coolant, and heat leaving
        /// with it is arguable. What is not arguable is that the code neither vents it nor keeps
        /// it, and the fraction it happens to keep is the ratio of two masses nobody chose.
        /// </para>
        /// </summary>
        [Fact]
        public void BreakingARingDestroysMostOfTheCoolantsHeat()
        {
            GridBuilder builder = GridBuilder.Large();
            List<BlockInstance> ring =
                PipeFitter.BuildRing(builder, PipeFitter.RectangleXZ(Vector3I.Zero, 3, 3));

            ThermalSimulation simulation = builder.BuildSimulation(Isolated());
            CoolantLoop loop = simulation.Solver.Loops[0];
            for (int i = 0; i < loop.Pipes.Count; i++) loop.SetSegmentTemperature(i, 900f);

            float segmentMass = loop.SegmentThermalMass;
            float nodeMass = simulation.Solver.GetNode(ring[0]).ThermalMass;
            float start = HeatAboveAmbient(simulation);

            BlockInstance popped = ring[3];
            float poppedHeat = (simulation.Solver.GetNode(popped).Temperature - Ambient)
                * simulation.Solver.GetNode(popped).ThermalMass;

            simulation.RemoveBlock(popped);
            simulation.RebuildAll();

            simulation.AddBlock(popped);
            simulation.RebuildAll();
            float end = HeatAboveAmbient(simulation);

            float lost = start - end;
            float predicted = start * segmentMass / (nodeMass + segmentMass);

            output.WriteLine("pipe node {0:n0} J/K, coolant parcel {1:n0} J/K", nodeMass, segmentMass);
            output.WriteLine("heat above ambient {0:n0} J -> {1:n0} J; lost {2:n0} ({3:n1} %)",
                start, end, lost, 100f * lost / start);
            output.WriteLine("the ground pipe itself accounts for {0:n0} J", poppedHeat);
            output.WriteLine("predicted loss from the mass ratio alone: {0:n0} J", predicted);

            Assert.Single(simulation.Solver.Loops);

            // **The mass ratio predicts it to a couple of per cent**, and the residue is the one
            // pipe that left: its whole parcel goes with the block rather than being mixed. Pinned
            // as a band rather than an equality, because the exact figure is two masses and a ring
            // length and none of the three is the finding.
            Assert.InRange(lost, predicted * 0.9f, predicted * 1.15f);

            // And it is most of the heat, not a rounding.
            Assert.True(lost > 0.5f * start,
                "only " + (100f * lost / start).ToString("n1") + " % was lost, so the arithmetic "
                + "this test describes has changed and the row it pins wants re-reading");
        }
    }
}
