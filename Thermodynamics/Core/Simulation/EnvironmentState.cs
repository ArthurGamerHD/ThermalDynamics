using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// State of a localized heat source in the environment (e.g., sun, reactor, etc.).
    /// Represents a directional radiant heat source that contributes to solar heating.
    /// </summary>
    public struct HeatSourceState
    {
        /// <summary>
        /// Direction to the heat source in local block space (normalized vector).
        /// Used for calculating solar incidence angle on block surfaces.
        /// </summary>
        public Vector3 DirectionLocal;

        /// <summary>
        /// Irradiance from this heat source in watts per square meter.
        /// Represents the power density of radiation arriving from this direction.
        /// </summary>
        public float Irradiance;


        /// <summary>
        /// Creates a new HeatSourceState instance.
        /// </summary>
        /// <param name="directionLocal">Direction to source in local space.</param>
        /// <param name="irradiance">Radiant power per unit area in W/m².</param>
        public HeatSourceState(Vector3 directionLocal, float irradiance)
        {
            DirectionLocal = directionLocal;
            Irradiance = irradiance;
        }
    }

    /// <summary>
    /// Complete environmental state for a single simulation node.
    /// Contains all environmental parameters needed to calculate heat transfer
    /// for a block at a specific location and time.
    /// </summary>
    public struct EnvironmentState
    {
        /// <summary>
        /// Ambient temperature in Kelvin.
        /// Base temperature of the surrounding environment.
        /// </summary>
        public float AmbientTemperature;

        /// <summary>
        /// Ambient temperature raised to the fourth power (T⁴).
        /// Used directly in Stefan-Boltzmann radiation calculations:
        ///   RadiationPower = emissivity * σ * T⁴
        /// Precomputed to avoid repeated exponentiation.
        /// </summary>
        public float AmbientTemperaturePow4;

        /// <summary>
        /// Air density as fraction of sea-level density (0-1).
        /// Affects convective heat transfer and atmospheric effects.
        /// 0 = vacuum, 1 = sea-level air density.
        /// </summary>
        public float AirDensity;

        /// <summary>
        /// Atmospheric factor from 0 to 1.
        /// Represents the thermal effect of the atmosphere (fourth-power law).
        /// 0 = vacuum, 1 = full atmosphere effects.
        /// </summary>
        public float AtmosphereFactor;

        /// <summary>
        /// Convection coefficient in W/m²·K.
        /// Heat transfer rate per degree temperature difference via air movement.
        /// Higher values = better convective cooling.
        /// </summary>
        public float ConvectionCoefficient;

        /// <summary>
        /// Gets the effective convection coefficient after applying atmosphere factor.
        /// Convection only occurs in atmosphere, not vacuum.
        /// </summary>
        public float EffectiveConvectionCoefficient
        {
            get { return ConvectionCoefficient * AtmosphereFactor; }
        }

        /// <summary>
        /// Solar energy incident on surfaces in watts per square meter.
        /// Adjusted for occlusion (eclipse), atmosphere, and weather.
        /// </summary>
        public float SolarEnergy;

        /// <summary>
        /// Sun direction in local block space (normalized vector).
        /// Used for calculating solar incidence on block surfaces.
        /// Zero vector if no sun or fully occluded.
        /// </summary>
        public Vector3 SunDirectionLocal;

        /// <summary>
        /// True if the block is completely shaded from direct sunlight.
        /// Occurs during eclipses, underground, or inside structures.
        /// </summary>
        public bool IsSolarOccluded;

        /// <summary>
        /// Solar occlusion factor (0-1).
        /// 0 = fully exposed, 1 = fully occluded.
        /// Used to scale solar energy when partial occlusion occurs.
        /// </summary>
        public float SolarOcclusion;

        /// <summary>
        /// Wind direction in local block space (normalized vector).
        /// Direction from which wind is coming (0,0,1) means wind coming from front.
        /// </summary>
        public Vector3 WindDirectionLocal;

        /// <summary>
        /// Wind speed in meters per second.
        /// Affects convective heat transfer and friction heating.
        /// </summary>
        public float WindSpeed;

        /// <summary>
        /// True if friction heating is active.
        /// Friction heating occurs when moving through atmosphere at sufficient speed.
        /// </summary>
        public bool FrictionActive;

        /// <summary>
        /// Weather intensity (0-1).
        /// 0 = calm, 1 = severe weather.
        /// Affects temperature, solar penetration, and wind effects.
        /// </summary>
        public float WeatherIntensity;

        /// <summary>
        /// Temperature offset from weather conditions in Kelvin.
        /// Weather can cool (negative offset) or heat (positive offset) the environment.
        /// </summary>
        public float WeatherTemperatureOffset;

        /// <summary>
        /// Array of additional heat sources beyond the main sun.
        /// Used for localized heat sources like reactors, engines, or artificial lighting.
        /// </summary>
        public HeatSourceState[] HeatSources;

        /// <summary>
        /// Number of valid heat sources in the HeatSources array.
        /// </summary>
        public int HeatSourceCount;


        /// <summary>
        /// Creates an environment state representing vacuum conditions.
        /// No atmosphere, no convection, only radiative heat transfer.
        /// </summary>
        /// <param name="vacuumTemperature">Temperature of deep space in Kelvin.</param>
        /// <returns>EnvironmentState configured for vacuum (2.7K cosmic background).</returns>
        public static EnvironmentState Vacuum(float vacuumTemperature)
        {
            EnvironmentState s = new EnvironmentState();
            s.SetAmbient(vacuumTemperature);
            s.SunDirectionLocal = Vector3.Zero;
            s.WindDirectionLocal = Vector3.Zero;
            s.IsSolarOccluded = true;
            return s;
        }


        /// <summary>
        /// Sets the ambient temperature and precomputes T⁴ for radiation calculations.
        /// </summary>
        /// <param name="kelvin">Ambient temperature in Kelvin.</param>
        /// <remarks>
        /// Precomputes AmbientTemperaturePow4 = kelvin⁴
        /// Using: squared = kelvin², then AmbientTemperaturePow4 = squared²
        /// This avoids expensive exponentiation in the heat balance equation.
        /// </remarks>
        public void SetAmbient(float kelvin)
        {
            AmbientTemperature = kelvin;
            float squared = kelvin * kelvin;
            AmbientTemperaturePow4 = squared * squared;
        }
    }
}
