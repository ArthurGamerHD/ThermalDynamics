using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// How far the scenario catalogue has drifted from the blocks it claims to stand in for.
    ///
    /// <para>
    /// `Catalog` opens by saying its masses are approximations of the real Space Engineers blocks
    /// and that its thermal properties "mirror Data/Cubes.xml". Every scenario in this repository is
    /// built out of it, so every scenario temperature is quoted off those numbers — and nothing has
    /// ever compared them with the things they mirror. [backlog](../../docs/backlog.md) `C4`.
    /// </para>
    ///
    /// <para>
    /// **This measures the disagreement rather than fixing it**, because fixing it moves every
    /// scenario figure in the repository at once and that is a pass of its own. What it buys now is
    /// that the size of the drift is visible and cannot grow quietly (`D5`), and that whoever does
    /// the fix knows what they are taking on before they start.
    /// </para>
    /// </summary>
    public class CatalogDriftTests
    {
        private readonly ITestOutputHelper output;

        public CatalogDriftTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>What the catalogue stands in for, by name.</summary>
        private class Stand
        {
            public string Catalogue;
            public float CatalogueMass;
            public string Vanilla;
        }

        private static List<Stand> Stands()
        {
            return new List<Stand>
            {
                new Stand { Catalogue = "LightArmorBlock", CatalogueMass = Mass(Catalog.LightArmor()), Vanilla = "LargeBlockArmorBlock" },
                new Stand { Catalogue = "HeavyArmorBlock", CatalogueMass = Mass(Catalog.HeavyArmor()), Vanilla = "LargeHeavyBlockArmorBlock" },
                new Stand { Catalogue = "SmallReactor", CatalogueMass = Mass(Catalog.Reactor()), Vanilla = "LargeBlockSmallGenerator" },
                new Stand { Catalogue = "LargeReactor", CatalogueMass = Mass(Catalog.LargeReactor()), Vanilla = "LargeBlockLargeGenerator" },
                new Stand { Catalogue = "Battery", CatalogueMass = Mass(Catalog.Battery()), Vanilla = "LargeBlockBatteryBlock" },
                new Stand { Catalogue = "LargeThruster", CatalogueMass = Mass(Catalog.Thruster()), Vanilla = "LargeBlockLargeThrust" },
            };
        }

        private static float Mass(BlockModel model)
        {
            return model.Mass;
        }

        /// <summary>
        /// **The masses are pinned, wrong ones included.** Four of the six the catalogue stands in
        /// for are out by more than five per cent, and two of them by nearly a factor of four: the
        /// battery carries the *small-grid* battery's 1,040 kg under a large-grid block's name, and
        /// the large thruster is 10,000 kg against a real 43,200 — **4.32x**, which is worse than
        /// the "up to 4x" the backlog row recorded.
        ///
        /// Pinned rather than corrected, because thermal mass is what sets how fast a scenario
        /// block heats and moving six of them moves every scenario figure at once.
        /// </summary>
        [Fact]
        public void TheCatalogueMassesAreWhereTheyAreAndNoWorse()
        {
            List<string> drifted = new List<string>();
            float worst = 1f;

            foreach (Stand stand in Stands())
            {
                Vanilla.Block real = Vanilla.Find(stand.Vanilla);
                Assert.True(real != null, "no vanilla reference for " + stand.Vanilla);

                float ratio = real.Mass <= 0f ? 1f : stand.CatalogueMass / real.Mass;
                float apart = ratio > 1f ? ratio : 1f / ratio;
                if (apart > worst) worst = apart;

                if (apart > 1.05f)
                {
                    drifted.Add(string.Format("{0,-18}{1,10:n0} kg against {2}'s {3:n0} — {4:n2}x",
                        stand.Catalogue, stand.CatalogueMass, stand.Vanilla, real.Mass, apart));
                }
            }

            foreach (string line in drifted) output.WriteLine(line);

            Assert.True(drifted.Count == 4,
                "four of the six stand-ins are known to be out; " + drifted.Count + " are now:\n  "
                + string.Join("\n  ", drifted.ToArray()));

            Assert.True(worst < 4.4f,
                "the worst drift has grown past the thruster's known 4.32x: " + worst.ToString("n2"));
        }

        /// <summary>
        /// **The reactor's waste fraction is the drift that reaches furthest.** `Catalog` says a
        /// quarter of a reactor's output becomes heat; `Cubes.xml` — which it claims to mirror —
        /// says a hundredth. Any scenario quoting a reactor temperature quotes one twenty-five times
        /// over-driven, and no player will ever see it.
        /// </summary>
        [Fact]
        public void TheReactorsWasteFractionIsTwentyFiveTimesTheShippedOne()
        {
            BlockThermalProperties catalogue = Catalog.ReactorThermal();

            // The shipped reactor figure is the type entry rather than a subtype's, so it is read
            // from the derivation the game applies to any reactor.
            BlockThermalProperties real =
                ShippedBlocks.DeriveWithFunction(new List<BlockComponent>(), "Reactor");

            Assert.NotNull(real);
            output.WriteLine("catalogue ProducerWasteEnergy " + catalogue.ProducerWasteEnergy
                + " against the shipped " + real.ProducerWasteEnergy);

            Assert.Equal(0.25f, catalogue.ProducerWasteEnergy, 3);
            Assert.Equal(0.01f, real.ProducerWasteEnergy, 3);
        }

        /// <summary>
        /// And the one that is *not* drifted, so the two above are a measurement rather than a
        /// property of how the comparison is made: armour matches exactly, because nothing has ever
        /// had a reason to move it.
        /// </summary>
        [Fact]
        public void ArmourMatchesExactly()
        {
            Assert.Equal(Vanilla.Find("LargeBlockArmorBlock").Mass, Mass(Catalog.LightArmor()), 1);
            Assert.Equal(Vanilla.Find("LargeHeavyBlockArmorBlock").Mass, Mass(Catalog.HeavyArmor()), 1);
        }
    }
}
