using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **What correcting the coolant's grid-size defect does, measured on a rig that has somewhere
    /// to put the heat.**
    ///
    /// <para>
    /// `CoolantMassPerPipe` is a flat 50 kg with no grid size in it: **3.2 kg/m³ in a 2.5 m cube,
    /// which is a gas, against 400 kg/m³ in a 0.5 m one, which is a liquid** — and on a small grid
    /// it is more fluid than the 32 kg pipe block carrying it weighs. That is the same defect
    /// <see cref="LoopThermalProperties.HeatTransferCoefficient"/> was corrected for when it stopped
    /// being a conductivity divided by half a cell, and correcting it the same way — a fixed
    /// density, the mass following the cell — is what ships since `C43`.
    /// <see cref="LoopBefore"/> holds the flat charge it replaced.
    /// </para>
    ///
    /// <para>
    /// **The first version of this class ran the rig with the environment disabled**, so the ring
    /// had a 125 kW source and no sink of any kind. Nothing settled: every arm climbed linearly and
    /// forever, and the four figures it published — 715.5 K and 387.0 K on a large grid, 661.5 K and
    /// 912.1 K on a small one — were that ramp read at step 400. At step 25,600 the same arms read
    /// 26,836 K and 5,908 K. **What it measured was the ratio of two heat capacities**, which is
    /// exactly what coolant mass is, so the answer looked like physics and was a stopwatch reading
    /// (`P2`: what the instrument could not see is part of the result — here it could not see an
    /// equilibrium, because there was none).
    /// </para>
    ///
    /// <para>
    /// With the environment on, the ring radiates and every arm settles by about step 1,600. The
    /// correction is then **worth 7 K to a large grid's mean and 1 K to a small one's**, costs no
    /// substeps either way, and what it actually moves is the ring's temperature *swing*: 100 K
    /// becomes 10 K on a large grid and 8 K becomes 95 K on a small one. Coolant mass buffers a
    /// ring; it does not decide where the ring runs. See balance.md, *The coolant mass is doing an
    /// undeclared job*.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class LoopCoolantMassTests
    {
        private readonly ITestOutputHelper output;

        public LoopCoolantMassTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Steps at which every arm of this rig has settled, established by
        /// <see cref="TheRigReachesASteadyStateAndSaysSo"/> rather than assumed.
        /// </summary>
        private const int SettledSteps = 6400;

        /// <summary>
        /// The load the headline figures are quoted at. A reactor's worth of waste into one sink
        /// face, which is the rig `C42` measured the pickup on.
        /// </summary>
        private const float ReferenceWatts = 125000f;

        /// <summary>
        /// **The environment is on**, which is the whole difference between this and the version
        /// that published a ramp. Solar, friction and damage are off so that the only path out of
        /// the ring is its own radiating skin; the substep and visit caps are off so that a cost
        /// reading is the demand rather than the allowance.
        /// </summary>
        private static ThermalSettings Radiating()
        {
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
            /// <summary>The hottest segment of the ring, which is what a bolted block sees.</summary>
            public float Hottest;

            /// <summary>The whole ring's temperature: where the fluid runs, on average.</summary>
            public float Mean;

            /// <summary>Hottest minus coldest — how far the fluid swings around that mean.</summary>
            public float Spread;

            public float Substeps;
            public float MassPerPipe;
        }

        /// <summary>
        /// The shipped fluid with nothing changed but the coolant charge, put back to the flat 50 kg
        /// a pipe carried before `C43`. **Only the charge**: <see cref="LoopBefore"/> also holds the
        /// pickup coefficient and the stopped share that `C42` moved, and applying all three would
        /// make this a measurement of the package rather than of the mass (`P6`).
        /// </summary>
        private static LoopThermalProperties Flat()
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();
            properties.CoolantMassPerPipe = LoopBefore.FlatKilogramsPerPipe;
            return properties.Clamp();
        }

        /// <summary>
        /// A five-by-five ring with one sink face onto a source, at one cell size and under one
        /// coolant charge. Same rig on both grids and under both charges, so nothing but the
        /// subject differs (`P6`).
        /// </summary>
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

        /// <summary>
        /// **The check that would have caught the first version of this class, and the reason every
        /// figure below is quoted at a steady state rather than at a step count.**
        ///
        /// <para>
        /// A temperature read off a rig that never settles is a reading of how fast it is climbing,
        /// and the arm with more thermal mass climbs more slowly whatever else is true — so such a
        /// rig reports a capacity ratio and looks like it reported a temperature. This doubles the
        /// run and asserts the answer stops moving, on every arm, which is the only evidence that
        /// a stop criterion is a criterion rather than a time limit (`P1`, `M1`).
        /// </para>
        /// </summary>
        [Fact]
        public void TheRigReachesASteadyStateAndSaysSo()
        {
            foreach (bool large in new[] { true, false })
            {
                foreach (bool flat in new[] { false, true })
                {
                    float settled = Ring(large, flat, ReferenceWatts, SettledSteps).Hottest;
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

        /// <summary>
        /// **The correction barely moves where the fluid runs**, which is the finding that unblocked
        /// it. At steady state the mean is set by what comes in and what radiates out, and coolant
        /// mass is in neither: it is a capacity, and a capacity that has finished charging does not
        /// appear in an energy balance.
        ///
        /// <para>
        /// Seven kelvin on a large grid and one on a small one, against the 328 K gain and 250 K
        /// loss this class used to publish. **The blocking objection to `C43` was the size of that
        /// loss**, and the loss is not there.
        /// </para>
        /// </summary>
        [Fact]
        public void TheDensityCorrectionLeavesTheMeanWhereItWas()
        {
            foreach (bool large in new[] { true, false })
            {
                Reading flat = Ring(large, true, ReferenceWatts, SettledSteps);
                Reading shipped = Ring(large, false, ReferenceWatts, SettledSteps);

                float moved = Absolute(shipped.Mean - flat.Mean);

                Assert.True(moved < 20f,
                    "on a " + (large ? "large" : "small") + " grid the correction moved the ring's"
                    + " mean by " + moved.ToString("n1") + " K, from " + flat.Mean.ToString("n1")
                    + " to " + shipped.Mean.ToString("n1") + "; it was applied on the finding that"
                    + " it does not, so a move this size means the finding needs re-reading rather"
                    + " than the threshold");
            }
        }

        /// <summary>
        /// **What it does move is the swing**, and in the direction a fixed density has to move it:
        /// a large pipe gains fluid and buffers better, a small one loses fluid and buffers worse.
        ///
        /// <para>
        /// This is the correction's real effect and the one worth keeping a test on. It is also the
        /// half a player can see: the hottest segment is what a bolted block is coupled to, so a
        /// ring that swings 95 K presents its source a hotter face than one that swings 8 K, even
        /// though the two rings hold the same average heat.
        /// </para>
        /// </summary>
        [Fact]
        public void TheDensityCorrectionMovesTheSwingRatherThanTheMean()
        {
            // Read at ten times the reference load, because a swing is proportional to the heat
            // being carried and at 125 kW both figures are within a few kelvin of each other.
            const float Watts = 1250000f;

            Reading largeFlat = Ring(true, true, Watts, SettledSteps);
            Reading largeShipped = Ring(true, false, Watts, SettledSteps);

            Assert.True(largeShipped.Spread < largeFlat.Spread * 0.5f,
                "a large-grid ring swings " + largeShipped.Spread.ToString("n1")
                + " K as it ships against " + largeFlat.Spread.ToString("n1")
                + " K on the flat charge; ten times the fluid was expected to buffer the ring, and"
                + " if it does not then the correction is not doing what balance.md says it does");

            Reading smallFlat = Ring(false, true, Watts, SettledSteps);
            Reading smallShipped = Ring(false, false, Watts, SettledSteps);

            Assert.True(smallShipped.Spread > smallFlat.Spread * 2f,
                "a small-grid ring swings " + smallShipped.Spread.ToString("n1")
                + " K as it ships against " + smallFlat.Spread.ToString("n1")
                + " K on the flat charge; a twelfth of the fluid was expected to buffer it worse,"
                + " and this is the half of the correction that costs a small grid something");
        }

        /// <summary>
        /// **And it costs the integrator nothing either way**, which was true on the ramp and is
        /// still true settled. Coolant mass is the *denominator* of a segment's substep — the
        /// conductance is untouched — so more of it can only make a ring cheaper to integrate, and
        /// in this rig the demand is set by something else entirely and does not move at all.
        /// </summary>
        [Fact]
        public void TheDensityCorrectionCostsNoSubsteps()
        {
            foreach (bool large in new[] { true, false })
            {
                Reading flat = Ring(large, true, ReferenceWatts, SettledSteps);
                Reading shipped = Ring(large, false, ReferenceWatts, SettledSteps);

                Assert.True(shipped.Substeps <= flat.Substeps + 0.01f,
                    "a " + (large ? "large" : "small") + "-grid ring demands "
                    + shipped.Substeps.ToString("n2") + " substeps as it ships against "
                    + flat.Substeps.ToString("n2") + " on the flat charge"
                    + "; less coolant is a stiffer parcel, so this is the assertion that would"
                    + " catch the correction becoming expensive on small grids");
            }
        }

        /// <summary>
        /// **A flat charge puts more fluid in a small pipe than the pipe weighs**, which is the
        /// fidelity half of the case and needs no rig at all. `Gauge_SG_CoolantPipe_Straight` is a
        /// 32 kg block and the shipped charge is 50 kg of water-glycol inside it.
        /// </summary>
        [Fact]
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

        private static float Absolute(float value)
        {
            return value < 0f ? -value : value;
        }
    }
}
