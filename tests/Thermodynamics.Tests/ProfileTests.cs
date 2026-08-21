using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The shipped settings profiles, and the property that makes the arcade end of them possible.
    ///
    /// A profile is a promise that a bundle of numbers is safe to run, and "safe" here is not
    /// rhetorical: the fast profiles deliberately refuse the substeps the stability estimate asks
    /// for and lean on the overshoot clamps instead. Before those clamps bounded a node with more
    /// than one neighbour, the same settings reached 1.5e22 K in twenty seconds. So every profile
    /// is run with the environment on and checked for staying in the world, and the defect that
    /// made that necessary has a test of its own.
    /// </summary>
    [Trait("speed", "slow")]
    public class ProfileTests
    {
        private readonly ITestOutputHelper output;

        public ProfileTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation Build(string profile)
        {
            ThermalSettings settings = new ThermalSettings();
            Assert.True(ThermalProfiles.Apply(settings, profile), "unknown profile " + profile);

            // The work budget shortens steps on its own; a profile has to stand up without it.
            settings.MaxElementVisitsPerStep = 0;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            int index = 0;
            foreach (Vector3I cell in GridShapes.Ship(fuselageLength: 18, fuselageWidth: 7, bulkheadSpacing: 6))
            {
                builder.Place((index++ % 8) == 0 ? Catalog.Grating() : Catalog.HeavyArmor(), cell);
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            while (simulation.HasPendingWork) simulation.Update(1f / 60f, Worlds.Shadow());
            return simulation;
        }

        public static IEnumerable<object[]> Profiles()
        {
            for (int i = 0; i < ThermalProfiles.Names.Length; i++)
            {
                yield return new object[] { ThermalProfiles.Names[i] };
            }
        }

        [Theory]
        [MemberData(nameof(Profiles))]
        public void EveryProfileIsValidAndDescribed(string profile)
        {
            ThermalSettings settings = new ThermalSettings();
            Assert.True(ThermalProfiles.Apply(settings, profile));
            Assert.Empty(settings.Validate());
            Assert.False(string.IsNullOrEmpty(ThermalProfiles.Describe(profile)));
            Assert.True(ThermalProfiles.IsKnown(profile));
        }

        [Fact]
        public void AnUnknownProfileIsRefusedRatherThanGuessedAt()
        {
            ThermalSettings settings = new ThermalSettings();
            int frequency = settings.Frequency;

            Assert.False(ThermalProfiles.Apply(settings, "ultra"));
            Assert.False(ThermalProfiles.IsKnown("ultra"));
            Assert.Equal(frequency, settings.Frequency);
        }

        /// <summary>
        /// Every profile, on a hull radiating into space with a hot spot in it, must stay in the
        /// world for a minute of play.
        ///
        /// This is the test that would have caught what the clamps were missing. The fast profiles
        /// clamp on every step by design, so "clamped" is not the failure — leaving the range a
        /// temperature can physically take is.
        /// </summary>
        [Theory]
        [MemberData(nameof(Profiles))]
        public void EveryProfileStaysPhysicalWithTheEnvironmentOn(string profile)
        {
            ThermalSimulation simulation = Build(profile);

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++) nodes[i].Temperature = 293.15f;
            nodes[nodes.Count / 2].Temperature = 1200f;

            EnvironmentSample sample = Worlds.Space(new Vector3(0f, 1f, 0f));

            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (int frame = 0; frame < 60 * 60; frame++)
            {
                simulation.Update(1f / 60f, sample);

                for (int i = 0; i < nodes.Count; i++)
                {
                    float t = nodes[i].Temperature;
                    Assert.False(float.IsNaN(t) || float.IsInfinity(t),
                        profile + " sent block " + i + " to " + t + " after " + frame + " frames");

                    if (t < lowest) lowest = t;
                    if (t > highest) highest = t;
                }
            }

            output.WriteLine(profile + ": " + lowest.ToString("n1") + " K to "
                + highest.ToString("n1") + " K over a minute.");

            // Nothing may go colder than the sky it radiates into, or hotter than it started —
            // there is no heat source on this hull, so anything above 1200 K was invented.
            Assert.True(lowest > 1f, profile + " drove a block to " + lowest + " K");
            Assert.True(highest <= 1201f, profile + " invented heat, reaching " + highest + " K");
        }

        /// <summary>
        /// The profiles have to actually differ in the way they claim: the responsive ones move
        /// heat further in the same wall-clock time than the accurate ones.
        ///
        /// Measured on a run of blocks with one end held hot, which is the thing a player means by
        /// "responsive" — how quickly the far side of a ship notices.
        /// </summary>
        [Fact]
        public void TheFastProfilesReallyMoveHeatFurtherInTheSameTime()
        {
            const float seconds = 8f;
            const int length = 60;

            Dictionary<string, int> reached = new Dictionary<string, int>();
            for (int i = 0; i < ThermalProfiles.Names.Length; i++)
            {
                string profile = ThermalProfiles.Names[i];

                ThermalSettings settings = new ThermalSettings();
                ThermalProfiles.Apply(settings, profile);
                settings.MaxElementVisitsPerStep = 0;
                settings.Derive();

                LoadBenchmarks.ReachRow row = LoadBenchmarks.Reach(profile, settings, seconds, length);
                reached[profile] = row.BlocksReached;

                output.WriteLine(profile + ": heat crossed " + row.BlocksReached + " blocks in "
                    + seconds + " s, at " + row.WorkPerRealSecond.ToString("n0") + " work/s.");
            }

            // The ladder has two axes. This is the pace one: responsive and arcade run the clock
            // at the tuned pace, the other three run at real time, and real time is slow — a hull
            // moves a fraction of a kelvin a minute, which is the claim `simulation` exists to
            // make and the reason it is not how anyone plays.
            Assert.True(reached[ThermalProfiles.Responsive] > reached[ThermalProfiles.Simulation],
                "responsive is simulation with the clock run fast, so it must outrun it");
            Assert.True(reached[ThermalProfiles.Arcade] > reached[ThermalProfiles.Optimized],
                "arcade is optimized with the clock run fast, so it must outrun it");

            // And the accuracy axis, checked at a pace where it can be measured: arcade is
            // responsive with the cost dials tuned, not different physics, so heat must travel
            // about as far under both. Comparing simulation with optimized would be comparing two
            // numbers that are both nearly zero, which proves nothing.
            float ratio = reached[ThermalProfiles.Arcade]
                / (float)Math.Max(1, reached[ThermalProfiles.Responsive]);

            Assert.True(ratio > 0.9f && ratio < 1.1f,
                "arcade should reach about as far as responsive, was " + ratio.ToString("n2"));
        }

        /// <summary>
        /// A node with several neighbours must stay bounded when the substeps it asked for are
        /// refused — the defect the arcade profiles are built on top of.
        ///
        /// The per-link clamp caps each exchange at the energy that equalises <em>that pair</em>,
        /// which bounds a node with one neighbour exactly and a node with six not at all: each
        /// neighbour is separately entitled to move it the whole way, so it lands six times past
        /// where it should and comes back further still. A stick of blocks was always fine; a
        /// lattice reached 1.5e22 K on the same settings, and two pairings reached infinity.
        ///
        /// The fix scales each exchange by the stricter of its two ends, so it stays equal and
        /// opposite and energy is still conserved. This test is the shape that failed: one block
        /// surrounded on all six sides, one substep, transfer far past what that substep can carry.
        /// </summary>
        [Fact]
        public void ANodeWithSixNeighboursStaysBoundedWhenItsSubstepsAreRefused()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Frequency = 4;
            settings.HeatTimeScale = 100000f;
            settings.MaxSubsteps = 1;
            settings.MaxElementVisitsPerStep = 0;
            settings.EnableEnvironment = false;
            settings.EnableRadiation = false;
            settings.EnableConvection = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            BlockModel armour = Catalog.HeavyArmor();

            builder.Place(armour, Vector3I.Zero);
            for (int face = 0; face < Face.Count; face++)
            {
                builder.Place(armour, Face.Offsets[face]);
            }

            ThermalSimulation simulation = builder.BuildSimulation(settings, 300f);
            simulation.Solver.GetNodeAt(Vector3I.Zero).Temperature = 1000f;

            Assert.Equal(6, simulation.Solver.GetNodeAt(Vector3I.Zero).LinkCount);

            float before = simulation.Solver.TotalEnergy;
            simulation.StepExact(200, Worlds.Shadow());
            float after = simulation.Solver.TotalEnergy;

            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                float t = nodes[i].Temperature;
                Assert.False(float.IsNaN(t) || float.IsInfinity(t), "block " + i + " diverged");
                Assert.InRange(t, 1f, 1001f);
            }

            output.WriteLine("six neighbours, one substep, transfer x100,000: energy "
                + before.ToString("n0") + " -> " + after.ToString("n0"));

            float drift = Math.Abs(after - before) / Math.Max(1f, Math.Abs(before));
            Assert.True(drift < 1e-3f,
                "scaling an exchange must keep it equal and opposite; energy moved by " + drift);
        }

        /// <summary>
        /// A fresh world runs the responsive profile, value for value.
        ///
        /// Without this the two drift: someone tunes a default, the menu starts reading "custom"
        /// on a fresh install, and applying the profile the game says it is already on silently
        /// changes the world. Every value the profile sets is compared, which is the only set that
        /// can disagree.
        /// </summary>
        [Fact]
        public void DefaultsMatchTheResponsiveProfile()
        {
            ThermalSettings shipped = new ThermalSettings();
            shipped.Derive();

            ThermalSettings responsive = new ThermalSettings();
            Assert.True(ThermalProfiles.Apply(responsive, ThermalProfiles.Responsive));

            Assert.Equal(responsive.Frequency, shipped.Frequency);
            Assert.Equal(responsive.SimulationSpeed, shipped.SimulationSpeed);
            Assert.Equal(responsive.HeatTimeScale, shipped.HeatTimeScale);
            Assert.Equal(responsive.MaxSubsteps, shipped.MaxSubsteps);
            Assert.Equal(responsive.MaxSubstepsPerBlock, shipped.MaxSubstepsPerBlock);
            Assert.Equal(responsive.MaxElementVisitsPerStep, shipped.MaxElementVisitsPerStep);
            Assert.Equal(responsive.ClampConductionOvershoot, shipped.ClampConductionOvershoot);
            Assert.Equal(responsive.ClampEnvironmentOvershoot, shipped.ClampEnvironmentOvershoot);
            Assert.Equal(responsive.SolarSelfShadowing, shipped.SolarSelfShadowing);
            Assert.Equal(responsive.EnableRoomAir, shipped.EnableRoomAir);
        }

        /// <summary>
        /// The default is playable, which means bounded: a step that lands whole in one frame is
        /// what the budget exists to prevent, and a world nobody configured should not be able to
        /// do it. Simulation is the one profile allowed to be unbounded, because it is a reference
        /// rather than a way to play.
        /// </summary>
        [Fact]
        public void TheDefaultIsFrameBoundedAndOnlySimulationIsNot()
        {
            ThermalSettings shipped = new ThermalSettings();
            Assert.True(shipped.MaxElementVisitsPerStep > 0);

            ThermalSettings simulation = new ThermalSettings();
            ThermalProfiles.Apply(simulation, ThermalProfiles.Simulation);
            Assert.Equal(0, simulation.MaxElementVisitsPerStep);

            foreach (string name in new[]
            {
                ThermalProfiles.Responsive, ThermalProfiles.Optimized,
                ThermalProfiles.Simlite, ThermalProfiles.Arcade,
            })
            {
                ThermalSettings settings = new ThermalSettings();
                ThermalProfiles.Apply(settings, name);

                Assert.True(settings.MaxElementVisitsPerStep > 0,
                    name + " is a profile people play on, so it must bound a step");
            }
        }

        /// <summary>
        /// The ladder's accuracy axis, as a shape rather than as five separate numbers.
        ///
        /// Simulation resolves whatever it is asked for, optimized takes the field-tuned pair, and
        /// simlite gives up more still. A change that reorders these has changed what the presets
        /// mean, which is worth failing a build over.
        /// </summary>
        [Fact]
        public void TheAccuracyAxisDescendsInOrder()
        {
            int simulation = SubstepCeiling(ThermalProfiles.Simulation);
            int optimized = SubstepCeiling(ThermalProfiles.Optimized);
            int simlite = SubstepCeiling(ThermalProfiles.Simlite);

            Assert.True(simulation > optimized, "simulation must resolve more than optimized");
            Assert.True(optimized > simlite, "optimized must resolve more than simlite");

            // And the floor moves with it: simulation floors nothing, the other two do.
            Assert.Equal(0, PerBlockCap(ThermalProfiles.Simulation));
            Assert.True(PerBlockCap(ThermalProfiles.Optimized) > 0);
            Assert.True(PerBlockCap(ThermalProfiles.Simlite) > 0);
        }

        /// <summary>
        /// The pace axis: three profiles run the clock at real time and two run it fast. Which is
        /// which is the thing a player chooses between, so it is pinned by name.
        /// </summary>
        [Fact]
        public void ThePaceAxisSeparatesRealTimeFromPlayable()
        {
            Assert.Equal(1f, Pace(ThermalProfiles.Simulation));
            Assert.Equal(1f, Pace(ThermalProfiles.Optimized));
            Assert.Equal(1f, Pace(ThermalProfiles.Simlite));

            Assert.True(Pace(ThermalProfiles.Responsive) > 1f);
            Assert.Equal(Pace(ThermalProfiles.Responsive), Pace(ThermalProfiles.Arcade));
        }

        private static ThermalSettings Applied(string profile)
        {
            ThermalSettings settings = new ThermalSettings();
            Assert.True(ThermalProfiles.Apply(settings, profile));
            return settings;
        }

        private static int SubstepCeiling(string profile)
        {
            return Applied(profile).MaxSubsteps;
        }

        private static int PerBlockCap(string profile)
        {
            return Applied(profile).MaxSubstepsPerBlock;
        }

        private static float Pace(string profile)
        {
            return Applied(profile).HeatTimeScale;
        }
    }
}
