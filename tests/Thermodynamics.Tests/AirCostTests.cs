using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class AirCostTests
    {
        private const float CandidateClock = 100f;

        private const float CandidateConductivity = 4f;

        private const float ShippedClock = 225f;

        private const float ThickAir = 1f;

        private const float BaseConductivity = 120f;

/// <summary>Sets the tings.</summary>
        private static ThermalSettings Settings(float clock, int frequency = 0)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.HeatTimeScale = clock;
            if (frequency > 0) settings.Frequency = frequency;

            settings.MaxSubsteps = Hulls.Unbounded;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

/// <summary>Block operation.</summary>
        private static BlockModel Block(float conductivity, float mass, bool radiates = true)
        {
            BlockThermalProperties thermal = Catalog.DefaultThermal();
            thermal.Conductivity = BaseConductivity * conductivity;

            if (!radiates) thermal.Emissivity = 0f;

            return BlockModel.Solid("rig", Vector3I.One, mass, thermal);
        }

/// <summary>Demand operation.</summary>
        private static float Demand(ThermalSimulation simulation, EnvironmentSample world)
        {
            for (int i = 0; i < 40; i++) simulation.StepExact(1, world);
            return simulation.Solver.LastRequiredSubsteps;
        }

/// <summary>Conduction operation.</summary>
        private static ThermalSimulation Conduction(float conductivity, float clock)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Block(conductivity, 4000f, false), Vector3I.Zero);
            builder.Place(Block(conductivity, 40f, false), new Vector3I(1, 0, 0));

            ThermalSimulation simulation = builder.BuildSimulation(Settings(clock), 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

/// <summary>Convection operation.</summary>
        private static ThermalSimulation Convection(float conductivity, float clock, int frequency = 0)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Block(conductivity, 40f), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Settings(clock, frequency), 293.15f);
            simulation.RebuildAll();
            return simulation;
        }

        [Fact]
/// <summary>AConductionLimitedBlockCostsConductivityTimesTheClock operation.</summary>
        public void AConductionLimitedBlockCostsConductivityTimesTheClock()
        {
/// <summary>Demand operation.</summary>
            float shipped = Demand(Conduction(1f, ShippedClock), Worlds.Shadow());
/// <summary>Demand operation.</summary>
            float candidate = Demand(Conduction(CandidateConductivity, CandidateClock),
                Worlds.Shadow());

            Assert.True(shipped > 0f);
            Assert.Equal(CandidateConductivity * CandidateClock / ShippedClock,
                candidate / shipped, 2);
        }

        [Fact]
/// <summary>AConvectionLimitedBlockCostsTheClockAloneAndTheRetuneMakesItCheaper operation.</summary>
        public void AConvectionLimitedBlockCostsTheClockAloneAndTheRetuneMakesItCheaper()
        {
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

/// <summary>Demand operation.</summary>
            float shipped = Demand(Convection(1f, ShippedClock), air);
/// <summary>Demand operation.</summary>
            float candidate = Demand(Convection(CandidateConductivity, CandidateClock), air);

            Assert.True(shipped > 0f);
            Assert.True(candidate < shipped,
                "a block whose stiffness is the air around it went " + shipped + " to " + candidate
                + " substeps; the G6 argument for shipping the retune rests on that falling");

            Assert.Equal(CandidateClock / ShippedClock, candidate / shipped, 2);
        }

        [Fact]
/// <summary>ConductivityDoesNotReachTheConvectiveTermAtAll operation.</summary>
        public void ConductivityDoesNotReachTheConvectiveTermAtAll()
        {
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

/// <summary>Demand operation.</summary>
            float plain = Demand(Convection(1f, ShippedClock), air);
/// <summary>Demand operation.</summary>
            float conductive = Demand(Convection(CandidateConductivity, ShippedClock), air);

            Assert.Equal(plain, conductive, 3);
        }

        [Fact]
/// <summary>AirIsWhereTheSubstepBudgetGoes operation.</summary>
        public void AirIsWhereTheSubstepBudgetGoes()
        {
/// <summary>Demand operation.</summary>
            float vacuum = Demand(Convection(1f, ShippedClock), Worlds.Shadow());
/// <summary>Demand operation.</summary>
            float air = Demand(Convection(1f, ShippedClock), Worlds.Flight(ThickAir, 200f));

            Assert.True(air > vacuum * 4f,
                "the same block demanded " + air + " substeps in air against " + vacuum
                + " in vacuum; if convection has stopped being the term that sets the cost then"
                + " every atmospheric figure in balance.md needs re-reading");
        }

        [Fact]
/// <summary>TheShippedClockSpendsMostOfTheCapOnAirAndTheRetunePullsItBack operation.</summary>
        public void TheShippedClockSpendsMostOfTheCapOnAirAndTheRetunePullsItBack()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings shipped = new ThermalSettings().Derive();
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

/// <summary>Demand operation.</summary>
            float before = Demand(Convection(1f, ShippedClock), air);
/// <summary>Demand operation.</summary>
            float after = Demand(Convection(CandidateConductivity, CandidateClock), air);

            Assert.True(before > shipped.MaxSubsteps / 4f,
                "a light exposed block at 200 m/s in thick air demanded " + before
                + " substeps against a cap of " + shipped.MaxSubsteps + ", which is a small enough"
                + " share that air is no longer where the budget goes");

            Assert.True(after < before / 2f,
                "the retune took the air demand from " + before + " to " + after
                + ", and the G6 argument for shipping it is that this roughly halves");
        }

        [Fact]
/// <summary>DemandIsExactlyProportionalToTheStep operation.</summary>
        public void DemandIsExactlyProportionalToTheStep()
        {
            EnvironmentSample air = Worlds.Flight(ThickAir, 200f);

/// <summary>Demand operation.</summary>
            float quarterSecond = Demand(Convection(1f, ShippedClock, 4), air);
/// <summary>Demand operation.</summary>
            float eighthSecond = Demand(Convection(1f, ShippedClock, 8), air);

            Assert.True(eighthSecond > 0f);
            Assert.Equal(2.0, quarterSecond / eighthSecond, 2);
        }

        [Fact]
/// <summary>TheShippedPairIsNotOverTheCapInVacuum operation.</summary>
        public void TheShippedPairIsNotOverTheCapInVacuum()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings shipped = new ThermalSettings().Derive();

/// <summary>Demand operation.</summary>
            float vacuum = Demand(Convection(1f, ShippedClock), Worlds.Shadow());

            Assert.True(vacuum < shipped.MaxSubsteps / 4f,
                "the rig block demanded " + vacuum + " substeps in vacuum, which is close enough to"
                + " the cap that the atmospheric reading is not isolating the air");
        }
    }
}
