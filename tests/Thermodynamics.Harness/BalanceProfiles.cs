using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **How physically true the model is**, as a simulated environment rather than as a setting — the
    /// axis the mod itself has no dial for, because <c>HeatTimeScale</c> makes it deliberately
    /// ninety times faster than the world and nothing shipped can turn that off.
    ///
    /// <para>
    /// This carries every knob that moves the balance, not just the integration ones: the two pace
    /// scales, the environment constants, which mechanisms run, how the coolant is modelled and what
    /// a reactor's waste heat is. **Nothing here changes the shipped configuration** — the conduction
    /// pace is expressed by scaling the materials the harness builds with, so the core needs no new
    /// setting to be measured against.
    /// </para>
    ///
    /// <para>
    /// This is the only thing in the repository still called a profile. The five shipped presets are
    /// gone; there is one configuration, and <see cref="Shipped"/> reads it rather than restating it.
    /// </para>
    /// </summary>
    public class BalanceProfile
    {
        public string Name;
        public string Intent;

        // ---- the two pace scales ------------------------------------------------------------

        /// <summary>
        /// Divides every heat capacity. 1 is the world; the mod ships 90, and shipped 225 until
        /// `C24`.
        ///
        /// The single largest departure from physics in the whole model, and the one that makes it
        /// a game: real steel at real specific heat gives a large-grid armour block a time constant
        /// of about half an hour.
        ///
        /// **The value here is a field default and not a claim about what ships** —
        /// <see cref="Shipped"/> and <see cref="Candidate"/> read the real defaults, and every other
        /// profile states its own. It is the shipped figure anyway, so that a profile added later
        /// and left unset lands on the world rather than on a retired one.
        /// </summary>
        public float HeatTimeScale = 90f;

        /// <summary>
        /// Multiplies every block's real conductivity. 1 is the world; the mod ships 9.6, and
        /// shipped 2.4 until `C24`.
        ///
        /// Applied here by scaling the material figures the harness builds blocks from, so no core
        /// setting has to exist for this to be measured.
        /// </summary>
        public float ConductionPace = ThermalConstants.ConductionScale;

        // ---- integration ---------------------------------------------------------------------

        /// <summary>
        /// Solver steps a second and the substep ceiling. **Defaulted to what ships**, on the same
        /// rule as the two paces above: `MaxSubsteps` sat at 16 here long after the shipped figure
        /// became 64, which is a default that describes a retired world.
        /// </summary>
        public int Frequency = new ThermalSettings().Frequency;

        /// <summary>See <see cref="Frequency"/>.</summary>
        public int MaxSubsteps = new ThermalSettings().MaxSubsteps;

        public bool ClampOvershoot = true;

        // ---- environment ---------------------------------------------------------------------

        /// <summary>W/m^2. The mod ships 1000; the real solar constant at 1 AU is 1361.</summary>
        public float SolarEnergy = 1000f;

        /// <summary>K. 2.7 is the real cosmic microwave background and the shipped value.</summary>
        public float VacuumTemperature = 2.7f;

        /// <summary>W/(m^2 K). 8 is a realistic still-air natural convection figure.</summary>
        public float RoomConvectionCoefficient = 8f;

        // ---- mechanisms ------------------------------------------------------------------------

        public bool EnableRoomAir = true;
        public bool SolarSelfShadowing = true;
        public bool EnableFriction = true;
        public bool EnableCoolantLoops = true;

        /// <summary>True collapses a ring to one lumped mass — cheaper, and no longer a fluid.</summary>
        public bool WellMixedCoolant = false;

        // ---- the balance of the systems themselves ------------------------------------------------

        /// <summary>
        /// Fraction of a reactor's electrical output that becomes waste heat.
        ///
        /// A real fission plant is about a third efficient, so it sheds roughly two watts for every
        /// watt it delivers — a fraction of 2.0 against its *electrical* output. The mod's
        /// validator refuses anything above 1 as "creating energy from nothing", which is true of a
        /// fraction of total energy and false of a fraction of electrical output. That mismatch is
        /// one of the realism gaps this class exists to price.
        /// </summary>
        public float ReactorWasteFraction = 0.25f;

        /// <summary>Coolant speed at one pump, m/s.</summary>
        public float FlowRate = 10f;

        // ---- deriving a runnable world ---------------------------------------------------------

        /// <summary>The core settings this profile implies.</summary>
        public ThermalSettings ToSettings()
        {
            ThermalSettings settings = new ThermalSettings
            {
                Frequency = Frequency,
                SimulationSpeed = 1f,
                HeatTimeScale = HeatTimeScale,
                MaxSubsteps = MaxSubsteps,
                ClampConductionOvershoot = ClampOvershoot,
                ClampEnvironmentOvershoot = ClampOvershoot,
                SolarEnergy = SolarEnergy,
                VacuumTemperature = VacuumTemperature,
                RoomConvectionCoefficient = RoomConvectionCoefficient,
                EnableRoomAir = EnableRoomAir,
                SolarSelfShadowing = SolarSelfShadowing,
                EnableFriction = EnableFriction,
                EnableCoolantLoops = EnableCoolantLoops,
                WellMixedCoolant = WellMixedCoolant,
            };
            return settings.Derive();
        }

        /// <summary>
        /// This profile's version of a material: the same block, at this world's conduction pace.
        ///
        /// The solver multiplies a definition's conductivity by the core's fixed
        /// <see cref="ThermalConstants.ConductionScale"/>, so dividing that out and multiplying by
        /// the profile's own pace lands on exactly the number a world configured that way would
        /// use. It is the one trick that lets a pace be measured without a core setting existing.
        /// </summary>
        public BlockThermalProperties Material(BlockThermalProperties source)
        {
            BlockThermalProperties copy = new BlockThermalProperties
            {
                Conductivity = source.Conductivity * (ConductionPace / ThermalConstants.ConductionScale),
                SpecificHeat = source.SpecificHeat,
                Emissivity = source.Emissivity,
                ExposedSurfaceMultiplier = source.ExposedSurfaceMultiplier,
                ProducerWasteEnergy = source.ProducerWasteEnergy,
                ConsumerWasteEnergy = source.ConsumerWasteEnergy,
                CriticalTemperature = source.CriticalTemperature,
                OverheatDamagePerKelvin = source.OverheatDamagePerKelvin,
            };
            return copy;
        }

        // ---- the profiles ------------------------------------------------------------------------

        /// <summary>
        /// As physically true as this model can be made without changing its equations.
        ///
        /// Real heat capacities, real conductivities, the real solar constant, every mechanism on,
        /// the fluid model that is actually a fluid, and enough substeps that nothing is ever
        /// clamped. What is left between this and physics is the list in
        /// <see cref="RealismGaps"/> — the departures that are structural rather than numerical.
        /// </summary>
        public static BalanceProfile Physical()
        {
            return new BalanceProfile
            {
                Name = "physical",
                Intent = "every constant its real value; nothing clamped",
                HeatTimeScale = 1f,
                ConductionPace = 1f,
                Frequency = 8,
                MaxSubsteps = 64,
                SolarEnergy = 1361f,
                VacuumTemperature = 2.725f,
                RoomConvectionCoefficient = 8f,
                WellMixedCoolant = false,
                ReactorWasteFraction = 2f,
                FlowRate = 10f,
            };
        }

        /// <summary>
        /// What the mod ships today, for the comparison to have a middle — **read off
        /// <see cref="ThermalSettings"/> rather than restated here**, which is the only way the two
        /// cannot drift. They had: this carried <c>MaxSubsteps 16</c> against a shipped 64.
        /// </summary>
        public static BalanceProfile Shipped()
        {
            ThermalSettings shipped = new ThermalSettings();

            return new BalanceProfile
            {
                Name = "shipped",
                Intent = "the current default",
                HeatTimeScale = shipped.HeatTimeScale,
                ConductionPace = ThermalConstants.ConductionScale,
                Frequency = shipped.Frequency,
                MaxSubsteps = shipped.MaxSubsteps,
            };
        }

        /// <summary>
        /// As responsive and as cheap as the model goes, with no pretence of being right.
        ///
        /// One substep, always clamped, transfer raised until the clamp alone decides what moves,
        /// every optional mechanism off and the ring collapsed to one lumped mass. The
        /// <c>HeatTimeScale / Frequency</c> ratio is held under 4,000 because past that the clamps
        /// carry the whole step and blocks are driven to the ambient floor, which is not fast — it
        /// is broken.
        /// </summary>
        public static BalanceProfile Arcade()
        {
            return new BalanceProfile
            {
                Name = "arcade",
                Intent = "one clamped substep, mechanisms off, ring lumped",
                HeatTimeScale = 20000f,
                ConductionPace = ThermalConstants.ConductionScale,
                Frequency = 6,
                MaxSubsteps = 1,
                EnableRoomAir = false,
                SolarSelfShadowing = false,
                EnableFriction = false,
                WellMixedCoolant = true,
            };
        }

        /// <summary>
        /// The candidate: real materials, played at a game's pace.
        ///
        /// Keeps every part of <c>physical</c> that costs nothing — the real conductivities, the
        /// real solar constant, the segmented fluid, every mechanism on — and spends its budget
        /// only where realism is genuinely expensive, which is the thermal clock. It is the
        /// hypothesis this comparison exists to test, not a conclusion.
        ///
        /// <para>
        /// **The clock and the integrator are read off <see cref="ThermalSettings"/>, for the same
        /// reason <see cref="Shipped"/> reads them**, and realism.md records what correcting it cost.
        /// *A game's pace* is
        /// whatever pace the game is shipping, so restating it here made this profile a claim about
        /// a world that no longer existed: it carried `HeatTimeScale = 225` and `MaxSubsteps = 16`,
        /// the pair that shipped before `C24`, against a shipped 90 and 64. Nothing about the
        /// comparison was wrong except which world it was about — which is the worst way for a
        /// figure to be wrong, because every column still added up.
        /// </para>
        ///
        /// <para>
        /// What it states for itself is only what it departs on: real conductivity, the real solar
        /// constant and background, the segmented fluid, and a reactor waste fraction between
        /// <c>physical</c>'s and the shipped one.
        /// </para>
        /// </summary>
        public static BalanceProfile Candidate()
        {
            ThermalSettings shipped = new ThermalSettings();

            return new BalanceProfile
            {
                Name = "candidate",
                Intent = "real materials and constants, game clock",
                HeatTimeScale = shipped.HeatTimeScale,
                ConductionPace = 1f,
                Frequency = shipped.Frequency,
                MaxSubsteps = shipped.MaxSubsteps,
                SolarEnergy = 1361f,
                VacuumTemperature = 2.725f,
                WellMixedCoolant = false,
                ReactorWasteFraction = 0.25f,
            };
        }

        public static List<BalanceProfile> All()
        {
            return new List<BalanceProfile> { Physical(), Candidate(), Shipped(), Arcade() };
        }

        /// <summary>
        /// Where this model departs from physics for reasons no setting can fix — the structural
        /// gaps, as opposed to the numerical ones a profile can close.
        ///
        /// Kept beside the profiles because <c>physical</c> is only meaningful with this list
        /// attached: it is as real as the equations allow, and the equations allow this much.
        /// </summary>
        public static readonly string[] RealismGaps =
        {
            "Grey body by default: a block absorbs at its emissivity unless a definition declares "
            + "a SolarAbsorptivity of its own. Selective surfaces are expressible now — that was "
            + "the gap — but no shipped block uses one, and the wavelength dependence a real "
            + "selective surface has is still two constants rather than a spectrum.",

            "No inter-block radiation: a face radiates to the sky or to nothing. Two hot blocks "
            + "facing each other across a gap do not see each other, and no view factors exist.",

            "Conduction treats every block as a solid billet of one material, with the path length "
            + "taken from its half-depth. A real block is a shell around a void, and a long thin "
            + "panel conducts far better along its skin than through its middle.",

            "No contact resistance at a joint. Two bolted blocks conduct as if welded, where a real "
            + "mechanical joint is often the dominant resistance in the path.",

            "The coolant's fluid-to-wall coupling is modelled as conduction through a slab, using a "
            + "conductivity. The real quantity is a convective heat transfer coefficient in "
            + "W/(m^2 K), which depends on flow speed — so in this model circulating faster moves "
            + "heat around the ring but does not improve the exchange with the pipe wall.",

            "Room air is one well-mixed mass per compartment: no stratification, no draughts, and "
            + "a fixed convection coefficient regardless of geometry or temperature difference.",

            "Waste heat is a fraction of electrical throughput, capped at 1. A real fission plant "
            + "sheds about two watts per watt delivered, which this cannot express without the "
            + "validator calling it energy from nothing.",

            "Thruster heat is a fraction of thrust power with no exhaust: a real rocket carries the "
            + "great majority of its waste heat away in the plume rather than into the ship.",
        };
    }
}
