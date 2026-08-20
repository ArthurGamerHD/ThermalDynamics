using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// A profile's definition overlay: what a preset changes about blocks, planets and coolant
    /// loops on top of what the definition files loaded.
    ///
    /// The point of it is stiffness. A block's demand on the integrator is its conductance over its
    /// heat capacity, so a light fitting asks for tens of substeps while the armour around it asks
    /// for one — and a profile granting three is integrating that block outside the range its own
    /// physics is stable in. The substep cap already floors exactly those blocks; this reaches the
    /// properties a capacity floor cannot, and lets a preset ship a balance rather than only a
    /// tolerance.
    /// </summary>
    public class ThermalOverlayTests
    {
        private static ThermalOverlay Overlay(params ThermalOverride[] blockValues)
        {
            ThermalOverlay overlay = new ThermalOverlay();
            overlay.Blocks.Add(new BlockOverride { Values = new List<ThermalOverride>(blockValues) });
            return overlay;
        }

        [Fact]
        public void AnOverlaySetsOnlyWhatItNames()
        {
            BlockThermalProperties block = new BlockThermalProperties();
            float untouched = block.Emissivity;

            Assert.True(Overlay(new ThermalOverride("SpecificHeat", 900f)).ApplyTo(block, "Anything"));

            Assert.Equal(900f, block.SpecificHeat);
            Assert.Equal(untouched, block.Emissivity);
        }

        /// <summary>
        /// An entry with no subtype is how a profile says "every block" without listing the game's
        /// whole catalogue — and it is the entry that matters, because the blocks that make a world
        /// stiff are usually the ones no definition file mentions.
        /// </summary>
        [Fact]
        public void AnEntryWithNoSubtypeReachesEveryBlock()
        {
            ThermalOverlay overlay = Overlay(new ThermalOverride("Conductivity", 2f));

            BlockThermalProperties light = new BlockThermalProperties();
            BlockThermalProperties armour = new BlockThermalProperties();

            Assert.True(overlay.ApplyTo(light, "LargeBlockLight_1corner"));
            Assert.True(overlay.ApplyTo(armour, "LargeBlockArmorBlock"));

            Assert.Equal(2f, light.Conductivity);
            Assert.Equal(2f, armour.Conductivity);
        }

        [Fact]
        public void ANamedSubtypeReachesOnlyThatBlock()
        {
            ThermalOverlay overlay = new ThermalOverlay();
            overlay.Blocks.Add(new BlockOverride
            {
                Subtype = "LargeBlockLight_1corner",
                Values = { new ThermalOverride("SpecificHeat", 900f) },
            });

            BlockThermalProperties light = new BlockThermalProperties();
            BlockThermalProperties armour = new BlockThermalProperties();
            float shipped = armour.SpecificHeat;

            Assert.True(overlay.ApplyTo(light, "LargeBlockLight_1corner"));
            Assert.False(overlay.ApplyTo(armour, "LargeBlockArmorBlock"));

            Assert.Equal(900f, light.SpecificHeat);
            Assert.Equal(shipped, armour.SpecificHeat);
        }

        /// <summary>Case is how a subtype is spelled, not what it means.</summary>
        [Fact]
        public void SubtypeMatchingIgnoresCase()
        {
            ThermalOverlay overlay = new ThermalOverlay();
            overlay.Blocks.Add(new BlockOverride
            {
                Subtype = "largeblocklight_1corner",
                Values = { new ThermalOverride("Emissivity", 0.9f) },
            });

            BlockThermalProperties light = new BlockThermalProperties();
            Assert.True(overlay.ApplyTo(light, "LargeBlockLight_1corner"));
            Assert.Equal(0.9f, light.Emissivity);
        }

        [Fact]
        public void LaterEntriesWinSoASubtypeCanRefineTheDefault()
        {
            ThermalOverlay overlay = new ThermalOverlay();
            overlay.Blocks.Add(new BlockOverride
            {
                Values = { new ThermalOverride("SpecificHeat", 450f) },
            });
            overlay.Blocks.Add(new BlockOverride
            {
                Subtype = "InteriorLight",
                Values = { new ThermalOverride("SpecificHeat", 900f) },
            });

            BlockThermalProperties light = new BlockThermalProperties();
            overlay.ApplyTo(light, "InteriorLight");
            Assert.Equal(900f, light.SpecificHeat);

            BlockThermalProperties other = new BlockThermalProperties();
            overlay.ApplyTo(other, "SomethingElse");
            Assert.Equal(450f, other.SpecificHeat);
        }

        [Fact]
        public void LoopAndPlanetValuesApplyToo()
        {
            ThermalOverlay overlay = new ThermalOverlay
            {
                Loops = new BlockOverride
                {
                    Values = { new ThermalOverride("LargeGridFlowRate", 4f) },
                },
            };
            overlay.Planets.Add(new BlockOverride
            {
                Values = { new ThermalOverride("ConvectionCoefficient", 20f) },
            });

            LoopThermalProperties loops = LoopThermalProperties.Default();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            Assert.True(overlay.ApplyTo(loops));
            Assert.True(overlay.ApplyTo(planet, "EarthLike"));

            Assert.Equal(4f, loops.LargeGridFlowRate);
            Assert.Equal(20f, planet.ConvectionCoefficient);
        }

        /// <summary>
        /// A hand-written file with a misspelled property would otherwise do nothing and say
        /// nothing, which is a long afternoon. Every name is checked against the properties that
        /// exist, and the ones that do not are reported.
        /// </summary>
        [Fact]
        public void AMisspelledPropertyIsReportedRatherThanIgnored()
        {
            ThermalOverlay overlay = new ThermalOverlay();
            overlay.Blocks.Add(new BlockOverride
            {
                Values =
                {
                    new ThermalOverride("SpecificHeat", 900f),
                    new ThermalOverride("SpecificHeet", 900f),
                },
            });

            List<string> bad = overlay.Unknown();

            Assert.Single(bad);
            Assert.Contains("SpecificHeet", bad[0]);
        }

        /// <summary>
        /// The reference profile has no overlay, and an empty one has to be recognisable as such:
        /// simulation runs the shipped definitions exactly, so it stays what everything else is
        /// measured against.
        /// </summary>
        [Fact]
        public void AnOverlayThatChangesNothingKnowsItIsEmpty()
        {
            Assert.True(new ThermalOverlay().IsEmpty);
            Assert.False(Overlay(new ThermalOverride("Conductivity", 1f)).IsEmpty);
        }

        [Fact]
        public void AnEmptyOverlayLeavesEveryPropertyAlone()
        {
            BlockThermalProperties block = new BlockThermalProperties();
            float conductivity = block.Conductivity;
            float specificHeat = block.SpecificHeat;

            Assert.False(new ThermalOverlay().ApplyTo(block, "Anything"));

            Assert.Equal(conductivity, block.Conductivity);
            Assert.Equal(specificHeat, block.SpecificHeat);
        }

        /// <summary>
        /// The arithmetic the whole idea rests on: stiffness is conductance over heat capacity, so
        /// raising specific heat is what buys a coarse profile its stability.
        /// </summary>
        [Fact]
        public void RaisingSpecificHeatIsWhatMakesAStiffBlockAffordable()
        {
            BlockThermalProperties before = new BlockThermalProperties { SpecificHeat = 2f };
            BlockThermalProperties after = new BlockThermalProperties { SpecificHeat = 2f };

            Overlay(new ThermalOverride("SpecificHeat", 900f)).ApplyTo(after, "InteriorLight");

            // Capacity is mass times specific heat, and demand falls with capacity, so the same
            // block on the same grid asks for 450 times fewer substeps.
            Assert.Equal(450f, after.SpecificHeat / before.SpecificHeat, 3);
        }

        /// <summary>
        /// The overlay's whole justification, measured rather than asserted: raising a block's
        /// specific heat lowers what it demands of the integrator.
        ///
        /// A profile that grants three substeps to a ship whose fittings ask for twenty is
        /// integrating those blocks outside the range their own physics is stable in. This is the
        /// arithmetic that fixes it — stiffness is conductance over capacity, and an overlay is
        /// how a preset raises the capacity of the blocks that have too little.
        /// </summary>
        [Fact]
        public void AnOverlayThatRaisesCapacityLowersWhatTheStepIsAskedFor()
        {
            float before = DemandWith(null);
            float after = DemandWith(Overlay(new ThermalOverride("SpecificHeat", 900f)));

            Assert.True(before > 0f, "the rig has to ask for something to begin with");
            Assert.True(after < before,
                "raising capacity must lower demand: " + before.ToString("n2")
                    + " became " + after.ToString("n2"));

            // Demand is inversely proportional to capacity, so a 450-fold capacity buys a
            // 450-fold reduction. Loose bounds, because the grid's own conductance is unchanged
            // and the estimate mixes several terms.
            Assert.True(after < before / 100f,
                "the fall should be proportional to the capacity: " + before.ToString("n2")
                    + " became " + after.ToString("n2"));
        }

        /// <summary>Substeps a stiff little grid asks for, with an overlay applied or without.</summary>
        private static float DemandWith(ThermalOverlay overlay)
        {
            // Deliberately stiff: a light body with a metal's conductivity, which is the shape of
            // block that sets the pace of a whole ship.
            BlockThermalProperties properties = new BlockThermalProperties
            {
                Conductivity = 50f,
                SpecificHeat = 2f,
            };

            if (overlay != null) overlay.ApplyTo(properties, "Fitting");

            ThermalSettings settings = new ThermalSettings
            {
                Frequency = 4,
                HeatTimeScale = 225f,
                MaxSubsteps = 4096,
                MaxSubstepsPerBlock = 0,
                MaxElementVisitsPerStep = 0,
                EnableEnvironment = false,
            };
            settings.Derive();

            BlockModel model = BlockModel.Solid("Fitting", Vector3I.One, 16f, properties);

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(model, Vector3I.Zero, new Vector3I(4, 1, 1));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            simulation.Solver.Nodes[0].Temperature = 900f;
            simulation.StepExact(1, Worlds.Shadow());

            return simulation.Solver.LastRequiredSubsteps;
        }
    }
}
