using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    public class EnvironmentSolverTests
    {
        [Fact]
        public void NoPlanetMeansVacuum()
        {
            ThermalSettings settings = new ThermalSettings();
            EnvironmentState state = EnvironmentSolver.Solve(settings, null, Worlds.Shadow());

            Assert.Equal(settings.VacuumTemperature, state.AmbientTemperature, 3);
            Assert.Equal(0f, state.AirDensity, 5);
            Assert.Equal(0f, state.AtmosphereFactor, 5);
            Assert.Equal(0f, state.ConvectionCoefficient, 5);
        }

        [Fact]
        public void AmbientIsNeverColderThanVacuum()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            // vanishingly thin air at midnight
            EnvironmentSample sample = Worlds.PlanetSurface(0.0001f, 0f);
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);

            Assert.True(state.AmbientTemperature >= settings.VacuumTemperature);
        }

        [Fact]
        public void NoonIsWarmerThanMidnight()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float noon = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).AmbientTemperature;
            float midnight = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0f)).AmbientTemperature;

            Assert.True(noon > midnight);
            Assert.Equal(planet.DayTemperature, noon, 1);
            Assert.Equal(planet.NightTemperature, midnight, 1);
        }

        [Fact]
        public void UndergroundUsesItsOwnTemperatureAndBlocksTheSun()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, Worlds.Underground());

            Assert.Equal(planet.UndergroundTemperature, state.AmbientTemperature, 1);
            Assert.True(state.IsSolarOccluded);
            Assert.Equal(0f, state.SolarEnergy, 5);
        }

        [Fact]
        public void AtmosphereFactorRisesFasterThanRawDensity()
        {
            Assert.Equal(0f, EnvironmentSolver.AtmosphereFactor(0f), 5);
            Assert.Equal(1f, EnvironmentSolver.AtmosphereFactor(1f), 5);

            float quarter = EnvironmentSolver.AtmosphereFactor(0.25f);
            Assert.True(quarter > 0.25f);
            Assert.True(quarter < 1f);

            // monotonic
            float previous = -1f;
            for (float d = 0f; d <= 1f; d += 0.05f)
            {
                float value = EnvironmentSolver.AtmosphereFactor(d);
                Assert.True(value >= previous);
                previous = value;
            }
        }

        [Fact]
        public void SolarEnergyDecaysThroughAtmosphere()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float vacuum = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(0f, 0.5f)).SolarEnergy;
            float thick = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).SolarEnergy;

            Assert.Equal(settings.SolarEnergy, vacuum, 1);
            Assert.Equal(settings.SolarEnergy * (1f - planet.SolarDecay), thick, 1);
        }

        [Fact]
        public void WindRaisesTheConvectionCoefficient()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float still = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).ConvectionCoefficient;
            float windy = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f, 100f)).ConvectionCoefficient;

            Assert.Equal(planet.ConvectionCoefficient, still, 2);
            Assert.True(windy > still);
        }

        [Fact]
        public void FrictionNeedsBothAirAndSpeed()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            Assert.False(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.8f, 10f)).FrictionActive);
            Assert.False(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.001f, 300f)).FrictionActive);
            Assert.True(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.8f, 300f)).FrictionActive);
        }

        [Fact]
        public void DisablingPlanetsFallsBackToVacuumEvenOnASurface()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnablePlanets = false;

            EnvironmentState state = EnvironmentSolver.Solve(
                settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));

            Assert.Equal(settings.VacuumTemperature, state.AmbientTemperature, 3);
        }

        [Fact]
        public void AmbientPow4TracksAmbient()
        {
            ThermalSettings settings = new ThermalSettings();
            EnvironmentState state = EnvironmentSolver.Solve(settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));

            float t = state.AmbientTemperature;
            float squared = t * t;
            Assert.Equal(1f, state.AmbientTemperaturePow4 / (squared * squared), 5);
        }
    }

    public class RadiationTests
    {
        private static ThermalSimulation SingleBlock(ThermalSettings settings, float temperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            simulation.Solver.CollectDiagnostics = true;
            return simulation;
        }

        [Fact]
        public void ALoneBlockRadiatesTowardAmbient()
        {
            ThermalSimulation simulation = SingleBlock(Fixture.EnvironmentOnly(), 800f);
            ThermalNode node = simulation.Solver.Nodes[0];

            simulation.StepExact(400, Worlds.Shadow());

            // Radiative cooling is fast at first and then very slow, because the driving term
            // falls with the fourth power. 800 K to under 300 K in 100 s, but the last hundred
            // kelvin take longer than the first five hundred.
            Assert.True(node.Temperature < 300f, "block should have shed most of its heat, at " + node.Temperature);
            Assert.True(node.Temperature > 0f);
        }

        [Fact]
        public void RadiatedPowerMatchesStefanBoltzmann()
        {
            ThermalSettings settings = Fixture.EnvironmentOnly(1000);
            ThermalSimulation simulation = SingleBlock(settings, 500f);
            ThermalNode node = simulation.Solver.Nodes[0];

            simulation.StepExact(1, Worlds.Shadow());

            // 6 faces of 6.25 m^2, emissivity 0.125, ambient 2.7 K
            float area = 6f * 6.25f;
            float expected = -0.125f * ThermalConstants.StefanBoltzmann * area
                * ((500f * 500f * 500f * 500f) - (2.7f * 2.7f * 2.7f * 2.7f));

            Assert.Equal(1f, node.LastRadiationWatts / expected, 3);
        }

        [Fact]
        public void ASealedBlockWithNoExposedFacesDoesNotRadiate()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(Fixture.EnvironmentOnly(), 293.15f);
            ThermalNode interior = simulation.Solver.GetNode(builder.Last);

            interior.Temperature = 900f;
            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(0, interior.TotalExposedFaces);
            Assert.Equal(0f, interior.LastRadiationWatts, 5);
        }

        [Fact]
        public void HigherEmissivityCoolsFaster()
        {
            BlockThermalProperties dull = Catalog.DefaultThermal();
            dull.Emissivity = 0.05f;
            BlockThermalProperties shiny = Catalog.DefaultThermal();
            shiny.Emissivity = 0.9f;

            float dullResult = CoolFor(dull);
            float shinyResult = CoolFor(shiny);

            Assert.True(shinyResult < dullResult);
        }

        private static float CoolFor(BlockThermalProperties thermal)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Test", Vector3I.One, 500f, thermal), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(Fixture.EnvironmentOnly(), 600f);
            simulation.StepExact(20, Worlds.Shadow());
            return simulation.Solver.Nodes[0].Temperature;
        }

        [Fact]
        public void ABlockColderThanAmbientWarmsUp()
        {
            ThermalSettings settings = Fixture.EnvironmentOnly();
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 100f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(100, Worlds.PlanetSurface(1f, 0.5f));

            Assert.True(simulation.Solver.Nodes[0].Temperature > 100f);
        }
    }

    public class ConvectionSolarFrictionTests
    {
        [Fact]
        public void AirCoolsFasterThanVacuum()
        {
            float inVacuum = CoolIn(Worlds.Shadow());
            float inAir = CoolIn(Worlds.PlanetSurface(1f, 0f));

            Assert.True(inAir < inVacuum,
                "convection should beat radiation at sea level: air " + inAir + " vs vacuum " + inVacuum);
        }

        private static float CoolIn(EnvironmentSample sample)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(Fixture.EnvironmentOnly(), 600f);
            simulation.Planet = PlanetThermalProperties.Default();
            simulation.StepExact(8, sample);
            return simulation.Solver.Nodes[0].Temperature;
        }

        [Fact]
        public void SolarGainMatchesTheProjectedArea()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;

            simulation.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

            // one of six exposed faces points at the sun: 37.5 m^2 / 6 = 6.25 m^2 projected
            float expected = 1000f * 0.125f * 6.25f;
            Assert.Equal(expected, simulation.Solver.Nodes[0].LastSolarWatts, 1);
        }

        [Fact]
        public void OcclusionRemovesSolarGainEntirely()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(0f, simulation.Solver.Nodes[0].LastSolarWatts, 5);
            Assert.Equal(200f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void SolarGainFollowsTheSunAroundTheBlock()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            // a flat plate: much more area facing up than sideways
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;

            ThermalNode centre = simulation.Solver.GetNodeAt(new Vector3I(1, 0, 1));

            simulation.StepExact(1, Worlds.Space(new Vector3(0f, 1f, 0f)));
            float fromAbove = centre.LastSolarWatts;

            simulation.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));
            float fromTheSide = centre.LastSolarWatts;

            Assert.True(fromAbove > fromTheSide,
                "overhead sun should beat a grazing one: " + fromAbove + " vs " + fromTheSide);
        }

        /// <summary>
        /// In the original solver the friction term was computed, written into a variable, and
        /// then overwritten by the conduction result before it was ever applied.
        /// </summary>
        [Fact]
        public void FrictionActuallyHeatsTheBlock()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            float before = simulation.Solver.Nodes[0].Temperature;
            simulation.StepExact(4, Worlds.Flight(0.8f, 300f));

            Assert.True(simulation.Solver.Nodes[0].LastFrictionWatts > 0f);
            Assert.True(simulation.Solver.Nodes[0].Temperature > before,
                "friction must raise the temperature");
        }

        [Fact]
        public void BelowTheThresholdFrictionDoesNothing()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(4, Worlds.Flight(0.8f, 10f));

            Assert.Equal(0f, simulation.Solver.Nodes[0].LastFrictionWatts, 5);
            Assert.Equal(293.15f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
        public void FrictionScalesWithTheCubeOfSpeed()
        {
            float slow = FrictionWattsAt(100f);
            float fast = FrictionWattsAt(200f);

            Assert.Equal(8f, fast / slow, 2);
        }

        private static float FrictionWattsAt(float speed)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            // The per-mechanism watt figures are diagnostics, and diagnostics are opt-in.
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(1, Worlds.Flight(0.8f, speed));
            return simulation.Solver.Nodes[0].LastFrictionWatts;
        }
    }
}
