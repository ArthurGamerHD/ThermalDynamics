using System;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Canned environments. These stand in for everything the game would supply: planets,
    /// raycasts, weather and grid velocity.
    /// </summary>
    public static class Worlds
    {
        /// <summary>Deep space, sun on the given local axis.</summary>
        public static EnvironmentSample Space(Vector3 sunDirectionLocal)
        {
            return EnvironmentSample.Vacuum(Vector3.Normalize(sunDirectionLocal));
        }

        /// <summary>Deep space in full shadow.</summary>
        public static EnvironmentSample Shadow()
        {
            return EnvironmentSample.DarkVacuum();
        }

        /// <summary>
        /// Standing on a planet.
        /// </summary>
        /// <param name="airDensity">0..1.</param>
        /// <param name="timeOfDay">
        /// 0 = midnight, 0.5 = noon. Drives both ambient temperature and the sun direction.
        /// </param>
        /// <param name="windSpeed">Weather wind, m/s.</param>
        public static EnvironmentSample PlanetSurface(float airDensity, float timeOfDay, float windSpeed = 0f)
        {
            EnvironmentSample sample = new EnvironmentSample();
            sample.HasPlanet = true;
            sample.AirDensity = airDensity;
            sample.IsUnderground = false;
            sample.UpDirection = Vector3.Up;

            // The sun swings from below the horizon at midnight to overhead at noon.
            double angle = (timeOfDay * 2d * Math.PI) - (Math.PI / 2d);
            Vector3 sun = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f);
            sample.SunDirection = sun;
            sample.SunDirectionLocal = sun;
            sample.IsSolarOccluded = sun.Y <= 0f;

            sample.WindSpeed = windSpeed;
            sample.WindDirection = Vector3.Forward;
            sample.RelativeWindSpeed = windSpeed;
            sample.RelativeWindDirectionLocal = windSpeed > 0f ? Vector3.Forward : Vector3.Zero;

            // Clear air on an earthlike ball, at sea level. A scenario that cares about altitude,
            // depth or weather says so; everything else gets a world where none of the three is
            // doing anything, which is what these fixtures meant before any of them existed.
            sample.Weather = WeatherResponse.Calm;
            sample.WeatherIntensity = 0f;
            sample.MeanRadius = EarthlikeRadius;
            sample.Radius = EarthlikeRadius;
            sample.Altitude = 0f;
            sample.Depth = 0f;

            return sample;
        }

        /// <summary>Metres from centre to sea level on the stand-in planet. An earthlike's radius.</summary>
        public const float EarthlikeRadius = 60000f;

        /// <summary>
        /// Buried in the ground: no sun, underground ambient.
        ///
        /// Deep enough that the surface's day has damped out entirely and shallow enough to stay
        /// inside the sea-level deadzone, so this is the flat part of the depth curve — the one
        /// place ambient is simply the planet's underground figure.
        /// </summary>
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

        /// <summary>
        /// Flying fast through still air. Composed the way the game composes it — velocity against
        /// a zero wind — so the airflow on the hull is the adapter's own arithmetic, not a fixture
        /// asserting what it should have been.
        /// </summary>
        public static EnvironmentSample Flight(float airDensity, float speed, float timeOfDay = 0.5f)
        {
            return WindAndMotion(airDensity, 0f, Vector3.Zero, Vector3.Backward * speed, timeOfDay);
        }

        /// <summary>A parked hull in an ambient wind — a storm, with the ship standing still.</summary>
        public static EnvironmentSample Storm(float airDensity, float windSpeed, float timeOfDay = 0.5f)
        {
            return WindAndMotion(airDensity, windSpeed, Vector3.Forward, Vector3.Zero, timeOfDay);
        }

        /// <summary>
        /// The general case: an ambient wind and a moving grid at once, composed exactly as the
        /// game adapter composes them. The hull feels the *relative* wind — the one scalar friction
        /// and forced convection read — so flying with the wind at the wind's own speed is calm air
        /// at full ground speed, and flying into it is the sum. World and local frames coincide.
        /// </summary>
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
