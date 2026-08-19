using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The total conductance into a coolant loop and into a room's air is summed once per rebuild
    /// rather than once per caller, and a cache is only worth having if it is invalidated
    /// correctly.
    ///
    /// <para>
    /// The reason it exists: both totals are read by the stability estimate and by the thermal
    /// mass floor, and both of those run twice in a step — once through
    /// <c>AffordableStepSeconds</c> asking what the step can afford, and once inside
    /// <c>BeginStep</c> taking it. So a step walked every room link four times to re-sum a number
    /// that only changes when the graph is rebuilt. On a 126,731-block hull that is 139,120 links
    /// walked four times before the first substep runs, for four identical answers.
    /// </para>
    ///
    /// <para>
    /// The failure mode a cache introduces is silence: a stale total makes the stability estimate
    /// wrong, which makes the step too long, which the overshoot clamps then absorb — so nothing
    /// throws and nothing looks wrong, the grid simply integrates worse. These pin the four paths
    /// that change what the totals are summing.
    /// </para>
    /// </summary>
    public class CoupledConductanceCacheTests
    {
        private static ThermalSettings Settings()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxLinkVisitsPerStep = 0;
            return settings.Derive();
        }

        /// <summary>Armour around a single sealed cell, which is a room the mapper will find.</summary>
        private static ThermalSimulation Sealed()
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

            ThermalSimulation simulation = builder.BuildSimulation(Settings(), 300f);
            simulation.RebuildAll();
            return simulation;
        }

        /// <summary>
        /// Air arriving in a room is the path most likely to be missed, because it happens long
        /// after the graph was last touched — a vent sweep, hundreds of steps into a session.
        /// </summary>
        [Fact]
        public void PressurisingARoomIsSeenByTheStabilityEstimate()
        {
            ThermalSimulation simulation = Sealed();

            // Step first, so the totals are cached before anything changes them.
            simulation.StepExact(4, Worlds.Shadow());
            float before = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;

            Assert.True(simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f),
                "the middle cell is not a room the mapper found, so nothing was pressurised");

            float after = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;

            // The *room's own* demand, not the grid's. Pressurising also adds conductance to the
            // blocks bounding the room, so the grid total moves whether or not the cached room
            // conductance was refreshed — the first version of this test asserted on the grid
            // total and passed with the cache deliberately broken.
            Assert.Equal(0f, before);
            Assert.True(after > 0f,
                "air was added to a sealed room and the room's own demand stayed at zero, so the"
                + " cached room conductance is stale");
        }

        /// <summary>
        /// And the reverse. A room that loses its air must stop being counted, or the grid keeps
        /// taking substeps for coupling that is no longer there.
        /// </summary>
        [Fact]
        public void DepressurisingARoomIsSeenToo()
        {
            ThermalSimulation simulation = Sealed();
            simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f);
            simulation.StepExact(4, Worlds.Shadow());

            float wet = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;
            Assert.True(wet > 0f, "the room never held air to begin with");

            simulation.SetRoomPressure(new Vector3I(1, 1, 1), 0f);
            float dry = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;

            Assert.True(dry < wet,
                "the room was emptied and its demand stayed at " + wet
                + ", so the cached room conductance is stale");
        }

        /// <summary>
        /// The profile reports the stiffest room and loop, and it reads the same cache. A profile
        /// that disagreed with the estimate would send someone tuning a cap against a number the
        /// solver is not acting on.
        /// </summary>
        [Fact]
        public void TheProfileAndTheEstimateReadTheSameTotals()
        {
            ThermalSimulation simulation = Sealed();
            simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f);
            simulation.StepExact(4, Worlds.Shadow());

            ThermalSolver.SubstepProfile profile = simulation.Solver.ProfileSubsteps();

            Assert.True(profile.WorstRoomAirDemand > 0f,
                "the profile sees no room air on a hull that has some");
            Assert.Equal(
                simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds),
                profile.RequiredSubstepsInForce,
                3);
        }

        /// <summary>
        /// Rebuilding the loops replaces the list the totals are indexed by, so a cache that
        /// survived it would be describing loops that no longer exist.
        /// </summary>
        [Fact]
        public void ReplacingTheLoopsDoesNotLeaveTotalsBehind()
        {
            ThermalSimulation simulation = Sealed();
            simulation.StepExact(2, Worlds.Shadow());

            simulation.Solver.SetLoops(new List<CoolantLoop>());

            // Reading the estimate is what would trip over a stale array, and it must not.
            float estimate = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);
            Assert.True(estimate > 0f && !float.IsNaN(estimate) && !float.IsInfinity(estimate));
            Assert.Empty(simulation.Solver.Loops);
        }

        /// <summary>
        /// Blocks arriving grow the solver's buffers, which is the other thing that invalidates
        /// the totals. A grid welded on while pressurised is the ordinary way this happens.
        /// </summary>
        [Fact]
        public void GrowingTheGridDoesNotLeaveTotalsBehind()
        {
            ThermalSimulation simulation = Sealed();
            simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f);
            simulation.StepExact(4, Worlds.Shadow());

            float before = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);

            for (int x = 0; x < 3; x++)
            {
                simulation.AddBlock(new BlockInstance(
                    Catalog.LightArmor(), new Vector3I(x, 0, 3), BlockOrientation.Identity), 300f);
            }

            simulation.StepExact(2, Worlds.Shadow());
            float after = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);

            Assert.True(after > 0f && !float.IsNaN(after));
            Assert.True(Math.Abs(after - before) < before,
                "welding three armour blocks moved the estimate from " + before + " to " + after);
        }
    }
}
