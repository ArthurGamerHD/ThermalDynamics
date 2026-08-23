using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The two guards on the corpus walks, both of which used to say the opposite of what they
    /// meant.
    ///
    /// <para>
    /// **The opt-in gate read an empty value as opted in.** It tested `THERMAL_CORPUS_TESTS`
    /// against null, and on Linux `THERMAL_CORPUS_TESTS=` is a variable that exists and holds
    /// nothing — so the obvious way to write "off" turned every corpus walk on. That is `C8` and
    /// [backlog](../../docs/backlog.md) `H3`.
    /// </para>
    ///
    /// <para>
    /// **The resume counted files while the writing was per ship.** A skip supplied by hand off a
    /// progress line both re-emitted the ships of the interrupted batch and lost the ones it had
    /// not reached; the shipped 2026-08-21 dataset carries fifty duplicate rows from exactly that.
    /// It is now a record of finished blueprints rather than a count, which is `H2`.
    /// </para>
    ///
    /// <para>
    /// Neither can be tested through the walks themselves — they read a corpus that is gigabytes
    /// and lives outside the repository — so both are tested as what they are: two decisions taken
    /// on values.
    /// </para>
    /// </summary>
    public class CorpusGuardTests
    {
        /// <summary>
        /// Off has to be spellable, and every spelling a person reaches for has to work (`C8`,
        /// `P8`). The empty case is the one that bit.
        /// </summary>
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        [InlineData("off", false)]
        [InlineData("false", false)]
        [InlineData("FALSE", false)]
        [InlineData("  off  ", false)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("true", true)]
        public void OffMeansOffHoweverItIsSpelled(string configured, bool expected)
        {
            Assert.Equal(expected, CorpusFixture.OptedIn(configured));
        }

        /// <summary>
        /// A resume takes out exactly the blueprints that finished, leaves the rest in the order
        /// the corpus was sorted into, and is unmoved by a record naming files this corpus does not
        /// hold — which is what a record written by a run against a different corpus looks like.
        /// </summary>
        [Fact]
        public void AResumeDropsTheFinishedAndKeepsTheOrder()
        {
            List<string> corpus = new List<string> { "a", "b", "c", "d", "e" };

            Assert.Equal(corpus, CorpusFixture.Remaining(corpus, new HashSet<string>()));
            Assert.Equal(corpus, CorpusFixture.Remaining(corpus, null));

            Assert.Equal(new List<string> { "a", "c", "e" },
                CorpusFixture.Remaining(corpus, new HashSet<string> { "b", "d" }));

            Assert.Equal(new List<string> { "a", "b", "c", "d", "e" },
                CorpusFixture.Remaining(corpus, new HashSet<string> { "x", "y" }));

            Assert.Empty(CorpusFixture.Remaining(corpus,
                new HashSet<string> { "a", "b", "c", "d", "e" }));
        }

        /// <summary>
        /// The property that makes the resume exact rather than approximate: running it twice over
        /// what it returned the first time leaves nothing, and nothing is ever visited twice. A
        /// count could not promise either.
        /// </summary>
        [Fact]
        public void ResumingIsIdempotent()
        {
            List<string> corpus = new List<string> { "a", "b", "c", "d", "e", "f" };
            HashSet<string> done = new HashSet<string>();

            List<string> visited = new List<string>();
            int rounds = 0;

            while (rounds++ < 10)
            {
                List<string> remaining = CorpusFixture.Remaining(corpus, done);
                if (remaining.Count == 0) break;

                // Two of them finish, then the run dies.
                for (int i = 0; i < 2 && i < remaining.Count; i++)
                {
                    visited.Add(remaining[i]);
                    done.Add(remaining[i]);
                }
            }

            Assert.Equal(corpus, visited);
            Assert.Empty(CorpusFixture.Remaining(corpus, done));
        }
    }
}
