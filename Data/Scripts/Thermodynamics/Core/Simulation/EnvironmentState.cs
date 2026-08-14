using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The environment reduced to exactly the numbers the solver consumes. Produced once per
    /// step by <see cref="EnvironmentSolver"/> and shared by every node on the grid.
    /// </summary>
    /// <summary>
    /// One directional heat source other than the sun, reduced to what the solver needs: which
    /// way it is, and how many watts per square metre it delivers to a face pointing at it.
    /// </summary>
    public struct HeatSourceState
    {
        /// <summary>Unit vector toward the source, in the grid's local frame.</summary>
        public Vector3 DirectionLocal;

        /// <summary>Irradiance at the grid, W/m^2, after distance falloff and occlusion.</summary>
        public float Irradiance;

        public HeatSourceState(Vector3 directionLocal, float irradiance)
        {
            DirectionLocal = directionLocal;
            Irradiance = irradiance;
        }
    }

    public struct EnvironmentState
    {
        /// <summary>Ambient temperature, K.</summary>
        public float AmbientTemperature;

        /// <summary>Ambient to the fourth power, precomputed for the radiation term.</summary>
        public float AmbientTemperaturePow4;

        /// <summary>Raw air density, 0..1.</summary>
        public float AirDensity;

        /// <summary>
        /// How much the atmosphere behaves like a fluid rather than a vacuum, 0..1. Rises much
        /// faster than raw density, so thin air still convects meaningfully.
        /// </summary>
        public float AtmosphereFactor;

        /// <summary>Convective coefficient including the wind speed bonus, W/(m^2 K).</summary>
        public float ConvectionCoefficient;

        /// <summary>Solar irradiance after atmospheric absorption, W/m^2.</summary>
        public float SolarEnergy;

        /// <summary>Sun direction in the grid's local frame.</summary>
        public Vector3 SunDirectionLocal;

        /// <summary>True when no solar energy reaches the grid.</summary>
        public bool IsSolarOccluded;

        /// <summary>Relative airflow direction in the grid's local frame.</summary>
        public Vector3 WindDirectionLocal;

        /// <summary>Relative airflow speed, m/s.</summary>
        public float WindSpeed;

        /// <summary>True when aerodynamic heating applies at all.</summary>
        public bool FrictionActive;

        /// <summary>
        /// Point heat sources registered by other mods, already reduced to a direction and an
        /// irradiance. Null when there are none, which is the ordinary case; the array may be
        /// longer than <see cref="HeatSourceCount"/> so a host can reuse one buffer.
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>Entries of <see cref="HeatSources"/> that are live this step.</summary>
        public int HeatSourceCount;

        public static EnvironmentState Vacuum(float vacuumTemperature)
        {
            EnvironmentState s = new EnvironmentState();
            s.SetAmbient(vacuumTemperature);
            s.SunDirectionLocal = Vector3.Zero;
            s.WindDirectionLocal = Vector3.Zero;
            s.IsSolarOccluded = true;
            return s;
        }

        public void SetAmbient(float kelvin)
        {
            AmbientTemperature = kelvin;
            float squared = kelvin * kelvin;
            AmbientTemperaturePow4 = squared * squared;
        }
    }
}
