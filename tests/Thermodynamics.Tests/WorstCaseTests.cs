using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether the expensive scenarios are actually expensive.
    ///
    /// <para>
    /// The performance report spent its whole existence stating that convection cost nothing. It
    /// was not wrong about what it measured: every benchmark ran in vacuum, where
    /// <c>AtmosphereFactor</c> is zero and the convection branch never executes. A confident zero
    /// from a case that never ran is worse than a wrong number, because a wrong number invites
    /// scrutiny and a zero closes the question.
    /// </para>
    ///
    /// <para>
    /// So every worst case has a test here saying it built the thing it is named after. These are
    /// not tests of the simulation — they are tests that the measurements have a subject.
    /// </para>
    /// </summary>
    public class WorstCaseTests
    {
        private const int Size = 2000;

        [Fact]
        public void APressurisedHullActuallyHoldsAir()
        {
            WorstCases.Built built = WorstCases.Pressurised("ship", Size);

            Assert.True(built.Rooms > 0,
                "the hull has no sealed compartments at all, so there is nothing to pressurise");
            Assert.True(built.RoomsWithAir > 0,
                built.Rooms + " compartments were found and none of them took air");

            // Air with mass and no coupling is the exact defect the room-air work was done to
            // fix, and a benchmark hull is where it would go unnoticed.
            IList<RoomAirNode> air = built.Simulation.Solver.RoomAir;
            int coupled = 0;
            for (int i = 0; i < air.Count; i++)
            {
                if (air[i].HasAir && air[i].Links.Count > 0) coupled++;
            }

            Assert.True(coupled > 0, "every room with air has no links to the blocks bounding it");
        }

        /// <summary>
        /// Air is the lightest thing on a ship and touches the most surface, so it appears in the
        /// substep estimate on its own account. This asserts that it does — not that it wins.
        ///
        /// It does not win, and that is worth recording rather than asserting around: the solver
        /// carries a comment saying room air is "usually what sets the substep count once a ship
        /// is pressurised", and neither the benchmark hull nor the field ship agrees. A field dump
        /// of a 42,051-block capital ship with 122 compartments put its stiffest air at 1.75
        /// substeps against 31 for a light fitting. Compartments are large and air is thin; the
        /// comment is a plausible expectation that measurement did not bear out.
        /// </summary>
        [Fact]
        public void AirIsCoupledTightlyEnoughToAppearInTheEstimate()
        {
            ThermalSolver.SubstepProfile dry =
                WorstCases.Hull("ship", Size).Simulation.Solver.ProfileSubsteps();
            ThermalSolver.SubstepProfile wet =
                WorstCases.Pressurised("ship", Size).Simulation.Solver.ProfileSubsteps();

            Assert.Equal(0f, dry.WorstRoomAirDemand);
            Assert.True(wet.WorstRoomAirDemand > 0f,
                "air was added to the compartments and the stability estimate still sees none of"
                + " it, so it is not coupled to the blocks around it");

            Assert.True(wet.WorstRoomAirIndex >= 0);
        }

        [Fact]
        public void APlumbedHullActuallyHasPlumbing()
        {
            WorstCases.Built built = WorstCases.Plumbed("ship", Size);

            Assert.True(built.CoolantLoops > 0,
                "no coolant loop closed, so the loop passes are measuring an empty list");
            Assert.True(built.HeatPumps > 0,
                "no heat pump bound to two nodes, so the pump pass is measuring an empty list");
        }

        [Fact]
        public void ABurningHullActuallyBurns()
        {
            WorstCases.Built built = WorstCases.Burning("ship", Size);

            Assert.True(built.Producers > 0, "nothing on the hull generates heat");

            int events = 0;
            for (int i = 0; i < 400; i++)
            {
                built.Simulation.StepExact(1, Worlds.Shadow());
                events += built.Simulation.Solver.Overheats.Count;
            }

            Assert.True(events > 0,
                "four hundred steps of a hull driven at twenty times census power raised no"
                + " overheat event, so the damage path is still unmeasured");
        }

        /// <summary>
        /// `burning` is not the worst case for the damage check, and this is what says so.
        ///
        /// A hull driven through its heat producers leaves most of itself under its rating, so the
        /// check that decides whether a block is burning fails on nearly every node — the ordinary
        /// case. `scorched` puts every node over every rating in the catalogue and holds it there,
        /// so the expensive branch is taken on every node of every substep, which is the bound.
        ///
        /// <para>
        /// This used to assert that by counting events, against <c>nodes × substeps</c>. The
        /// solver now accumulates a block's damage across the substeps and files one event a step,
        /// so the count is a count of blocks and no longer scales with the substeps taken — see
        /// <c>OverheatEventTests</c>. What is asserted instead is the thing the scenario is for:
        /// <em>every</em> node is burning, on a step that takes more than one substep, and it
        /// stays that way.
        /// </para>
        /// </summary>
        [Fact]
        public void AScorchedHullBurnsOnEveryNodeOfEverySubstep()
        {
            WorstCases.Built built = WorstCases.Scorched("ship", Size);

            Assert.True(built.Nodes > 0, "the hull is empty");

            built.Simulation.StepExact(1, Worlds.Shadow());

            Assert.True(built.Simulation.Solver.LastSubsteps > 1,
                "the scorched hull took one substep, so the damage check is not being run"
                + " repeatedly and this is not the worst case it is meant to be");

            Assert.Equal(built.Nodes, built.Simulation.Solver.Overheats.Count);

            // And it stays there. Held at temperature rather than driven to it, or the hull
            // radiates itself cold in one substep and the rest of the run measures the ordinary
            // case with extra steps.
            for (int i = 0; i < 20; i++) built.Simulation.StepExact(1, Worlds.Shadow());
            Assert.Equal(built.Nodes, built.Simulation.Solver.Overheats.Count);
        }

        /// <summary>
        /// The scenario that no benchmark could ever be, and the one that produced the largest
        /// finding of the project when a telemetry dump finally showed it: a world is many grids,
        /// and each pays its own fixed per-step cost.
        /// </summary>
        [Fact]
        public void AFleetIsManyGridsAndNotOneLargeOne()
        {
            List<WorstCases.Built> fleet = WorstCases.Fleet("ship", 4000, 20);

            Assert.Equal(20, fleet.Count);

            int total = 0;
            for (int i = 0; i < fleet.Count; i++)
            {
                Assert.True(fleet[i].Nodes > 0, "grid " + i + " is empty");
                total += fleet[i].Nodes;
            }

            Assert.True(total > 2000,
                "twenty grids came to " + total + " nodes, which is not the fleet that was asked for");
        }

        [Fact]
        public void SteppingAFleetAdvancesEveryGridInIt()
        {
            List<WorstCases.Built> fleet = WorstCases.Fleet("ship", 2000, 8);

            long before = 0;
            for (int i = 0; i < fleet.Count; i++) before += fleet[i].Simulation.Solver.StepCount;

            WorstCases.StepFleet(fleet, Worlds.Shadow(), 3);

            for (int i = 0; i < fleet.Count; i++)
            {
                Assert.True(fleet[i].Simulation.Solver.StepCount > 0,
                    "grid " + i + " never stepped");
            }

            Assert.Equal(0, before);
        }

        /// <summary>
        /// Convection is the case that started this: it is switched off by a vacuum rather than by
        /// its setting, so a world with no air makes the feature free and the report says so.
        /// </summary>
        [Fact]
        public void AnAtmosphereActuallyEngagesConvectionAndFriction()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            WorstCases.Built built = WorstCases.Hull("ship", Size, settings);

            EnvironmentState vacuum = EnvironmentSolver.Solve(
                settings, built.Simulation.Planet, Worlds.Space(new Vector3(0f, 1f, 0f)));
            EnvironmentState flight = EnvironmentSolver.Solve(
                settings, built.Simulation.Planet, Worlds.Flight(1f, 300f));

            Assert.True(vacuum.AtmosphereFactor <= 0f,
                "the vacuum sample has air in it, so it is not the case it is named after");
            Assert.True(flight.AtmosphereFactor > 0f,
                "the flight sample has no air, so convection would be switched off by the world"
                + " rather than by its setting — which is exactly how it came to be measured as free");
            Assert.True(flight.FrictionActive,
                "the flight sample is not moving fast enough to engage friction");
        }

        /// <summary>
        /// A truss is the cheapest hull in vacuum and the stiffest in flight. A benchmark suite
        /// that only ever measured a ship in vacuum was measuring the cheapest of nine cases, and
        /// this is what stops that being true again quietly.
        /// </summary>
        [Fact]
        public void TheWorstShapeDependsOnTheWorld()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            float shipVacuum = Demand("ship", settings, Worlds.Space(new Vector3(0f, 1f, 0f)));
            float trussVacuum = Demand("truss", settings, Worlds.Space(new Vector3(0f, 1f, 0f)));
            float trussFlight = Demand("truss", settings, Worlds.Flight(1f, 300f));

            Assert.True(trussVacuum < shipVacuum,
                "a truss is meant to be the soft case in vacuum: " + trussVacuum + " against " + shipVacuum);
            Assert.True(trussFlight > trussVacuum * 1.5f,
                "an atmosphere is meant to make a truss far stiffer, and took it from "
                + trussVacuum + " to " + trussFlight);
        }

        private static float Demand(string shape, ThermalSettings settings, EnvironmentSample sample)
        {
            WorstCases.Built built = WorstCases.Hull(shape, Size, settings);
            built.Simulation.StepExact(2, sample);
            return built.Simulation.Solver.LastRequiredSubsteps;
        }
    }
}
