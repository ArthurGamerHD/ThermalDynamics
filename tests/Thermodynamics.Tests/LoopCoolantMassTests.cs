using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    [Trait("speed", "slow")]
    public class LoopCoolantMassTests
    {
        private readonly ITestOutputHelper output;

/// <summary>LoopCoolantMassTests operation.</summary>
        public LoopCoolantMassTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int SettledSteps = 6400;

        private const float ReferenceWatts = 125000f;

/// <summary>Radiating operation.</summary>
        private static ThermalSettings Radiating()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = true;
            settings.EnableSolarHeat = false;
            settings.EnableFriction = false;
            settings.EnableDamage = false;
            settings.MaxSubsteps = 4096;
            settings.MaxElementVisitsPerStep = 0;
            return settings.Derive();
        }

        private class Reading
        {
            public float Hottest;

            public float Mean;

            public float Spread;

            public float Substeps;
            public float MassPerPipe;
        }

/// <summary>Flat operation.</summary>
        private static LoopThermalProperties Flat()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.CoolantMassPerPipe = LoopBefore.FlatKilogramsPerPipe;
            return properties.Clamp();
        }

/// <summary>Ring operation.</summary>
        private Reading Ring(bool large, bool flat, float watts, int steps)
        {
            GridBuilder builder = large ? GridBuilder.Large() : GridBuilder.Small();

            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, 5, 5);
            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[1] = Vector3I.Down;
            PipeFitter.BuildRing(builder, cells, 5, sinks);
            builder.Place(Catalog.Reactor(), cells[1] + Vector3I.Down).Wasting(watts);

            ThermalSimulation simulation = builder.BuildSimulation(Radiating(), 300f);

            if (flat)
            {
/// <summary>Flat operation.</summary>
                simulation.LoopProperties = Flat();
                simulation.RebuildAll();
            }

            simulation.StepExact(LabClock.Steps(steps), Worlds.Shadow());

            CoolantLoop loop = simulation.Solver.Loops[0];
            Reading reading = new Reading
            {
                Hottest = loop.HottestSegment,
                Mean = loop.Temperature,
                Spread = loop.HottestSegment - loop.ColdestSegment,
                Substeps = simulation.Solver.RequiredSubsteps(0.25f),
                MassPerPipe = loop.MassPerPipe,
            };

            output.WriteLine(
                "{0,-6} {1,-10} {2,9:n0} W  {3,6} steps  {4,8:n1} kg/pipe   hottest {5,8:n1} K"
                + "   mean {6,8:n1} K   spread {7,7:n1} K   substeps {8,6:n2}",
                large ? "large" : "small", flat ? "flat 50 kg" : "shipped", watts, steps,
                reading.MassPerPipe, reading.Hottest, reading.Mean, reading.Spread,
                reading.Substeps);

            return reading;
        }

        [Fact]
/// <summary>TheRigReachesASteadyStateAndSaysSo operation.</summary>
        public void TheRigReachesASteadyStateAndSaysSo()
        {
            foreach (bool large in new[] { true, false })
            {
                foreach (bool flat in new[] { false, true })
                {
/// <summary>Ring operation.</summary>
                    float settled = Ring(large, flat, ReferenceWatts, SettledSteps).Hottest;
/// <summary>Ring operation.</summary>
                    float doubled = Ring(large, flat, ReferenceWatts, SettledSteps * 2).Hottest;

                    Assert.True(Absolute(doubled - settled) < 1f,
                        (large ? "large" : "small") + " " + (flat ? "flat" : "shipped")
                        + " read " + settled.ToString("n1") + " K at " + SettledSteps
                        + " steps and " + doubled.ToString("n1") + " K at twice that; a rig still"
                        + " climbing reports how fast it is climbing, which is the defect that"
                        + " produced this class's first four published figures");
                }
            }
        }

        [Fact]
