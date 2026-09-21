using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class RoomAirCouplingTests
    {
        private static readonly Vector3I Interior = Vector3I.Zero;

/// <summary>Shell operation.</summary>
        private static ThermalSimulation Shell(float blockTemperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            return builder.BuildSimulation(new ThermalSettings(), blockTemperature);
        }

/// <summary>AirOf operation.</summary>
        private static RoomAirNode AirOf(ThermalSimulation simulation)
        {
            IList<RoomAirNode> air = simulation.Solver.RoomAir;
            return air.Count == 0 ? null : air[0];
        }

        [Fact]
/// <summary>AirGivenToARoomIsCoupledToTheWalls operation.</summary>
        public void AirGivenToARoomIsCoupledToTheWalls()
        {
/// <summary>Shell operation.</summary>
            ThermalSimulation simulation = Shell(300f);

/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            Assert.False(air.HasAir);
            Assert.Empty(air.Links);

            simulation.SetRoomPressure(Interior, 1f);

            Assert.True(air.HasAir);
            Assert.True(air.AirMass > 0f);

            Assert.NotEmpty(air.Links);
        }

        [Fact]
/// <summary>AirAppearingForTheFirstTimeTakesTheTemperatureOfTheWalls operation.</summary>
        public void AirAppearingForTheFirstTimeTakesTheTemperatureOfTheWalls()
        {
/// <summary>Shell operation.</summary>
            ThermalSimulation simulation = Shell(300f);
            simulation.Update(1f, Worlds.Shadow());

            simulation.SetRoomPressure(Interior, 1f);

/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            Assert.Equal(300f, air.Temperature, 0);
            Assert.True(air.Temperature > 100f,
                "air seeded from ambient rather than from the walls: " + air.Temperature + " K");
        }

        [Fact]
/// <summary>APressurisedRoomActuallyMovesHeat operation.</summary>
        public void APressurisedRoomActuallyMovesHeat()
        {
/// <summary>Shell operation.</summary>
            ThermalSimulation simulation = Shell(300f);
            simulation.SetRoomPressure(Interior, 1f);

/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);
            Assert.NotNull(air);

            air.Temperature = 200f;
            float before = air.Temperature;

            for (int i = 0; i < 200; i++) simulation.Update(0.25f, Worlds.Shadow());

            Assert.True(air.Temperature > before + 1f,
                "cold air beside warm walls did not warm: " + air.Temperature + " K");
        }

        [Fact]
/// <summary>ConductanceToARoomsAirDoesNotDependOnHowFullTheRoomIs operation.</summary>
        public void ConductanceToARoomsAirDoesNotDependOnHowFullTheRoomIs()
        {
/// <summary>Shell operation.</summary>
            ThermalSimulation simulation = Shell(300f);

            simulation.SetRoomPressure(Interior, 1f);
/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);

            float full = 0f;
            foreach (RoomLink link in air.Links) full += link.Conductance;
            float fullMass = air.ThermalMass;

            simulation.SetRoomPressure(Interior, 0.02f);

            float sliver = 0f;
            foreach (RoomLink link in air.Links) sliver += link.Conductance;

            Assert.True(full > 0f, "a full room has no coupling at all, so this judges nothing");
            Assert.Equal(full, sliver, 4);

            Assert.True(air.ThermalMass < fullMass * 0.1f,
                "a room at 2 % pressure holds " + air.ThermalMass + " J/K against a full room's "
                + fullMass + " J/K, so pressure is not reaching the capacity either");

            simulation.SetRoomPressure(Interior, 0f);
            Assert.Empty(AirOf(simulation).Links);
        }

        [Fact]
/// <summary>RefusingTheAirsDemandApproximatesRatherThanDiverging operation.</summary>
        public void RefusingTheAirsDemandApproximatesRatherThanDiverging()
        {
/// <summary>AirSpreadAtCeiling operation.</summary>
            float refused = AirSpreadAtCeiling(1, true);
/// <summary>AirSpreadAtCeiling operation.</summary>
            float unclamped = AirSpreadAtCeiling(1, false);

            Assert.True(refused <= 300f,
                "the hull started 300 K apart with nothing making heat and reached " + refused
                + " K apart, so a substep left the range the temperatures pulling on it span");

            Assert.True(unclamped > 300f,
                "the unclamped run stayed inside " + unclamped + " K, so this rig no longer"
                + " over-subscribes the air and the clamped figure above is proving nothing");
        }

/// <summary>AirSpreadAtCeiling operation.</summary>
        private static float AirSpreadAtCeiling(int ceiling, bool clamp)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 1;                  // a one second step, so one substep is one h
            settings.EnableEnvironment = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = ceiling;
            settings.MaxSubstepsPerBlock = 0;
            settings.MaxElementVisitsPerStep = 0;
            settings.ClampConductionOvershoot = clamp;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 600f);
            simulation.SetRoomPressure(Interior, 0.02f);

/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);
            air.Temperature = 300f;

            float widest = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;

            for (int step = 0; step < 300; step++)
            {
                simulation.StepExact(1, Worlds.Shadow());

                float hottest = air.Temperature;
                float coldest = air.Temperature;

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Temperature > hottest) hottest = nodes[i].Temperature;
                    if (nodes[i].Temperature < coldest) coldest = nodes[i].Temperature;
                }

                if (hottest - coldest > widest) widest = hottest - coldest;
            }

            return widest;
        }

        [Fact]
/// <summary>DroppingPressureTakesTheLinksAwayAgain operation.</summary>
        public void DroppingPressureTakesTheLinksAwayAgain()
        {
/// <summary>Shell operation.</summary>
            ThermalSimulation simulation = Shell(300f);
            simulation.SetRoomPressure(Interior, 1f);

            Assert.NotEmpty(AirOf(simulation).Links);

            simulation.SetRoomPressure(Interior, 0f);

/// <summary>AirOf operation.</summary>
            RoomAirNode air = AirOf(simulation);
            Assert.False(air.HasAir);
            Assert.Empty(air.Links);
        }
    }
}
