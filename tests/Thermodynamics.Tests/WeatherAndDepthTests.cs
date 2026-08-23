using System;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The three things the climate learned to react to after a test world with a four minute day
    /// was measured: the weather standing over a grid, how far above sea level it is, and how far
    /// under the ground.
    ///
    /// The run that produced these is worth stating, because one of the tests below exists only
    /// because of it. Three grids on an earthlike, telemetry on, 851 seconds. The snowfield sat at
    /// 5.6 km with an air density of 0.61 and reported an ambient of <b>36 K</b> for the whole run
    /// — 220 K below what the model was asked for — and every block on it froze to match. Nothing
    /// in the climate was wrong. The scale for thin air was being applied to the running ambient
    /// instead of to the target the ambient was chasing, so it compounded against the lag on every
    /// step and settled at a seventh of the intended figure.
    /// </summary>
    public class WeatherAndDepthTests
    {
        private static PlanetThermalProperties Earthlike()
        {
            PlanetThermalProperties planet = PlanetThermalProperties.Default();
            planet.NightTemperature = 283.15f;
            planet.DayTemperature = 294.15f;
            planet.PoleTemperatureDrop = 40f;
            planet.AmbientLapseRate = 4f;
            planet.UndergroundTemperature = 280f;
            planet.UndergroundDampingDepth = 20f;
            planet.CoreTemperature = 3000f;
            planet.SealevelDeadzone = 2000f;
            return planet;
        }

        // ---- the bug the telemetry found ---------------------------------------------------

        /// <summary>
        /// The exact arrangement that measured 36 K in game: an ambient scale below 1, a lag long
        /// against the step, and enough steps to settle. Run for fifteen minutes of play — twenty
        /// times the lag — and the answer has to be the target, not a fraction of it.
        /// </summary>
        [Fact]
        public void ThinAirDoesNotCompoundAgainstTheLag()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentSample sample = Worlds.PlanetSurface(0.612f, 0.5f);
            sample.SecondsSincePrevious = 1f / 6f;      // the mod's own step
            planet.AmbientLagSeconds = 45f;

            // Started from the vacuum the grid loaded holding, with a history, so the lag has to
            // actually run the whole way up. Seeding it at the target would pass either way.
            sample.PreviousAmbient = settings.VacuumTemperature;
            sample.HasPreviousAmbient = true;

            float ambient = 0f;
            for (int i = 0; i < 6 * 900; i++)
            {
                EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);
                ambient = state.AmbientTemperature;

                sample.PreviousAmbient = ambient;
                sample.HasPreviousAmbient = true;
            }

            // What the place is actually worth: noon at the equator, thinned for its air.
            float target = ClimateModel.Thin(planet.DayTemperature, 0.612f, settings.VacuumTemperature);

            Assert.Equal(target, ambient, 1);

            // And the failure it replaces, stated as the number the report carried, so this test
            // fails loudly rather than subtly if the ordering is ever put back.
            Assert.True(ambient > 250f, "ambient collapsed toward vacuum: " + ambient + " K");
        }

        /// <summary>
        /// A grid that has just arrived has no ambient to chase from. Seeding the lag with the
        /// vacuum every state starts out holding froze whole ships for the first three minutes of
        /// a session — the desert grid's mean block temperature fell from 257 K to 103 K.
        /// </summary>
        [Fact]
        public void AGridWithNoHistoryStartsAtItsClimateRatherThanAtVacuum()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);
            sample.PreviousAmbient = settings.VacuumTemperature;
            sample.SecondsSincePrevious = 1f / 6f;
            sample.HasPreviousAmbient = false;

            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);

            Assert.Equal(planet.DayTemperature, state.AmbientTemperature, 1);

            // With a history it lags, which is the whole point of the flag being a flag.
            sample.HasPreviousAmbient = true;
            EnvironmentState lagged = EnvironmentSolver.Solve(settings, planet, sample);
            Assert.True(lagged.AmbientTemperature < 10f);
        }

        // ---- altitude ----------------------------------------------------------------------

        [Fact]
        public void AirCoolsWithHeightAboveSeaLevel()
        {
            PlanetThermalProperties planet = Earthlike();

            Assert.Equal(280f, ClimateModel.Lapse(280f, 0f, 4f), 3);
            Assert.Equal(276f, ClimateModel.Lapse(280f, 1000f, 4f), 3);
            Assert.Equal(258f, ClimateModel.Lapse(280f, 5500f, 4f), 3);

            // Below sea level is warmer, and a planet with no lapse rate has no opinion at all.
            Assert.Equal(284f, ClimateModel.Lapse(280f, -1000f, 4f), 3);
            Assert.Equal(280f, ClimateModel.Lapse(280f, 5500f, 0f), 3);
        }

        [Fact]
        public void AmbientHoldsUpThroughTheAtmosphereAndDiesAtTheEdgeOfIt()
        {
            // Blunter than the curve convection and solar run on: at two thirds density the air
            // still has essentially all of its temperature, which is the fact the old single
            // multiply got wrong.
            Assert.True(ClimateModel.AmbientDensityFactor(0.61f) > 0.999f);
            Assert.True(ClimateModel.AmbientDensityFactor(0.61f) > EnvironmentSolver.AtmosphereFactor(0.61f));

            Assert.Equal(0f, ClimateModel.AmbientDensityFactor(0f), 5);
            Assert.Equal(1f, ClimateModel.AmbientDensityFactor(1f), 5);

            // Monotonic, and it really does reach vacuum rather than stopping short.
            float previous = -1f;
            for (float d = 0f; d <= 1.0001f; d += 0.05f)
            {
                float factor = ClimateModel.AmbientDensityFactor(d);
                Assert.True(factor >= previous);
                previous = factor;
            }

            Assert.Equal(2.7f, ClimateModel.Thin(300f, 0f, 2.7f), 3);
            Assert.Equal(300f, ClimateModel.Thin(300f, 1f, 2.7f), 3);
        }

        // ---- underground -------------------------------------------------------------------

        [Fact]
        public void DepthBluntsTheDayAndThenRemovesIt()
        {
            PlanetThermalProperties planet = Earthlike();
            float radius = Worlds.EarthlikeRadius;

            float noon = 300f;
            float underground = planet.UndergroundTemperature;

            // At the surface the day is untouched; halfway down the damping it is halfway gone;
            // below it there is no day left at all.
            Assert.Equal(noon, ClimateModel.Underground(planet, noon, 0f, radius, radius), 2);

            float shallow = ClimateModel.Underground(planet, noon, 10f, radius - 10f, radius);
            Assert.Equal((noon + underground) * 0.5f, shallow, 1);

            Assert.Equal(underground, ClimateModel.Underground(planet, noon, 20f, radius - 20f, radius), 2);
            Assert.Equal(underground, ClimateModel.Underground(planet, noon, 500f, radius - 500f, radius), 2);

            // A cold night and a hot noon converge on the same rock, which is the point of it.
            float night = ClimateModel.Underground(planet, 250f, 500f, radius - 500f, radius);
            Assert.Equal(underground, night, 2);
        }

        [Fact]
        public void BelowTheDeadzoneTheRockWarmsTowardTheCore()
        {
            PlanetThermalProperties planet = Earthlike();
            float radius = Worlds.EarthlikeRadius;

            // Inside the deadzone nothing has started yet.
            float atDeadzone = ClimateModel.Underground(planet, 290f, 2000f, radius - 2000f, radius);
            Assert.Equal(planet.UndergroundTemperature, atDeadzone, 1);

            float deeper = ClimateModel.Underground(planet, 290f, 5000f, radius - 5000f, radius);
            Assert.True(deeper > planet.UndergroundTemperature);
            Assert.True(deeper < planet.CoreTemperature);

            // Monotonic all the way down, and the centre is the core.
            float previous = planet.UndergroundTemperature - 1f;
            for (float depth = 2000f; depth <= radius; depth += 1000f)
            {
                float here = ClimateModel.Underground(planet, 290f, depth, radius - depth, radius);
                Assert.True(here >= previous, "not monotonic at " + depth + " m");
                previous = here;
            }

            Assert.Equal(planet.CoreTemperature, ClimateModel.Underground(planet, 290f, radius, 0f, radius), 0);
        }

        [Fact]
        public void ATunnelIntoAMountainStaysCold()
        {
            PlanetThermalProperties planet = Earthlike();
            float radius = Worlds.EarthlikeRadius;

            // A kilometre into a peak that stands 5 km above sea level: deep in the rock, and
            // still four kilometres above the level where the deadzone even begins.
            float inMountain = ClimateModel.Underground(planet, 260f, 1000f, radius + 4000f, radius);
            Assert.Equal(planet.UndergroundTemperature, inMountain, 2);

            // The same depth measured from a beach is the same, because the deadzone is 2 km deep.
            float fromBeach = ClimateModel.Underground(planet, 290f, 1000f, radius - 1000f, radius);
            Assert.Equal(planet.UndergroundTemperature, fromBeach, 2);
        }

        [Fact]
        public void TheSolverBuriesAGridAndTakesItsSunAway()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentState state = EnvironmentSolver.Solve(settings, planet, Worlds.Underground());

            Assert.Equal(planet.UndergroundTemperature, state.AmbientTemperature, 1);
            Assert.True(state.IsSolarOccluded);
            Assert.Equal(0f, state.SolarEnergy, 5);
        }

        // ---- weather -----------------------------------------------------------------------

        [Fact]
        public void EveryWeatherTheGameShipsIsRecognised()
        {
            // Every subtype in Keen's WeatherEffects.sbc, less ExampleWeather which is a comment.
            string[] weathers =
            {
                "ColdFront", "ExtremeCold", "HeatWave", "ExtremeHeat", "LowWinds", "HighWinds",
                "FogLight", "FogHeavy", "RainLight", "RainHeavy",
                "ThunderstormLight", "ThunderstormHeavy", "Hailstorm", "SnowLight", "SnowHeavy",
                "Dust", "SandStormLight", "SandStormHeavy",
                "MarsStormLight", "MarsStormHeavy", "MarsSnow",
                "AlienFogLight", "AlienFogHeavy", "AlienRainLight", "AlienRainHeavy",
                "AlienThunderstormLight", "AlienThunderstormHeavy",
                "AlienSandStormLight", "AlienSandStormHeavy",
                "AlienHeatWave", "AlienExtremeHeat", "ElectricStorm",
            };

            for (int i = 0; i < weathers.Length; i++)
            {
                WeatherResponse.Weather weather = WeatherResponse.For(weathers[i]);

                bool recognised = weather.TemperatureOffset != 0f
                    || weather.SolarMultiplier != 1f
                    || weather.WindMultiplier != 1f
                    || weather.ConvectionMultiplier != 1f;

                Assert.True(recognised, weathers[i] + " fell through the table");
            }
        }

        [Fact]
        public void NoWeatherAndAnUnknownWeatherAreBothCalm()
        {
            AssertCalm(WeatherResponse.For(""));
            AssertCalm(WeatherResponse.For(null));
            AssertCalm(WeatherResponse.For("SomeOtherModsWeather"));
        }

        private static void AssertCalm(WeatherResponse.Weather weather)
        {
            Assert.Equal(0f, weather.TemperatureOffset, 5);
            Assert.Equal(1f, weather.SolarMultiplier, 5);
            Assert.Equal(1f, weather.WindMultiplier, 5);
            Assert.Equal(1f, weather.ConvectionMultiplier, 5);
        }

        [Fact]
        public void TheKindsKeepTheOrderTheGameGaveThem()
        {
            // Snow is the cold one and a sandstorm the hot one; a storm blows and fog does not;
            // and heavy weather keeps more of the sun out than light.
            Assert.True(WeatherResponse.For("SnowHeavy").TemperatureOffset < -10f);
            Assert.True(WeatherResponse.For("SandStormHeavy").TemperatureOffset > 10f);

            Assert.True(WeatherResponse.For("ThunderstormHeavy").WindMultiplier > 1.5f);
            Assert.True(WeatherResponse.For("FogHeavy").WindMultiplier < 0.5f);

            Assert.True(WeatherResponse.For("RainHeavy").SolarMultiplier < WeatherResponse.For("RainLight").SolarMultiplier);

            // Rain is wet and dust is not, so rain takes more heat off a hull.
            Assert.True(WeatherResponse.For("RainHeavy").ConvectionMultiplier
                > WeatherResponse.For("Dust").ConvectionMultiplier);

            // LowWinds must not read as a gale just because it has "wind" in the name.
            Assert.True(WeatherResponse.For("LowWinds").WindMultiplier < 1f);
            Assert.True(WeatherResponse.For("HighWinds").WindMultiplier > 1f);
        }

        [Fact]
        public void LightWeatherIsHalfOfHeavy()
        {
            WeatherResponse.Weather heavy = WeatherResponse.For("RainHeavy");
            WeatherResponse.Weather light = WeatherResponse.For("RainLight");

            Assert.Equal(heavy.TemperatureOffset * 0.5f, light.TemperatureOffset, 3);
            Assert.Equal(1f + ((heavy.SolarMultiplier - 1f) * 0.5f), light.SolarMultiplier, 3);
        }

        [Fact]
        public void IntensityFadesAWeatherInFromCalm()
        {
            WeatherResponse.Weather storm = WeatherResponse.For("SnowHeavy");

            AssertCalm(WeatherResponse.Soften(storm, 0f));
            AssertCalm(WeatherResponse.Soften(storm, -1f));

            WeatherResponse.Weather half = WeatherResponse.Soften(storm, 0.5f);
            Assert.Equal(storm.TemperatureOffset * 0.5f, half.TemperatureOffset, 3);
            Assert.Equal(1f + ((storm.ConvectionMultiplier - 1f) * 0.5f), half.ConvectionMultiplier, 3);

            Assert.Equal(storm.TemperatureOffset, WeatherResponse.Soften(storm, 1f).TemperatureOffset, 3);
            Assert.Equal(storm.TemperatureOffset, WeatherResponse.Soften(storm, 2f).TemperatureOffset, 3);
        }

        [Fact]
        public void OvercastFlattensTheDay()
        {
            // Cloud that keeps the sun off by day keeps the heat in at night: it is one fact, so
            // the swing follows the solar multiplier rather than a column of its own.
            Assert.Equal(1f, WeatherResponse.SwingMultiplier(WeatherResponse.Calm), 3);

            float storm = WeatherResponse.SwingMultiplier(WeatherResponse.For("SnowHeavy"));
            Assert.True(storm < 1f);
            Assert.True(storm > 0f);
        }

        // ---- weather, through the solver ---------------------------------------------------

        [Fact]
        public void AStormCoolsTheAirDarkensTheSunAndStripsHeatFaster()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentSample clear = Worlds.PlanetSurface(1f, 0.5f);
            EnvironmentState calm = EnvironmentSolver.Solve(settings, planet, clear);

            EnvironmentSample stormy = clear;
            stormy.Weather = WeatherResponse.For("SnowHeavy");
            stormy.WeatherIntensity = 1f;
            EnvironmentState storm = EnvironmentSolver.Solve(settings, planet, stormy);

            Assert.True(storm.AmbientTemperature < calm.AmbientTemperature - 10f);
            Assert.True(storm.SolarEnergy < calm.SolarEnergy * 0.2f);
            Assert.True(storm.ConvectionCoefficient > calm.ConvectionCoefficient * 2f);

            // And it says so, so a readout can explain itself.
            Assert.Equal(1f, storm.WeatherIntensity, 3);
            Assert.True(storm.WeatherTemperatureOffset < 0f);
        }

        [Fact]
        public void ClearAirCostsTheModelNothing()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentSample clear = Worlds.PlanetSurface(1f, 0.5f);

            // A sample that names a weather at zero intensity has to be identical to one that
            // names none: intensity is the only thing that decides whether weather is happening.
            EnvironmentSample named = clear;
            named.Weather = WeatherResponse.For("ThunderstormHeavy");
            named.WeatherIntensity = 0f;

            EnvironmentState none = EnvironmentSolver.Solve(settings, planet, clear);
            EnvironmentState idle = EnvironmentSolver.Solve(settings, planet, named);

            Assert.Equal(none.AmbientTemperature, idle.AmbientTemperature, 4);
            Assert.Equal(none.SolarEnergy, idle.SolarEnergy, 4);
            Assert.Equal(none.ConvectionCoefficient, idle.ConvectionCoefficient, 4);
        }

        [Fact]
        public void AHeatWaveWarmsTheAirAndBrightensTheSun()
        {
            ThermalSettings settings = new ThermalSettings();
            PlanetThermalProperties planet = Earthlike();

            EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);
            EnvironmentState calm = EnvironmentSolver.Solve(settings, planet, sample);

            sample.Weather = WeatherResponse.For("ExtremeHeat");
            sample.WeatherIntensity = 1f;
            EnvironmentState hot = EnvironmentSolver.Solve(settings, planet, sample);

            Assert.True(hot.AmbientTemperature > calm.AmbientTemperature);
            Assert.True(hot.SolarEnergy > calm.SolarEnergy);
        }

        [Fact]
        public void WeatherDoesNotReachAGridWithNoPlanet()
        {
            ThermalSettings settings = new ThermalSettings();

            EnvironmentSample sample = Worlds.Space(Vector3.Up);
            sample.Weather = WeatherResponse.For("SnowHeavy");
            sample.WeatherIntensity = 1f;

            EnvironmentState state = EnvironmentSolver.Solve(settings, null, sample);

            Assert.Equal(settings.VacuumTemperature, state.AmbientTemperature, 3);
            Assert.Equal(0f, state.ConvectionCoefficient, 5);
        }

        // ---- wind --------------------------------------------------------------------------

        /// <summary>
        /// Same ceiling, same intensity, same place: a gale and a fog have to differ, and before the
        /// modifier existed they could not.
        ///
        /// <para>
        /// **How far they may differ is bounded, and the bound is the model.** The modifier scales
        /// how fast the share climbs from calm to storm rather than multiplying the finished share,
        /// so the windiest weather lands exactly on `StormFraction` and the stillest cannot fall
        /// below `CalmFraction` — the whole spread is the ratio of those two. Multiplying the share
        /// instead let a sandstorm's 2.25 carry the wind past the planet's own ceiling, which is
        /// [backlog](../../docs/backlog.md) `B17`.
        /// </para>
        /// </summary>
        [Fact]
        public void TheWindFieldTakesTheWeathersOwnWindModifier()
        {
            float gale = WindField.Speed(80f, 1f, 0.5f, WeatherResponse.For("SandStormHeavy").WindMultiplier);
            float fog = WindField.Speed(80f, 1f, 0.5f, WeatherResponse.For("FogHeavy").WindMultiplier);

            Assert.True(gale > fog * 3f,
                "a sandstorm and a fog should be plainly different weather: "
                + gale.ToString("n1") + " against " + fog.ToString("n1"));

            float widest = WindField.StormFraction / WindField.CalmFraction;
            Assert.True(gale / fog <= widest,
                "no two weathers may differ by more than the storm share over the calm one: "
                + (gale / fog).ToString("n2") + " against " + widest.ToString("n2"));

            // The windiest weather lands on the storm share and does not pass it, whatever its
            // modifier says — that is what makes the share mean "the worst weather".
            Assert.Equal(WindField.Speed(80f, 1f, 0.5f, 1f),
                WindField.Speed(80f, 1f, 0.5f, 9f), 4);

            // And the old three-argument form still means exactly what it did.
            Assert.Equal(WindField.Speed(80f, 1f, 0.5f), WindField.Speed(80f, 1f, 0.5f, 1f), 4);

            // The ceiling is still a ceiling however hard the weather blows.
            Assert.True(WindField.Speed(80f, 1f, 1f, 4f) <= 80f);
        }
    }
}
