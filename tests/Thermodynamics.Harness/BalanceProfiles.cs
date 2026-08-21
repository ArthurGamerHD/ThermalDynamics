using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Whole-system balance profiles, as simulated environments rather than as shipped settings.
    ///
    /// <see cref="ThermalProfiles"/> in the core ships five bundles, and every one of them tunes
    /// the same axis: how accurately a step is integrated. All five run <c>HeatTimeScale = 225</c>,
    /// so even <c>simulation</c> — the accuracy-first profile — is a faithful integration of a
    /// model that is deliberately 225 times faster than the world. There is no dial anywhere that
    /// asks how *physically true* the model is, because until now nothing needed one.
    ///
    /// This is that axis, built here so it can be measured before anything is decided. A profile
    /// carries every knob that changes the balance of the mod, not just the integration ones:
    /// the two pace scales, the environment constants, which mechanisms run, how the coolant is
    /// modelled and what a reactor's waste heat actually is.
    ///
    /// **Nothing here changes the shipped configuration.** The conduction pace is expressed by
    /// scaling the conductivity of the materials the harness builds with, which reaches the same
    /// number the solver would see, so the core needs no new setting to be measured against.
    /// </summary>
    public class BalanceProfile
    {
        public string Name;
        public string Intent;

        // ---- the two pace scales ------------------------------------------------------------

        /// <summary>
        /// Divides every heat capacity. 1 is the world; the mod ships 225.
        ///
        /// The single largest departure from physics in the whole model, and the one that makes it
        /// a game: real steel at real specific heat gives a large-grid armour block a time constant
        /// of about half an hour.
        /// </summary>
        public float HeatTimeScale = 225f;

        /// <summary>
        /// Multiplies every block's real conductivity. 1 is the world; the mod ships 2.4.
        ///
        /// Applied here by scaling the material figures the harness builds blocks from, so no core
        /// setting has to exist for this to be measured.
        /// </summary>
        public float ConductionPace = 2.4f;

        // ---- integration ---------------------------------------------------------------------

        public int Frequency = 4;
        public int MaxSubsteps = 16;
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

        /// <summary>What the mod ships today, for the comparison to have a middle.</summary>
        public static BalanceProfile Shipped()
        {
            return new BalanceProfile
            {
                Name = "shipped",
                Intent = "the current default",
                HeatTimeScale = 225f,
                ConductionPace = ThermalConstants.ConductionScale,
                Frequency = 4,
                MaxSubsteps = 16,
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
        /// </summary>
        public static BalanceProfile Candidate()
        {
            return new BalanceProfile
            {
                Name = "candidate",
                Intent = "real materials and constants, game clock",
                HeatTimeScale = 225f,
                ConductionPace = 1f,
                Frequency = 4,
                MaxSubsteps = 16,
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
            "Grey body: Emissivity doubles as solar absorptivity, so a good radiator is forced to "
            + "be a good absorber. Real spacecraft radiators use selective surfaces with a high "
            + "emissivity and a low absorptivity, which is exactly the combination this cannot express.",

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
