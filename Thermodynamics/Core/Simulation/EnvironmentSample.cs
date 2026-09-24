using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Sample of environmental conditions at a specific point in the simulation.
    /// Contains raw location and weather data that EnvironmentSolver uses to compute
    /// the full EnvironmentState with effective temperatures and coefficients.
    /// </summary>
    public struct EnvironmentSample
    {
        /// <summary>
        /// True if this location is on or near a planet with atmosphere.
        /// </summary>
        public bool HasPlanet;

        /// <summary>
        /// Air density at this location as fraction of sea-level density (0-1).
        /// Used to calculate atmospheric effects on heat transfer.
        /// </summary>
        public float AirDensity;

        /// <summary>
        /// True if this location is underground (below surface).
        /// Underground locations have different thermal properties and are solar-occluded.
        /// </summary>
        public bool IsUnderground;

        /// <summary>
        /// Depth below surface in meters (positive = underground).
        /// Used to calculate underground temperature and solar shielding.
        /// </summary>
        public float Depth;

        /// <summary>
        /// Altitude above reference level (e.g., sea level) in meters.
        /// Used for lapse rate calculations (temperature change with height).
        /// </summary>
        public float Altitude;

        /// <summary>
        /// Distance from planetary center in meters.
        /// Used for underground temperature and gravity calculations.
        /// </summary>
        public float Radius;

        /// <summary>
        /// Mean planetary radius in meters.
        /// Used for sealevel deadzone calculations.
        /// </summary>
        public float MeanRadius;

        /// <summary>
        /// Sine of latitude angle (0 at equator, ±1 at poles).
        /// Used to calculate pole temperature drops.
        /// </summary>
        public float LatitudeSine;

        /// <summary>
        /// Ground surface temperature offset in Kelvin.
        /// Local terrain effects on surface temperature.
        /// </summary>
        public float GroundOffset;

        /// <summary>
        /// Day/night temperature swing multiplier (0-1).
        /// Reduces the effective temperature variation at this location.
        /// </summary>
        public float GroundSwing;

        /// <summary>
        /// Current weather conditions.
        /// Affects temperature, solar penetration, and wind effects.
        /// </summary>
        public WeatherResponse.Weather Weather;

        /// <summary>
        /// Weather intensity (0-1).
        /// 0 = no weather effects, 1 = full weather effects.
        /// </summary>
        public float WeatherIntensity;

        /// <summary>
        /// Previous ambient temperature in Kelvin.
        /// Used for calculating thermal lag when approaching new equilibrium.
        /// </summary>
        public float PreviousAmbient;

        /// <summary>
        /// Time since previous ambient measurement in seconds.
        /// Used for calculating temperature change rate and lag effects.
        /// </summary>
        public float SecondsSincePrevious;

        /// <summary>
        /// Length of the local day in seconds.
        /// Used for calculating thermal lag based on day cycle.
        /// </summary>
        public float DayLengthSeconds;

        /// <summary>
        /// True if PreviousAmbient contains valid data.
        /// </summary>
        public bool HasPreviousAmbient;

        /// <summary>
        /// Up direction (local vertical) in world space.
        /// Direction away from planetary center or away from "down".
        /// </summary>
        public Vector3 UpDirection;

        /// <summary>
        /// Sun direction in world space (normalized vector pointing toward sun).
        /// Used for calculating solar incidence and shadowing.
        /// </summary>
        public Vector3 SunDirection;

        /// <summary>
        /// Sun direction in local block space (normalized vector).
        /// Pre-transformed from world to local space for efficient calculations.
        /// </summary>
        public Vector3 SunDirectionLocal;

        /// <summary>
        /// True if this location is in permanent shadow (e.g., eclipse).
        /// Overrides SolarOcclusion to fully block solar energy.
        /// </summary>
        public bool IsSolarOccluded;

        /// <summary>
        /// Solar occlusion factor (0-1).
        /// Partial occlusion from clouds, terrain, or other blocks.
        /// 0 = clear sky, 1 = fully occluded.
        /// </summary>
        public float SolarOcclusion;

        /// <summary>
        /// Wind speed in meters per second.
        /// Speed of air movement relative to the local frame.
        /// </summary>
        public float WindSpeed;

        /// <summary>
        /// Wind profile factor for height above ground.
        /// Adjusts wind speed based on altitude (logarithmic wind profile).
        /// </summary>
        public float WindProfileFactor;

        /// <summary>
        /// Wind speed multiplier for height effects.
        /// Combined with WindProfileFactor for accurate wind modeling.
        /// </summary>
        public float WindSpeedUp;

        /// <summary>
        /// Wind sheltering factor (0-1).
        /// 1 = fully exposed, 0 = completely sheltered.
        /// Reduces wind effects in protected areas (valleys, behind obstacles).
        /// </summary>
        public float WindShelter;

        /// <summary>
        /// Wind heating factor from solar heating of ground.
        /// Affects thermal convection due to ground heating.
        /// </summary>
        public float WindHeating;

        /// <summary>
        /// Height above ground in meters for wind calculations.
        /// Used for logarithmic wind profile and terrain interaction.
        /// </summary>
        public float WindHeightAboveGround;

        /// <summary>
        /// Burial factor for underground wind reduction (0-1).
        /// 1 = at surface, 0 = deep underground (no wind).
        /// Wind decreases exponentially with depth below surface.
        /// </summary>
        public float WindBurial;

        /// <summary>
        /// Share of wind coming from atmospheric bands.
        /// Represents contribution of large-scale wind patterns.
        /// </summary>
        public float WindBandShare;

        /// <summary>
        /// Angle in degrees of wind channeling through terrain.
        /// 0 = unchannelled, 90 = fully channeled (max speed-up).
        /// </summary>
        public float WindChannelDegrees;

        /// <summary>
        /// Wind direction in world space (normalized vector).
        /// Direction wind is blowing toward.
        /// </summary>
        public Vector3 WindDirection;

        /// <summary>
        /// Velocity of the grid/block in world space (m/s).
        /// Used for calculating relative wind between moving object and air.
        /// </summary>
        public Vector3 GridVelocity;

        /// <summary>
        /// Relative wind direction in local block space (normalized vector).
        /// Wind direction after accounting for block orientation and movement.
        /// </summary>
        public Vector3 RelativeWindDirectionLocal;

        /// <summary>
        /// Speed of relative wind in meters per second.
        /// Magnitude of RelativeWindDirectionLocal.
        /// </summary>
        public float RelativeWindSpeed;

        /// <summary>
        /// Array of additional heat sources at this location.
        /// Localized sources beyond the main sun (reactors, engines, etc.).
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>
        /// Number of valid heat sources in the HeatSources array.
        /// </summary>
        public int HeatSourceCount;


        /// <summary>
        /// Computes relative wind considering both atmospheric wind and grid movement.
        /// Converts absolute wind to relative wind in local block space.
        /// </summary>
        /// <param name="windDirection">Absolute wind direction in world space.</param>
        /// <param name="windSpeed">Absolute wind speed in m/s.</param>
        /// <param name="worldToLocal">Transformation matrix from world to local space.</param>
        /// <remarks>
        /// Relative wind = atmospheric wind - grid velocity
        /// Then transformed to local space for use in surface calculations.
        /// </remarks>
        public void ComposeRelativeWind(Vector3 windDirection, float windSpeed, Matrix worldToLocal)
        {
            WindSpeed = windSpeed;
            WindDirection = windDirection;

            // No relative wind if both wind and grid are stationary
            if (windSpeed <= 0f && GridVelocity.LengthSquared() <= 0f)
            {
                RelativeWindSpeed = 0f;
                RelativeWindDirectionLocal = Vector3.Zero;
                return;
            }

            // Relative wind = wind vector - grid velocity vector
            Vector3 relative = (windDirection * windSpeed) - GridVelocity;
            RelativeWindSpeed = relative.Length();

            // Transform to local space for surface calculations
            RelativeWindDirectionLocal = RelativeWindSpeed > 0f
                ? Vector3.Normalize(Vector3.TransformNormal(relative / RelativeWindSpeed, worldToLocal))
                : Vector3.Zero;
        }


        /// <summary>
        /// Creates an EnvironmentSample representing vacuum conditions with sunlight.
        /// Used for space-based grids with no atmosphere.
        /// </summary>
        /// <param name="sunDirectionLocal">Direction to sun in local block space.</param>
        /// <returns>EnvironmentSample configured for space/vacuum environment.</returns>
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


        /// <summary>
        /// Creates an EnvironmentSample representing complete darkness/vacuum.
        /// No sunlight, no heat sources. Used for eclipses or underground locations.
        /// </summary>
        /// <returns>EnvironmentSample with all solar effects disabled.</returns>
        public static EnvironmentSample DarkVacuum()
        {
            EnvironmentSample s = Vacuum(Vector3.Zero);
            s.IsSolarOccluded = true;
            s.SolarOcclusion = 1f;
            return s;
        }
    }
}