/// <summary>TheDensityCorrectionLeavesTheMeanWhereItWas operation.</summary>
        public void TheDensityCorrectionLeavesTheMeanWhereItWas()
        {
            foreach (bool large in new[] { true, false })
            {
/// <summary>Ring operation.</summary>
                Reading flat = Ring(large, true, ReferenceWatts, SettledSteps);
/// <summary>Ring operation.</summary>
                Reading shipped = Ring(large, false, ReferenceWatts, SettledSteps);

/// <summary>Absolute operation.</summary>
                float moved = Absolute(shipped.Mean - flat.Mean);

                Assert.True(moved < 20f,
                    "on a " + (large ? "large" : "small") + " grid the correction moved the ring's"
                    + " mean by " + moved.ToString("n1") + " K, from " + flat.Mean.ToString("n1")
                    + " to " + shipped.Mean.ToString("n1") + "; it was applied on the finding that"
                    + " it does not, so a move this size means the finding needs re-reading rather"
                    + " than the threshold");
            }
        }

        [Fact]
/// <summary>TheDensityCorrectionMovesTheSwingRatherThanTheMean operation.</summary>
        public void TheDensityCorrectionMovesTheSwingRatherThanTheMean()
        {
            const float Watts = 1250000f;

/// <summary>Ring operation.</summary>
            Reading largeFlat = Ring(true, true, Watts, SettledSteps);
/// <summary>Ring operation.</summary>
            Reading largeShipped = Ring(true, false, Watts, SettledSteps);

            Assert.True(largeShipped.Spread < largeFlat.Spread * 0.5f,
                "a large-grid ring swings " + largeShipped.Spread.ToString("n1")
                + " K as it ships against " + largeFlat.Spread.ToString("n1")
                + " K on the flat charge; ten times the fluid was expected to buffer the ring, and"
                + " if it does not then the correction is not doing what balance.md says it does");

/// <summary>Ring operation.</summary>
            Reading smallFlat = Ring(false, true, Watts, SettledSteps);
/// <summary>Ring operation.</summary>
            Reading smallShipped = Ring(false, false, Watts, SettledSteps);

            Assert.True(smallShipped.Spread > smallFlat.Spread * 2f,
                "a small-grid ring swings " + smallShipped.Spread.ToString("n1")
                + " K as it ships against " + smallFlat.Spread.ToString("n1")
                + " K on the flat charge; a twelfth of the fluid was expected to buffer it worse,"
                + " and this is the half of the correction that costs a small grid something");
        }

        [Fact]
/// <summary>TheDensityCorrectionCostsNoSubsteps operation.</summary>
        public void TheDensityCorrectionCostsNoSubsteps()
        {
            foreach (bool large in new[] { true, false })
            {
/// <summary>Ring operation.</summary>
                Reading flat = Ring(large, true, ReferenceWatts, SettledSteps);
/// <summary>Ring operation.</summary>
                Reading shipped = Ring(large, false, ReferenceWatts, SettledSteps);

                Assert.True(shipped.Substeps <= flat.Substeps + 0.01f,
                    "a " + (large ? "large" : "small") + "-grid ring demands "
                    + shipped.Substeps.ToString("n2") + " substeps as it ships against "
                    + flat.Substeps.ToString("n2") + " on the flat charge"
                    + "; less coolant is a stiffer parcel, so this is the assertion that would"
                    + " catch the correction becoming expensive on small grids");
            }
        }

        [Fact]
/// <summary>TheFlatChargeOutweighsTheSmallPipeCarryingIt operation.</summary>
        public void TheFlatChargeOutweighsTheSmallPipeCarryingIt()
        {
            float pipe = ShippedBlocks.Model("Gauge_SG_CoolantPipe_Straight").Mass;
            float flat = LoopBefore.FlatKilogramsPerPipe;
            float corrected = LoopThermalProperties.Default().MassPerPipe(Catalog.SmallGridSize);

            output.WriteLine("small pipe {0:n0} kg, flat charge {1:n0} kg, corrected {2:n1} kg",
                pipe, flat, corrected);

            Assert.True(flat > pipe,
                "the flat charge is " + flat.ToString("n0") + " kg against a " + pipe.ToString("n0")
                + " kg pipe, which is the claim this test exists to pin; if the pipe's mass moved,"
                + " balance.md's argument moved with it");

            Assert.True(corrected < pipe,
                "the corrected charge is " + corrected.ToString("n1") + " kg in a "
                + pipe.ToString("n0") + " kg pipe, and a correction that still overfilled the block"
                + " would not be a correction");
        }

/// <summary>Absolute operation.</summary>
        private static float Absolute(float value)
        {
            return value < 0f ? -value : value;
        }
    }
}
