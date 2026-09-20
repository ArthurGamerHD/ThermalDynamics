using System;
using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Where the two validators are heard.
    ///
    /// <para>
    /// <see cref="BlockThermalProperties.Validate"/> and <see cref="ThermalSettings.Validate"/>
    /// describe, in their own words, "problems a mod author would want to hear about" — a zero
    /// specific heat, an emissivity above one, a waste fraction that creates energy from nothing, a
    /// substep long enough that the overshoot clamps carry the whole step. Both were correct, both
    /// were tested, and neither was called anywhere the game runs, so no author or administrator
    /// had ever seen one. This is the other half: the line that gets written and the count
    /// <c>/thermal problems</c> lists.
    /// </para>
    ///
    /// <para>
    /// **Every problem is said once.** A block definition is checked the first time a block of that
    /// type is placed and a hull holds thousands of them; settings are checked on every set, and a
    /// slider drag is a set a frame. Either would otherwise fill a log with one fault.
    /// </para>
    ///
    /// <para>
    /// Nothing here changes a value — that is <c>Clamp</c>'s job, and by the time a player sees a
    /// number it has already run. A check therefore runs on what was *declared*, before clamping,
    /// because a clamped value has no problem left to report.
    /// </para>
    ///
    /// <para>
    /// It writes through <see cref="Writer"/> rather than to a log directly, which is what keeps it
    /// in the core: the game layer installs the log, a test installs a list, and the collection and
    /// the deduplication are the same code in both (`C5`).
    /// </para>
    /// </summary>
    public static class ThermalValidation
    {
        private static readonly object Lock = new object();
        private static readonly HashSet<string> Said = new HashSet<string>();
        private static readonly List<string> Found = new List<string>();

        /// <summary>
        /// Where a problem goes when it is first seen. Null until the host installs one, and a
        /// problem found before then is still recorded — it is the writer that is optional, not the
        /// finding.
        /// </summary>
        public static Action<string> Writer;

        /// <summary>Every distinct problem seen this session, in the order it was first seen.</summary>
        public static List<string> Problems
        {
            get { lock (Lock) { return new List<string>(Found); } }
        }

        public static int Count
        {
            get { lock (Lock) { return Found.Count; } }
        }

        /// <summary>Forgets what has been said, so the same fault reports again.</summary>
        public static void Reset()
        {
            lock (Lock)
            {
                Said.Clear();
                Found.Clear();
            }
        }

        /// <summary>
        /// Checks one block type's declared properties. <paramref name="subject"/> is what an
        /// author has to go and edit, so it is the block's own name rather than an index.
        /// </summary>
        public static void Check(string subject, BlockThermalProperties declared)
        {
            if (declared != null) Report(subject, declared.Validate());
        }

        /// <summary>Checks the world's settings, after they have been derived.</summary>
        public static void Check(ThermalSettings settings)
        {
            if (settings != null) Report("settings", settings.Validate());
        }

        private static void Report(string subject, List<string> problems)
        {
            if (problems == null || problems.Count == 0) return;

            for (int i = 0; i < problems.Count; i++)
            {
                string line = subject + ": " + problems[i];

                lock (Lock)
                {
                    if (!Said.Add(line)) continue;
                    Found.Add(line);
                }

                Action<string> writer = Writer;
                if (writer == null) continue;

                try
                {
                    writer(line);
                }
                catch (Exception)
                {
                    // A validator must never be the reason a world fails to load. It runs on the
                    // definition pass, which is early enough that a log may not be there yet.
                }
            }
        }
    }
}
