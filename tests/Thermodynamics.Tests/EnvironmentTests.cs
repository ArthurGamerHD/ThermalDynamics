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
/// <summary>NoPlanetMeansVacuum operation.</summary>
        public void NoPlanetMeansVacuum()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            EnvironmentState state = EnvironmentSolver.Solve(settings, null, Worlds.Shadow());

            Assert.Equal(settings.VacuumTemperature, state.AmbientTemperature, 3);
            Assert.Equal(0f, state.AirDensity, 5);
            Assert.Equal(0f, state.AtmosphereFactor, 5);
            Assert.Equal(0f, state.ConvectionCoefficient, 5);
        }

        [Fact]
/// <summary>AmbientIsNeverColderThanVacuum operation.</summary>
        public void AmbientIsNeverColderThanVacuum()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            EnvironmentSample sample = Worlds.PlanetSurface(0.0001f, 0f);
            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);

            Assert.True(state.AmbientTemperature >= settings.VacuumTemperature);
        }

        [Fact]
/// <summary>NoonIsWarmerThanMidnight operation.</summary>
        public void NoonIsWarmerThanMidnight()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float noon = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).AmbientTemperature;
            float midnight = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0f)).AmbientTemperature;

            Assert.True(noon > midnight);
            Assert.Equal(planet.DayTemperature, noon, 1);
            Assert.Equal(planet.NightTemperature, midnight, 1);
        }

        [Fact]
/// <summary>UndergroundUsesItsOwnTemperatureAndBlocksTheSun operation.</summary>
        public void UndergroundUsesItsOwnTemperatureAndBlocksTheSun()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, Worlds.Underground());

            Assert.Equal(planet.UndergroundTemperature, state.AmbientTemperature, 1);
            Assert.True(state.IsSolarOccluded);
            Assert.Equal(0f, state.SolarEnergy, 5);
        }

        [Fact]
/// <summary>AtmosphereFactorRisesFasterThanRawDensity operation.</summary>
        public void AtmosphereFactorRisesFasterThanRawDensity()
        {
            Assert.Equal(0f, EnvironmentSolver.AtmosphereFactor(0f), 5);
            Assert.Equal(1f, EnvironmentSolver.AtmosphereFactor(1f), 5);

            float quarter = EnvironmentSolver.AtmosphereFactor(0.25f);
            Assert.True(quarter > 0.25f);
            Assert.True(quarter < 1f);

            float previous = -1f;
            for (float d = 0f; d <= 1f; d += 0.05f)
            {
                float value = EnvironmentSolver.AtmosphereFactor(d);
                Assert.True(value >= previous);
                previous = value;
            }
        }

        [Fact]
/// <summary>SolarEnergyDecaysThroughAtmosphere operation.</summary>
        public void SolarEnergyDecaysThroughAtmosphere()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float vacuum = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(0f, 0.5f)).SolarEnergy;
            float thick = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).SolarEnergy;

            Assert.Equal(settings.SolarEnergy, vacuum, 1);
            Assert.Equal(settings.SolarEnergy * (1f - planet.SolarDecay), thick, 1);
        }

        [Fact]
/// <summary>WindRaisesTheConvectionCoefficient operation.</summary>
        public void WindRaisesTheConvectionCoefficient()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float still = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f)).ConvectionCoefficient;
            float windy = EnvironmentSolver.Solve(settings, planet, Worlds.PlanetSurface(1f, 0.5f, 100f)).ConvectionCoefficient;

            Assert.Equal(planet.ConvectionCoefficient, still, 2);
            Assert.True(windy > still);
        }

        [Fact]
/// <summary>FrictionNeedsBothAirAndSpeed operation.</summary>
        public void FrictionNeedsBothAirAndSpeed()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            Assert.False(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.8f, 0f)).FrictionActive);
            Assert.False(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.001f, 300f)).FrictionActive);
            Assert.True(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.8f, 10f)).FrictionActive);
            Assert.True(EnvironmentSolver.Solve(settings, planet, Worlds.Flight(0.8f, 300f)).FrictionActive);
        }

        [Fact]
/// <summary>DisablingPlanetsFallsBackToVacuumEvenOnASurface operation.</summary>
        public void DisablingPlanetsFallsBackToVacuumEvenOnASurface()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnablePlanets = false;

            EnvironmentState state = EnvironmentSolver.Solve(
                settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));

            Assert.Equal(settings.VacuumTemperature, state.AmbientTemperature, 3);
        }

        [Fact]
