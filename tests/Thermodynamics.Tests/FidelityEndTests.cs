using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Which way each integration dial points is stated in the code, and the page cannot drift
    /// from it.**
    ///
    /// <para>
    /// `C15` wants a mechanism configured from `off` to `realistic`. The four integration dials are
    /// the exception, and configuration.md said so and then said *nothing tells a reader which way
    /// each points but this page* — which is the sentence this file exists to make false. They run
    /// in three directions: two are faithful at zero, two at their ceiling, and a player in the
    /// menu had four differently worded tips and no rule (`B31`).
    /// </para>
    /// </summary>
    public class FidelityEndTests
    {
        private static string Configuration()
        {
            return File.ReadAllText(Path.Combine(ShippedBlocks.RepoRoot(), "docs", "configuration.md"));
        }

        /// <summary>
        /// Every setting's shipped default, read from the source.
        ///
        /// <para>
        /// Textual for the reason `ConfigurationDocTests` gives: `Settings.cs` reads `Sandbox.*` and
        /// cannot be linked into this project, so the alternative to reading it is not reading it.
        /// </para>
        /// </summary>
        private static Dictionary<string, float> Defaults()
        {
            string source = File.ReadAllText(Path.Combine(ShippedBlocks.RepoRoot(),
                "Thermodynamics", "Settings.cs"));

            Dictionary<string, float> defaults = new Dictionary<string, float>(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(source,
                @"\[ProtoMember\(\d+\)\]\s*public\s+[A-Za-z0-9_<>\[\]]+\s+([A-Za-z0-9_]+)\s*=\s*(-?[0-9.]+)f?\s*;"))
            {
                float value;
                if (float.TryParse(match.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    defaults[match.Groups[1].Value] = value;
                }
            }

            Assert.True(defaults.Count > 20,
                "only " + defaults.Count + " defaults were read from Settings.cs, so this test is"
                + " not reading it");

            return defaults;
        }

        /// <summary>
        /// Every dial in the table is a setting that exists and can be read, so a rename cannot
        /// leave the table describing something that is gone (`D2`).
        /// </summary>
        [Fact]
        public void EveryDialNamesASettingThatExists()
        {
            Dictionary<string, float> defaults = Defaults();

            Assert.NotEmpty(FidelityEnds.All);

            foreach (FidelityEnds.End end in FidelityEnds.All)
            {
                Assert.True(defaults.ContainsKey(end.Setting),
                    end.Setting + " is in the fidelity table and is not a setting in Settings.cs");
            }
        }

        /// <summary>
        /// **The four claims configuration.md makes, held against the table.** The page is where
        /// this was written down first and it is still where it is argued; what must not happen is
        /// the two saying different things, which is how a reader ends up trusting the wrong one
        /// (`D3`).
        /// </summary>
        [Fact]
        public void ThePageAndTheTableAgreeAboutWhichEndIsFaithful()
        {
            string page = Configuration();

            Assert.Equal(0f, Faithful("MaxSubstepsPerBlock"));
            Assert.Contains("`MaxSubstepsPerBlock` 0 is the *faithful* end", page);

            Assert.Equal(0f, Faithful("MaxElementVisitsPerStep"));
            Assert.Contains("`MaxElementVisitsPerStep` 0 means uncapped", page);

            // The two that point the other way.
            Assert.True(Faithful("MaxSubsteps") > 0f);
            Assert.Contains("`MaxSubsteps` is the opposite", page);

            Assert.True(Faithful("Frequency") > Defaults()["Frequency"]);
            Assert.Contains("`Frequency` up is dearer and more faithful", page);
        }

        /// <summary>
        /// **Two of the four do not ship at their faithful end, and that is the fact worth
        /// pinning.** A dial whose default *is* its faithful end tells a reader nothing they did
        /// not assume; the two that are not are exactly the ones the sentence in the menu is for,
        /// and each has a reason this repository measured — `Frequency` is the quarter-second basis
        /// every substep figure is quoted on, and the step budget is `G6` asking a grid to keep up
        /// with real time.
        ///
        /// <para>
        /// It is asserted rather than described so that a later change to either default is a
        /// failure a reader has to look at, rather than a silent move of what *realistic* means.
        /// </para>
        /// </summary>
        [Fact]
        public void TheTwoDialsThatDoNotShipFaithfulAreTheTwoTheSentenceIsFor()
        {
            Dictionary<string, float> shipped = Defaults();

            Assert.Equal(Faithful("MaxSubstepsPerBlock"), shipped["MaxSubstepsPerBlock"]);
            Assert.Equal(Faithful("MaxSubsteps"), shipped["MaxSubsteps"]);

            Assert.NotEqual(Faithful("Frequency"), shipped["Frequency"]);
            Assert.NotEqual(Faithful("MaxElementVisitsPerStep"), shipped["MaxElementVisitsPerStep"]);
        }

        /// <summary>
        /// The sentence is one sentence, said the same way for every dial, and absent for a setting
        /// that has no faithful end — four dials described in four voices is what a reader has to
        /// learn rather than read.
        /// </summary>
        [Fact]
        public void TheSentenceIsUniformAndOnlyForDialsThatHaveOne()
        {
            foreach (FidelityEnds.End end in FidelityEnds.All)
            {
                string sentence = FidelityEnds.Sentence(end.Setting);
                Assert.StartsWith(" Most faithful at ", sentence);
                Assert.EndsWith(".", sentence);
                Assert.Contains(end.Means, sentence);
            }

            Assert.Equal(string.Empty, FidelityEnds.Sentence("EnableConduction"));
            Assert.Equal(string.Empty, FidelityEnds.Sentence("NoSuchSetting"));
            Assert.Null(FidelityEnds.For("EnableConduction"));
        }

        /// <summary>
        /// **The menu says it, which is the whole point.** The table and the page could agree
        /// perfectly while the surface a player actually reads said nothing, which is the state
        /// this was in. Read from the source because the menu binds Rich HUD and no test project
        /// compiles it.
        ///
        /// <para>
        /// **One place builds every tooltip now**, where there were four call sites appending the
        /// sentence separately and a fifth that could have forgotten to. So this judges the builder
        /// — that it appends the sentence and names its one exception — and then that the window
        /// takes every control's tip from it rather than writing one of its own.
        /// </para>
        /// </summary>
        [Fact]
        public void TheMenuRendersTheSentenceIntoEveryDialsTip()
        {
            string menu = File.ReadAllText(Path.Combine(ShippedBlocks.RepoRoot(),
                "Thermodynamics", "ThermalSettingsMenu.cs"));
            string window = File.ReadAllText(Path.Combine(ShippedBlocks.RepoRoot(),
                "Thermodynamics", "ThermalSettingsWindow.cs"));

            // The one builder, appending the sentence in the one place a tooltip is made.
            Assert.Equal(1, Occurrences(menu, "internal static ToolTip TipFor"));
            Assert.Equal(1, Occurrences(menu, "FidelityEnds.Sentence(name)"));

            // The overlay dropdown is a view chooser rather than a dial and is the one exception.
            int builder = menu.IndexOf("internal static ToolTip TipFor", StringComparison.Ordinal);
            Assert.Contains("DebugBlockOverlay", menu.Substring(builder, 600));

            // Every control the window offers is given that tip, and none of them builds its own —
            // a control with a tooltip made elsewhere is a dial whose faithful end is invisible in
            // exactly one place.
            Assert.Equal(1, Occurrences(window, "ThermalSettingsMenu.TipFor("));
            Assert.Equal(0, Occurrences(window, "new ToolTip"));

            int given = Occurrences(window, "ToolTip = tip");
            Assert.True(given >= 4, "only " + given + " controls were given a tip, so either a"
                + " control kind has lost its tooltip or this is reading the wrong file");
        }

        private static float Faithful(string setting)
        {
            FidelityEnds.End? end = FidelityEnds.For(setting);
            Assert.True(end != null, setting + " has no faithful end in the table");
            return end.Value.Faithful;
        }

        private static int Occurrences(string text, string needle)
        {
            int count = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
