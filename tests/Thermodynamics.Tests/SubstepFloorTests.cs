using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A step is divided into as many substeps as the stiffest node on the grid needs, and every
    /// other node pays for them. On a real capital ship that stiffest node is a light fitting:
    /// measured on a 42,051-block hull, forty-three 16 kg lights asked for twenty-eight substeps
    /// while the five hundred kilogram armour around them asked for one, and the ship ran at a
    /// third of real time to pay for it.
    ///
    /// <c>MaxSubstepsPerBlock</c> is the answer, and it is an approximation, so what it does and
    /// does not disturb has to be pinned rather than asserted in prose:
    ///
    /// <list type="bullet">
    /// <item>it bounds what conduction can ask for;</item>
    /// <item>it moves no node that was already above the floor;</item>
    /// <item>it does not move the temperature anything settles at, because a steady state is
    /// where the watts cancel and has nothing to do with heat capacity;</item>
    /// <item>it leaves the block's real capacity on the node, so every readout still describes
    /// the block rather than the approximation used to integrate it;</item>
    /// <item>and it is inert when off.</item>
    /// </list>
    /// </summary>
    public class SubstepFloorTests
    {
        private readonly ITestOutputHelper output;

        public SubstepFloorTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>A 16 kg fitting, the block the field dump found setting the substep count.</summary>
        private static BlockModel LightFitting()
        {
            return BlockModel.Solid("LightFitting", Vector3I.One, 16f, Catalog.DefaultThermal());
        }

        private static ThermalSettings Settings(int cap)
        {
            // Pinned: substep demand is proportional to step length, so these figures are
            // about the rate they were computed at.
            ThermalSettings settings = new ThermalSettings { Frequency = 4 };
            settings.MaxSubstepsPerBlock = cap;

            // The two bounds that would otherwise hide what the floor does: one refuses the
            // substeps the estimate asks for, the other shortens the step instead of paying.
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        /// <summary>
        /// A 3x3x3 of armour with one block at its centre. The two callers differ only in what
        /// that one block is, so any difference between them is the fitting and nothing else.
        /// </summary>
        private static ThermalSimulation Hull(ThermalSettings settings, float temperature, BlockModel centre)
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        Vector3I cell = new Vector3I(x, y, z);
                        bool middle = x == 1 && y == 1 && z == 1;
                        builder.Place(middle ? centre : armour, cell);
                    }
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            simulation.RebuildAll();
            return simulation;
        }

        private static ThermalSimulation Hull(ThermalSettings settings, float temperature)
        {
            return Hull(settings, temperature, LightFitting());
        }

        [Fact]
        public void OneLightBlockSetsTheSubstepCountForTheWholeGrid()
        {
            ThermalSimulation armourOnly = Hull(Settings(0), 300f, Catalog.LightArmor());
            ThermalSimulation withFitting = Hull(Settings(0), 300f);

            float plain = armourOnly.Solver.RequiredSubsteps(armourOnly.Settings.StepSeconds);
            float stiff = withFitting.Solver.RequiredSubsteps(withFitting.Settings.StepSeconds);

            // The grids differ by one block in twenty-seven, and the difference in what a step
            // costs the other twenty-six is the whole finding.
            Assert.True(stiff > plain * 8f,
                "one 16 kg block took the substep estimate from " + plain + " to " + stiff
                + "; if that ratio has collapsed the premise of MaxSubstepsPerBlock has too");
        }

        /// <summary>
        /// The cap has to bound the estimate exactly, not approximately, because the whole point
        /// of setting it to one is that the grid then takes one substep. It therefore has to be
        /// computed from the same rate the estimate reads — conduction *and* the linearised
        /// radiation and convection — and <c>ThermalSolver.NodeStabilityRate</c> is the single
        /// place both get it from.
        /// </summary>
        [Fact]
        public void TheCapBoundsTheEstimateExactly()
        {
            ThermalSimulation uncapped = Hull(Settings(0), 300f);
            float before = uncapped.Solver.RequiredSubsteps(uncapped.Settings.StepSeconds);

            foreach (int cap in new int[] { 8, 4, 2, 1 })
            {
                ThermalSimulation capped = Hull(Settings(cap), 300f);
                float after = capped.Solver.RequiredSubsteps(capped.Settings.StepSeconds);

                Assert.True(after < before,
                    "cap " + cap + " left the estimate at " + after + ", against " + before + " uncapped");

                Assert.True(after <= cap + 0.001f,
                    "cap " + cap + " still asked for " + after + " substeps");
            }
        }

        /// <summary>
        /// A block can be stiff through the sky rather than through what it is bolted to — thin,
        /// exposed and in an atmosphere, where convection alone is worth more than six faces of
        /// armour. A cap that only looked at conduction would leave those setting the substep
        /// count and would quietly stop meaning what it says.
        /// </summary>
        [Fact]
        public void TheCapReachesBlocksMadeStiffByTheSkyRatherThanByTheirNeighbours()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(LightFitting(), Vector3I.Zero);

            ThermalSimulation exposed = builder.BuildSimulation(Settings(0), 300f);
            exposed.RebuildAll();

            // Thick air moving over one small block: convection is the only coupling it has.
            EnvironmentSample air = Worlds.PlanetSurface(1f, 0.5f);
            exposed.StepExact(1, air);

            float before = exposed.Solver.RequiredSubsteps(exposed.Settings.StepSeconds);
            Assert.True(before > 4f,
                "the lone exposed block only asked for " + before
                + " substeps, so this test is no longer exercising environment stiffness");

            GridBuilder second = GridBuilder.Large();
            second.Place(LightFitting(), Vector3I.Zero);

            ThermalSimulation capped = second.BuildSimulation(Settings(2), 300f);
            capped.RebuildAll();
            capped.StepExact(1, air);

            float after = capped.Solver.RequiredSubsteps(capped.Settings.StepSeconds);
            Assert.True(after <= 2f + 0.001f,
                "cap 2 left an environment-stiff block asking for " + after + " substeps");
        }

        [Fact]
        public void ANodeAlreadyAboveTheFloorIsNotMoved()
        {
            ThermalSimulation uncapped = Hull(Settings(0), 300f);
            ThermalSimulation capped = Hull(Settings(4), 300f);

            EnvironmentSample sample = Worlds.Shadow();
            uncapped.StepExact(20, sample);
            capped.StepExact(20, sample);

            IList<ThermalNode> a = uncapped.Solver.Nodes;
            IList<ThermalNode> b = capped.Solver.Nodes;
            Assert.Equal(a.Count, b.Count);

            int moved = 0;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Block.Model.Name == "LightFitting") continue;

                // Armour next to the fitting sees a neighbour that warms differently, so this is
                // not bit-identical — but it is small, and it must stay small.
                if (Math.Abs(a[i].Temperature - b[i].Temperature) > 0.5f) moved++;
            }

            Assert.Equal(0, moved);
        }

        /// <summary>
        /// The property that makes the approximation acceptable: heat capacity decides how fast
        /// a node gets somewhere, not where it ends up. So the difference the floor introduces
        /// is a transient — it is largest while the grid is moving and decays as it settles,
        /// rather than accumulating into a bias.
        ///
        /// Asserted as a decay rather than as an exact steady state because a hull radiating
        /// into vacuum takes an extremely long time to actually arrive at one, and a test that
        /// waited for it would be measuring patience.
        /// </summary>
        [Fact]
        public void TheDifferenceIsATransientAndDecaysAsTheGridSettles()
        {
            ThermalSimulation uncapped = Hull(Settings(0), 700f);
            ThermalSimulation capped = Hull(Settings(2), 700f);

            EnvironmentSample sample = Worlds.Shadow();

            uncapped.StepExact(40, sample);
            capped.StepExact(40, sample);
            float early = WorstDifference(uncapped, capped);

            // A length of thermal time rather than of steps: at the clock `C24` ships, four
            // thousand steps leave the hull still moving and the difference half-decayed — 3.15 K
            // to 1.58 K, which is a decay that has not finished rather than one that failed.
            uncapped.StepExact(LabClock.Steps(4000), sample);
            capped.StepExact(LabClock.Steps(4000), sample);
            float late = WorstDifference(uncapped, capped);

            Assert.True(early > 0f, "the floor changed nothing at all, so the test is not testing it");
            Assert.True(late < early * 0.5f,
                "the worst difference went from " + early + " K to " + late
                + " K; the floor's error is supposed to decay as the grid settles, not persist");
        }

        private static float WorstDifference(ThermalSimulation a, ThermalSimulation b)
        {
            IList<ThermalNode> left = a.Solver.Nodes;
            IList<ThermalNode> right = b.Solver.Nodes;

            float worst = 0f;
            for (int i = 0; i < left.Count; i++)
            {
                float difference = Math.Abs(left[i].Temperature - right[i].Temperature);
                if (difference > worst) worst = difference;
            }

            return worst;
        }

        /// <summary>
        /// The count of floored nodes describes the grid's current state, not the one step that
        /// happened to move a row.
        ///
        /// It read zero on every step, including the first, while the floor was doing all of its
        /// work: <c>SyncNodeState</c> only refreshes a mirrored row whose node is dirty, so the row
        /// still held the floored capacity from the previous step, and a pass that compared against
        /// the row found nothing left to raise. A live dump reported "blocks raised by cap 0" beside
        /// its own projection that the configured cap raises 804 blocks on that ship.
        /// </summary>
        [Fact]
        public void TheFlooredCountHoldsForAsLongAsTheFloorDoes()
        {
            ThermalSimulation simulation = Hull(Settings(3), 900f);
            EnvironmentSample sample = Worlds.Shadow();

            for (int step = 0; step < 6; step++)
            {
                simulation.StepExact(1, sample);
                Assert.Equal(1, simulation.Solver.FlooredNodes);
            }
        }

        /// <summary>
        /// Whenever a grid's own blocks demand more substeps than the cap allows, the floor must
        /// bring the estimate to exactly the cap — not below it.
        ///
        /// The floor is sized to do precisely that: hold <c>C &gt;= G dt / (safety cap)</c>, so the
        /// demand lands on the cap. A capped grid demanding less has been damped by something the
        /// cap did not ask for. Computing the floor from the mirrored row rather than from the
        /// block's capacity did exactly that: <c>SyncNodeState</c> leaves a clean row alone, so the
        /// row still held the previous step's floor, this pass compared against it and raised it
        /// again, and a 20 kg fitting on heavy armour settled at 1.94 substeps demanded against a
        /// cap of 3.
        ///
        /// The raw demand is read through <c>NodeSubstepDemand</c>, which divides by the block's
        /// real capacity and so is independent of the floor — otherwise this would be asking the
        /// floor to confirm its own work.
        /// </summary>
        [Fact]
        public void AFlooredGridDemandsExactlyItsCapAndNotLess()
        {
            // Heavy armour under a very light fitting: the pair where the floor has most to do.
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(3, 3, 3));
            builder.Place(BlockModel.Solid("Interior", Vector3I.One, 20f, Catalog.DefaultThermal()),
                new Vector3I(3, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(3), 900f);
            EnvironmentSample sample = Worlds.Shadow();

            for (int step = 0; step < 20; step++)
            {
                simulation.StepExact(1, sample);

                float raw = 0f;
                for (int i = 0; i < simulation.Solver.Nodes.Count; i++)
                {
                    float demand = simulation.Solver.NodeSubstepDemand(i);
                    if (demand > raw) raw = demand;
                }

                // Below the cap the floor has nothing to do and the demand is the grid's own.
                if (raw <= 3f) continue;

                Assert.Equal(3f, simulation.Solver.LastRequiredSubsteps, 2);
            }
        }

        [Fact]
        public void TheBlockKeepsItsRealHeatCapacity()
        {
            ThermalSimulation capped = Hull(Settings(1), 300f);
            capped.StepExact(5, Worlds.Shadow());

            ThermalNode fitting = null;
            IList<ThermalNode> nodes = capped.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.Model.Name == "LightFitting") fitting = nodes[i];
            }

            Assert.NotNull(fitting);

            // 450 J/(kg K) x 16 kg, divided by the default heat time scale. The floor lives in
            // the solver's mirrored row; the node is what every readout asks.
            float expected = (450f * 16f) / capped.Settings.HeatTimeScale;
            Assert.Equal(expected, fitting.ThermalMass, 3);
        }

        /// <summary>
        /// The profile is the instrument the setting will be tuned with, so it has to agree with
        /// the number the solver actually acts on, name the block responsible, and describe the
        /// grid rather than the settings — otherwise two dumps taken under different caps cannot
        /// be compared, which is the only thing anyone will want to do with it.
        /// </summary>
        [Fact]
        public void TheProfileAgreesWithTheEstimateAndNamesTheBlock()
        {
            ThermalSimulation sim = Hull(Settings(0), 300f);
            ThermalSolver.SubstepProfile profile = sim.Solver.ProfileSubsteps();

            float estimate = sim.Solver.RequiredSubsteps(sim.Settings.StepSeconds);

            Assert.Equal(estimate, profile.RequiredSubsteps, 3);
            Assert.Equal(estimate, profile.RequiredSubstepsInForce, 3);
            Assert.Equal(sim.Solver.Nodes.Count, profile.Nodes);

            Assert.True(profile.WorstNodeIndex >= 0);
            Assert.Equal("LightFitting", sim.Solver.Nodes[profile.WorstNodeIndex].Block.Model.Name);

            // Bolted to armour on all six faces and in shadow, so its stiffness is conduction.
            Assert.True(profile.WorstNodeConductionShare > 0.9f,
                "conduction share was " + profile.WorstNodeConductionShare);

            long counted = 0;
            for (int i = 0; i < profile.Buckets.Length; i++) counted += profile.Buckets[i];
            Assert.Equal(profile.Nodes, counted);
        }

        /// <summary>
        /// A profile taken with the cap on must still describe the grid, so that the "what would
        /// a cap do" projection means the same thing in every dump.
        /// </summary>
        [Fact]
        public void TheProfileDescribesTheGridRatherThanTheSettings()
        {
            ThermalSolver.SubstepProfile off = Hull(Settings(0), 300f).Solver.ProfileSubsteps();
            ThermalSolver.SubstepProfile on = Hull(Settings(1), 300f).Solver.ProfileSubsteps();

            Assert.Equal(off.RequiredSubsteps, on.RequiredSubsteps, 3);
            Assert.Equal(off.WorstNodeDemand, on.WorstNodeDemand, 3);

            for (int i = 0; i < off.Buckets.Length; i++)
            {
                Assert.Equal(off.Buckets[i], on.Buckets[i]);
            }

            // What is in force does move, and it is the pair of them that tells the story.
            Assert.True(on.RequiredSubstepsInForce < off.RequiredSubstepsInForce);
            Assert.True(on.RequiredSubstepsInForce <= 1.001f);
        }

        /// <summary>
        /// The projection has to predict what the cap actually does, or it is decoration.
        /// </summary>
        [Fact]
        public void TheProjectionPredictsWhatTheCapDoes()
        {
            ThermalSolver.SubstepProfile profile = Hull(Settings(0), 300f).Solver.ProfileSubsteps();
            int[] caps = ThermalSolver.SubstepProfile.ProjectedCaps;

            for (int c = 0; c < caps.Length; c++)
            {
                ThermalSimulation capped = Hull(Settings(caps[c]), 300f);
                float actual = capped.Solver.RequiredSubsteps(capped.Settings.StepSeconds);

                Assert.Equal(profile.CapRequiredSubsteps[c], actual, 2);

                int floored = capped.Solver.FlooredNodes;
                Assert.Equal(profile.CapNodesFloored[c], floored);
            }
        }

        /// <summary>
        /// Room air is the lightest thing on a ship and touches the most surface, so a small
        /// pressurised compartment is routinely stiffer than any block around it. A field dump
        /// found a 42,051-block capital ship still needing two substeps after every block on it
        /// had been capped at one, because a two-cell room demanded 1.75 on its own — so the cap
        /// has to reach the air as well, or it cannot deliver the value it is set to.
        /// </summary>
        [Fact]
        public void TheCapReachesRoomAirAndNotOnlyBlocks()
        {
            ThermalSimulation open = Sealed(SealedSettings(0));
            float before = open.Solver.RequiredSubsteps(open.Settings.StepSeconds);

            Assert.True(open.Solver.RoomAir.Count > 0, "the test hull holds no air");

            ThermalSimulation capped = Sealed(SealedSettings(1));
            float after = capped.Solver.RequiredSubsteps(capped.Settings.StepSeconds);

            Assert.True(before > 1f,
                "the sealed hull only asked for " + before + " substeps to begin with");
            Assert.True(after <= 1f + 0.001f,
                "cap 1 left the grid asking for " + after + " substeps, so something it cannot"
                + " reach is still setting the count");
        }

        /// <summary>
        /// The same settings at half the rate, for the sealed rig alone.
        ///
        /// **A cap of one is only a cap where something asks for more than one.** A substep demand
        /// is proportional to the step it is counted against, and `C24` slowed the clock by two and
        /// a half — so this one-cell compartment's air, which asked for 2.3 substeps of a
        /// quarter-second step, now asks for 0.94 and a cap of one is inert on it. At a half-second
        /// step it asks for 1.88 and the rig is the rig again. Nothing else in this class needs the
        /// change, which is why it is here rather than in <see cref="Settings"/>.
        /// </summary>
        private static ThermalSettings SealedSettings(int cap)
        {
            ThermalSettings settings = new ThermalSettings { Frequency = 2 };
            settings.MaxSubstepsPerBlock = cap;
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        /// <summary>A sealed one-cell compartment with air in it, walled in armour.</summary>
        private static ThermalSimulation Sealed(ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.LightArmor();

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        if (x == 1 && y == 1 && z == 1) continue;
                        builder.Place(armour, new Vector3I(x, y, z));
                    }
                }
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            simulation.RebuildAll();
            simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f);
            return simulation;
        }

        [Fact]
        public void TheCapIsInertWhenItIsOff()
        {
            ThermalSimulation a = Hull(Settings(0), 500f);
            ThermalSimulation b = Hull(Settings(0), 500f);

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));
            a.StepExact(50, sample);
            b.StepExact(50, sample);

            for (int i = 0; i < a.Solver.Nodes.Count; i++)
            {
                Assert.Equal(a.Solver.Nodes[i].Temperature, b.Solver.Nodes[i].Temperature);
            }
        }

        /// <summary>
        /// **The floor bounds the whole grid's demand, which is what makes it `C19`'s third route.**
        ///
        /// <para>
        /// The atmospheric breach is a demand of 73.4 against the 64 the ceiling grants. A per-block
        /// floor does not refuse that demand, it removes it: every node it raises stops asking for
        /// more than the cap, so the grid's own estimate lands on the cap and the ceiling never
        /// binds. Asserted in air, because convection is what makes the demand large in the first
        /// place and a vacuum rig would agree for the wrong reason.
        /// </para>
        /// </summary>
        [Fact]
        public void TheFloorTakesTheWholeGridsDemandDownToItsCap()
        {
            const int Cap = 6;

            ThermalSettings uncapped = Settings(0);
            ThermalSettings capped = Settings(Cap);

            float demanded = DemandInAir(uncapped);
            float bounded = DemandInAir(capped);

            output.WriteLine("demand {0:n2} uncapped, {1:n2} at a cap of {2}", demanded, bounded, Cap);

            // The rig has to be stiff enough in air for the cap to be doing anything (`E8`).
            Assert.True(demanded > Cap * 2f,
                "the hull demands only " + demanded + " substeps in air, so a cap of " + Cap
                + " has nothing to bound and this judges nothing");

            Assert.True(bounded <= Cap + 0.01f,
                "the cap left the grid demanding " + bounded + " against a cap of " + Cap);
        }

        /// <summary>What the stiffest element asks of a step, with the grid in thick air.</summary>
        private static float DemandInAir(ThermalSettings settings)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));
            builder.Place(LightFitting(), new Vector3I(2, 4, 2));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            // Stepped once first: the estimate reads the environment, so a demand taken before the
            // grid has met its air is a vacuum figure.
            simulation.StepExact(1, Worlds.Flight(1f, 200f));
            return simulation.Solver.LastRequiredSubsteps;
        }
    }
}
