using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Every runtime-tunable value for the simulation. Pure data — the host is responsible for
    /// serialising it, so this type carries no XML or ProtoBuf attributes and no file IO.
    /// </summary>
    public class ThermalSettings
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;

        // ---- feature switches -------------------------------------------------------------

        /// <summary>Radiation and convection against the environment.</summary>
        public bool EnableEnvironment = true;

        /// <summary>Solar gain.</summary>
        public bool EnableSolarHeat = true;

        /// <summary>Planetary climate. When off, ambient is always <see cref="VacuumTemperature"/>.</summary>
        public bool EnablePlanets = true;

        /// <summary>Aerodynamic heating at speed in atmosphere.</summary>
        public bool EnableFriction = true;

        /// <summary>Damage above a block's critical temperature.</summary>
        public bool EnableDamage = true;

        /// <summary>Coolant loop heat transport.</summary>
        public bool EnableCoolantLoops = true;

        // ---- rates ------------------------------------------------------------------------

        /// <summary>
        /// Simulation steps per simulated second. Sets the integration step to 1/Frequency.
        /// Higher is more accurate and more expensive.
        /// </summary>
        public int Frequency = 4;

        /// <summary>
        /// How much faster than real time heat evolves. Applied by running more steps per
        /// second, not by lengthening the step, so accuracy is unaffected.
        /// </summary>
        public float SimulationSpeed = 1f;

        // ---- environment ------------------------------------------------------------------

        /// <summary>Cosmic microwave background, K.</summary>
        public float VacuumTemperature = 2.7f;

        /// <summary>Solar irradiance above the atmosphere, W/m^2.</summary>
        public float SolarEnergy = 1000f;

        /// <summary>Relative airspeed, m/s, above which aerodynamic heating starts.</summary>
        public float FrictionAtSpeedsAbove = 50f;

        /// <summary>Coefficient on the v^3 aerodynamic heating term.</summary>
        public float FrictionScale = 0.001f;

        // ---- solver -----------------------------------------------------------------------

        /// <summary>
        /// Clamp each conduction exchange so a node can never overshoot the temperature it is
        /// exchanging with. Keeps the explicit integrator stable at low
        /// <see cref="Frequency"/> or with very light blocks. Disabling it reproduces the
        /// unbounded behaviour of the original solver.
        /// </summary>
        public bool ClampConductionOvershoot = true;

        /// <summary>
        /// Damage per second is <c>(T - critical) * CriticalTemperatureScaler</c>. When false,
        /// damage is applied per solver step instead, which makes damage scale with
        /// <see cref="Frequency"/> — the original behaviour.
        /// </summary>
        public bool DamageIsPerSecond = true;

        // ---- derived ----------------------------------------------------------------------

        /// <summary>Simulated seconds advanced by one solver step. Derived from Frequency.</summary>
        public float StepSeconds { get; private set; }

        /// <summary>Solver steps executed per real second. Derived.</summary>
        public float StepsPerSecond { get; private set; }

        public ThermalSettings()
        {
            Derive();
        }

        /// <summary>
        /// Clamps out-of-range values and recomputes the derived fields. Must be called after
        /// loading or mutating settings.
        /// </summary>
        public ThermalSettings Derive()
        {
            if (Frequency < 1) Frequency = 1;
            if (SimulationSpeed <= 0f) SimulationSpeed = 1f;
            if (VacuumTemperature < 0f) VacuumTemperature = 0f;
            if (SolarEnergy < 0f) SolarEnergy = 0f;
            if (FrictionAtSpeedsAbove < 0f) FrictionAtSpeedsAbove = 0f;
            if (FrictionScale < 0f) FrictionScale = 0f;

            StepSeconds = 1f / Frequency;
            StepsPerSecond = Frequency * SimulationSpeed;
            return this;
        }

        /// <summary>Human-readable problems with the current values. Empty when healthy.</summary>
        public List<string> Validate()
        {
            List<string> problems = new List<string>();
            if (Frequency < 1) problems.Add("Frequency must be at least 1.");
            if (Frequency > 60) problems.Add("Frequency above 60 costs more than one step per render frame.");
            if (SimulationSpeed <= 0f) problems.Add("SimulationSpeed must be positive.");
            if (VacuumTemperature < 0f) problems.Add("VacuumTemperature cannot be negative.");
            return problems;
        }

        public ThermalSettings Clone()
        {
            return (ThermalSettings)MemberwiseClone();
        }
    }
}
