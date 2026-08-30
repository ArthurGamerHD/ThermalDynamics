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

        /// <summary>
        /// Whether the drag the friction term already computes is taken out of the ship's motion.
        ///
        /// <para>
        /// **Off, and not because it is unfinished.** Two mods that both slow a ship down is
        /// `B38` with a name on it, and the mod's answer to *another mod is doing this too* is a
        /// switch rather than a detection (`C7`). A world running an aerodynamics mod turns this
        /// off and keeps the heat; a world running only this one turns it on.
        /// </para>
        /// </summary>
        public bool EnableDrag = false;

        /// <summary>
        /// Whether a block behind another is sheltered from the wind, for heat and for drag.
        ///
        /// <para>
        /// **The same self-shadowing pass the sun uses, aimed at the relative wind.** Off by
        /// default because it is a second sliced pass and a second six-floats-a-node array — 3 MB
        /// on a 126,731-block hull — and because without it the model's answer is the conservative
        /// one: every face is treated as being in the open, so a hull is heated and dragged at
        /// least as much as it should be.
        /// </para>
        /// </summary>
        public bool EnableWindwardShielding = false;

        /// <summary>
        /// The drag coefficient a hull is treated as having, dimensionless.
        ///
        /// <para>
        /// **A different number from <see cref="FrictionScale"/>, and that is the whole of `K3`.**
        /// The two are one product — `FrictionScale = ½ · C_d · η`, where `η` is the share of the
        /// work done against drag that lands in the surface rather than the wake — so authoring
        /// both leaves `η` derived, which at these defaults is 0.002. Authoring the *heat* dial and
        /// deriving the force from it instead would mean a world that tuned its temperatures
        /// silently re-tuned its handling.
        /// </para>
        ///
        /// <para>
        /// **Authored rather than derived from the hull, which is measured.** The solver's windward
        /// term is a projected area, and a projected area is not a shape: a brick and a
        /// stair-stepped wedge sharing a frontal cross-section compute the *same* drag here while
        /// their real coefficients differ by about ten times (`DragShapeTests`). Deriving this
        /// needs a shape term the model does not have, which is `K6` and `K7`.
        /// </para>
        ///
        /// <para>
        /// **The default is 0.5 and was measured rather than reasoned to.** A bluff body's own
        /// coefficient is about 1, and at 1 this fails `K5` on the population: drag at 100 m/s in
        /// sea-level air beats the ship's own thrust on **14.06 %** of hulls that can lift
        /// themselves, and the worst percentile of them cannot hold **55.7 m/s** — against a
        /// criterion of 5 % and 60 m/s registered before the walk. At 0.5 it passes both, 3.13 %
        /// and 78.8 m/s.
        ///
        /// <para>
        /// **And half is the physically expected place for it to land.** What multiplies this is a
        /// *Newtonian flat-plate* projection: every exposed face, weighted by its incidence, with no
        /// wake and no pressure recovery behind the hull. That over-predicts a real bluff body's
        /// drag at the speeds a ship actually flies, so the coefficient that matches reality is
        /// below the one an aerodynamicist would quote for the shape.
        /// </para>
        /// </summary>
        public float DragCoefficient = 0.5f;

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
        /// <para>
        /// **This bounds a step, and smoothness is a property of a frame.** A frame does
        /// <c>budget * frameSeconds * StepsPerSecond</c> of work, so the two are related by
        /// <see cref="Frequency"/>: at half the step rate a step spans twice as many frames and the
        /// same budget costs half as much per frame. At 4,000,000 and <c>Frequency</c> 4 a frame is
        /// bounded at 266,667 element visits, which is the only quantity this setting controls.
        /// </para>
        /// <para>
        /// **4,000,000 since 2026-08-24, from 2,000,000, and what moved was the fidelity it costs
        /// rather than the milliseconds it saves.** Measured: a hull that reaches the bound is not
        /// made less accurate — each step is as faithful as it was and there are fewer of them, so
        /// its thermal clock runs slow, worth 4.4 K standing on a 16,558-block hull in flight at the
        /// old value and more than 37 K at 32,800. Against 0.028 K for the substep ceiling this
        /// world accepts and 0.607 K for the per-block cap it refuses to ship as a default, that
        /// made this the largest approximation the mod shipped. See benchmarks.md, What the
        /// allowance is worth, and backlog.md `C27`.
        /// </para>
        /// </remarks>
        public int MaxElementVisitsPerStep = 4000000;

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
        /// When a grid cannot afford the substeps its demand asks for, floor its stiffest blocks to
        /// what <see cref="MaxElementVisitsPerStep"/> grants instead of shortening its step.
        ///
        /// <para>
        /// The allowance bounds a step's work by making the step *shorter*, so an over-budget grid
        /// advances less simulated time per real second — a loss of the whole clock, with no bound
        /// on it. Flooring the stiffest blocks lowers the demand instead, so the step stays whole
        /// and the cost is a bounded error on the blocks it re-masses. See configuration.md, FloorBlocksWhenOverBudget.
        /// </para>
        ///
        /// <para>
        /// **Not <see cref="MaxSubstepsPerBlock"/> under another name.** That reaches every hull;
        /// this engages per grid and per step, only where the budget binds, and the cap it applies
        /// is what that grid can afford rather than a number chosen in advance.
        /// </para>
        /// </summary>
        public bool FloorBlocksWhenOverBudget = false;

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
            if (DragCoefficient < 0f) DragCoefficient = 0f;
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
