using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Raw environment readings for one grid at one instant, supplied by the host.
    ///
    /// This is the boundary between the simulation and the game world: everything requiring a
    /// planet, a raycast or a session lives on the host side of this struct, so the solver can be
    /// driven from a test with plain numbers.
    /// </summary>
    public struct EnvironmentSample
    {
        /// <summary>True when the grid is near a planet at all.</summary>
        public bool HasPlanet;

        /// <summary>Air density, 0 (vacuum) to 1 (sea level).</summary>
        public float AirDensity;

        /// <summary>True when the grid's reference point is below the surface.</summary>
        public bool IsUnderground;

        /// <summary>
        /// Metres of ground over the grid; zero or less is open air. This model's own depth figure,
        /// distinct from <see cref="IsUnderground"/>, which is the game's flag and is used only to
        /// establish that no sunlight reaches the grid.
        /// </summary>
        public float Depth;

        /// <summary>
        /// Metres above the planet's mean radius, negative below sea level. Drives the lapse rate
        /// that makes higher ground colder.
        /// </summary>
        public float Altitude;

        /// <summary>Metres from the planet's centre, and the planet's own sea-level radius.</summary>
        public float Radius;

        public float MeanRadius;

        /// <summary>
        /// Sine of latitude on the planet: 0 at the equator, ±1 at a pole. Zero when the host does
        /// not supply it, which is treated as the equator.
        /// </summary>
        public float LatitudeSine;

        /// <summary>
        /// Temperature offset from the surface material under the grid, K. Zero when the host does
        /// not supply it.
        /// </summary>
        public float GroundOffset;

        /// <summary>
        /// Multiplier the surface material applies to the day-night swing. 1 leaves the planet's own
        /// figures unchanged.
        /// </summary>
        public float GroundSwing;

        /// <summary>
        /// The weather over the grid at full strength, held separately from its intensity because
        /// the two come from different sources: the weather is a cached name lookup, the intensity
        /// is recomputed by the game as the front moves.
        ///
        /// A default-constructed <see cref="WeatherResponse.Weather"/> is not calm: every multiplier
        /// in it is zero, which would extinguish the sun. This does not arise in practice because a
        /// sample naming no weather also reports zero intensity, which softens any weather to
        /// <see cref="WeatherResponse.Calm"/>. A caller setting an intensity directly must also set
        /// this from <see cref="WeatherResponse.For"/>.
        /// </summary>
        public WeatherResponse.Weather Weather;

        /// <summary>Weather intensity here, 0..1. Zero is clear air.</summary>
        public float WeatherIntensity;

        /// <summary>
        /// Ambient at this grid a moment ago, K, and how long ago in seconds of play. The lag term
        /// integrates from this pair.
        /// </summary>
        public float PreviousAmbient;

        public float SecondsSincePrevious;

        /// <summary>
        /// Whether <see cref="PreviousAmbient"/> is a climate this grid actually held.
        ///
        /// False on the step a grid arrives at a planet, where the state still holds the vacuum it
        /// was seeded with; lagging up from 2.7 K would take minutes of play during which every
        /// block on the grid is dragged towards absolute zero. False means start at the target.
        /// </summary>
        public bool HasPreviousAmbient;

        /// <summary>
        /// Unit vector from the planet centre to the grid, in world space. Used for the
        /// day/night term. Ignored when <see cref="HasPlanet"/> is false.
        /// </summary>
        public Vector3 UpDirection;

        /// <summary>Unit vector toward the sun, in world space.</summary>
        public Vector3 SunDirection;

        /// <summary>
        /// The same direction in the grid's local frame, so the solver can weight block faces without
        /// handling transforms.
        /// </summary>
        public Vector3 SunDirectionLocal;

        /// <summary>True when something blocks the line to the sun.</summary>
        public bool IsSolarOccluded;

        /// <summary>
        /// Share of the grid the sun cannot reach, 0..1, from whatever stands between it and the
        /// sun: a planet, a voxel, or another grid.
        ///
        /// A fraction rather than a flag because a grid is not a point: a large grid crossing a
        /// terminator or emerging from behind an asteroid is partly lit throughout, which a flag
        /// would render as a step change.
        ///
        /// <see cref="IsSolarOccluded"/> is the fully shadowed case, retained because most of the
        /// model only needs to know whether there is any sun at all.
        /// </summary>
        public float SolarOcclusion;

        /// <summary>Weather or planetary wind speed at the grid, m/s.</summary>
        public float WindSpeed;

        /// <summary>Wind direction in world space.</summary>
        public Vector3 WindDirection;

        /// <summary>The grid's own velocity in world space, m/s.</summary>
        public Vector3 GridVelocity;

        /// <summary>
        /// Relative airflow direction in the grid's local frame, computed by the host as wind minus
        /// grid velocity.
        /// </summary>
        public Vector3 RelativeWindDirectionLocal;

        /// <summary>Magnitude of wind minus grid velocity, m/s.</summary>
        public float RelativeWindSpeed;

        /// <summary>
        /// Point heat sources other than the sun, each reduced by the host to a local direction and
        /// an irradiance at this grid. Null when there are none.
        ///
        /// The host owns the buffer and may reuse it between samples, so the array may be longer
        /// than <see cref="HeatSourceCount"/>. Occlusion is also the host's responsibility: an
        /// occluded source is omitted.
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>Number of live entries in <see cref="HeatSources"/>.</summary>
        public int HeatSourceCount;

        /// <summary>A sample for deep space with the sun overhead.</summary>
        public static EnvironmentSample Vacuum(Vector3 sunDirectionLocal)
        {
            EnvironmentSample s = new EnvironmentSample();
            s.HasPlanet = false;
            s.AirDensity = 0f;
            s.SunDirection = sunDirectionLocal;
            s.SunDirectionLocal = sunDirectionLocal;
            s.UpDirection = Vector3.Up;
            s.WindDirection = Vector3.Zero;
            s.RelativeWindDirectionLocal = Vector3.Zero;
            s.Weather = WeatherResponse.Calm;
            return s;
        }

        /// <summary>A sample for deep space with no sun.</summary>
        public static EnvironmentSample DarkVacuum()
        {
            EnvironmentSample s = Vacuum(Vector3.Zero);
            s.IsSolarOccluded = true;
            s.SolarOcclusion = 1f;
            return s;
        }
    }
}
