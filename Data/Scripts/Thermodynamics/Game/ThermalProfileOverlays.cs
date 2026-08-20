using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// The definition overlay belonging to the profile a world is running.
    ///
    /// <para>
    /// A profile is two things: settings, and the definitions those settings are safe with. The
    /// second half is what this loads. A block's demand on the integrator is its conductance over
    /// its heat capacity, so a profile granting three substeps to a ship whose lights ask for
    /// twenty is integrating those blocks outside the range their own physics is stable in — and
    /// no amount of setting can fix a number that belongs to a definition.
    /// </para>
    ///
    /// <para>
    /// The files live in <c>Profiles/</c> at the mod root rather than under <c>Data/</c>, and that
    /// is not tidiness: everything under <c>Data/</c> is loaded by the game and scanned by
    /// Definition Extensions, so a second Cubes.xml there would collide with the first. These are
    /// read by this mod, applied over what the definitions loaded, and never seen by anything else.
    /// </para>
    ///
    /// <para>
    /// **`simulation` has no overlay and must not get one.** It runs the shipped definitions
    /// exactly, which is what makes it the reference every other configuration is measured against
    /// — and what stops a benchmark quietly becoming a comparison between two sets of definitions
    /// rather than between two settings. `Benchmarking` forces that state for the same reason.
    /// </para>
    /// </summary>
    public static class ThermalProfileOverlays
    {
        /// <summary>Where the files live, relative to the mod's own folder.</summary>
        private const string Folder = "Profiles";

        /// <summary>
        /// Set to run the shipped definitions whatever profile is in force.
        ///
        /// Performance work compares configurations, and a comparison is only about settings if
        /// both sides read the same definitions. A run with this off is measuring two things at
        /// once and cannot say which moved.
        /// </summary>
        public static bool Benchmarking;

        private static readonly Dictionary<string, ThermalOverlay> Loaded =
            new Dictionary<string, ThermalOverlay>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The overlay in force, or null when the world runs the shipped definitions.</summary>
        public static ThermalOverlay Current { get; private set; }

        /// <summary>
        /// Loads the overlay for a profile name, or clears it for the reference profile and for a
        /// world whose settings match no profile — a hand-tuned world gets the definitions it was
        /// tuned against, not a preset's idea of them.
        /// </summary>
        public static void Use(string profile)
        {
            if (Benchmarking || string.IsNullOrEmpty(profile)
                || profile == ThermalProfiles.Simulation)
            {
                Current = null;
                return;
            }

            Current = Load(profile);
        }

        private static ThermalOverlay Load(string profile)
        {
            ThermalOverlay cached;
            if (Loaded.TryGetValue(profile, out cached)) return cached;

            ThermalOverlay overlay = Read(profile);
            Loaded[profile] = overlay;
            return overlay;
        }

        private static ThermalOverlay Read(string profile)
        {
            string path = Folder + "/" + profile + ".xml";

            try
            {
                MyObjectBuilder_Checkpoint.ModItem mod = ModContext();

                if (!MyAPIGateway.Utilities.FileExistsInModLocation(path, mod))
                {
                    MyLog.Default.Info("[" + Settings.Name + "] no definition overlay for " + profile);
                    return null;
                }

                string text;
                using (System.IO.TextReader reader =
                    MyAPIGateway.Utilities.ReadFileInModLocation(path, mod))
                {
                    text = reader.ReadToEnd();
                }

                ThermalOverlay overlay =
                    MyAPIGateway.Utilities.SerializeFromXML<ThermalOverlay>(text);

                if (overlay == null || overlay.IsEmpty)
                {
                    MyLog.Default.Info("[" + Settings.Name + "] definition overlay for "
                        + profile + " changes nothing");
                    return null;
                }

                // A misspelled property would otherwise do nothing and say nothing, which is a
                // long afternoon for whoever writes the next one.
                List<string> unknown = overlay.Unknown();
                for (int i = 0; i < unknown.Count; i++)
                {
                    MyLog.Default.Warning("[" + Settings.Name + "] overlay " + profile
                        + " sets an unknown property: " + unknown[i]);
                }

                MyLog.Default.Info("[" + Settings.Name + "] definition overlay for "
                    + profile + " loaded");

                return overlay;
            }
            catch (Exception e)
            {
                MyLog.Default.Info("[" + Settings.Name + "] failed to read the "
                    + profile + " overlay\n" + e);
                return null;
            }
        }

        /// <summary>
        /// This mod's own entry in the world's mod list, which is what the file APIs address a mod
        /// folder by.
        /// </summary>
        private static MyObjectBuilder_Checkpoint.ModItem ModContext()
        {
            return ((MyModContext)Session.Instance.ModContext).ModItem;
        }

        /// <summary>
        /// Applies the overlay in force to a block's properties. Called where properties are built
        /// rather than where they are read, so the cost is per definition rather than per block.
        /// </summary>
        public static void Apply(BlockThermalProperties properties, string subtype)
        {
            if (Current == null) return;
            Current.ApplyTo(properties, subtype);
        }

        public static void Apply(LoopThermalProperties properties)
        {
            if (Current == null) return;
            Current.ApplyTo(properties);
        }

        public static void Apply(PlanetThermalProperties properties, string subtype)
        {
            if (Current == null) return;
            Current.ApplyTo(properties, subtype);
        }
    }
}
