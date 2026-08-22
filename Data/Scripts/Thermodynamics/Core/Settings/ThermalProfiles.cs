using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Ready-made settings bundles, spanning accuracy-first to arcade. Each sets only the four
    /// integration values, plus two mechanism switches at the cheapest end.
    /// See profiles.md, which is the ladder in full and the two relationships that bound it.
    /// </summary>
    public static class ThermalProfiles
    {
        public const string Simulation = "simulation";
        public const string Optimized = "optimized";
        public const string Simlite = "simlite";
        public const string Responsive = "responsive";
        public const string Arcade = "arcade";

        /// <summary>
        /// The presets, most faithful first: a ladder on two axes, where the simulation is integrated
        /// and how fast heat is made to move.
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
        /// Applies a profile to a settings object and calls <see cref="ThermalSettings.Derive"/>. Only
        /// the integration values and two mechanism switches; the caller returns everything else to
        /// the shipped value first, which is what makes a profile the whole world.
        /// </summary>
        /// <returns>False when the name is not one of <see cref="Names"/>.</returns>
        public static bool Apply(ThermalSettings settings, string name)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            switch (Normalise(name))
            {
                case Simulation:
                    // Real time, and therefore real physics — and the cheapest thing here to
                    // integrate, which is the opposite of what a maximum-quality preset usually
                    // means. profiles.md, The two axes.
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
