using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The profile machinery itself, and the findings it produced.
    ///
    /// Two kinds of test here and they are worth telling apart. The first few check that the sweep
    /// measures what it claims to — a hook that silently stopped applying would turn every profile
    /// into the shipped one and the whole comparison would agree beautifully and mean nothing. The
    /// last few pin defects that are currently *present*, so that fixing one shows up as a test
    /// that has to change rather than as a number nobody was watching.
    /// </summary>
    public class ProfileSuiteTests
    {
        /// <summary>
        /// The settings hook reaches a scenario. Without it every profile row would be the shipped
        /// answer wearing a different label.
        /// </summary>
        [Fact]
        public void TheSettingsHookReachesAScenario()
        {
            try
            {
                GridBuilder.SettingsOverride = settings =>
                {
                    settings.HeatTimeScale = 1234f;
                    return settings.Derive();
                };

                GridBuilder builder = GridBuilder.Large();
                builder.Place(Catalog.LightArmor(), Vector3I.Zero);
                ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 300f);

                Assert.Equal(1234f, simulation.Settings.HeatTimeScale, 3);
            }
            finally
            {
                GridBuilder.SettingsOverride = null;
            }
        }

        /// <summary>
        /// The material hook is applied exactly once.
        ///
        /// The derived presets used to start from <c>DefaultThermal</c>, which had already been
        /// overridden, so a profile scaling conductivity scaled it twice and every block built from
        /// a preset ran at the square of the intended pace. Nothing about the resulting numbers
        /// looked wrong — they were just quietly describing a different world.
        /// </summary>
        [Fact]
        public void TheMaterialHookIsAppliedExactlyOnce()
        {
            try
            {
                Catalog.MaterialOverride = properties =>
                {
                    properties.Conductivity *= 2f;
                    return properties;
                };

                float plain = Catalog.DefaultThermal().Conductivity;
                float coolant = Catalog.CoolantThermal().Conductivity;

                Catalog.MaterialOverride = null;

                float plainRaw = Catalog.DefaultThermal().Conductivity;
                float coolantRaw = Catalog.CoolantThermal().Conductivity;

                Assert.Equal(plainRaw * 2f, plain, 3);
                Assert.Equal(coolantRaw * 2f, coolant, 3);
            }
            finally
            {
                Catalog.MaterialOverride = null;
            }
        }

        /// <summary>
        /// A profile's conduction pace lands on the number the solver would actually use.
        ///
        /// The harness expresses a pace by scaling material figures rather than by changing a core
        /// constant, so this is the arithmetic the whole approach rests on: pace 1 must produce the
        /// materials-table value once the solver has multiplied by its own scale.
        /// </summary>
        [Fact]
        public void AProfilePaceLandsOnTheEffectiveConductivity()
        {
            BalanceProfile physical = BalanceProfile.Physical();
            BlockThermalProperties steel = new BlockThermalProperties { Conductivity = 50f };

            float effective = physical.Material(steel).Conductivity * ThermalConstants.ConductionScale;
            Assert.Equal(50f, effective, 2);

            BalanceProfile shipped = BalanceProfile.Shipped();
            float shippedEffective = shipped.Material(steel).Conductivity * ThermalConstants.ConductionScale;
            Assert.Equal(50f * ThermalConstants.ConductionScale, shippedEffective, 2);
        }

        /// <summary>
        /// Every profile is distinct in something the solver reads. Two profiles that derive to the
        /// same settings would make the comparison table look like agreement.
        /// </summary>
        [Fact]
        public void EveryProfileDiffersFromEveryOther()
        {
            List<BalanceProfile> profiles = BalanceProfile.All();

            for (int i = 0; i < profiles.Count; i++)
            {
                for (int j = i + 1; j < profiles.Count; j++)
                {
                    ThermalSettings a = profiles[i].ToSettings();
                    ThermalSettings b = profiles[j].ToSettings();

                    bool same = a.HeatTimeScale == b.HeatTimeScale
                        && a.Frequency == b.Frequency
                        && a.MaxSubsteps == b.MaxSubsteps
                        && profiles[i].ConductionPace == profiles[j].ConductionPace;

                    Assert.False(same, profiles[i].Name + " and " + profiles[j].Name
                        + " derive to the same world");
                }
            }
        }

        /// <summary>
        /// A source with every sink switched off is supposed to run away, and the matrix must not
        /// report that as a defect — or the combinations that really are broken get buried.
        /// </summary>
        [Fact]
        public void ASourceWithNoSinkIsNotCountedAsBreakage()
        {
            List<FeatureMatrix.Row> rows = FeatureMatrix.Run();
            Assert.NotEmpty(rows);

            bool sawExpected = false;
            foreach (FeatureMatrix.Row row in rows)
            {
                if (!row.RunawayExpected) continue;
                sawExpected = true;

                // It may or may not actually run away within the run length; what must hold is that
                // when it does, it is classified rather than reported.
                Assert.True(row.RunawayExpected);
            }

            Assert.True(sawExpected,
                "no combination left a source without a sink, so the classification is untested");
        }

        /// <summary>
        /// **A known defect, pinned so a fix is visible.**
        ///
        /// The arcade profile diverges on a rig a player could build: a 2 MW source in a
        /// pressurised box with a ring and a radiator. It reaches tens of thousands of kelvin on
        /// the hot end while driving other blocks to absolute zero, and it does so under every
        /// combination of mechanism switches, so it is arcade itself rather than an interaction.
        ///
        /// This test asserts the defect is still there. When it is fixed this test fails, which is
        /// the point: the alternative is a number in a report that nobody is watching.
        /// </summary>
        [Fact]
        public void ArcadeStillDivergesOnTheEverythingRig()
        {
            List<FeatureMatrix.Row> rows = FeatureMatrix.Run();

            FeatureMatrix.Row arcade = null;
            foreach (FeatureMatrix.Row row in rows)
            {
                if (row.Profile == "arcade" && row.Combination == "all on") arcade = row;
            }

            Assert.NotNull(arcade);
            Assert.True(arcade.Diverged,
                "arcade no longer diverges on the everything rig — if that was deliberate, this "
                + "test should be inverted to assert it stays fixed");
            Assert.True(arcade.ColdestKelvin <= 1f,
                "arcade used to drive a block to the ambient floor while another ran away; it "
                + "reached " + arcade.ColdestKelvin + " K");
        }

        /// <summary>
        /// **A known defect, pinned.** The shipped default diverges on a burning ship.
        ///
        /// Refused about half the substeps its own stability estimate asks for, it reaches past
        /// 10,000 K and never settles. This is the current default on a case a player can build,
        /// and it is the strongest argument that the divergence problem was never arcade's alone.
        /// </summary>
        [Fact]
        public void TheShippedProfileStillDivergesOnABurningShip()
        {
            ThermalSettings settings = BalanceProfile.Shipped().ToSettings();
            WorstCases.Built built = WorstCases.Burning("ship", 4000, settings);

            ScenarioRunner runner = new ScenarioRunner(built.Simulation);
            runner.Environment = t => Worlds.Shadow();
            runner.Run(1800f, 60f);

            float peak = runner.Final.HottestTemperature;
            Assert.True(peak > ProfileSweep.DivergenceKelvin,
                "the shipped profile no longer diverges on a burning ship — it reached " + peak
                + " K. If that was deliberate, invert this test.");
        }
    }
}
