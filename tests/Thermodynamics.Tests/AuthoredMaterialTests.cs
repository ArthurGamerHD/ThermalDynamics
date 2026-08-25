using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every material figure `Cubes.xml` authors says where it came from, and the ones that claim a
    /// real material are held to it.
    ///
    /// <para>
    /// **A comment reading `real units: aluminium` beside a `237` is a claim, and nothing checked
    /// it.** That is the shape of backlog.md `C2` — the authored values
    /// predate three changes to the model that reads them, and no reader could tell which numbers
    /// still meant what they said. These checks do not decide whether a value is *balanced*; they
    /// decide whether it is what it claims to be, which is the part a test can settle and the part
    /// that was silently rotting.
    /// </para>
    ///
    /// <para>
    /// Two rules, and the second is what makes the first durable: a figure may name a material in
    /// <see cref="ReferenceMaterials"/> and must then match it, or it may say `invented` and is then
    /// nobody's business but the author's — and it must do one of the two, so a value cannot be
    /// added with no provenance at all. See definitions.md, Conductivity is in real W/(m·K).
    /// </para>
    /// </summary>
    public class AuthoredMaterialTests
    {
        /// <summary>
        /// How far an authored figure may sit from the material it names.
        ///
        /// Five per cent, because a definition is allowed to round to something readable and one
        /// already does: the environment default carries mild steel's 466 J/(kg·K) as 450, and says
        /// so in its own comment. Wider than that and the name has stopped describing the number.
        /// </summary>
        private const float Tolerance = 0.05f;

        /// <summary>The two properties this file states in real units.</summary>
        private static readonly string[] Material = { "Conductivity", "SpecificHeat" };

        /// <summary>
        /// Every authored `Conductivity` and `SpecificHeat` in `Cubes.xml`, with the comment above
        /// each — which is where the provenance lives, so the comment is data here rather than
        /// prose. The reading itself is <see cref="AuthoredValues"/>, shared with
        /// <see cref="AuthoredWasteTests"/>.
        /// </summary>
        private static List<AuthoredValues.Entry> Read()
        {
            return AuthoredValues.Read(Material);
        }

        /// <summary>The material a note claims, or null where it claims none.</summary>
        private static string Claimed(string note)
        {
            Match match = Regex.Match(note ?? "", @"real units:\s*([^.,\n]+)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static bool Invented(string note)
        {
            return (note ?? "").TrimStart().StartsWith("invented", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every figure that names a material matches it, within a rounding the file is allowed.
        ///
        /// The check `C2` needed and did not have: an authored value drifting away from the material
        /// it claims is exactly the silent failure this repository exists to convert into a loud one.
        /// </summary>
        [Fact]
        public void EveryFigureThatNamesAMaterialMatchesIt()
        {
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

            int judged = 0;
            List<string> wrong = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
                string material = Claimed(entry.Note);
                if (material == null || !ReferenceMaterials.IsKnown(material)) continue;

                judged++;

                ReferenceMaterials.Reference reference = ReferenceMaterials.Get(material);
                float expected = entry.Property == "Conductivity"
                    ? reference.Conductivity
                    : reference.SpecificHeat;

                if (expected <= 0f) continue;
                if (Math.Abs(entry.Value - expected) / expected <= Tolerance) continue;

                wrong.Add(entry + " claims " + material + ", which is "
                    + expected.ToString("n3", CultureInfo.InvariantCulture));
            }

            Assert.True(judged > 0, "no authored figure names a material, so nothing was judged");
            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Every figure says where it came from — a material, or that it was invented.
        ///
        /// This is the rule that keeps the one above from decaying. Without it a new value can be
        /// added with no comment at all and pass, because a figure claiming nothing is a figure the
        /// first check skips.
        /// </summary>
        [Fact]
        public void EveryFigureSaysWhereItCameFrom()
        {
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

            List<string> unexplained = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
                string material = Claimed(entry.Note);

                if (material != null && ReferenceMaterials.IsKnown(material)) continue;
                if (Invented(entry.Note)) continue;

                unexplained.Add(material == null
                    ? entry + " names no material and does not say it is invented"
                    : entry + " claims '" + material + "', which is not a reference material");
            }

            Assert.True(unexplained.Count == 0, string.Join("\n  ", unexplained));
        }

        /// <summary>
        /// The check can fail. A material name that does not match its figure has to be caught, or
        /// the two above are a pair of tests that pass because they judge nothing.
        /// </summary>
        [Fact]
        public void AFigureThatDisagreesWithItsMaterialIsCaught()
        {
            ReferenceMaterials.Reference aluminium = ReferenceMaterials.Get("aluminium");

            Assert.Equal(237f, aluminium.Conductivity);
            Assert.True(Math.Abs(120f - aluminium.Conductivity) / aluminium.Conductivity > Tolerance,
                "steel's conductivity must not pass as aluminium's");

            Assert.Null(Claimed("invented: a lens and a board"));
            Assert.True(Invented("invented: a lens and a board"));
            Assert.Equal("aluminium", Claimed("W/(m K), real units: aluminium"));
            Assert.False(ReferenceMaterials.IsKnown("unobtanium"));
            Assert.Throws<ArgumentException>(delegate { ReferenceMaterials.Get("unobtanium"); });
        }
    }
}
