using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Every runtime-tunable value for the simulation. Pure data: serialisation belongs to the
    /// host, so this type carries no XML or ProtoBuf attributes and performs no file IO.
    /// </summary>
    public class ThermalSettings
    {
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;

        // ---- feature switches -------------------------------------------------------------
        // Read only by the stage each belongs to, so switching one off removes exactly its cost.

        /// <summary>Master switch for radiation and convection against the environment.</summary>
        public bool EnableEnvironment = true;

        /// <summary>Conduction between touching blocks.</summary>
        public bool EnableConduction = true;

        /// <summary>Radiative exchange between exposed faces and the ambient sky.</summary>
        public bool EnableRadiation = true;

        /// <summary>Convective exchange between exposed faces and the surrounding air.</summary>
        public bool EnableConvection = true;

        /// <summary>Solar gain.</summary>
        public bool EnableSolarHeat = true;

        /// <summary>
        /// Whether a grid shadows itself, through a per-grid <see cref="SunShadowMap"/>.
        /// See configuration.md, What self-shadowing costs.
        /// </summary>
        public bool SolarSelfShadowing = true;

        /// <summary>Gain from mod-registered point heat sources.</summary>
        public bool EnableHeatSources = true;

        /// <summary>Waste heat from power production, power draw and thrust.</summary>
        public bool EnableWasteHeat = true;

        /// <summary>Planetary climate. When off, ambient is always <see cref="VacuumTemperature"/>.</summary>
        public bool EnablePlanets = true;

        /// <summary>Aerodynamic heating at speed in atmosphere.</summary>
        public bool EnableFriction = true;

        /// <summary>Damage above a block's critical temperature.</summary>
        public bool EnableDamage = true;

        /// <summary>
        /// Heat as something that can hurt a player, not only a block. Off leaves the suit
        /// unsimulated and costs nothing (`C7`).
        /// </summary>
        public bool EnableSuitDamage = true;

        /// <summary>Coolant loop heat transport.</summary>
        public bool EnableCoolantLoops = true;

        /// <summary>
        /// A loop's fluid as one well-mixed mass rather than one parcel per pipe.
        /// See thermal-model.md, Coolant loops.
        /// </summary>
        public bool WellMixedCoolant = false;

        /// <summary>
        /// Sealed rooms hold an air mass that carries heat between the surfaces facing it. When
        /// off, a sealed face exchanges nothing.
        /// </summary>
        public bool EnableRoomAir = true;

        /// <summary>
        /// Heat pumps move heat against a gradient for an electrical cost. When off they behave as
        /// ordinary blocks.
        /// </summary>
        public bool EnableHeatPumps = true;

        // ---- rates ------------------------------------------------------------------------

        /// <summary>
        /// Solver steps per simulated second; the integration step is 1/Frequency.
        /// See configuration.md, Frequency is not the cost dial it looks like.
        /// </summary>
        public int Frequency = 4;

        /// <summary>
        /// Multiplier on how fast heat evolves relative to real time. Applied by running more steps
        /// per second rather than by lengthening the step, so accuracy is unaffected and cost rises
        /// in proportion.
        /// </summary>
        public float SimulationSpeed = 1f;

        /// <summary>
        /// Factor by which thermal time runs faster than real physics; divides every heat capacity.
        /// 1 is fully physical. See configuration.md, Time and pace.
        /// </summary>
        public float HeatTimeScale = 90f;

        // ---- environment ------------------------------------------------------------------

        /// <summary>Cosmic microwave background, K.</summary>
        public float VacuumTemperature = 2.7f;

        /// <summary>Solar irradiance above the atmosphere, W/m^2.</summary>
        public float SolarEnergy = 1000f;

        /// <summary>Relative airspeed, m/s, above which aerodynamic heating starts.</summary>
        public float FrictionAtSpeedsAbove = 50f;

        /// <summary>Coefficient on the v^3 aerodynamic heating term.</summary>
        public float FrictionScale = 0.001f;

        // ---- room air ---------------------------------------------------------------------

        /// <summary>
        /// Convective coefficient between a room's air and the surfaces facing it, W/(m^2 K). Lower
        /// than the planetary figure because room air is still.
        /// </summary>
        public float RoomConvectionCoefficient = 8f;

        /// <summary>
        /// Air density inside a fully pressurised room, kg/m^3. Defaults to Earth sea level; the
        /// game's breathable atmospheres are close enough that one figure serves.
        /// </summary>
        public float RoomAirDensity = 1.225f;

        // ---- heat pumps -------------------------------------------------------------------

        /// <summary>
        /// Fraction of the Carnot limit a heat pump achieves, 0..1, and the block's whole balance.
        /// See configuration.md, Heat pumps.
        /// </summary>
        public float HeatPumpCarnotFraction = 0.4f;

        /// <summary>
        /// Ceiling on the coefficient of performance, which Carnot's figure has none of.
        /// See configuration.md, Heat pumps.
        /// </summary>
        public float HeatPumpMaxCoefficient = 8f;

        // ---- the suit ---------------------------------------------------------------------

        /// <summary>
        /// How well the environment reaches the occupant through a sealed suit, W/K. With the
        /// cooling rating this sets the hottest room a player can stand in indefinitely; see
        /// <see cref="SuitThermal.SurvivableKelvin"/>.
        /// </summary>
        public float SuitConductance = 2.5f;

        /// <summary>
        /// Heat capacity of the occupant and the suit together, J/K — about eighty kilograms of
        /// mostly water. Divided by <see cref="HeatTimeScale"/> like every block's, so a player
        /// heats on the same clock as the ship around them.
        /// </summary>
        public float SuitHeatCapacity = 240000f;

        /// <summary>Heat the suit can move, either way, W.</summary>
        public float SuitCoolingWatts = 500f;

        /// <summary>
        /// Interior temperature above which the occupant is being hurt, K. 42 C: the core body
        /// temperature at which heat stroke becomes life-threatening, and only a few degrees above
        /// where the suit holds them.
        /// </summary>
        public float SuitCriticalTemperature = 315.15f;

        /// <summary>Hit points a second per kelvin above <see cref="SuitCriticalTemperature"/>.</summary>
        public float SuitDamagePerKelvin = 1f;

        // ---- solver -----------------------------------------------------------------------

        /// <summary>
        /// Clamp each conduction exchange so a node cannot overshoot the temperature it is
        /// exchanging with. Keeps the explicit integrator stable at low <see cref="Frequency"/> or
        /// with very light blocks. Disabling it leaves conduction unbounded.
        /// </summary>
        public bool ClampConductionOvershoot = true;

        /// <summary>
        /// Clamp radiation and convection at ambient, as <see cref="ClampConductionOvershoot"/> does
        /// for a pair of blocks. See profiles.md, Designing your own.
        /// </summary>
        public bool ClampEnvironmentOvershoot = true;

        /// <summary>
        /// Ceiling on the substeps the stability estimate may be granted. Reaching it is reported as
        /// a clamped step. See configuration.md, Solver.
        /// </summary>
        public int MaxSubsteps = 64;

        /// <summary>
        /// Most element visits one step may make — one per link, plus <see cref="NodeCostInLinks"/>
        /// per node — before the step is made shorter rather than coarser. Zero removes the bound.
        /// See configuration.md, Solver.
        /// </summary>
        /// <remarks>
        /// **This bounds a step, and smoothness is a property of a frame.** A frame does
        /// <c>budget * frameSeconds * StepsPerSecond</c> of work, so the two are related by
        /// <see cref="Frequency"/>: at half the step rate a step spans twice as many frames and the
        /// same budget costs half as much per frame. The figure moved with <c>Frequency</c> from 8 to
        /// 4 for that reason — 2,000,000 at four steps a second is the per-frame cost 1,000,000 was at
        /// eight, and it keeps the bound off the grids it was never meant to reach.
        /// </remarks>
        public int MaxElementVisitsPerStep = 2000000;

        /// <summary>
        /// What one node is worth, in links, when a step's cost is counted. Measured rather than
        /// chosen: see benchmarks.md, What a substep costs.
        /// </summary>
        public const int NodeCostInLinks = 4;

        /// <summary>
        /// Most substeps any single block may demand of the whole grid before its integration
        /// capacity is floored. Zero leaves every block's real capacity in place.
        /// See stiffness.md, A per-block substep cap.
        /// </summary>
        public int MaxSubstepsPerBlock = 0;

        /// <summary>
        /// When true, damage per second is <c>(T - critical) * OverheatDamagePerKelvin</c>. When
        /// false, that figure is applied per solver step, making total damage scale with
        /// <see cref="Frequency"/>.
        /// </summary>
        public bool DamageIsPerSecond = true;

        // ---- derived ----------------------------------------------------------------------

        /// <summary>Simulated seconds advanced by one solver step. Derived from Frequency.</summary>
        public float StepSeconds { get; private set; }

        /// <summary>Solver steps executed per real second. Derived.</summary>
        public float StepsPerSecond { get; private set; }

        /// <summary>
        /// Incremented by every <see cref="Derive"/>. A simulation compares it against the revision
        /// it last acted on, so a mid-session change is picked up on the next step: heat capacities
        /// are rescaled, coolant loops rebuilt or dropped, and room air re-derived.
        /// </summary>
        public int Revision { get; private set; }

        public ThermalSettings()
        {
            Derive();
        }

        /// <summary>
        /// Clamps out-of-range values, recomputes the derived fields and bumps
        /// <see cref="Revision"/>. Must be called after loading or mutating settings.
        /// </summary>
        public ThermalSettings Derive()
        {
            if (Frequency < 1) Frequency = 1;
            if (SimulationSpeed <= 0f) SimulationSpeed = 1f;
            if (HeatTimeScale <= 0f) HeatTimeScale = 1f;
            if (VacuumTemperature < 0f) VacuumTemperature = 0f;
            if (SolarEnergy < 0f) SolarEnergy = 0f;
            if (FrictionAtSpeedsAbove < 0f) FrictionAtSpeedsAbove = 0f;
            if (FrictionScale < 0f) FrictionScale = 0f;
            if (RoomConvectionCoefficient < 0f) RoomConvectionCoefficient = 0f;
            if (RoomAirDensity < 0f) RoomAirDensity = 0f;
            if (HeatPumpCarnotFraction < 0f) HeatPumpCarnotFraction = 0f;
            if (HeatPumpCarnotFraction > 1f) HeatPumpCarnotFraction = 1f;
            if (HeatPumpMaxCoefficient < 0f) HeatPumpMaxCoefficient = 0f;

            if (SuitConductance < 0f) SuitConductance = 0f;
            if (SuitHeatCapacity <= 0f) SuitHeatCapacity = ThermalConstants.MinimumThermalMass;
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

        /// <summary>Human-readable problems with the current values. Empty when healthy.</summary>
        public List<string> Validate()
        {
            List<string> problems = new List<string>();
            if (Frequency < 1) problems.Add("Frequency must be at least 1.");
            if (Frequency > 60) problems.Add("Frequency above 60 costs more than one step per render frame.");
            if (SimulationSpeed <= 0f) problems.Add("SimulationSpeed must be positive.");
            if (HeatTimeScale <= 0f) problems.Add("HeatTimeScale must be positive.");
            // Clamping itself is not a fault: the fast profiles rely on it and both halves of the
            // exchange are bounded. What does bite is a substep long enough that the clamps carry
            // the whole step, which is HeatTimeScale divided by Frequency. Measurement puts the
            // edge near 4000: the arcade profile sits at 3,333 and is stable over an hour, while
            // the same transfer at Frequency 2 gives 10,000 and drives blocks to the ambient floor.
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
