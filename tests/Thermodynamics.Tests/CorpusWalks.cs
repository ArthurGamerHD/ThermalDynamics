using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{

    public class BlockAccountingWalk
    {
        [Fact]

        public void EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown()
        {
            LabInvariantTests.EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown();
        }
    }

    public class DeterminismWalk
    {
        private const int Sample = 200;

        [Fact]

        public void RunningTheSameThingTwiceGivesTheSameAnswer()
        {
            List<Blueprints.Ship> ships = CorpusFixture.Spread(Sample);
            if (ships.Count == 0) return;


            List<string> drifted = new List<string>();
            foreach (List<string> report in LabRun.Map(ships, LabInvariantTests.Compare, LabMode.Parallel))
            {
                drifted.AddRange(report);
            }

            Assert.Empty(drifted);
        }
    }
}
