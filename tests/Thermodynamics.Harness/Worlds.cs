using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class Worlds
    {

        public static EnvironmentSample Space(Vector3 sunDirectionLocal)
        {
            return EnvironmentSample.Vacuum(Vector3.Normalize(sunDirectionLocal));
        }


        public static EnvironmentSample Shadow()
        {
            return EnvironmentSample.DarkVacuum();
        }


        public static EnvironmentSample DarkVacuumWithSources(int count)
        {
            EnvironmentSample sample = EnvironmentSample.DarkVacuum();
            if (count <= 0) return sample;

            HeatSourceState[] sources = new HeatSourceState[count];
            for (int i = 0; i < count; i++)
            {
                double a = i * 2.399963f;
                double z = 1.0 - (2.0 * (i + 0.5) / count);
                double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));

                Vector3 direction = new Vector3((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r), (float)z);

                sources[i] = new HeatSourceState(direction, 400f + (i % 5) * 120f);
            }

            sample.HeatSources = sources;
            sample.HeatSourceCount = count;
            return sample;
        }


        public static EnvironmentSample PlanetSurface(float airDensity, float timeOfDay, float windSpeed = 0f)
        {

            EnvironmentSample sample = new EnvironmentSample();
            sample.HasPlanet = true;
            sample.AirDensity = airDensity;
            sample.IsUnderground = false;
            sample.UpDirection = Vector3.Up;

            double angle = (timeOfDay * 2d * Math.PI) - (Math.PI / 2d);

            Vector3 sun = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f);
            sample.SunDirection = sun;
            sample.SunDirectionLocal = sun;
            sample.IsSolarOccluded = sun.Y <= 0f;

            sample.WindSpeed = windSpeed;
            sample.WindDirection = Vector3.Forward;
            sample.RelativeWindSpeed = windSpeed;
            sample.RelativeWindDirectionLocal = windSpeed > 0f ? Vector3.Forward : Vector3.Zero;

            sample.Weather = WeatherResponse.Calm;
            sample.WeatherIntensity = 0f;
            sample.MeanRadius = EarthlikeRadius;
            sample.Radius = EarthlikeRadius;
            sample.Altitude = 0f;
            sample.Depth = 0f;

            return sample;
        }

        public const float EarthlikeRadius = 60000f;


        public static EnvironmentSample Underground(float airDensity = 1f, float depth = 100f)
        {

            EnvironmentSample sample = PlanetSurface(airDensity, 0.5f);
            sample.IsUnderground = true;
            sample.IsSolarOccluded = true;
            sample.Depth = depth;
            sample.Radius = EarthlikeRadius - depth;
            sample.Altitude = -depth;
            return sample;
        }


        public static EnvironmentSample Flight(float airDensity, float speed, float timeOfDay = 0.5f)
        {

            return WindAndMotion(airDensity, 0f, Vector3.Zero, Vector3.Backward * speed, timeOfDay);
        }

        public static class Ab
        {

            public static EnvironmentSample EveryTermLive()
            {

                return PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 300f);
            }


            public static EnvironmentSample MildAtmosphere()
            {

                return PlanetSurface(0.8f, timeOfDay: 0.35f, windSpeed: 22f);
            }


            public static EnvironmentSample SunlitVacuum()
            {
                return Space(new Vector3(0.3f, 0.9f, 0.2f));
            }
        }


        public static EnvironmentSample Storm(float airDensity, float windSpeed, float timeOfDay = 0.5f)
        {

            return WindAndMotion(airDensity, windSpeed, Vector3.Forward, Vector3.Zero, timeOfDay);
        }


        public static EnvironmentSample WindAndMotion(
            float airDensity, float windSpeed, Vector3 windDirection, Vector3 velocity,
            float timeOfDay = 0.5f)
        {

            EnvironmentSample sample = PlanetSurface(airDensity, timeOfDay);
            sample.GridVelocity = velocity;
            sample.ComposeRelativeWind(windDirection, windSpeed, Matrix.Identity);
            return sample;
        }
    }
}
