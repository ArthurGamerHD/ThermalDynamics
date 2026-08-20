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

            // The ladder has two axes, and this is the pace one: responsive and arcade run the
            // clock fast, the other three run it at the tuned pace. Both fast profiles must carry
            // heat further in the same wall time than either of the profiles they are built from.
            Assert.True(reached[ThermalProfiles.Responsive] > reached[ThermalProfiles.Simulation],
                "responsive is simulation with the clock run fast, so it must outrun it");
            Assert.True(reached[ThermalProfiles.Arcade] > reached[ThermalProfiles.Optimized],
                "arcade is optimized with the clock run fast, so it must outrun it");

            // And the accuracy axis: optimized is simulation with the cost dials tuned, not a
            // different physics, so heat must travel about as far under both. Ten per cent is the
            // room the substep cap is allowed to cost.
            float ratio = reached[ThermalProfiles.Optimized]
                / (float)Math.Max(1, reached[ThermalProfiles.Simulation]);

            Assert.True(ratio > 0.9f && ratio < 1.1f,
                "optimized should reach about as far as simulation, was " + ratio.ToString("n2"));
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
    }
}
