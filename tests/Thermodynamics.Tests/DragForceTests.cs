using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The force the mod has been computing and throwing away.**
    ///
    /// <para>
    /// The friction term is drag power under another name, so the force is one division: power is
    /// force times speed. Deriving it from the watts rather than recomputing it from the geometry
    /// is what keeps the two consistent — a second expression would be a second place for the
    /// exposure, the wind direction and the density to be got subtly differently, and the two would
    /// disagree only on the ships where it mattered.
    /// </para>
    ///
    /// <para>
    /// These tests are about the arithmetic and the refusals. Whether the force is *applied* is the
    /// game layer, which no harness runs — see known-issues.md.
    /// </para>
    /// </summary>
    public class DragForceTests
    {
        private static ThermalSettings Settings(float frictionScale = 0.001f, float dragCoefficient = 1f)
        {
            ThermalSettings settings = new ThermalSettings();
            settings.FrictionScale = frictionScale;
            settings.DragCoefficient = dragCoefficient;
            settings.Derive();
            return settings;
        }

        /// <summary>
        /// **Power is force times speed, and this is that identity.** A megawatt of drag work at
        /// 100 m/s is 10 kN — divided by the share of the work that lands in the surface, which at
        /// the shipped pair is 0.002.
        /// </summary>
        [Fact]
        public void TheForceIsThePowerOverTheSpeed()
        {
            ThermalSettings settings = Settings();

            // eta = FrictionScale / (0.5 * C_d) = 0.001 / 0.5 = 0.002
            // total drag power = 1e6 / 0.002 = 5e8 W; at 100 m/s that is 5e6 N.
            Assert.Equal(5.0e6f, DragForce.Newtons(1.0e6f, 100f, settings), 0);
        }

        /// <summary>
        /// **The measured population figure, carried through.** The median published hull is given
        /// 5.05 MW at `reentry`, 300 m/s — which the backlog states as 16.8 kN of force never
        /// applied. That figure is `½ C_d ρ A v²` with the mod's own numbers, so it is the whole
        /// drag rather than the heating share, and this is where it comes from.
        /// </summary>
        [Fact]
        public void TheMedianHullsMissingForceIsWhatTheBacklogSays()
        {
            // The backlog's 16.8 kN is the *heating* power over the speed: 5.05 MW / 300 m/s.
            // The whole drag force is that divided by eta, which is what a physics engine would
            // feel, and is three orders larger — which is the point `K3` settles.
            float heatingShare = 5.05e6f / 300f;
            Assert.True(System.Math.Abs(heatingShare - 16.8e3f) < 100f,
                "the backlog's 16.8 kN is not 5.05 MW over 300 m/s, it is " + heatingShare);

            ThermalSettings settings = Settings();
            float whole = DragForce.Newtons(5.05e6f, 300f, settings);

            // Relative rather than absolute: these are millions of newtons and the claim is about
            // the factor of eta, not about the last bit of a float.
            float expected = heatingShare / 0.002f;
            Assert.True(System.Math.Abs(whole - expected) <= expected * 1e-5f,
                "expected " + expected + " N and got " + whole + " N");
        }

        /// <summary>**Doubling the coefficient doubles the force**, which is what a dial is for.</summary>
        [Fact]
        public void TheForceScalesWithTheDragCoefficient()
        {
            float one = DragForce.Newtons(1.0e6f, 100f, Settings(dragCoefficient: 1f));
            float two = DragForce.Newtons(1.0e6f, 100f, Settings(dragCoefficient: 2f));

            Assert.Equal(2f * one, two, 0);
        }

        /// <summary>
        /// **A world that tuned `FrictionScale` for heat does not get a different force.**
        ///
        /// Doubling `FrictionScale` doubles the friction watts the solver produces, and halves the
        /// share this divides by — so the force is unchanged. That is `K3`'s promise made
        /// arithmetic: the heat dial moves heat and the drag dial moves drag.
        /// </summary>
        [Fact]
        public void TuningTheHeatDialLeavesTheForceAlone()
        {
            // The solver's watts are linear in FrictionScale, so a world at 2x produces 2x watts.
            float baseline = DragForce.Newtons(1.0e6f, 100f, Settings(frictionScale: 0.001f));
            float tuned = DragForce.Newtons(2.0e6f, 100f, Settings(frictionScale: 0.002f));

            Assert.Equal(baseline, tuned, 0);
        }

        /// <summary>**Nothing to divide by is no force, not an infinity** (`E8`).</summary>
        [Theory]
        [InlineData(0f, 100f)]      // no drag work
        [InlineData(1.0e6f, 0f)]    // at rest
        [InlineData(-1f, 100f)]     // nonsense in, nought out
        public void NoForceWhereThereIsNothingToDivide(float watts, float speed)
        {
            Assert.Equal(0f, DragForce.Newtons(watts, speed, Settings()));
        }

        /// <summary>
        /// **A world that switched the friction term off gets no force from it.** Deriving one from
        /// a `FrictionScale` of nought would be dividing by the number that says *there is no
        /// friction here*.
        /// </summary>
        [Fact]
        public void AWorldWithNoFrictionTermHasNoDrag()
        {
            Assert.Equal(0f, DragForce.Newtons(1.0e6f, 100f, Settings(frictionScale: 0f)));
            Assert.Equal(0f, DragForce.Newtons(1.0e6f, 100f, Settings(dragCoefficient: 0f)));
        }

        /// <summary>
        /// **The force is along the relative wind**, so a ship parked in a storm is pushed downwind
        /// rather than pulled along its own heading — which it does not have.
        /// </summary>
        [Fact]
        public void TheForceIsAlongTheRelativeWind()
        {
            ThermalSettings settings = Settings();
            Vector3 wind = new Vector3(0f, 0f, 100f);

            Vector3 force = DragForce.Vector(1.0e6f, wind, settings);

            Assert.Equal(5.0e6f, force.Length(), 0);
            Assert.True(Vector3.Dot(Vector3.Normalize(force), Vector3.Normalize(wind)) > 0.999f,
                "the force is not along the wind");
        }

        /// <summary>And a headwind pushes the other way, which is the same statement.</summary>
        [Fact]
        public void AHeadwindAndATailwindPushOppositeWays()
        {
            ThermalSettings settings = Settings();

            Vector3 ahead = DragForce.Vector(1.0e6f, new Vector3(0f, 0f, 100f), settings);
            Vector3 behind = DragForce.Vector(1.0e6f, new Vector3(0f, 0f, -100f), settings);

            Assert.Equal(ahead.Length(), behind.Length(), 0);
            Assert.True(Vector3.Dot(ahead, behind) < 0f);
        }

        /// <summary>**Still air is no force**, and returns a zero vector rather than a NaN.</summary>
        [Fact]
        public void StillAirIsNoForce()
        {
            Assert.Equal(Vector3.Zero, DragForce.Vector(1.0e6f, Vector3.Zero, Settings()));
        }

        /// <summary>
        /// **End to end against the solver**, which is the check that the division is against the
        /// same quantity the solver publishes rather than against a number of the right size.
        /// </summary>
        [Fact]
        public void TheForceComesOutOfTheSolversOwnWatts()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Fill(Catalog.HeavyArmor(), Vector3I.Zero, new Vector3I(4, 4, 4));

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Planet = PlanetThermalProperties.Default();
            simulation.StepExact(1, Worlds.Flight(1f, 120f));

            float watts = simulation.Solver.LastFrictionWatts;
            Assert.True(watts > 0f, "the hull took no drag, so this proves nothing");

            float newtons = DragForce.Newtons(watts, 120f, settings);

            // The whole drag power over the speed, which is the definition being asserted.
            float expected = watts / 0.002f / 120f;
            Assert.True(System.Math.Abs(newtons - expected) <= expected * 1e-5f,
                "expected " + expected + " N and got " + newtons + " N");
        }
    }
}
