using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;

namespace Thermodynamics.Tests
{
    public class CoupledConductanceCacheTests
    {
/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

/// <summary>Sealed operation.</summary>
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

        [Fact]
/// <summary>PressurisingARoomIsSeenByTheStabilityEstimate operation.</summary>
        public void PressurisingARoomIsSeenByTheStabilityEstimate()
        {
/// <summary>Sealed operation.</summary>
            ThermalSimulation simulation = Sealed();

            simulation.StepExact(4, Worlds.Shadow());
            float before = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;

            Assert.True(simulation.SetRoomPressure(new Vector3I(1, 1, 1), 1f),
                "the middle cell is not a room the mapper found, so nothing was pressurised");

            float after = simulation.Solver.ProfileSubsteps().WorstRoomAirDemand;

            Assert.Equal(0f, before);
            Assert.True(after > 0f,
                "air was added to a sealed room and the room's own demand stayed at zero, so the"
                + " cached room conductance is stale");
        }

        [Fact]
/// <summary>DepressurisingARoomIsSeenToo operation.</summary>
        public void DepressurisingARoomIsSeenToo()
        {
/// <summary>Sealed operation.</summary>
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

        [Fact]
/// <summary>TheProfileAndTheEstimateReadTheSameTotals operation.</summary>
        public void TheProfileAndTheEstimateReadTheSameTotals()
        {
/// <summary>Sealed operation.</summary>
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

        [Fact]
/// <summary>ReplacingTheLoopsDoesNotLeaveTotalsBehind operation.</summary>
        public void ReplacingTheLoopsDoesNotLeaveTotalsBehind()
        {
/// <summary>Sealed operation.</summary>
            ThermalSimulation simulation = Sealed();
            simulation.StepExact(2, Worlds.Shadow());

            simulation.Solver.SetLoops(new List<CoolantLoop>());

            float estimate = simulation.Solver.RequiredSubsteps(simulation.Settings.StepSeconds);
            Assert.True(estimate > 0f && !float.IsNaN(estimate) && !float.IsInfinity(estimate));
            Assert.Empty(simulation.Solver.Loops);
        }

        [Fact]
/// <summary>GrowingTheGridDoesNotLeaveTotalsBehind operation.</summary>
        public void GrowingTheGridDoesNotLeaveTotalsBehind()
        {
/// <summary>Sealed operation.</summary>
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
