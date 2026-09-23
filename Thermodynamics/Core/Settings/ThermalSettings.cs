using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public class ThermalSettings
    {
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;


        public bool EnableEnvironment = true;

        public bool EnableConduction = true;

        public bool EnableRadiation = true;

        public bool EnableConvection = true;

        public bool EnableSolarHeat = true;

        public bool SolarSelfShadowing = true;

        public bool EnableHeatSources = true;

        public bool EnableWasteHeat = true;

        public bool EnablePlanets = true;

        public bool EnableFriction = true;

        public bool EnableDamage = true;

        public bool EnableSuitDamage = true;

        public bool EnableCoolantLoops = true;

        public bool WellMixedCoolant = false;

        public bool EnableRoomAir = true;

        public bool EnableHeatPumps = true;


        public int Frequency = 4;

        public float SimulationSpeed = 1f;

        public float HeatTimeScale = 90f;


        public float VacuumTemperature = 2.7f;

        public float SolarEnergy = 1000f;

        public float FrictionAtSpeedsAbove = 0f;

        public float FrictionScale = 0.001f;

        public bool EnableDrag = true;

        public bool EnableWindwardShielding = false;

        public bool EnableShapeDrag = true;

        public bool EnableLift = true;

        public float LiftCoefficient = 1f;

        public float DragCoefficient = 1.54f;


        public float RoomConvectionCoefficient = 8f;

        public float RoomAirDensity = 1.225f;


        public float HeatPumpCarnotFraction = 0.4f;

        public float HeatPumpMaxCoefficient = 8f;


        public float SuitConductance = 2.5f;

        public float SuitHeatCapacity = 240000f;

        public const float MinimumSuitHeatCapacity = 1f;

        public float SuitCoolingWatts = 500f;

        public float SuitCriticalTemperature = 315.15f;

        public float SuitDamagePerKelvin = 1f;


        public bool ClampConductionOvershoot = true;

        public bool ClampEnvironmentOvershoot = true;

        public int MaxSubsteps = 64;

        public int MaxElementVisitsPerStep = 4000000;

        public const int NodeCostInLinks = 4;

        public int MaxSubstepsPerBlock = 0;

        public bool FloorBlocksWhenOverBudget = false;

        public bool DamageIsPerSecond = true;


        public float StepSeconds { get; private set; }

        public float StepsPerSecond { get; private set; }

        public int Revision { get; private set; }


        public ThermalSettings()
        {
            Derive();
        }


        public ThermalSettings Derive()
        {
            if (Frequency < 1) Frequency = 1;
            if (SimulationSpeed <= 0f) SimulationSpeed = 1f;
            if (HeatTimeScale <= 0f) HeatTimeScale = 1f;
            if (VacuumTemperature < 0f) VacuumTemperature = 0f;
            if (SolarEnergy < 0f) SolarEnergy = 0f;
            if (FrictionAtSpeedsAbove < 0f) FrictionAtSpeedsAbove = 0f;
            if (DragCoefficient < 0f) DragCoefficient = 0f;
            if (LiftCoefficient < 0f) LiftCoefficient = 0f;
            if (FrictionScale < 0f) FrictionScale = 0f;
            if (RoomConvectionCoefficient < 0f) RoomConvectionCoefficient = 0f;
            if (RoomAirDensity < 0f) RoomAirDensity = 0f;
            if (HeatPumpCarnotFraction < 0f) HeatPumpCarnotFraction = 0f;
            if (HeatPumpCarnotFraction > 1f) HeatPumpCarnotFraction = 1f;
            if (HeatPumpMaxCoefficient < 0f) HeatPumpMaxCoefficient = 0f;

            if (SuitConductance < 0f) SuitConductance = 0f;
            if (SuitHeatCapacity <= 0f) SuitHeatCapacity = MinimumSuitHeatCapacity;
            if (SuitCoolingWatts < 0f) SuitCoolingWatts = 0f;
            if (SuitCriticalTemperature < 0f) SuitCriticalTemperature = 0f;
            if (SuitDamagePerKelvin < 0f) SuitDamagePerKelvin = 0f;
            if (MaxElementVisitsPerStep < 0) MaxElementVisitsPerStep = 0;
            if (MaxSubsteps < 1) MaxSubsteps = 1;
            if (MaxSubstepsPerBlock < 0) MaxSubstepsPerBlock = 0;

            StepSeconds = 1f / Frequency;
            StepsPerSecond = Frequency * SimulationSpeed;
            Revision++;
            return this;
        }


        public List<string> Validate()
        {

            List<string> problems = new List<string>();
            if (Frequency < 1) problems.Add("Frequency must be at least 1.");
            if (Frequency > 60) problems.Add("Frequency above 60 costs more than one step per render frame.");
            if (SimulationSpeed <= 0f) problems.Add("SimulationSpeed must be positive.");
            if (HeatTimeScale <= 0f) problems.Add("HeatTimeScale must be positive.");
            float perSubstep = Frequency < 1 ? HeatTimeScale : HeatTimeScale / Frequency;
            if (perSubstep > 4000f)
            {
                problems.Add("HeatTimeScale / Frequency is " + perSubstep.ToString("n0")
                    + "; above about 4000 the overshoot clamps carry the whole step and blocks "
                    + "can be driven to the ambient floor. Raise Frequency or lower HeatTimeScale.");
            }
            if (VacuumTemperature < 0f) problems.Add("VacuumTemperature cannot be negative.");
            if (SuitCriticalTemperature > 0f && SuitCriticalTemperature <= SuitThermal.ComfortKelvin)
            {
                problems.Add("SuitCriticalTemperature is at or below the temperature the suit holds "
                    + "its occupant at, so a player is being damaged even when the suit is working.");
            }
            return problems;
        }


        public ThermalSettings Clone()
        {
            return (ThermalSettings)MemberwiseClone();
        }
    }
}
