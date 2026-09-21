using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public struct HeatSourceState
    {
        public Vector3 DirectionLocal;

        public float Irradiance;

/// <summary>HeatSourceState operation.</summary>
        public HeatSourceState(Vector3 directionLocal, float irradiance)
        {
            DirectionLocal = directionLocal;
            Irradiance = irradiance;
        }
    }

    public struct EnvironmentState
    {
        public float AmbientTemperature;

        public float AmbientTemperaturePow4;

        public float AirDensity;

        public float AtmosphereFactor;

        public float ConvectionCoefficient;

        public float EffectiveConvectionCoefficient
        {
            get { return ConvectionCoefficient * AtmosphereFactor; }
        }

        public float SolarEnergy;

        public Vector3 SunDirectionLocal;

        public bool IsSolarOccluded;

        public float SolarOcclusion;

        public Vector3 WindDirectionLocal;

        public float WindSpeed;

        public bool FrictionActive;

        public float WeatherIntensity;

        public float WeatherTemperatureOffset;

        public HeatSourceState[] HeatSources;

        public int HeatSourceCount;

/// <summary>Vacuum operation.</summary>
        public static EnvironmentState Vacuum(float vacuumTemperature)
        {
/// <summary>EnvironmentState operation.</summary>
            EnvironmentState s = new EnvironmentState();
            s.SetAmbient(vacuumTemperature);
            s.SunDirectionLocal = Vector3.Zero;
            s.WindDirectionLocal = Vector3.Zero;
            s.IsSolarOccluded = true;
            return s;
        }

/// <summary>Sets the ambient.</summary>
        public void SetAmbient(float kelvin)
        {
            AmbientTemperature = kelvin;
            float squared = kelvin * kelvin;
            AmbientTemperaturePow4 = squared * squared;
        }
    }
}
