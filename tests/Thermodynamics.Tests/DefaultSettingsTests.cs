using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The shipped settings are the most faithful configuration the model has, and they stay in the
    /// world when run.
    ///
    /// <para>
    /// There is one configuration now, so there is no ladder to check and nothing to compare it
    /// against: what is left is whether it is still the honest one — every mechanism on, no
    /// approximation switched on for anybody — and whether a hull run on it stays inside the
    /// temperatures physics allows. See configuration.md.
    /// </para>
    ///
    /// <para>
    /// The six-neighbour test below is here because it is the defect that made the question worth
    /// asking, and it is not about the defaults at all: it builds its own deliberately refused
    /// settings.
    /// </para>
    /// </summary>
    [Trait("speed", "slow")]
    public class DefaultSettingsTests
    {
        private readonly ITestOutputHelper output;

        public DefaultSettingsTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSimulation BuildDefault()
        {
            ThermalSettings settings = new ThermalSettings();

            // The work budget shortens steps on its own; the defaults have to stand up without it.
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

        /// <summary>
        /// **A fresh world runs every mechanism, at the most faithful setting each one has.**
        ///
        /// This is the whole of what the defaults are meant to be, so it is worth being a test
        /// rather than an intention. Each assertion below names the cheaper value it is refusing:
        /// an approximation a player did not ask for is one nobody knows they are running.
        ///
        /// <para>
        /// **One approximation does ship on, and it is named here rather than left implied.** The
        /// global substep ceiling binds on about a fifth of a real population in thick air at
        /// flying speed. It is worth 0.028 K on the hottest block, which is below every instrument
        /// this repository has, so by `P14` the cheap form simply *is* the model and there is
        /// nothing to switch — see the comment on `MaxSubsteps` below, and
        /// [backlog.md](../../docs/backlog.md) `C19`.
        /// </para>
        /// </summary>
        [Fact]
        public void TheDefaultsAreTheMostFaithfulConfiguration()
        {
            ThermalSettings shipped = new ThermalSettings();

            // Every mechanism runs.
            Assert.True(shipped.EnableEnvironment);
            Assert.True(shipped.EnableConduction);
            Assert.True(shipped.EnableRadiation);
            Assert.True(shipped.EnableConvection);
            Assert.True(shipped.EnableSolarHeat);
            Assert.True(shipped.EnableHeatSources);
            Assert.True(shipped.EnableWasteHeat);
            Assert.True(shipped.EnablePlanets);
            Assert.True(shipped.EnableFriction);
            Assert.True(shipped.EnableDamage);
            Assert.True(shipped.EnableCoolantLoops);
            Assert.True(shipped.EnableRoomAir);
            Assert.True(shipped.EnableHeatPumps);

            // Each mechanism at its faithful setting rather than its cheap one.
            Assert.True(shipped.SolarSelfShadowing);          // not the "any face pointing at the sun" model
            Assert.False(shipped.WellMixedCoolant);           // a stopped pump stops cooling
            Assert.True(shipped.DamageIsPerSecond);           // damage that does not scale with Frequency

            // No approximation is switched on for anybody — with one exception, below.
            Assert.Equal(0, shipped.MaxSubstepsPerBlock);     // no block is integrated as heavier than it is

            // **`MaxSubsteps` is the exception, and it is a bound rather than a switch.** In
            // vacuum the estimate is always granted — the 49-ship panel asks 7.1 at p95 against
            // the 64 here. In thick air at 200 m/s it is not: the same panel's p99 demand is 73.4
            // and 14 of 50 hulls are refused, every one of them at 1.14-1.15x over-subscribed,
            // because the stiffest block class is the same fitting on every ship and its demand is
            // set by the convection coefficient rather than by the hull. So this is a ceiling, not
            // a tail, and no ship can be far past it.
            //
            // **It stays at 64, and the number is why.** Refusing 1.15x of the demand moves the
            // hottest block of a driven census hull 0.028 K over 600 simulated seconds and the
            // worst block 0.041 K, while *buying* 1.15x of the step; the approximation stays free
            // to about 2x and breaks between 2x and 3x, which no hull in either population reaches
            // even projected to 300 m/s. Paying 1.15x of the solver in the most expensive
            // environment a ship ever flies in, to buy three hundredths of a kelvin, is the trade
            // `P14` exists to refuse. `MaxSubstepsPerBlock` is the same kind of approximation at
            // 0.607 K on the worst-placed block, which is twenty times more and on the other side
            // of what a player can see — so that one is a switch and this one is the model.
            // ([backlog.md](../../docs/backlog.md) `C19`, and stiffness.md, What refusing the
            // demand costs.)
            Assert.True(shipped.MaxSubsteps >= 64,
                "MaxSubsteps of " + shipped.MaxSubsteps + " refuses the stability estimate in more"
                + " than the thick-air ceiling this is priced against");

            // **The clamps stay on, and in air they are what carries the refused step.** They bound
            // every exchange at the energy that brings a pair to equilibrium, so a step integrated
            // at the ceiling is damped and bounded rather than unstable. The claim that they never
            // engage was a vacuum claim.
            Assert.True(shipped.ClampConductionOvershoot);
            Assert.True(shipped.ClampEnvironmentOvershoot);

            // Real time, run at the mod's one clock.
            Assert.Equal(1f, shipped.SimulationSpeed);
            Assert.Equal(4, shipped.Frequency);

            output.WriteLine("shipped: Frequency " + shipped.Frequency
                + ", SimulationSpeed " + shipped.SimulationSpeed
                + ", HeatTimeScale " + shipped.HeatTimeScale.ToString("n0")
                + ", MaxSubsteps " + shipped.MaxSubsteps
                + ", MaxSubstepsPerBlock " + shipped.MaxSubstepsPerBlock);
        }

        /// <summary>
        /// **A step is bounded, so no single grid can drop a hundred-millisecond step into a frame.**
        ///
        /// The budget costs no *accuracy* — it shortens a step rather than coarsening it, so an
        /// oversized grid advances less simulated time at the same fidelity instead of stuttering at
        /// full rate. That is why it is the one bound a faithful default keeps.
        ///
        /// **What that sentence leaves out is what the lost time is worth, and `C27` priced it.** A
        /// slow thermal clock is 0.00 K on a parked ship, because two hulls heading for the same
        /// equilibrium agree once they arrive; under a load that is *moving* it is 1.19 K standing
        /// at a 5 % deficit and 36.98 K at 60 %. Beside 0.028 K for the substep ceiling this world
        /// accepts and 0.607 K for the per-block cap it refuses, that makes this the largest
        /// approximation the mod ships rather than a free guarantee — which is why the allowance
        /// doubled the day it was first measured. See configuration.md, What a shortened step costs.
        /// </summary>
        [Fact]
        public void TheDefaultIsFrameBounded()
        {
            ThermalSettings shipped = new ThermalSettings();

            Assert.True(shipped.MaxElementVisitsPerStep > 0,
                "the shipped default must bound a step's cost");
        }

        /// <summary>
        /// The defaults, on a hull radiating into space with a hot spot in it, stay in the world for a
        /// minute of play.
        ///
        /// Nothing here may go colder than the sky it radiates into or hotter than it started, since
        /// the hull carries no heat source: anything above the seeded peak was invented.
        /// </summary>
        [Fact]
        public void TheDefaultsStayPhysicalWithTheEnvironmentOn()
        {
            ThermalSimulation simulation = BuildDefault();

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
                        "block " + i + " reached " + t + " after " + frame + " frames");

                    if (t < lowest) lowest = t;
                    if (t > highest) highest = t;
                }
            }

            output.WriteLine("defaults: " + lowest.ToString("n1") + " K to "
                + highest.ToString("n1") + " K over a minute.");

            Assert.True(lowest > 1f, "a block was driven to " + lowest + " K");
            Assert.True(highest <= 1201f, "heat was invented, reaching " + highest + " K");
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
