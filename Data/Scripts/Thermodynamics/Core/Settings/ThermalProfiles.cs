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
                    return "Every mechanism, integrated as finely as it asks for. Costs the most, and is what the others are measured against.";
                case Optimized:
                    return "The same simulation with every performance dial at its measured sweet spot. Nearly the accuracy, a third of the cost.";
                case Simlite:
                    return "Realistic and knowingly approximate. Trades the shape of a curve for frames on a crowded server.";
                case Responsive:
                    return "Simulation, with heat moving sixteen times faster. Accurate, and quick enough to watch.";
                case Arcade:
                    return "Optimized, with heat moving sixteen times faster. The cheap one you can see working.";
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
                    // Accuracy first, cost last. Substeps are never refused, no block has its
                    // capacity floored and no step is shortened to fit a budget, so what comes out
                    // is what the equations say. Every other profile is measured against this one.
                    settings.Frequency = 8;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 64;
                    settings.MaxSubstepsPerBlock = 0;
                    settings.MaxElementVisitsPerStep = 0;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = true;
                    settings.EnableRoomAir = true;
                    break;

                case Optimized:
                    // Simulation's answer at the price the field tuning found: the per-block cap
                    // and the substep ceiling moved together to six, which resolves nine tenths of
                    // what the cap was flooring for about a third of simulation's cost. Same
                    // mechanisms, same pace, same settling temperatures — see field-tuning.md.
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

                case Simlite:
                    // Realistic and knowingly approximate. Every mechanism still runs; the
                    // integrator is given less to work with, and self-shadowing — which walks the
                    // grid — is dropped. Three substeps finds where heat settles but not the shape
                    // of the curve on the way, which is exactly the trade being made.
                    settings.Frequency = 4;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 225f;
                    settings.MaxSubsteps = 3;
                    settings.MaxSubstepsPerBlock = 3;
                    settings.MaxElementVisitsPerStep = 400000;
                    settings.ClampConductionOvershoot = true;
                    settings.ClampEnvironmentOvershoot = true;
                    settings.SolarSelfShadowing = false;
                    settings.EnableRoomAir = true;
                    break;

                case Responsive:
                    // Simulation with the clock run fast: sixteen times the transfer of the tuned
                    // pace, and the substeps to resolve it. Heat you can watch move, with the
                    // curve still decided by the equations rather than by a clamp.
                    settings.Frequency = 8;
                    settings.SimulationSpeed = 1f;
                    settings.HeatTimeScale = 3600f;
                    settings.MaxSubsteps = 64;
                    settings.MaxSubstepsPerBlock = 0;
                    settings.MaxElementVisitsPerStep = 0;
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
                    settings.HeatTimeScale = 3600f;
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