/// <summary>AmbientPow4TracksAmbient operation.</summary>
        public void AmbientPow4TracksAmbient()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            EnvironmentState state = EnvironmentSolver.Solve(settings, PlanetThermalProperties.Default(), Worlds.PlanetSurface(1f, 0.5f));

            float t = state.AmbientTemperature;
            float squared = t * t;
            Assert.Equal(1f, state.AmbientTemperaturePow4 / (squared * squared), 5);
        }
    }

    public class RadiationTests
    {
/// <summary>SingleBlock operation.</summary>
        private static ThermalSimulation SingleBlock(ThermalSettings settings, float temperature)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, temperature);
            simulation.Solver.CollectDiagnostics = true;
            return simulation;
        }

        [Fact]
/// <summary>ALoneBlockRadiatesTowardAmbient operation.</summary>
        public void ALoneBlockRadiatesTowardAmbient()
        {
/// <summary>SingleBlock operation.</summary>
            ThermalSimulation simulation = SingleBlock(Fixture.EnvironmentOnly(), 800f);
            ThermalNode node = simulation.Solver.Nodes[0];

            simulation.StepExact(400, Worlds.Shadow());

            Assert.True(node.Temperature < 300f, "block should have shed most of its heat, at " + node.Temperature);
            Assert.True(node.Temperature > 0f);
        }

        [Fact]
/// <summary>RadiatedPowerMatchesStefanBoltzmann operation.</summary>
        public void RadiatedPowerMatchesStefanBoltzmann()
        {
            ThermalSettings settings = Fixture.EnvironmentOnly(1000);
/// <summary>SingleBlock operation.</summary>
            ThermalSimulation simulation = SingleBlock(settings, 500f);
            ThermalNode node = simulation.Solver.Nodes[0];

            simulation.StepExact(1, Worlds.Shadow());

            float area = 6f * 6.25f;
            float emissivity = node.Thermal.Emissivity;
            float expected = -emissivity * ThermalConstants.StefanBoltzmann * area
                * ((500f * 500f * 500f * 500f) - (2.7f * 2.7f * 2.7f * 2.7f));

            Assert.Equal(1f, node.LastRadiationWatts / expected, 3);
        }

        [Fact]
/// <summary>ASealedBlockWithNoExposedFacesDoesNotRadiate operation.</summary>
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
/// <summary>HigherEmissivityCoolsFaster operation.</summary>
        public void HigherEmissivityCoolsFaster()
        {
            BlockThermalProperties dull = Catalog.DefaultThermal();
            dull.Emissivity = 0.05f;
            BlockThermalProperties shiny = Catalog.DefaultThermal();
            shiny.Emissivity = 0.9f;

/// <summary>CoolFor operation.</summary>
            float dullResult = CoolFor(dull);
/// <summary>CoolFor operation.</summary>
            float shinyResult = CoolFor(shiny);

            Assert.True(shinyResult < dullResult);
        }

/// <summary>CoolFor operation.</summary>
        private static float CoolFor(BlockThermalProperties thermal)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Place(BlockModel.Solid("Test", Vector3I.One, 500f, thermal), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(Fixture.EnvironmentOnly(), 600f);
            simulation.StepExact(20, Worlds.Shadow());
            return simulation.Solver.Nodes[0].Temperature;
        }

        [Fact]
/// <summary>ABlockColderThanAmbientWarmsUp operation.</summary>
        public void ABlockColderThanAmbientWarmsUp()
        {
            ThermalSettings settings = Fixture.EnvironmentOnly();
            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 100f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(100, Worlds.PlanetSurface(1f, 0.5f));

            Assert.True(simulation.Solver.Nodes[0].Temperature > 100f);
        }
    }

    public class ConvectionSolarFrictionTests
    {
        [Fact]
/// <summary>AirCoolsFasterThanVacuum operation.</summary>
        public void AirCoolsFasterThanVacuum()
        {
/// <summary>CoolIn operation.</summary>
            float inVacuum = CoolIn(Worlds.Shadow());
/// <summary>CoolIn operation.</summary>
            float inAir = CoolIn(Worlds.PlanetSurface(1f, 0f));

            Assert.True(inAir < inVacuum,
                "convection should beat radiation at sea level: air " + inAir + " vs vacuum " + inVacuum);
        }

/// <summary>CoolIn operation.</summary>
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
/// <summary>SolarGainMatchesTheProjectedArea operation.</summary>
        public void SolarGainMatchesTheProjectedArea()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            simulation.Solver.CollectDiagnostics = true;

            simulation.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));

            ThermalNode node = simulation.Solver.Nodes[0];
            float expected = 1000f * node.Thermal.EffectiveSolarAbsorptivity * 6.25f;
            Assert.Equal(expected, node.LastSolarWatts, 1);
        }

        [Fact]
