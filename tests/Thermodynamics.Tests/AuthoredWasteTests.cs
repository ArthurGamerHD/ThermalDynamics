using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Every waste fraction `Cubes.xml` authors says where it came from, and the ones that claim a
    /// real conversion or the game's own field are held to it.
    ///
    /// <para>
    /// **This is <see cref="AuthoredMaterialTests"/>'s rule applied to the half of a definition
    /// that had no rule at all.** A `Conductivity` claiming aluminium has been checked since `C2`;
    /// a `ConsumerWasteEnergy` claimed nothing, which is how the jump drive came to sit at `0.15`
    /// under the note *"storage is efficient; the dump is not"* while its own definition stated
    /// `PowerEfficiency 0.8` — on the block carrying most of the corpus's full-load waste heat. See
    /// [backlog.md](../../docs/backlog.md) `C21`.
    /// </para>
    ///
    /// <para>
    /// Four kinds of provenance, and a fraction must claim exactly one. `waste:` names a conversion
    /// in <see cref="ReferenceEfficiencies"/> and is held to its band. `derived:` names a field the
    /// game's own definitions state and is recomputed from them. `no producer:` says the fraction
    /// is never read, and is checked against every definition of that type declaring no power
    /// output. `invented:` is an admitted opinion and is nobody's business but the author's — but it
    /// has to be said, so that a value cannot arrive with no provenance and read as though it had
    /// one. See definitions.md, Where a block's properties come from.
    /// </para>
    /// </summary>
    public class AuthoredWasteTests
    {
        /// <summary>The two fractions that decide what a block does with the power crossing it.</summary>
        private static readonly string[] Waste = { "ProducerWasteEnergy", "ConsumerWasteEnergy" };

        private static List<AuthoredValues.Entry> Read()
        {
            return AuthoredValues.Read(Waste);
        }

        /// <summary>The conversion a note names, or null where it names none.</summary>
        private static string Claimed(string note)
        {
            Match match = Regex.Match(note ?? "", @"^waste:\s*([^.,\n]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        /// <summary>The game field a note derives from, or null.</summary>
        private static string Derived(string note)
        {
            Match match = Regex.Match(note ?? "", @"^derived:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static bool Unreachable(string note)
        {
            return Regex.IsMatch(note ?? "", @"^no producer\s*:", RegexOptions.IgnoreCase);
        }

        private static bool Invented(string note)
        {
            return (note ?? "").TrimStart().StartsWith("invented", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every fraction says where it came from — a conversion, a field the game states, a
        /// statement that nothing reads it, or that it was invented.
        ///
        /// The rule that keeps the others from decaying: without it a new fraction can be added
        /// with no comment at all and pass, because a value claiming nothing is a value every other
        /// check skips.
        /// </summary>
        [Fact]
        public void EveryWasteFractionSaysWhereItCameFrom()
        {
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

            List<string> unexplained = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
                string conversion = Claimed(entry.Note);

                if (conversion != null && ReferenceEfficiencies.IsKnown(conversion)) continue;
                if (Derived(entry.Note) != null) continue;
                if (Unreachable(entry.Note)) continue;
                if (Invented(entry.Note)) continue;

                unexplained.Add(conversion == null
                    ? entry + " names no source and does not say it is invented"
                    : entry + " claims '" + conversion + "', which is not a reference conversion");
            }

            Assert.True(unexplained.Count == 0, string.Join("\n  ", unexplained));
        }

        /// <summary>
        /// Every fraction that names a conversion is inside the band that conversion spans.
        ///
        /// A band rather than a tolerance, because efficiency figures are ranges over frame sizes
        /// and duty points and a point value with a percentage around it would be inventing a
        /// precision the source does not have.
        /// </summary>
        [Fact]
        public void EveryFractionThatNamesAConversionIsInsideItsBand()
        {
            List<AuthoredValues.Entry> authored = Read();
            Assert.NotEmpty(authored);

            int judged = 0;
            List<string> wrong = new List<string>();

            foreach (AuthoredValues.Entry entry in authored)
            {
                string name = Claimed(entry.Note);
                if (name == null || !ReferenceEfficiencies.IsKnown(name)) continue;

                judged++;

                ReferenceEfficiencies.Reference reference = ReferenceEfficiencies.Get(name);
                if (reference.Admits(entry.Value)) continue;

                wrong.Add(entry + " claims " + name + ", which spans "
                    + reference.Low.ToString(CultureInfo.InvariantCulture) + " to "
                    + reference.High.ToString(CultureInfo.InvariantCulture)
                    + " — " + reference.Basis);
            }

            Assert.True(judged > 0, "no authored fraction names a conversion, so nothing was judged");
            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Every fraction called unreachable is on a type where no definition in the game declares
        /// power output, so nothing multiplies it.
        ///
        /// **The claim is about the producer side only**, because that is the side the game answers.
        /// A block's *draw* is a live figure the definitions frequently do not state — a turret, a
        /// beacon and an antenna all draw and none of them says so in an `.sbc` — so "this block
        /// consumes nothing" is not a claim this repository can check, and no fraction makes it.
        /// </summary>
        [Fact]
        public void EveryUnreachableFractionIsOnATypeThatProducesNothing()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, float> output = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (GameBlocks.Definition definition in GameBlocks.All())
            {
                // Every type is entered, including at zero: a dictionary written only where output
                // is positive says "the game has never heard of this type" for every type that
                // produces nothing, which is exactly the set this check is about.
                float most;
                if (!output.TryGetValue(definition.TypeId, out most) || definition.PowerOutputWatts > most)
                {
                    output[definition.TypeId] = definition.PowerOutputWatts;
                }
            }

            int judged = 0, verified = 0;
            List<string> wrong = new List<string>();

            foreach (AuthoredValues.Entry entry in Read())
            {
                if (!Unreachable(entry.Note)) continue;

                judged++;

                if (entry.Property != "ProducerWasteEnergy")
                {
                    wrong.Add(entry + " is called unreachable, and only a producer fraction may be");
                    continue;
                }

                if (entry.Value != 0f)
                {
                    wrong.Add(entry + " is called unreachable and is not zero, so it reads as a"
                        + " claim about a block that produces power");
                    continue;
                }

                float watts;
                if (!output.TryGetValue(entry.TypeId, out watts))
                {
                    // A type the game has never heard of is a claim nothing here can settle, and
                    // counting it as judged would let the check pass on a file of them (`E8`).
                    continue;
                }

                verified++;

                if (watts > 0f)
                {
                    wrong.Add(entry + " is called unreachable, but a " + entry.TypeId
                        + " definition declares " + watts.ToString("n0", CultureInfo.InvariantCulture)
                        + " W of output");
                }
            }

            Assert.True(judged > 0,
                "no fraction claims to be unreachable, so this test judged nothing (`E8`)");
            Assert.True(verified > 100,
                "only " + verified + " of " + judged + " unreachable claims name a type the game"
                + " knows, so most of them were not checked against anything (`E8`)");
            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Every fraction that says it is derived matches what the game's own definitions state,
        /// recomputed through the rule the mod itself uses.
        ///
        /// The authored value stands in for the family, so it is held to the efficiency most of the
        /// family states — the prototech drives override it per block, which is the whole reason
        /// the derivation exists.
        /// </summary>
        [Fact]
        public void EveryDerivedFractionMatchesTheEfficiencyTheGameStates()
        {
            if (!GameBlocks.IsInstalled) return;

            int judged = 0;
            List<string> wrong = new List<string>();

            foreach (AuthoredValues.Entry entry in Read())
            {
                string field = Derived(entry.Note);
                if (field == null) continue;

                judged++;

                Assert.Equal("PowerEfficiency", field);

                Dictionary<float, int> stated = new Dictionary<float, int>();
                foreach (GameBlocks.Definition definition in GameBlocks.All())
                {
                    if (!string.Equals(definition.TypeId, entry.TypeId, StringComparison.OrdinalIgnoreCase)) continue;

                    float waste = BlockThermalDerivation.WasteFromEfficiency(definition.PowerEfficiency);
                    if (waste < 0f) continue;

                    int seen;
                    stated.TryGetValue(waste, out seen);
                    stated[waste] = seen + 1;
                }

                if (stated.Count == 0)
                {
                    wrong.Add(entry + " says it is derived from " + field
                        + ", and no " + entry.TypeId + " definition states one");
                    continue;
                }

                float modal = 0f;
                int best = 0;
                foreach (KeyValuePair<float, int> pair in stated)
                {
                    if (pair.Value <= best) continue;
                    best = pair.Value;
                    modal = pair.Key;
                }

                if (Math.Abs(entry.Value - modal) > 1e-4f)
                {
                    wrong.Add(entry + " says it is derived from " + field + ", which most "
                        + entry.TypeId + " definitions put at "
                        + modal.ToString(CultureInfo.InvariantCulture));
                }
            }

            Assert.True(judged > 0,
                "no fraction claims to be derived, so this test judged nothing (`E8`)");
            Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
        }

        /// <summary>
        /// What the file's provenance actually is, pinned so it cannot drift quietly.
        ///
        /// **The counts are the finding**, and they are what definitions.md quotes: the sources that
        /// exist for a waste fraction are thin, and most of the file is an admitted opinion. A block
        /// added without a source moves the invented count, which is a decision somebody should see
        /// in a diff rather than a number nobody is watching (`D5`).
        /// </summary>
        [Fact]
        public void TheProvenanceOfEveryFractionIsCounted()
        {
            List<AuthoredValues.Entry> authored = Read();

            int sourced = 0, derived = 0, unreachable = 0, invented = 0;
            foreach (AuthoredValues.Entry entry in authored)
            {
                if (Claimed(entry.Note) != null) sourced++;
                else if (Derived(entry.Note) != null) derived++;
                else if (Unreachable(entry.Note)) unreachable++;
                else if (Invented(entry.Note)) invented++;
            }

            Assert.Equal(228, authored.Count);
            Assert.Equal(sourced + derived + unreachable + invented, authored.Count);

            Assert.Equal(15, sourced);
            Assert.Equal(1, derived);
            Assert.Equal(108, unreachable);
            Assert.Equal(104, invented);
        }

        /// <summary>
        /// The checks can fail. A fraction outside its band, a conversion nobody has heard of and a
        /// note that claims nothing all have to be caught, or the tests above pass because they
        /// judge nothing.
        /// </summary>
        [Fact]
        public void AFractionThatDisagreesWithItsConversionIsCaught()
        {
            ReferenceEfficiencies.Reference motor = ReferenceEfficiencies.Get("electric motor");

            Assert.True(motor.Admits(0.1f));
            Assert.False(motor.Admits(0.9f));
            Assert.False(ReferenceEfficiencies.Get("all of it").Admits(0.9f));

            Assert.Equal("electric motor", Claimed("waste: electric motor"));
            Assert.Equal("all of it", Claimed("waste: all of it. A circulator does no work."));
            Assert.Null(Claimed("invented: no source"));
            Assert.True(Invented("invented: no source"));
            Assert.True(Unreachable("no producer: nothing of this type produces"));
            Assert.Equal("PowerEfficiency", Derived("derived: PowerEfficiency, the vanilla drive"));

            Assert.False(ReferenceEfficiencies.IsKnown("perpetual motion"));
            Assert.Throws<ArgumentException>(delegate { ReferenceEfficiencies.Get("perpetual motion"); });
        }
    }
}
