using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Ready-made settings bundles, from simulation-first to arcade.
    ///
    /// <para>
    /// This is a game, and not every world wants the same thing from a thermal model. One wants a
    /// reactor that takes ten minutes to cook a hull and rewards planning; another wants heat that
    /// visibly rushes down a corridor the moment something breaks, and does not care whether the
    /// curve on the way is right. Both are reasonable and the difference between them is four
    /// numbers.
    /// </para>
    ///
    /// <para>
    /// The one relationship worth understanding before changing any of them: <b>heat spreads as
    /// the square root of the arithmetic you spend on it.</b> Diffusion is a square-root process,
    /// so a front crosses blocks at a rate proportional to the square root of substeps per second,
    /// while cost is proportional to substeps per second outright. Doubling how responsive a world
    /// feels therefore costs four times as much. There is no setting that escapes it — the profiles
    /// below are points along that curve, not ways around it.
    /// </para>
    ///
    /// <para>
    /// What <em>does</em> change the exchange rate is where the substeps are spent.
    /// <see cref="ThermalSettings.HeatTimeScale"/> buys transfer per substep and
    /// <see cref="ThermalSettings.MaxSubsteps"/> refuses to pay for more of them; together they
    /// let a step be deliberately too long and lean on the overshoot clamps to stay bounded. That
    /// is approximate by construction and it is roughly three times more responsive per unit of
    /// cost than the accurate route, which is the whole basis of the arcade end of this list.
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

        /// <summary>
        /// One-line summaries, for a settings menu or a chat command.
        /// </summary>
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
        /// Applies a profile to a settings object and derives it.
        ///
        /// Only the settings a profile is about are touched: the integration knobs, and for the
        /// cheapest profile the two mechanisms that cost the most for what a player notices.
        /// Everything else — vacuum temperature, friction, heat pump behaviour, the debug switches
        /// — is left exactly as it was found, because a profile is a starting point rather than a
        /// factory reset.
        /// </summary>
        /// <returns>False when the name is not one of <see cref="Names"/>.</returns>
        public static bool Apply(ThermalSettings settings, string name)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            switch (Normalise(name))
            {
                case Simulation:
                    // Enough substeps that the stability estimate is never refused, so no clamp
                    // ever binds and the curve between two temperatures is the real one.
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
                    // Sixteen times the transfer of default, and the substeps to carry most of it
                    // honestly. Heat you can see move without the model stopping being a model.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 3600f;
                    settings.MaxSubsteps = 8;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Arcade:
                    // One substep, always clamped, transfer turned up until the clamp is the only
                    // thing deciding how much heat moves. That is the most a substep can carry, so
                    // it is the most responsive a world can be per unit of cost.
                    //
                    // 20,000 rather than more because past roughly that the environment terms stop
                    // being recoverable by the clamps and blocks start slamming to absolute zero.
                    // See docs/configuration.md for the measurements behind the number.
                    settings.Frequency = 6;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 20000f;
                    settings.MaxSubsteps = 1;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    break;

                case Minimal:
                    // The same trick as arcade at a third of the rate, plus the two mechanisms
                    // that cost the most per thing a player actually notices: room air couples
                    // every surface bounding a compartment, and self-shadowing walks the grid.
                    //
                    // The transfer comes down with the rate rather than staying at arcade's. What
                    // destabilises a step is the substep length times the stiffness, and a third
                    // of the steps makes each one three times longer — arcade's 20,000 at
                    // Frequency 2 put blocks at absolute zero, which is what this number is
                    // avoiding rather than a taste decision.
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
