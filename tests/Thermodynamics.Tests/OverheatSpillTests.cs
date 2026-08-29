using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **A block that leaves the world above its critical temperature leaves its heat behind.**
    ///
    /// <para>
    /// Heat departing with a departing block is a deliberate limit, argued in
    /// known-issues.md, Deliberate limits, and pinned by
    /// `EnergyIsNotConservedWhenTheBlockPopulationChanges`. The argument for it is about a block a
    /// **player takes away**: conserving that means a grinder that heats the ship around it and a
    /// welder that chills it, a mechanism nobody would connect to a cause.
    /// </para>
    ///
    /// <para>
    /// **A node past critical is a different event.** It did not leave — it failed in place, and
    /// the mod is what destroyed it. Letting its energy go makes overheating a *reward*: cook a
    /// cheap block and the world is that much cooler, for free and repeatably. That is the exploit
    /// backlog.md `B42` names and the half of it the coolant consumable
    /// (`B43`, `B44`) does not price — the sacrificial block, the grind-and-reweld timer on a
    /// glowing block, and the crudest version that needs no grinder at all, because the mod
    /// destroys the block for you.
    /// </para>
    ///
    /// <para>
    /// **The rule is read off the temperature rather than off the cause**, because the cause is not
    /// knowable where the decision is made: the game removes a block and the mod is told, with
    /// nothing to say whether a grinder or a fire did it. One test answers all three variants and
    /// leaves a cool block ground off exactly as it was.
    /// </para>
    /// </summary>
    public class OverheatSpillTests
    {
        /// <summary>A 3x3x3 of light armour with one corner driven whereever the caller wants it.</summary>
        private static ThermalSimulation Cube()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(2, 2, 2));
            return builder.BuildSimulation(Fixture.ConductionOnly());
        }

        private static float Critical(ThermalNode node)
        {
            return node.Thermal.CriticalTemperature;
        }

        /// <summary>
        /// Two energies equal to a part in a million.
        ///
        /// **Relative rather than absolute, because a total over twenty-seven nodes is a sum of
        /// floats** and the claim being made is about where the energy went, not about the last bit
        /// of a megajoule. An absolute tolerance here would be a tolerance on the size of the test
        /// grid rather than on the physics.
        /// </summary>
        private static void AssertJoules(float expected, float actual)
        {
            float scale = System.Math.Max(System.Math.Abs(expected), 1f);
            Assert.True(System.Math.Abs(expected - actual) <= scale * 1e-6f,
                "expected " + expected + " J and got " + actual + " J");
        }

        /// <summary>
        /// **The exploit, measured before it is closed**: a block removed below critical takes its
        /// energy out of the world, which is the limit and is deliberate.
        /// </summary>
        [Fact]
        public void ACoolBlockStillTakesItsHeatWithIt()
        {
            ThermalSimulation simulation = Cube();
            ThermalNode leaving = simulation.Solver.GetNodeAt(Vector3I.Zero);
            ThermalNode neighbour = simulation.Solver.GetNodeAt(new Vector3I(1, 0, 0));

            // Comfortably under the rating, which is what a block a player grinds off looks like.
            leaving.Temperature = Critical(leaving) * 0.5f;
            simulation.Solver.BuildLinksIfNeeded();

            float before = simulation.Solver.TotalEnergy;
            float carried = leaving.Energy;
            float neighbourBefore = neighbour.Temperature;

            simulation.RemoveBlock(leaving.Block);

            Assert.Equal(0f, simulation.Solver.SpilledEnergy);
            AssertJoules(before - carried, simulation.Solver.TotalEnergy);
            Assert.Equal(neighbourBefore,
                simulation.Solver.GetNodeAt(new Vector3I(1, 0, 0)).Temperature, 4);
        }

        /// <summary>
        /// **A block that cooked keeps the world's energy in the world.** The total is unchanged to
        /// the joule, which is the whole claim: the sacrificial-block heat sink is worth nothing.
        /// </summary>
        [Fact]
        public void ABlockThatLeavesAboveCriticalHandsItsHeatToItsNeighbours()
        {
            ThermalSimulation simulation = Cube();
            ThermalNode leaving = simulation.Solver.GetNodeAt(Vector3I.Zero);

            leaving.Temperature = Critical(leaving) + 100f;
            simulation.Solver.BuildLinksIfNeeded();

            float before = simulation.Solver.TotalEnergy;
            float carried = leaving.Energy;
            Assert.True(carried > 0f, "the departing block holds no energy, so this proves nothing");

            simulation.RemoveBlock(leaving.Block);

            AssertJoules(carried, simulation.Solver.SpilledEnergy);

            // The energy stayed in the world rather than leaving with the block.
            AssertJoules(before, simulation.Solver.TotalEnergy);
        }

        /// <summary>
        /// **Every neighbour takes the same temperature rise**, which is the mixing answer — where
        /// conduction would have carried them given time — rather than a guess at a rate.
        /// </summary>
        [Fact]
        public void TheHeatIsSpreadByCapacitySoEveryNeighbourRisesTheSame()
        {
            ThermalSimulation simulation = Cube();
            ThermalNode leaving = simulation.Solver.GetNodeAt(Vector3I.Zero);
            simulation.Solver.BuildLinksIfNeeded();

            Vector3I[] touching =
            {
                new Vector3I(1, 0, 0), new Vector3I(0, 1, 0), new Vector3I(0, 0, 1),
            };

            float[] before = new float[touching.Length];
            for (int i = 0; i < touching.Length; i++)
            {
                before[i] = simulation.Solver.GetNodeAt(touching[i]).Temperature;
            }

            leaving.Temperature = Critical(leaving) + 100f;
            float energy = leaving.Energy;

            simulation.RemoveBlock(leaving.Block);

            float first = simulation.Solver.GetNodeAt(touching[0]).Temperature - before[0];
            Assert.True(first > 0f, "the neighbours took nothing, so nothing was spilled");

            for (int i = 1; i < touching.Length; i++)
            {
                float rise = simulation.Solver.GetNodeAt(touching[i]).Temperature - before[i];
                Assert.Equal(first, rise, 3);
            }

            // And the rise is the energy over the capacity that took it, not an invented number.
            float capacity = 0f;
            for (int i = 0; i < touching.Length; i++)
            {
                capacity += simulation.Solver.GetNodeAt(touching[i]).ThermalMass;
            }

            Assert.Equal(energy / capacity, first, 3);
        }

        /// <summary>
        /// **A block with nowhere to put it keeps today's behaviour.** A single unattached block
        /// that cooks itself really does take its heat with it, and inventing a recipient would be
        /// worse than the limit (`E8`).
        /// </summary>
        [Fact]
        public void ALoneBlockWithNoNeighboursTakesItsHeatWithIt()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(Fixture.ConductionOnly());

            ThermalNode leaving = simulation.Solver.GetNodeAt(Vector3I.Zero);
            leaving.Temperature = Critical(leaving) + 100f;
            simulation.Solver.BuildLinksIfNeeded();

            float before = simulation.Solver.TotalEnergy;
            float carried = leaving.Energy;

            simulation.RemoveBlock(leaving.Block);

            Assert.Equal(0f, simulation.Solver.SpilledEnergy);
            AssertJoules(before - carried, simulation.Solver.TotalEnergy);
        }

        /// <summary>
        /// **The exploit is worth nothing now, measured as a player would run it**: cook a corner
        /// block, let the mod destroy it, weld a fresh one back, repeat. Before this change every
        /// cycle removed a block's worth of energy from the hull for the price of welding time.
        /// </summary>
        [Fact]
        public void CookAndRewealdIsNoLongerAHeatSink()
        {
            ThermalSimulation simulation = Cube();
            simulation.Solver.BuildLinksIfNeeded();

            // Put the hull somewhere warm so there is something to pump away.
            foreach (ThermalNode node in simulation.Solver.Nodes) node.Temperature = 600f;

            float start = simulation.Solver.TotalEnergy;

            for (int cycle = 0; cycle < 5; cycle++)
            {
                ThermalNode victim = simulation.Solver.GetNodeAt(Vector3I.Zero);

                // The pump: drive the sacrificial block past its rating out of the hull's own heat.
                victim.Temperature = Critical(victim) + 200f;
                float pumped = victim.Energy;

                simulation.RemoveBlock(victim.Block);
                AssertJoules(pumped, simulation.Solver.SpilledEnergy);

                // And the reweld, which arrives at ambient as it always has.
                simulation.AddBlock(new BlockInstance(
                    Catalog.LightArmor(), Vector3I.Zero, BlockOrientation.Identity));
                simulation.Solver.BuildLinksIfNeeded();
            }

            // Every joule the pump moved came back. What the hull gained is the five welded blocks
            // arriving at ambient, which is the welding half of the limit and is not this row.
            float welded = 5f * simulation.Solver.GetNodeAt(Vector3I.Zero).Energy;
            Assert.True(simulation.Solver.TotalEnergy >= start - welded,
                "the cycle removed energy from the hull, so it is still a heat sink");
        }
        /// <summary>
        /// **A block that dies while the link graph is dirty still leaves its heat behind.**
        ///
        /// <para>
        /// This was a hole in the first draft and it was the silent kind. A node's links are an
        /// intrusive chain of indices, so a stale chain does not read as *empty* — it reads as
        /// somebody else's neighbours. Guarding on the flag and skipping the spill would have meant
        /// a block that happened to fail while a rebuild was pending leaked its heat, with nothing
        /// anywhere saying which ones had (`E4`). Building the graph first is bounded: a rebuild
        /// clears the flag, so a cascade of failures pays for one.
        /// </para>
        /// </summary>
        [Fact]
        public void ABlockThatDiesWithADirtyGraphStillSpills()
        {
            ThermalSimulation simulation = Cube();
            simulation.Solver.BuildLinksIfNeeded();

            // Dirty the graph the way a layout change does: weld a block on and do not step.
            simulation.AddBlock(new BlockInstance(
                Catalog.LightArmor(), new Vector3I(3, 0, 0), BlockOrientation.Identity));

            ThermalNode leaving = simulation.Solver.GetNodeAt(Vector3I.Zero);
            leaving.Temperature = Critical(leaving) + 100f;

            float before = simulation.Solver.TotalEnergy;
            float carried = leaving.Energy;

            simulation.RemoveBlock(leaving.Block);

            AssertJoules(carried, simulation.Solver.SpilledEnergy);
            AssertJoules(before, simulation.Solver.TotalEnergy);
        }

    }
}
