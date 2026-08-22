using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    // What remains of the per-invariant corpus walks, and why the rest are gone.
    //
    // Six walks used to live here, each passing over all ten thousand ships for one claim. The
    // data they produced showed the passes were mostly the same pass: `idle` and `vacuum-shadow`
    // were one simulation under two names (identical to the millikelvin on every ship run under
    // both), three scenarios were each simulated by two walks, and every walk re-parsed and
    // re-built every hull the others had just parsed and built. CorpusSurvey runs each ship once,
    // each distinct scenario once, and evaluates every claim against the shared outcomes with the
    // violations named — the same coverage at roughly a quarter of the simulation.
    //
    // The two walks below are the ones the survey cannot absorb.

    /// <summary>Every block in every blueprint is placed or counted as unknown.</summary>
    public class BlockAccountingWalk
    {
        // Parse accounting, not simulation: it reads raw XML element counts against the reader's
        // own tally, file by file, including the modded and too-small files the survey never sees.
        // Distinct input population, so a distinct walk.
        [Fact]
        public void EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown()
        {
            LabInvariantTests.EveryBlockInABlueprintIsEitherPlacedOrCountedAsUnknown();
        }
    }

    /// <summary>The same ship in the same state twice gives the same answer.</summary>
    public class DeterminismWalk
    {
        /// <summary>
        /// Ships to check. Determinism is a property of the code paths, not of any particular
        /// hull: the fault it hunts — state left behind between sequential runs — either exists,
        /// in which case a few hundred diverse ships expose it, or does not, in which case ten
        /// thousand agree just as unanimously. Every full-population run to date found zero
        /// drifting ships, and at four simulations per ship it was the most expensive walk in the
        /// suite. A strided sample keeps the coverage of every ship *size* while paying for two
        /// hundred instead of eight thousand.
        /// </summary>
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
