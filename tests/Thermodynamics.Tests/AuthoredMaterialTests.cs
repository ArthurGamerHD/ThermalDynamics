using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class AuthoredMaterialTests
    {
        private const float Tolerance = 0.05f;

        private static readonly string[] Material = { "Conductivity", "SpecificHeat" };

/// <summary>Read operation.</summary>
        private static List<AuthoredValues.Entry> Read()
        {
            return AuthoredValues.Read(Material);
        }

/// <summary>Claimed operation.</summary>
        private static string Claimed(string note)
        {
            Match match = Regex.Match(note ?? "", @"real units:\s*([^.,\n]+)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

/// <summary>Invented operation.</summary>
        private static bool Invented(string note)
        {
            return (note ?? "").TrimStart().StartsWith("invented", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
/// <summary>EveryFigureThatNamesAMaterialMatchesIt operation.</summary>
        public void EveryFigureThatNamesAMaterialMatchesIt()
        {
/// <summary>Read operation.</summary>
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

            int judged = 0;
/// <summary>List operation.</summary>
            List<string> wrong = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
/// <summary>Claimed operation.</summary>
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

        [Fact]
/// <summary>EveryFigureSaysWhereItCameFrom operation.</summary>
        public void EveryFigureSaysWhereItCameFrom()
        {
/// <summary>Read operation.</summary>
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

/// <summary>List operation.</summary>
            List<string> unexplained = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
/// <summary>Claimed operation.</summary>
                string material = Claimed(entry.Note);

                if (material != null && ReferenceMaterials.IsKnown(material)) continue;
                if (Invented(entry.Note)) continue;

                unexplained.Add(material == null
                    ? entry + " names no material and does not say it is invented"
                    : entry + " claims '" + material + "', which is not a reference material");
            }

            Assert.True(unexplained.Count == 0, string.Join("\n  ", unexplained));
        }

        [Fact]
/// <summary>AFigureThatDisagreesWithItsMaterialIsCaught operation.</summary>
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
