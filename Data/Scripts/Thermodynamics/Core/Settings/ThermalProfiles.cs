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
        public const string Default = "default";
        public const string Responsive = "responsive";
        public const string Arcade = "arcade";
        public const string Minimal = "minimal";

        public static readonly string[] Names =
        {
            Simulation, Default, Responsive, Arcade, Minimal
        };

        /// <summary>One-line summary of a profile, for a settings menu or chat command.</summary>
        public static string Describe(string name)
        {
            switch (Normalise(name))
            {
                case Simulation:
                    return "Accuracy first. Never clamps, heat moves at a realistic pace, costs the most.";
                case Default:
                    return "The shipped balance. Heat is slow enough to plan around and cheap enough to ignore.";
                case Responsive:
                    return "Heat you can watch move, about four times the pace of default, for the same cost.";
                case Arcade:
                    return "Fast, cheap and approximate. Heat rushes; the curve on the way is not to be trusted.";
                case Minimal:
                    return "For a crowded server. Still quicker than default, at half its cost.";
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
                    // Enough substeps that the stability estimate is never refused, so no clamp
                    // binds and the curve between two temperatures is fully resolved.
                    settings.Frequency = 8;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 64;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Default:
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 16;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Responsive:
                    // Sixteen times the transfer of default, with enough substeps to resolve most
                    // of it without relying on the clamps.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 3600f;
                    settings.MaxSubsteps = 8;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Arcade:
                    // One substep, always clamped, with transfer raised until the clamp alone
                    // decides how much heat moves — the most a single substep can carry, and so the
                    // most responsive setting per unit of cost.
                    //
                    // Capped at 20,000: beyond roughly that the environment terms are no longer
                    // recoverable by the clamps and blocks are driven to the ambient floor. See
                    // docs/configuration.md for the measurements.
                    settings.Frequency = 6;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 20000f;
                    settings.MaxSubsteps = 1;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Minimal:
                    // The arcade approach at a third of the rate, with the two most expensive
                    // mechanisms disabled: room air couples every surface bounding a compartment,
                    // and self-shadowing walks the grid.
                    //
                    // Transfer scales down with the rate. Stability depends on substep length times
                    // stiffness, and a third of the step rate makes each substep three times
                    // longer; arcade's 20,000 at Frequency 2 drives blocks to the ambient floor.
                    settings.Frequency = 2;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 6000f;
                    settings.MaxSubsteps = 1;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.EnableRoomAir = false;
                    settings.SolarSelfShadowing = false;
                    break;

                default:
                    return false;
            }

            settings.Derive();
            return true;
        }
    }
}