/// <summary>OcclusionRemovesSolarGainEntirely operation.</summary>
        public void OcclusionRemovesSolarGainEntirely()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LightArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            simulation.Solver.CollectDiagnostics = true;

            simulation.StepExact(1, Worlds.Shadow());

            Assert.Equal(0f, simulation.Solver.Nodes[0].LastSolarWatts, 5);
            Assert.Equal(200f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
/// <summary>SolarGainFollowsTheSunAroundTheBlock operation.</summary>
        public void SolarGainFollowsTheSunAroundTheBlock()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.LightArmor(), Vector3I.Zero, new Vector3I(3, 1, 3));
            ThermalSimulation simulation = builder.BuildSimulation(settings, 200f);
            simulation.Solver.CollectDiagnostics = true;

            ThermalNode centre = simulation.Solver.GetNodeAt(new Vector3I(1, 0, 1));

            simulation.StepExact(1, Worlds.Space(new Vector3(0f, 1f, 0f)));
            float fromAbove = centre.LastSolarWatts;

            simulation.StepExact(1, Worlds.Space(new Vector3(1f, 0f, 0f)));
            float fromTheSide = centre.LastSolarWatts;

            Assert.True(fromAbove > fromTheSide,
                "overhead sun should beat a grazing one: " + fromAbove + " vs " + fromTheSide);
        }

        [Fact]
/// <summary>FrictionActuallyHeatsTheBlock operation.</summary>
        public void FrictionActuallyHeatsTheBlock()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            float before = simulation.Solver.Nodes[0].Temperature;
            simulation.StepExact(4, Worlds.Flight(0.8f, 300f));

            Assert.True(simulation.Solver.Nodes[0].LastFrictionWatts > 0f);
            Assert.True(simulation.Solver.Nodes[0].Temperature > before,
                "friction must raise the temperature");
        }

        [Fact]
/// <summary>BelowAConfiguredFloorFrictionDoesNothing operation.</summary>
        public void BelowAConfiguredFloorFrictionDoesNothing()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.FrictionAtSpeedsAbove = 50f;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(4, Worlds.Flight(0.8f, 10f));

            Assert.Equal(0f, simulation.Solver.Nodes[0].LastFrictionWatts, 5);
            Assert.Equal(293.15f, simulation.Solver.Nodes[0].Temperature, 3);
        }

        [Fact]
/// <summary>FrictionScalesWithTheCubeOfSpeed operation.</summary>
        public void FrictionScalesWithTheCubeOfSpeed()
        {
/// <summary>FrictionWattsAt operation.</summary>
            float slow = FrictionWattsAt(100f);
/// <summary>FrictionWattsAt operation.</summary>
            float fast = FrictionWattsAt(200f);

            Assert.Equal(8f, fast / slow, 2);
        }

/// <summary>FrictionWattsAt operation.</summary>
        private static float FrictionWattsAt(float speed)
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();

            simulation.StepExact(1, Worlds.Flight(0.8f, speed));
            return simulation.Solver.Nodes[0].LastFrictionWatts;
        }


        [Fact]
/// <summary>TheEffectiveCoefficientCarriesTheAtmosphereBlend operation.</summary>
        public void TheEffectiveCoefficientCarriesTheAtmosphereBlend()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            foreach (float density in new float[] { 1f, 0.5f, 0.25f, 0.05f, 0.001f })
            {
                EnvironmentState state = EnvironmentSolver.Solve(
                    settings, planet, Worlds.PlanetSurface(density, 0.5f));

                Assert.Equal(
                    state.ConvectionCoefficient * state.AtmosphereFactor,
                    state.EffectiveConvectionCoefficient,
                    4);

                Assert.Equal(planet.ConvectionCoefficient, state.ConvectionCoefficient, 2);
            }
        }

        [Fact]
