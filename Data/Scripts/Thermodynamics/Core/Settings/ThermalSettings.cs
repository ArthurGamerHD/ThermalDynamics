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
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;

        // ---- feature switches -------------------------------------------------------------
        //
        // Every mechanism is independently switchable and every switch takes effect on the next
        // step, so a server can turn one off without a reload. Nothing reads a switch except the
        // stage it belongs to, so switching one off removes exactly its own cost.

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
        /// Whether a grid shadows itself.
        ///
        /// Off, a face is lit whenever it points at the sun, whatever the ship has built in front
        /// of it — the inside of a doorway recess and a hull plate under an overhang both heat as
        /// if they were in the open. On, a <see cref="SunShadowMap"/> is kept per grid and a face
        /// only takes sunlight if nothing of the grid stands between it and the sun.
        ///
        /// The cost is one pass over the grid's cells whenever the sun has moved appreciably, so it
        /// scales with ship size rather than with step rate. Off is the cheap model and stays
        /// available for anyone who would rather spend nothing.
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

        /// <summary>Coolant loop heat transport.</summary>
        public bool EnableCoolantLoops = true;

        /// <summary>
        /// Sealed rooms hold an air mass that carries heat between the surfaces facing it.
        /// When off, a sealed face simply exchanges nothing, which is the behaviour before room
        /// air existed.
        /// </summary>
        public bool EnableRoomAir = true;

        /// <summary>
        /// Heat pumps move heat against a gradient for an electrical cost. When off they are
        /// ordinary blocks, which is what they were before the mechanism existed.
        /// </summary>
        public bool EnableHeatPumps = true;

        // ---- rates ------------------------------------------------------------------------

        /// <summary>
        /// Simulation steps per simulated second. Sets the integration step to 1/Frequency.
        /// Higher is more accurate and more expensive.
        /// </summary>
        public int Frequency = 4;

        /// <summary>
        /// How much faster than real time heat evolves. Applied by running more steps per
        /// second, not by lengthening the step, so accuracy is unaffected — and so the cost
        /// rises with it.
        /// </summary>
        public float SimulationSpeed = 1f;

        /// <summary>
        /// How many times faster than real physics heat moves.
        ///
        /// Specific heat in the block definitions is in real J/(kg K) — steel is about 450 —
        /// which makes a ship behave like a real ship: a hot hull takes hours to cool. That is
        /// accurate and unplayable, so the simulation divides every heat capacity by this one
        /// number. Dividing capacity by <em>k</em> is exactly running thermal time at
        /// <em>k</em>×: every rate scales together, so equilibrium temperatures, the balance
        /// between conduction and radiation, and every ratio between block types are unchanged.
        /// Only the clock moves.
        ///
        /// This is deliberately the single place the mod trades physics for pace. It is not
        /// free: coupling per unit capacity rises with it, so the solver takes more substeps,
        /// and a large value on a grid with very light blocks is what makes a step clamp. The
        /// telemetry report shows both.
        ///
        /// 1 is fully physical. 225 is the pace the mod shipped with: steel's real 450 J/(kg K)
        /// divided by 225 is the flat "2" the definitions used to carry.
        /// </summary>
        public float HeatTimeScale = 225f;

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
        /// Convective coefficient between a room's air and the surfaces facing it, W/(m^2 K).
        /// Lower than the planetary figure: room air is still, and still air convects poorly.
        /// </summary>
        public float RoomConvectionCoefficient = 8f;

        /// <summary>
        /// Air density inside a fully pressurised room, kg/m^3. Sea level on Earth is 1.225;
        /// the game's breathable atmospheres are close enough that one figure serves.
        /// </summary>
        public float RoomAirDensity = 1.225f;

        // ---- heat pumps -------------------------------------------------------------------

        /// <summary>
        /// How much of the Carnot limit a heat pump achieves, 0..1. A real domestic heat pump
        /// manages about 0.4 of it; 1 would be a thermodynamically perfect machine.
        ///
        /// This is the whole balance of the block in one number. It does not change what the pump
        /// can do — the shape of the cost curve is Carnot's and is not negotiable — only how far
        /// up it the block sits.
        /// </summary>
        public float HeatPumpCarnotFraction = 0.4f;

        /// <summary>
        /// Ceiling on the coefficient of performance, so a pump working across almost no
        /// difference cannot lift unbounded heat for nothing. Carnot's figure goes to infinity as
        /// the two sides converge; a real machine is limited by its compressor long before that.
        /// </summary>
        public float HeatPumpMaxCoefficient = 8f;

        // ---- solver -----------------------------------------------------------------------

        /// <summary>
        /// Clamp each conduction exchange so a node can never overshoot the temperature it is
        /// exchanging with. Keeps the explicit integrator stable at low
        /// <see cref="Frequency"/> or with very light blocks. Disabling it reproduces the
        /// unbounded behaviour of the original solver.
        /// </summary>
        public bool ClampConductionOvershoot = true;

        /// <summary>
        /// Most link visits one solver step may make — substeps times links — before the step is
        /// shortened to fit. Zero removes the bound.
        ///
        /// This is the setting that trades simulation rate for smoothness, and it is the one that
        /// makes a very large grid playable.
        ///
        /// A step's cost is not its length but its substep count times its links, and the substep
        /// count is set by the stiffest node on the grid, which changes as the grid heats. On a
        /// large ship that produced a step costing fifteen milliseconds most of the time and
        /// seventy occasionally, from the same grid doing the same thing — a five-fold spike with
        /// no cause a player could see or avoid.
        ///
        /// When a step would exceed this, the step is made <em>shorter</em> rather than its
        /// substeps coarser. That distinction is the whole point. Coarsening substeps takes steps
        /// too large for the stiffness and leans on the overshoot clamp to stay bounded, which is
        /// an accuracy loss. Shortening the step advances less simulated time at exactly the same
        /// accuracy: heat moves more slowly, and nothing else about it changes. A grid too large
        /// to simulate at full rate therefore runs at a lower rate smoothly, rather than at full
        /// rate in lurches.
        ///
        /// The default is about one 60 fps frame's worth of link visits at the measured cost per
        /// visit. Grids below roughly a hundred thousand blocks never reach it and are unaffected.
        /// </summary>
        public int MaxLinkVisitsPerStep = 1000000;

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

        /// <summary>
        /// Bumped by every <see cref="Derive"/>. A simulation compares it against the revision it
        /// last acted on, so a setting changed mid-session is picked up on the next step: heat
        /// capacities are rescaled, coolant loops are rebuilt or dropped, and room air is
        /// re-derived. Without it a live edit would apply to some mechanisms and not others.
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
            if (MaxLinkVisitsPerStep < 0) MaxLinkVisitsPerStep = 0;

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
            if (HeatTimeScale > 10000f) problems.Add("HeatTimeScale above 10000 will make most grids clamp.");
            if (VacuumTemperature < 0f) problems.Add("VacuumTemperature cannot be negative.");
            return problems;
        }

        public ThermalSettings Clone()
        {
            return (ThermalSettings)MemberwiseClone();
        }
    }
}
