using System.Collections.Generic;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The cheap pass of the balance lab: what a ship is, before anything is stepped.
    ///
    /// Every figure here becomes a row in the matrix the balance criteria are decided from, so an
    /// error in one of them is an error in a default. The load model is the part most able to be
    /// wrong while looking right, and the tests are weighted accordingly.
    /// </summary>
    public class ScreeningTests
    {
        /// <summary>
        /// A ship accelerates one way at a time. The thrusters facing that way burn and the ones
        /// facing the other five do not, so the sustained load is the **strongest single
        /// direction** rather than the sum of every thruster on the hull.
        ///
        /// The first version of this pass summed them, which inflated the load on thruster-heavy
        /// ships several-fold: one 349-block hull reported 16,926 W/m² and actually runs at 2,244,
        /// a factor of 7.5. Every balance conclusion drawn from that would have been wrong in the
        /// same direction, and a corpus of ten thousand ships would have agreed with it
        /// enthusiastically.
        /// </summary>
        [Fact]
        public void ThrustIsTheStrongestDirectionRatherThanTheSumOfAllOfThem()
        {
            if (!GameBlocks.IsInstalled) return;

            ShipProfile balanced = Measure(new string[]
            {
                "LargeBlockArmorBlock:Forward:0",
                "LargeBlockSmallThrust:Forward:2",
                "LargeBlockSmallThrust:Backward:4",
            });

            if (balanced == null) return;

            // Two thrusters one way and two the other: the ship can burn one pair at a time.
            Assert.True(balanced.ThrustNewtons > 0f, "no thrust was found at all");
            Assert.True(balanced.ThrustNewtonsAllDirections > balanced.ThrustNewtons,
                "a ship with opposed thrusters reported the same figure both ways: "
                + balanced.ThrustNewtons + " against " + balanced.ThrustNewtonsAllDirections);
        }

        /// <summary>
        /// Thermal stress is the number the whole pass exists to produce, and it is a ratio: watts
        /// of heat over square metres of skin. A ship with no exposed surface reports zero rather
        /// than dividing by it.
        /// </summary>
        [Fact]
        public void ThermalStressIsWattsOverExposedArea()
        {
            ShipProfile profile = new ShipProfile { WasteWatts = 5000f, ExposedArea = 100f };
            Assert.Equal(50f, profile.ThermalStress, 3);

            Assert.Equal(0f, new ShipProfile { WasteWatts = 5000f, ExposedArea = 0f }.ThermalStress);
        }

        /// <summary>
        /// The equilibrium estimate has to invert Stefan–Boltzmann, or the screening pass is
        /// sorting ships by a number that means nothing. A grey body at 0.15 shedding 1 kW/m²
        /// sits near 1,000 K.
        /// </summary>
        [Fact]
        public void TheEquilibriumEstimateInvertsStefanBoltzmann()
        {
            ShipProfile profile = new ShipProfile { WasteWatts = 8500f, ExposedArea = 1f };
            float kelvin = profile.EquilibriumKelvin(0.15f);

            Assert.InRange(kelvin, 950f, 1050f);
            Assert.Equal(0f, new ShipProfile().EquilibriumKelvin());
        }

        /// <summary>
        /// A panel with no example at the end of an axis cannot say anything about that axis, so
        /// the extremes are taken before anything else. The largest ship in the corpus must be in
        /// any panel big enough to hold it.
        /// </summary>
        [Fact]
        public void ThePanelAlwaysHoldsTheExtremesOfEveryAxis()
        {
            List<ShipProfile> corpus = Corpus();
            List<Specimens.Scored> panel = Specimens.Select(corpus, 8);

            int largest = 0;
            for (int i = 0; i < corpus.Count; i++)
            {
                if (corpus[i].Blocks > corpus[largest].Blocks) largest = i;
            }

            bool found = false;
            foreach (Specimens.Scored scored in panel)
            {
                if (ReferenceEquals(scored.Ship, corpus[largest])) found = true;
            }

            Assert.True(found, "the largest ship in the corpus is not in the panel");
        }

        /// <summary>
        /// Selection is by coverage rather than by frequency. Twenty near-identical ships and one
        /// unusual one must not produce a panel of twenty duplicates: the interior of a cluster is
        /// predictable from its edges, and the edges are where a balance figure fails first.
        /// </summary>
        [Fact]
        public void ACrowdOfNearDuplicatesDoesNotCrowdOutTheOutlier()
        {
            List<ShipProfile> corpus = new List<ShipProfile>();

            for (int i = 0; i < 20; i++)
            {
                corpus.Add(new ShipProfile
                {
                    Name = "clone " + i,
                    Blocks = 100 + i,
                    Mass = 10000f,
                    ExposedArea = 200f,
                    WasteWatts = 50000f,
                    PeakSubstepDemand = 2f,
                });
            }

            ShipProfile outlier = new ShipProfile
            {
                Name = "outlier",
                Blocks = 9000,
                Mass = 6000000f,
                ExposedArea = 80000f,
                WasteWatts = 400000f,
                PeakSubstepDemand = 40f,
                Large = true,
            };
            corpus.Add(outlier);

            List<Specimens.Scored> panel = Specimens.Select(corpus, 3);

            bool found = false;
            foreach (Specimens.Scored scored in panel)
            {
                if (ReferenceEquals(scored.Ship, outlier)) found = true;
            }

            Assert.True(found, "a panel of three took three clones and left the outlier out");
        }

        /// <summary>
        /// The redundancy measure is what says when a corpus has stopped being worth growing:
        /// ships that sit on top of another in feature space are the ones the next thousand
        /// downloads will mostly be.
        /// </summary>
        [Fact]
        public void NearDuplicatesAreReportedAsRedundantAndTheOutlierIsNot()
        {
            List<ShipProfile> corpus = new List<ShipProfile>();

            for (int i = 0; i < 5; i++)
            {
                corpus.Add(new ShipProfile
                {
                    Name = "clone " + i,
                    Blocks = 100,
                    Mass = 10000f,
                    ExposedArea = 200f,
                    WasteWatts = 50000f,
                    PeakSubstepDemand = 2f,
                });
            }

            ShipProfile outlier = new ShipProfile
            {
                Name = "outlier",
                Blocks = 9000,
                Mass = 6000000f,
                ExposedArea = 80000f,
                WasteWatts = 400000f,
                PeakSubstepDemand = 40f,
            };
            corpus.Add(outlier);

            List<KeyValuePair<ShipProfile, double>> redundant = Specimens.Redundant(corpus, 0.05d);

            Assert.Equal(5, redundant.Count);
            foreach (KeyValuePair<ShipProfile, double> entry in redundant)
            {
                Assert.NotEqual("outlier", entry.Key.Name);
            }
        }

        /// <summary>
        /// A panel that is the whole corpus serves every ship perfectly. Anything less than that
        /// is a fidelity above zero, and a panel that grows can only improve it — which is the
        /// property that makes the number usable as a stopping rule.
        /// </summary>
        [Fact]
        public void FidelityImprovesAsThePanelGrows()
        {
            List<ShipProfile> corpus = Corpus();

            double small = Specimens.Fidelity(corpus, Specimens.Select(corpus, 3));
            double large = Specimens.Fidelity(corpus, Specimens.Select(corpus, 10));
            double whole = Specimens.Fidelity(corpus, Specimens.Select(corpus, corpus.Count));

            Assert.True(large <= small + 1e-9d, "a bigger panel served the corpus worse");
            Assert.Equal(0d, whole, 6);
        }

        /// <summary>A corpus spanning several orders of magnitude on every axis.</summary>
        private static List<ShipProfile> Corpus()
        {
            List<ShipProfile> corpus = new List<ShipProfile>();

            for (int i = 0; i < 24; i++)
            {
                corpus.Add(new ShipProfile
                {
                    Name = "ship " + i,
                    Blocks = 20 + (i * i * 15),
                    Mass = 1000f + (i * 250000f),
                    ExposedArea = 30f + (i * 3000f),
                    ExposedFraction = 0.5f + (i % 5) * 0.1f,
                    WasteWatts = i * 25000f,
                    PeakSubstepDemand = 1.2f + (i * 1.7f),
                    Large = (i % 2) == 0,
                });
            }

            return corpus;
        }

        /// <summary>
        /// Builds a tiny ship out of named blocks and measures it, or returns null when a subtype
        /// this test wants is not in the installed game.
        ///
        /// Each entry is <c>subtype:direction:cell</c>, placed along the x axis so nothing is
        /// buried and every block keeps its exposure.
        /// </summary>
        private static ShipProfile Measure(string[] entries)
        {
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.BySubtype();

            System.Text.StringBuilder xml = new System.Text.StringBuilder();
            foreach (string entry in entries)
            {
                string[] parts = entry.Split(':');
                if (!definitions.ContainsKey(parts[0])) return null;

                xml.Append("<MyObjectBuilder_CubeBlock xsi:type=\"MyObjectBuilder_CubeBlock\">")
                   .Append("<SubtypeName>").Append(parts[0]).Append("</SubtypeName>")
                   .Append("<Min x=\"").Append(parts[2]).Append("\" y=\"0\" z=\"0\" />")
                   .Append("<BlockOrientation Forward=\"").Append(parts[1]).Append("\" Up=\"Up\" />")
                   .Append("</MyObjectBuilder_CubeBlock>");
            }

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "thermal-screen-" + System.Guid.NewGuid().ToString("n"));
            System.IO.Directory.CreateDirectory(path);

            string file = System.IO.Path.Combine(path, "bp.sbc");
            System.IO.File.WriteAllText(file,
                "<?xml version=\"1.0\"?>\n" +
                "<Definitions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<ShipBlueprints><ShipBlueprint>" +
                "<Id Type=\"MyObjectBuilder_ShipBlueprintDefinition\" Subtype=\"T\" />" +
                "<CubeGrids><CubeGrid><DisplayName>T</DisplayName>" +
                "<GridSizeEnum>Large</GridSizeEnum>" +
                "<CubeBlocks>" + xml + "</CubeBlocks>" +
                "</CubeGrid></CubeGrids></ShipBlueprint></ShipBlueprints></Definitions>");

            List<Blueprints.Ship> ships = Blueprints.Read(file);
            return ships.Count == 0 ? null : ShipProfile.Measure(ships[0]);
        }
    }
}
