using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class EnvironmentSolver
    {
        public const float WindConvectionScale = 0.1f;

/// <summary>Solve operation.</summary>
        public static EnvironmentState Solve(ThermalSettings settings, PlanetThermalProperties planet, EnvironmentSample sample)
        {
            if (settings == null) throw new ArgumentNullException("settings");

/// <summary>EnvironmentState operation.</summary>
            EnvironmentState state = new EnvironmentState();
            state.SunDirectionLocal = sample.SunDirectionLocal;
            state.WindDirectionLocal = sample.RelativeWindDirectionLocal;
            state.WindSpeed = Math.Max(0f, sample.RelativeWindSpeed);

            if (settings.EnableHeatSources && sample.HeatSources != null)
            {
                state.HeatSources = sample.HeatSources;
                state.HeatSourceCount = Math.Min(sample.HeatSourceCount, sample.HeatSources.Length);
            }

            state.SolarOcclusion = ThermalMath.Clamp01(sample.SolarOcclusion);
            if (sample.IsSolarOccluded || !settings.EnableSolarHeat) state.SolarOcclusion = 1f;

            state.IsSolarOccluded = state.SolarOcclusion >= 1f;

            bool usePlanet = settings.EnablePlanets && sample.HasPlanet && planet != null;
            if (!usePlanet)
            {
                state.SetAmbient(settings.VacuumTemperature);
                state.AirDensity = 0f;
                state.AtmosphereFactor = 0f;
                state.ConvectionCoefficient = 0f;
                state.SolarEnergy = settings.SolarEnergy * (1f - state.SolarOcclusion);
                state.FrictionActive = false;
                return state;
            }

            float density = ThermalMath.Clamp01(sample.AirDensity);
            state.AirDensity = density;
/// <summary>AtmosphereFactor operation.</summary>
            state.AtmosphereFactor = AtmosphereFactor(density);

            WeatherResponse.Weather weather = WeatherResponse.Soften(sample.Weather, ThermalMath.Clamp01(sample.WeatherIntensity));

            state.WeatherIntensity = ThermalMath.Clamp01(sample.WeatherIntensity);
            state.WeatherTemperatureOffset = weather.TemperatureOffset;


            float elevation = Vector3.Dot(SafeNormalize(sample.UpDirection), SafeNormalize(sample.SunDirection));

            float groundSwing = sample.GroundSwing > 0f ? sample.GroundSwing : 1f;

            float offset = sample.GroundOffset + weather.TemperatureOffset;
            float swing = groundSwing * WeatherResponse.SwingMultiplier(weather);

            float target = ClimateModel.Target(planet, sample.LatitudeSine, elevation, offset, swing);

            target = ClimateModel.Lapse(target, sample.Altitude, planet.AmbientLapseRate);
            target = ClimateModel.Thin(target, density, settings.VacuumTemperature);

            if (sample.Depth > 0f)
            {
                target = ClimateModel.Underground(
                    planet, target, sample.Depth, sample.Radius, sample.MeanRadius);
            }

            if (sample.IsUnderground || sample.Depth > 0f)
            {
                state.IsSolarOccluded = true;
                state.SolarOcclusion = 1f;
            }

            float ambient = sample.HasPreviousAmbient
                ? ClimateModel.Follow(
                    sample.PreviousAmbient, target, sample.SecondsSincePrevious,
                    planet.LagSecondsFor(sample.DayLengthSeconds))
                : target;

            state.SetAmbient(Math.Max(settings.VacuumTemperature, ambient));

            float windBonus = 1f + (WindConvectionScale * (float)Math.Sqrt(state.WindSpeed));
            float air = planet.ConvectionCoefficient * windBonus
                * Math.Max(0f, weather.ConvectionMultiplier);

/// <summary>InRock operation.</summary>
            float rock = InRock(sample.Depth);
            state.ConvectionCoefficient = rock <= 0f
                ? air
                : air + ((planet.UndergroundConvectionCoefficient - air) * rock);

            state.SolarEnergy = settings.SolarEnergy
                * (1f - state.SolarOcclusion)
                * (1f - (planet.SolarDecay * state.AtmosphereFactor))
                * Math.Max(0f, weather.SolarMultiplier);
            if (state.SolarEnergy < 0f) state.SolarEnergy = 0f;

            state.FrictionActive = settings.EnableFriction
                && density > 0.01f
                && state.WindSpeed > settings.FrictionAtSpeedsAbove;

            return state;
        }

/// <summary>InRock operation.</summary>
        public static float InRock(float depth)
        {
            if (depth <= 0f) return 0f;

            float share = depth / ThermalConstants.UndergroundContactDepth;
            return share > 1f ? 1f : share;
        }

/// <summary>AtmosphereFactor operation.</summary>
        public static float AtmosphereFactor(float airDensity)
        {
            float inverse = 1f - ThermalMath.Clamp01(airDensity);
            float squared = inverse * inverse;
            return 1f - (squared * squared);
        }

/// <summary>SafeNormalize operation.</summary>
        private static Vector3 SafeNormalize(Vector3 v)
        {
            float lengthSquared = v.LengthSquared();
            if (lengthSquared <= 1e-12f) return Vector3.Zero;
            return v / (float)Math.Sqrt(lengthSquared);
        }
    }
}
