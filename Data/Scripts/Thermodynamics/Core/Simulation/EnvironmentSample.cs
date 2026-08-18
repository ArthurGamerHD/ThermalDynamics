using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Raw environment readings for one grid at one instant, supplied by the host.
    ///
    /// This is the whole boundary between the simulation and the game world: everything that
    /// needs a planet, a raycast or a session lives on the host side of this struct, so the
    /// solver can be driven from a test with plain numbers.
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
        /// Metres of ground over the grid. Zero or less means open air, and the sign is the whole
        /// test — <see cref="IsUnderground"/> is the game's own answer and is kept for what it is
        /// good for, which is knowing the sun cannot get in.
        /// </summary>
        public float Depth;

        /// <summary>
        /// Metres above the planet's mean radius. Negative in a valley below sea level. The air
        /// cools with this, which is what makes a mountain top colder than the plain.
        /// </summary>
        public float Altitude;

        /// <summary>Metres from the planet's centre, and the planet's own sea-level radius.</summary>
        public float Radius;

        public float MeanRadius;

        /// <summary>
        /// Sine of latitude on the planet: 0 at the equator, ±1 at a pole. Zero when the host does
        /// not say, which reads as the equator and is what the model did before it was asked.
        /// </summary>
        public float LatitudeSine;

        /// <summary>
        /// What the ground under the grid is worth, K — snow cold, sand warm. Zero when the host
        /// does not care or cannot tell.
        /// </summary>
        public float GroundOffset;

        /// <summary>
        /// How much the ground widens or narrows the day-night swing. 1 leaves the planet's own
        /// figures alone, which is what a host that does not care should send.
        /// </summary>
        public float GroundSwing;

        /// <summary>
        /// The weather over the grid at full strength, and how much of it is actually blowing.
        ///
        /// Split that way because the two come from different places and change at different
        /// rates: which weather it is comes from a name lookup the host caches, while the
        /// intensity is a number the game recomputes as the front moves over.
        ///
        /// A default-constructed <see cref="WeatherResponse.Weather"/> is <em>not</em> calm — every
        /// multiplier in it is zero, which would put the sun out. It never matters in practice
        /// because a sample that names no weather also reports no intensity, and intensity zero
        /// softens anything to <see cref="WeatherResponse.Calm"/>. A caller setting an intensity by
        /// hand should set this from <see cref="WeatherResponse.For"/> as well.
        /// </summary>
        public WeatherResponse.Weather Weather;

        /// <summary>Weather intensity here, 0..1. Zero is clear air and costs nothing.</summary>
        public float WeatherIntensity;

        /// <summary>
        /// Ambient at this grid a moment ago, K, and how long ago in seconds of play. The pair the
        /// lag needs: air chases the sun rather than tracking it, and chasing needs a start.
        /// </summary>
        public float PreviousAmbient;

        public float SecondsSincePrevious;

        /// <summary>
        /// Whether <see cref="PreviousAmbient"/> is a climate this grid actually had, rather than
        /// whatever the field happened to hold.
        ///
        /// The lag needs somewhere to start and there is no such place on the step a grid arrives
        /// at a planet — the state still holds the vacuum it was seeded with, and chasing 300 K
        /// from 2.7 K at 45 seconds a decade takes three minutes of play during which every block
        /// on the ship is dragged toward absolute zero. False means take the target and start
        /// there, which is what a grid that has just arrived should do.
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
        /// The same direction expressed in the grid's local frame, so the solver can weight
        /// block faces without knowing anything about matrices.
        /// </summary>
        public Vector3 SunDirectionLocal;

        /// <summary>True when something blocks the line to the sun.</summary>
        public bool IsSolarOccluded;

        /// <summary>
        /// How much of the grid the sun cannot reach, 0..1, from whatever stands between it and the
        /// sun: a planet, an asteroid, another ship.
        ///
        /// A fraction rather than a flag because a ship is not a point. A kilometre of hull crossing
        /// a terminator, or drifting out from behind an asteroid, is partly lit for as long as it
        /// takes to cross — and a flag makes that a step change from full sun to none, which reads
        /// as a bug in the shadow rather than as the crude answer it is.
        ///
        /// <see cref="IsSolarOccluded"/> is the fully-shadowed case, kept because most of the model
        /// only cares whether there is any sun at all.
        /// </summary>
        public float SolarOcclusion;

        /// <summary>Weather or planetary wind speed at the grid, m/s.</summary>
        public float WindSpeed;

        /// <summary>Wind direction in world space.</summary>
        public Vector3 WindDirection;

        /// <summary>The grid's own velocity in world space, m/s.</summary>
        public Vector3 GridVelocity;

        /// <summary>
        /// Relative airflow direction in the grid's local frame. The host computes this from
        /// wind minus grid velocity.
        /// </summary>
        public Vector3 RelativeWindDirectionLocal;

        /// <summary>Magnitude of wind minus grid velocity, m/s.</summary>
        public float RelativeWindSpeed;

        /// <summary>
        /// Point heat sources other than the sun, each already reduced by the host to a local
        /// direction and an irradiance at this grid. Null when there are none.
        ///
        /// The host owns the buffer and may reuse it between samples, so the array is allowed to
        /// be longer than <see cref="HeatSourceCount"/>. Occlusion is the host's business too: a
        /// source it cannot see is simply left out.
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>Entries of <see cref="HeatSources"/> that are live.</summary>
        public int HeatSourceCount;

        /// <summary>A grid sitting in deep space with the sun overhead.</summary>
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

        /// <summary>Deep space with no sun at all.</summary>
        public static EnvironmentSample DarkVacuum()
        {
            EnvironmentSample s = Vacuum(Vector3.Zero);
            s.IsSolarOccluded = true;
            s.SolarOcclusion = 1f;
            return s;
        }
    }
}
