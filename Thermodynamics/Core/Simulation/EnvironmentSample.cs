using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct EnvironmentSample
    {
        public bool HasPlanet;

        public float AirDensity;

        public bool IsUnderground;

        public float Depth;

        public float Altitude;

        public float Radius;

        public float MeanRadius;

        public float LatitudeSine;

        public float GroundOffset;

        public float GroundSwing;

        public WeatherResponse.Weather Weather;

        public float WeatherIntensity;

        public float PreviousAmbient;

        public float SecondsSincePrevious;

        public float DayLengthSeconds;

        public bool HasPreviousAmbient;

        public Vector3 UpDirection;

        public Vector3 SunDirection;

        public Vector3 SunDirectionLocal;

        public bool IsSolarOccluded;

        public float SolarOcclusion;

        public float WindSpeed;

        public float WindProfileFactor;

        public float WindSpeedUp;

        public float WindShelter;

        public float WindHeating;

        public float WindHeightAboveGround;

        public float WindBurial;

        public float WindBandShare;

        public float WindChannelDegrees;

        public Vector3 WindDirection;

        public Vector3 GridVelocity;

        public Vector3 RelativeWindDirectionLocal;

        public float RelativeWindSpeed;

        public HeatSourceState[] HeatSources;

        public int HeatSourceCount;


        public void ComposeRelativeWind(Vector3 windDirection, float windSpeed, Matrix worldToLocal)
        {
            WindSpeed = windSpeed;
            WindDirection = windDirection;

            if (windSpeed <= 0f && GridVelocity.LengthSquared() <= 0f)
            {
                RelativeWindSpeed = 0f;
                RelativeWindDirectionLocal = Vector3.Zero;
                return;
            }

            Vector3 relative = (windDirection * windSpeed) - GridVelocity;
            RelativeWindSpeed = relative.Length();
            RelativeWindDirectionLocal = RelativeWindSpeed > 0f
                ? Vector3.Normalize(Vector3.TransformNormal(relative / RelativeWindSpeed, worldToLocal))
                : Vector3.Zero;
        }


        public static EnvironmentSample Vacuum(Vector3 sunDirectionLocal)
        {

            EnvironmentSample s = new EnvironmentSample();
            s.HasPlanet = false;
            s.AirDensity = 0f;
            s.SunDirection = sunDirectionLocal;
            s.SunDirectionLocal = sunDirectionLocal;
            s.UpDirection = Vector3.Up;
            s.WindDirection = Vector3.Zero;
            s.WindProfileFactor = 1f;
            s.WindSpeedUp = 1f;
            s.WindShelter = 1f;
            s.WindHeating = 0f;
            s.WindHeightAboveGround = 0f;
            s.WindBurial = 1f;
            s.WindBandShare = 0f;
            s.WindChannelDegrees = 0f;
            s.RelativeWindDirectionLocal = Vector3.Zero;
            s.Weather = WeatherResponse.Calm;
            return s;
        }


        public static EnvironmentSample DarkVacuum()
        {

            EnvironmentSample s = Vacuum(Vector3.Zero);
            s.IsSolarOccluded = true;
            s.SolarOcclusion = 1f;
            return s;
        }
    }
}
