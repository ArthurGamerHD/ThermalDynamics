using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    public class CorpusGuardTests
    {
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

                for (int i = 0; i < 2 && i < remaining.Count; i++)
                {
                    visited.Add(remaining[i]);
                    done.Add(remaining[i]);
                }
            }

            Assert.Equal(corpus, visited);
            Assert.Empty(CorpusFixture.Remaining(corpus, done));
        }
        [Fact]

        public void NarrowingThenResumingKeepsTheSameShips()
        {
            List<string> corpus = new List<string> { "a", "b", "c", "d", "e" };
            HashSet<string> selection = new HashSet<string> { "a", "b", "c" };
            HashSet<string> done = new HashSet<string> { "a", "b" };

            List<string> narrowedFirst = CorpusFixture.Remaining(
                Keep(corpus, selection), done);


            List<string> resumedFirst = Keep(
                CorpusFixture.Remaining(corpus, done), selection);

            Assert.Equal(new List<string> { "c" }, narrowedFirst);
            Assert.Equal(narrowedFirst, resumedFirst);
        }


        private static List<string> Keep(IList<string> corpus, ICollection<string> wanted)
        {

            List<string> kept = new List<string>();
            foreach (string path in corpus)
            {
                if (wanted.Contains(path)) kept.Add(path);
            }

            return kept;
        }

    }
}
