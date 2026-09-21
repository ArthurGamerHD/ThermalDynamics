using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class WorstCaseTests
    {
        private const int Size = 2000;

        [Fact]
/// <summary>APressurisedHullActuallyHoldsAir operation.</summary>
        public void APressurisedHullActuallyHoldsAir()
        {
            WorstCases.Built built = WorstCases.Pressurised("ship", Size);

            Assert.True(built.Rooms > 0,
                "the hull has no sealed compartments at all, so there is nothing to pressurise");
            Assert.True(built.RoomsWithAir > 0,
                built.Rooms + " compartments were found and none of them took air");

            IList<RoomAirNode> air = built.Simulation.Solver.RoomAir;
            int coupled = 0;
            for (int i = 0; i < air.Count; i++)
            {
                if (air[i].HasAir && air[i].Links.Count > 0) coupled++;
            }

            Assert.True(coupled > 0, "every room with air has no links to the blocks bounding it");
        }

        [Fact]
/// <summary>AirIsCoupledTightlyEnoughToAppearInTheEstimate operation.</summary>
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
/// <summary>APlumbedHullActuallyHasPlumbing operation.</summary>
        public void APlumbedHullActuallyHasPlumbing()
        {
            WorstCases.Built built = WorstCases.Plumbed("ship", Size);

            Assert.True(built.CoolantLoops > 0,
                "no coolant loop closed, so the loop passes are measuring an empty list");
            Assert.True(built.HeatPumps > 0,
                "no heat pump bound to two nodes, so the pump pass is measuring an empty list");
        }

        [Fact]
/// <summary>TheRingsFixtureIsWhereThePlumbingSetsTheDemand operation.</summary>
        public void TheRingsFixtureIsWhereThePlumbingSetsTheDemand()
        {
            WorstCases.Built built = WorstCases.HeatedRings(2);

            Assert.True(built.CoolantLoops > 0,
                "no coolant loop closed, so the fixture is a reactor in a box of pipes");

            ThermalSolver.SubstepProfile profile = built.Simulation.Solver.ProfileSubsteps();
            IList<CoolantLoop> plumbing = built.Simulation.Solver.Loops;

            bool stiffestIsPlumbed = false;
            for (int l = 0; l < plumbing.Count && !stiffestIsPlumbed; l++)
            {
                for (int i = 0; i < plumbing[l].Links.Count; i++)
                {
                    if (plumbing[l].Links[i].NodeIndex != profile.WorstNodeIndex) continue;
                    stiffestIsPlumbed = true;
                    break;
                }
            }

            Assert.True(stiffestIsPlumbed,
                "the stiffest node on the fixture asks for " + profile.WorstNodeDemand
                + " substeps and is not one the plumbing touches, so this fixture is measuring the"
                + " same thing census does");

            built.Simulation.StepExact(600, Worlds.Shadow());

            Assert.True(plumbing[0].LastWattsAbsorbed > 0f,
                "the ring absorbed nothing over ten simulated minutes beside a running reactor");
        }

        [Fact]
/// <summary>ABurningHullActuallyBurns operation.</summary>
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

        [Fact]
/// <summary>AScorchedHullBurnsOnEveryNodeOfEverySubstep operation.</summary>
        public void AScorchedHullBurnsOnEveryNodeOfEverySubstep()
        {
            WorstCases.Built built = WorstCases.Scorched("ship", Size);

            Assert.True(built.Nodes > 0, "the hull is empty");

            built.Simulation.StepExact(1, Worlds.Shadow());

            Assert.True(built.Simulation.Solver.LastSubsteps > 1,
                "the scorched hull took one substep, so the damage check is not being run"
                + " repeatedly and this is not the worst case it is meant to be");

            Assert.Equal(built.Nodes, built.Simulation.Solver.Overheats.Count);

            for (int i = 0; i < 20; i++) built.Simulation.StepExact(1, Worlds.Shadow());
            Assert.Equal(built.Nodes, built.Simulation.Solver.Overheats.Count);
        }

        [Fact]
/// <summary>AFleetIsManyGridsAndNotOneLargeOne operation.</summary>
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
/// <summary>SteppingAFleetAdvancesEveryGridInIt operation.</summary>
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

        [Fact]
/// <summary>AnAtmosphereActuallyEngagesConvectionAndFriction operation.</summary>
        public void AnAtmosphereActuallyEngagesConvectionAndFriction()
        {
/// <summary>ThermalSettings operation.</summary>
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

        [Fact]
/// <summary>TheWorstShapeDependsOnTheWorld operation.</summary>
        public void TheWorstShapeDependsOnTheWorld()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

/// <summary>Demand operation.</summary>
            float shipVacuum = Demand("ship", settings, Worlds.Space(new Vector3(0f, 1f, 0f)));
/// <summary>Demand operation.</summary>
            float trussVacuum = Demand("truss", settings, Worlds.Space(new Vector3(0f, 1f, 0f)));
/// <summary>Demand operation.</summary>
            float trussFlight = Demand("truss", settings, Worlds.Flight(1f, 300f));

            Assert.True(trussVacuum < shipVacuum,
                "a truss is meant to be the soft case in vacuum: " + trussVacuum + " against " + shipVacuum);
            Assert.True(trussFlight > trussVacuum * 1.25f,
                "an atmosphere is meant to make a truss stiffer, and took it from "
                + trussVacuum + " to " + trussFlight);
        }

/// <summary>Demand operation.</summary>
        private static float Demand(string shape, ThermalSettings settings, EnvironmentSample sample)
        {
            WorstCases.Built built = WorstCases.Hull(shape, Size, settings);
            built.Simulation.StepExact(2, sample);
            return built.Simulation.Solver.LastRequiredSubsteps;
        }
    }
}
