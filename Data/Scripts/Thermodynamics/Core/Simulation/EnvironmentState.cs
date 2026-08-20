using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The environment reduced to exactly the numbers the solver consumes. Produced once per
    /// step by <see cref="EnvironmentSolver"/> and shared by every node on the grid.
    /// </summary>
    /// <summary>
    /// One directional heat source other than the sun, reduced to a direction and the watts per
    /// square metre it delivers to a face pointing at it.
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
        /// How much the atmosphere behaves as a fluid rather than a vacuum, 0..1. Rises faster than
        /// raw density, so thin air still convects.
        /// </summary>
        public float AtmosphereFactor;

        /// <summary>
        /// Convective coefficient including the wind speed bonus and weather, W/(m^2 K), *before*
        /// the atmosphere blend. This is the planet's own figure scaled by conditions, not the
        /// rate a surface actually exchanges at — see <see cref="EffectiveConvectionCoefficient"/>.
        /// </summary>
        public float ConvectionCoefficient;

        /// <summary>
        /// The coefficient a surface actually exchanges at, W/(m^2 K): the one above, blended by
        /// how fluid-like the air is.
        ///
        /// The solver applies <see cref="AtmosphereFactor"/> to the transfer rather than to the
        /// coefficient, because the same factor also weights radiation down as it weights
        /// convection up, and the blend belongs where the two meet. That left the raw coefficient
        /// as the only figure anything reported: a field dump showed 50 W/(m^2 K) at 44 km with
        /// the air density column reading 0.0000, which reads as a defect and is not one. Anything
        /// reporting what convection is doing wants this.
        /// </summary>
        public float EffectiveConvectionCoefficient
        {
            get { return ConvectionCoefficient * AtmosphereFactor; }
        }

        /// <summary>Solar irradiance after atmospheric absorption, W/m^2.</summary>
        public float SolarEnergy;

        /// <summary>Sun direction in the grid's local frame.</summary>
        public Vector3 SunDirectionLocal;

        /// <summary>True when no solar energy reaches the grid.</summary>
        public bool IsSolarOccluded;

        /// <summary>
        /// How much of the grid the sun cannot reach, 0..1. Solar gain is scaled by what is left.
        /// </summary>
        public float SolarOcclusion;

        /// <summary>Relative airflow direction in the grid's local frame.</summary>
        public Vector3 WindDirectionLocal;

        /// <summary>Relative airflow speed, m/s.</summary>
        public float WindSpeed;

        /// <summary>True when aerodynamic heating applies at all.</summary>
        public bool FrictionActive;

        /// <summary>
        /// Weather intensity in force, 0..1, and the temperature offset it applied, K.
        ///
        /// Neither is read by the simulation: every effect the weather has is already folded into the
        /// ambient, the convection coefficient and the solar figure above. Carried for readouts.
        /// </summary>
        public float WeatherIntensity;

        public float WeatherTemperatureOffset;

        /// <summary>
        /// Point heat sources registered by other mods, reduced to a direction and an irradiance. Null
        /// when there are none, which is the usual case. The array may be longer than
        /// <see cref="HeatSourceCount"/> so a host can reuse one buffer.
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>Live entries in <see cref="HeatSources"/> this step.</summary>
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