/// <summary>TheEffectiveCoefficientFallsWithTheAir operation.</summary>
        public void TheEffectiveCoefficientFallsWithTheAir()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float sealevel = EnvironmentSolver.Solve(
                settings, planet, Worlds.PlanetSurface(1f, 0.5f)).EffectiveConvectionCoefficient;
            float thin = EnvironmentSolver.Solve(
                settings, planet, Worlds.PlanetSurface(0.1f, 0.5f)).EffectiveConvectionCoefficient;
            float nearVacuum = EnvironmentSolver.Solve(
                settings, planet, Worlds.PlanetSurface(0.0001f, 0.5f)).EffectiveConvectionCoefficient;

            Assert.Equal(planet.ConvectionCoefficient, sealevel, 2);
            Assert.True(sealevel > thin);
            Assert.True(thin > nearVacuum);
            Assert.True(nearVacuum < 0.05f);
        }

        [Fact]
/// <summary>ConvectiveWattsFallWithTheAir operation.</summary>
        public void ConvectiveWattsFallWithTheAir()
        {
/// <summary>ConvectionWatts operation.</summary>
            float thick = ConvectionWatts(1f);
/// <summary>ConvectionWatts operation.</summary>
            float thin = ConvectionWatts(0.05f);
/// <summary>ConvectionWatts operation.</summary>
            float nearVacuum = ConvectionWatts(0.0001f);

            Assert.True(thick < 0f);
            Assert.True(thick < thin);
            Assert.True(thin < nearVacuum);
            Assert.True(nearVacuum > -1000f);
        }

        [Fact]
/// <summary>ASettledTemperatureIsNotMonotonicInAirDensity operation.</summary>
        public void ASettledTemperatureIsNotMonotonicInAirDensity()
        {
/// <summary>Sets the tledtemperature.</summary>
            float sealevel = SettledTemperature(1f);
/// <summary>Sets the tledtemperature.</summary>
            float thin = SettledTemperature(0.05f);
/// <summary>Sets the tledtemperature.</summary>
            float vacuum = SettledTemperature(0f);

            Assert.True(thin < sealevel);
            Assert.True(vacuum > sealevel);
        }

        [Fact]
/// <summary>WindStillRaisesTheCoefficientAtEveryDensity operation.</summary>
        public void WindStillRaisesTheCoefficientAtEveryDensity()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            foreach (float density in new float[] { 1f, 0.25f })
            {
                float still = EnvironmentSolver.Solve(settings, planet,
                    Worlds.PlanetSurface(density, 0.5f)).EffectiveConvectionCoefficient;
                float windy = EnvironmentSolver.Solve(settings, planet,
                    Worlds.PlanetSurface(density, 0.5f, 30f)).EffectiveConvectionCoefficient;

                Assert.True(windy > still);
            }
        }

/// <summary>Rig operation.</summary>
        private static ThermalSimulation Rig(float density, out EnvironmentSample sample)
        {
            ThermalSettings settings = new ThermalSettings
            {
                MaxSubsteps = 64,
                MaxElementVisitsPerStep = 0,
                EnableSolarHeat = false,
            };
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.Reactor(), Vector3I.Zero).Wasting(50000f);

            ThermalSimulation simulation = builder.BuildSimulation(settings);
            simulation.Planet = PlanetThermalProperties.Default();

            sample = Worlds.PlanetSurface(density, 0.5f);
            return simulation;
        }

/// <summary>Sets the tledtemperature.</summary>
        private static float SettledTemperature(float density)
        {
            EnvironmentSample sample;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(density, out sample);

            for (int step = 0; step < LabClock.Steps(600); step++) simulation.StepExact(1, sample);
            return simulation.Solver.Nodes[0].Temperature;
        }

/// <summary>ConvectionWatts operation.</summary>
        private static float ConvectionWatts(float density)
        {
            EnvironmentSample sample;
/// <summary>Rig operation.</summary>
            ThermalSimulation simulation = Rig(density, out sample);
            simulation.Solver.CollectDiagnostics = true;

            for (int step = 0; step < LabClock.Steps(600); step++) simulation.StepExact(1, sample);
            return simulation.Solver.Nodes[0].LastConvectionWatts;
        }
    }
}
