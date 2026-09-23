using System;

namespace Thermodynamics.Core
{
    public static class ClimateModel
    {

        public static float Target(
            PlanetThermalProperties planet, float latitudeSine, float sunElevationSine, float groundOffset)
        {

            return Target(planet, latitudeSine, sunElevationSine, groundOffset, 1f);
        }


        public static float Target(
            PlanetThermalProperties planet,
            float latitudeSine,
            float sunElevationSine,
            float groundOffset,
            float groundSwing)
        {
            if (planet == null) return 0f;
            if (groundSwing < 0f) groundSwing = 0f;

            float latitude = (float)Math.Sqrt(Math.Max(0f, 1f - (latitudeSine * latitudeSine)));
            float drop = planet.PoleTemperatureDrop * (1f - latitude);

            float night;
            float day;

            if (groundSwing == 1f)
            {
                night = planet.NightTemperature - drop;
                day = planet.DayTemperature - drop;
            }
            else
            {
                float mean = ((planet.NightTemperature + planet.DayTemperature) * 0.5f) - drop;
                float half = (planet.DayTemperature - planet.NightTemperature) * 0.5f * groundSwing;

                night = mean - half;
                day = mean + half;
            }

            float insolation = sunElevationSine <= 0f ? 0f : sunElevationSine;

            float target = night + ((day - night) * insolation) + groundOffset;
            return target < 0f ? 0f : target;
        }


        public static float Lapse(float target, float altitude, float lapseRatePerKm)
        {
            if (lapseRatePerKm == 0f || altitude == 0f) return target;

            float cooled = target - (lapseRatePerKm * altitude * 0.001f);
            return cooled < 0f ? 0f : cooled;
        }


        public static float AmbientDensityFactor(float airDensity)
        {
            float inverse = 1f - ThermalMath.Clamp01(airDensity);
            float squared = inverse * inverse;
            float fourth = squared * squared;
            return 1f - (fourth * fourth);
        }


        public static float Thin(float target, float airDensity, float vacuum)
        {

            float share = AmbientDensityFactor(airDensity);
            return vacuum + ((target - vacuum) * share);
        }


        public static float Underground(
            PlanetThermalProperties planet, float surface, float depth, float radius, float meanRadius)
        {
            if (planet == null) return surface;
            if (depth <= 0f) return surface;

            float damping = planet.UndergroundDampingDepth;
            float buried = damping <= 0f ? 1f : depth / damping;
            if (buried > 1f) buried = 1f;

            float ambient = surface + ((planet.UndergroundTemperature - surface) * buried);

            float deadzone = meanRadius - planet.SealevelDeadzone;
            if (deadzone <= 0f || radius >= deadzone) return ambient;

            float descended = 1f - (radius / deadzone);
            if (descended > 1f) descended = 1f;

            return ambient + ((planet.CoreTemperature - ambient) * descended);
        }


        public static float Follow(float current, float target, float seconds, float lagSeconds)
        {
            if (lagSeconds <= 0f || seconds <= 0f) return target;
            if (current <= 0f) return target;

            float closed = 1f - (float)Math.Exp(-seconds / lagSeconds);
            return current + ((target - current) * closed);
        }
    }
}
