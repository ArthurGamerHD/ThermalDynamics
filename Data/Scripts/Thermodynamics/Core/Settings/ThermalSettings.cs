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
        //
        // Each mechanism is independently switchable and each switch takes effect on the next step,
        // so a server can disable one without a reload. A switch is read only by the stage it
        // belongs to, so disabling it removes exactly that stage's cost.

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
        /// When off, a face is lit whenever it points at the sun regardless of what the grid has
        /// built in front of it. When on, a <see cref="SunShadowMap"/> is kept per grid and a face
        /// takes sunlight only if no part of the grid stands between it and the sun.
        ///
        /// Costs one pass over the grid's cells whenever the sun has moved appreciably, so it
        /// scales with grid size rather than step rate.
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
        /// When true, a loop's fluid is one well-mixed mass instead of one parcel per pipe.
        ///
        /// The well-mixed model is the older one. It is cheaper — one temperature and one integration
        /// per ring rather than one per pipe — and it is wrong in a way that matters for gameplay:
        /// heat crosses from a reactor to a radiator on the far side of the ship instantly, whether
        /// anything is circulating or not, so a pump's only possible effect is to exist. Segmented
        /// fluid is the default because a stopped pump ought to stop cooling.
        ///
        /// Kept switchable rather than deleted so the two can be measured against each other on the
        /// same grid, and so a very large station can buy back the difference if it ever needs to.
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
        /// Simulation steps per simulated second. Sets the integration step to 1/Frequency.
        /// Higher is more accurate and more expensive.
        /// </summary>
        public int Frequency = 4;

        /// <summary>
        /// Multiplier on how fast heat evolves relative to real time. Applied by running more steps
        /// per second rather than by lengthening the step, so accuracy is unaffected and cost rises
        /// in proportion.
        /// </summary>
        public float SimulationSpeed = 1f;

        /// <summary>
        /// Factor by which thermal time runs faster than real physics.
        ///
        /// Block definitions carry real specific heats in J/(kg K) — steel is about 450 — under
        /// which a hot hull takes hours to cool. Every heat capacity is divided by this value,
        /// which is equivalent to running thermal time at that multiple: all rates scale together,
        /// so equilibrium temperatures, the balance between conduction and radiation, and the
        /// ratios between block types are unchanged.
        ///
        /// The cost is that coupling per unit capacity rises with it, so the solver takes more
        /// substeps, and a high value on a grid of light blocks is what drives a step to clamp.
        /// Both are reported in the telemetry.
        ///
        /// 1 is fully physical. The shipped default of 225 divides steel's 450 J/(kg K) to 2.
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
        /// Fraction of the Carnot limit a heat pump achieves, 0..1. A real domestic heat pump
        /// manages about 0.4; 1 would be thermodynamically perfect.
        ///
        /// The primary balance lever for the block. It scales the coefficient of performance but
        /// not the shape of the Carnot cost curve.
        /// </summary>
        public float HeatPumpCarnotFraction = 0.4f;

        /// <summary>
        /// Ceiling on the coefficient of performance. The Carnot figure diverges as the two sides
        /// converge, so without a ceiling a pump across a negligible difference would lift
        /// unbounded heat; a real machine is compressor-limited well before that.
        /// </summary>
        public float HeatPumpMaxCoefficient = 8f;

        // ---- solver -----------------------------------------------------------------------

        /// <summary>
        /// Clamp each conduction exchange so a node cannot overshoot the temperature it is
        /// exchanging with. Keeps the explicit integrator stable at low <see cref="Frequency"/> or
        /// with very light blocks. Disabling it leaves conduction unbounded.
        /// </summary>
        public bool ClampConductionOvershoot = true;

        /// <summary>
        /// Clamp radiation and convection so a node cannot overshoot the ambient it exchanges with,
        /// as <see cref="ClampConductionOvershoot"/> does for a pair of blocks.
        ///
        /// Without it the integrator is only conditionally stable, on a condition the substep
        /// estimate is trusted to meet — but <see cref="MaxSubsteps"/> exists to refuse that
        /// estimate, and a clamped step then has an unguarded environment term. Measured at
        /// <c>HeatTimeScale 3600</c> with one substep, node temperatures diverged past 1e22 K
        /// while conduction, which had a clamp, stayed bounded.
        ///
        /// The cap is the same rule as the conduction one: within a substep a node cannot radiate
        /// past the temperature it radiates towards, since that is where the exchange reverses.
        /// Where substeps are generous it never binds. It is what makes the low-substep profiles
        /// usable.
        /// </summary>
        public bool ClampEnvironmentOvershoot = true;

        /// <summary>
        /// Most substeps one solver step may divide itself into.
        ///
        /// The stability estimate asks for as many as the stiffest node on the grid needs; this is
        /// the ceiling on granting that. Reaching it is reported as a clamped step. A clamped step
        /// is approximate rather than wrong: every exchange is still capped at the energy that
        /// equalises its pair, so the integrator stays bounded and conserves energy at any setting.
        /// What is lost is the shape of the curve between two temperatures, not the temperatures
        /// it settles between.
        ///
        /// A simulation-oriented world sets this high enough never to bind. A low value — down to
        /// one substep per step, every link equalising once — moves the most heat for the least
        /// arithmetic.
        /// </summary>
        public int MaxSubsteps = 16;

        /// <summary>
        /// Most element visits one solver step may make — substeps times the elements a substep
        /// touches — before the step is shortened to fit. Zero removes the bound. Trades
        /// simulation rate for frame smoothness.
        ///
        /// An element visit is one link, or one node weighted by <see cref="NodeCostInLinks"/>: a
        /// substep walks both, and a node's visit costs several times a link's. This was counted
        /// in link visits alone until it was measured, which under-charged a ship-shaped grid by
        /// two to four times and under-charged a sparsely linked one by more.
        ///
        /// A step's cost is its substep count times that, and the substep count is set by the
        /// stiffest node on the grid, which moves as the grid heats. Unbounded, that makes an
        /// otherwise steady large grid produce occasional steps several times the median cost.
        ///
        /// A step that would exceed the bound is made shorter rather than coarser. Coarsening the
        /// substeps would take steps too large for the grid's stiffness and rely on the overshoot
        /// clamps, losing accuracy. Shortening advances less simulated time at the same accuracy,
        /// so an oversized grid runs at a reduced rate smoothly rather than at full rate in bursts.
        ///
        /// The default is unchanged in value and tighter in effect, which is the point: on the
        /// three 42,051-block ships a field report measured at 11 substeps a step and 85–150 ms a
        /// tick, the same 1,000,000 now buys about 4 substeps. Grids below about a hundred
        /// thousand blocks still never reach it.
        /// </summary>
        public int MaxElementVisitsPerStep = 1000000;

        /// <summary>
        /// What one node is worth, in links, when a step's cost is counted.
        ///
        /// A substep visits every link once and every node once, and a node's visit is the more
        /// expensive of the two: the environment pass integrates radiation as a fourth power,
        /// convection and solar, and above about a hundred thousand blocks the node state stops
        /// fitting in cache while the link arrays go on streaming. Measured across shapes chosen
        /// for their link-to-node ratio, a node is worth 2.8 links at four thousand nodes, 3.3 at
        /// a hundred thousand and 7.5 at a quarter of a million — see
        /// [element-cost.md](../../../../docs/element-cost.md).
        ///
        /// Four is the low end of the range over the sizes where the budget binds at all. A grid
        /// past a quarter of a million blocks is therefore charged slightly less than it costs,
        /// which errs towards letting a large grid run rather than throttling it on a machine that
        /// could have kept up.
        /// </summary>
        public const int NodeCostInLinks = 4;

        /// <summary>
        /// Most substeps any single block may demand of the whole grid before its heat capacity is
        /// floored. Zero leaves every block's real capacity in place.
        ///
        /// <para>
        /// A step is divided into as many substeps as the stiffest node on the grid needs, and
        /// every other node pays for them. On a large hull that demand typically comes from a tiny
        /// minority of very light blocks: measured on a 42,051-block ship, forty-three 16 kg
        /// fittings at 32 J/K demanded twenty-eight substeps while the surrounding armour demanded
        /// one, holding the whole grid to 35 % of real time.
        /// </para>
        ///
        /// <para>
        /// Such a block reaches its neighbour's temperature in tens of milliseconds, so across a
        /// quarter-second step it does not carry an independent temperature. This raises its
        /// capacity to the least value that keeps its demand within the cap.
        /// </para>
        ///
        /// <para>
        /// The cost is that block's own transient: it warms and cools more slowly than its real
        /// mass would. Its steady state is unaffected, since steady state is where the watts cancel
        /// and does not depend on capacity, and no other block changes. The floor applies to
        /// conduction stiffness only; environment coupling can still demand more substeps, since
        /// that is a response to a real gradient rather than an artefact of block size.
        /// </para>
        ///
        /// <para>
        /// Expressed in substeps rather than kilograms so it keeps its meaning when
        /// <see cref="Frequency"/> changes: a shorter step lowers the floor and the cap holds.
        /// </para>
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
            return problems;
        }

        public ThermalSettings Clone()
        {
            return (ThermalSettings)MemberwiseClone();
        }
    }
}
