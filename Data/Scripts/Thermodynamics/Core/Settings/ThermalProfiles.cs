using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Ready-made settings bundles, spanning accuracy-first to arcade. Each sets only the four
    /// integration values, plus two mechanism switches at the cheapest end.
    ///
    /// <para>
    /// The governing relationship: diffusion is a square-root process, so a heat front crosses
    /// blocks at a rate proportional to the square root of substeps per second while cost is
    /// linear in substeps per second. Doubling responsiveness at fixed accuracy therefore costs
    /// four times as much, and the profiles are points along that curve.
    /// </para>
    ///
    /// <para>
    /// The exception is where the substeps are spent.
    /// <see cref="ThermalSettings.HeatTimeScale"/> buys transfer per substep and
    /// <see cref="ThermalSettings.MaxSubsteps"/> caps how many are taken; together they allow a
    /// deliberately over-long step bounded by the overshoot clamps. That route is approximate by
    /// construction and roughly three times more responsive per unit of cost, which is what the
    /// arcade end of this list uses.
    /// </para>
    /// </summary>
    public static class ThermalProfiles
    {
        public const string Simulation = "simulation";
        public const string Optimized = "optimized";
        public const string Simlite = "simlite";
        public const string Responsive = "responsive";
        public const string Arcade = "arcade";

        /// <summary>
        /// The presets, most faithful first.
        ///
        /// A ladder rather than five unrelated tunings, on two axes: how faithfully the simulation
        /// is integrated, and how fast heat is made to move. Simulation, optimized and simlite
        /// share the tuned pace and descend in accuracy; responsive and arcade are simulation and
        /// optimized with the pace raised.
        /// </summary>
        public static readonly string[] Names =
        {
            Simulation, Optimized, Simlite, Responsive, Arcade
        };

        /// <summary>One-line summary of a profile, for a settings menu or chat command.</summary>
        public static string Describe(string name)
        {
            switch (Normalise(name))
            {
                case Simulation:
                    return "Real time and real physics. A ship takes the hours a ship takes, so nothing appears to happen in a session; the reference, not a way to play.";
                case Optimized:
                    return "Real time, with the cost dials tuned. Same temperatures at the same moments, less work to get them.";
                case Simlite:
                    return "Real time, knowingly approximate. Trades the shape of a curve for frames on a crowded server.";
                case Responsive:
                    return "Simulation with the clock run fast, which is what makes the mod playable. Heat you can watch, integrated properly.";
                case Arcade:
                    return "Responsive's pace at optimized's price. The cheap one you can see working.";
                default:
                    return "";
            }
        }

        private static string Normalise(string name)
        {
            return name == null ? "" : name.Trim().ToLowerInvariant();
        }

        public static bool IsKnown(string name)
        {
            string wanted = Normalise(name);
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i] == wanted) return true;
            }
            return false;
        }

        /// <summary>
        /// Applies a profile to a settings object and calls <see cref="ThermalSettings.Derive"/>.
        ///
        /// Only the integration values are written, plus two mechanism switches for the cheapest
        /// profile. Vacuum temperature, friction, heat pump behaviour and the debug switches are
        /// left as found: a profile is a starting point, not a reset.
        /// </summary>
        /// <returns>False when the name is not one of <see cref="Names"/>.</returns>
        public static bool Apply(ThermalSettings settings, string name)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            switch (Normalise(name))
            {
                case Simulation:
                    // Real time, and therefore real physics: HeatTimeScale divides every heat
                    // capacity, so anything above 1 is thermal time running fast. At 1 a ship
                    // takes the hours a ship really takes, which is the whole claim this profile
                    // makes and the reason it is not the default.
                    //
                    // It is also the *cheapest* profile to integrate, which is the opposite of
                    // what a "maximum quality" preset usually means. Stiffness is conductance over
                    // capacity, so dividing capacity by 225 multiplies substep demand by 225:
                    // measured on a 150-block hull, demand is 0.00 substeps at scale 1, 0.90 at
                    // 225 and 14.40 at 3600. Accuracy here costs patience, not frames.
                    settings.Frequency = 8;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 1f;
                    settings.MaxSubsteps = 64;
                    settings.MaxSubstepsPerBlock = 0;
                    settings.MaxElementVisitsPerStep = 0;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = true;
                    settings.EnableRoomAir = true;
                    break;

                case Optimized:
                    // Simulation's physics with the cost dials tuned: the same real-time pace, and
                    // therefore the same temperatures at the same moments, bought with the caps the
                    // field tuning settled on — see load-and-hitching.md. What it gives up is headroom
                    // on a grid stiff enough to need it, not fidelity on an ordinary one.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 1f;
                    settings.MaxSubsteps = 6;
                    settings.MaxSubstepsPerBlock = 6;
                    settings.MaxElementVisitsPerStep = 1000000;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = true;
                    settings.EnableRoomAir = true;
                    break;

                case Simlite:
                    // Real time still, so the physics is honest; what is traded is the work spent
                    // drawing it. Every mechanism runs, the integrator is given less, and
                    // self-shadowing — which walks the grid — is dropped.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 1f;
                    settings.MaxSubsteps = 3;
                    settings.MaxSubstepsPerBlock = 3;
                    settings.MaxElementVisitsPerStep = 400000;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = false;
                    settings.EnableRoomAir = true;
                    break;

                case Responsive:
                    // Simulation with the clock run fast. This is the only difference from
                    // simulation, and it is the one that makes the mod playable: at real time a
                    // hull moves a fraction of a kelvin a minute, and 225 is the pace every
                    // balance figure in the docs was measured at.
                    //
                    // The cost of the acceleration is stiffness, which is why this profile keeps
                    // simulation's substep budget rather than optimized's.
                    settings.Frequency = 8;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 64;
                    settings.MaxSubstepsPerBlock = 0;

                    // Kept, unlike simulation's. The budget costs no accuracy — it shortens a step
                    // rather than coarsening it, so a grid too large for a frame advances less
                    // simulated time at the same fidelity instead of stuttering at full rate. This
                    // is the profile people actually play on, and a default that can drop a
                    // hundred-millisecond step into a frame is not a default.
                    settings.MaxElementVisitsPerStep = 1000000;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = true;
                    settings.EnableRoomAir = true;
                    break;

                case Arcade:
                    // Responsive's pace at optimized's price, which is what most people mean by
                    // wanting to see it work without paying for it.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 6;
                    settings.MaxSubstepsPerBlock = 6;
                    settings.MaxElementVisitsPerStep = 1000000;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = true;
                    settings.EnableRoomAir = true;
                    break;


                default:
                    return false;
            }

            settings.Derive();
            return true;
        }
    }
}
