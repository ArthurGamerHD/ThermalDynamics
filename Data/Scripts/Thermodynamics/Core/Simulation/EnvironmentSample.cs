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
            return s;
        }

        /// <summary>Deep space with no sun at all.</summary>
        public static EnvironmentSample DarkVacuum()
        {
            EnvironmentSample s = Vacuum(Vector3.Zero);
            s.IsSolarOccluded = true;
            return s;
        }
    }
}
