using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Wind and grid velocity, separately and together, against the friction system.
    ///
    /// The hull feels one thing: the relative wind, composed as ambient wind minus grid velocity.
    /// Friction, its 50 m/s threshold and forced convection all read that one scalar, which makes
    /// the composed cases impossible to attribute in play — a session with a storm and a moving
    /// ship shows a heat that could belong to either. These tests hold each contributor still while
    /// the other moves, and then pin the compositions where the sum does something neither part
    /// does: a headwind that trips friction though neither wind nor speed alone reaches the
    /// threshold, and a downwind run that switches it off at full throttle.
    /// </summary>
    public class FrictionIsolationTests
    {
        private const float ThickAir = 1f;

        /// <summary>One heavy block, environment features off so friction is the only path.</summary>
        private static ThermalSimulation Rig()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.EnableEnvironment = false;
            settings.EnableSolarHeat = false;
            settings.EnableDamage = false;
            settings.Derive();

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.HeavyArmor(), Vector3I.Zero);

            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.Solver.CollectDiagnostics = true;
            simulation.Planet = PlanetThermalProperties.Default();
            return simulation;
        }

        private static float FrictionWatts(EnvironmentSample sample)
        {
            ThermalSimulation simulation = Rig();
            simulation.StepExact(1, sample);
            return simulation.Solver.Nodes[0].LastFrictionWatts;
        }

        // ---- each contributor alone ----------------------------------------------------------

        [Fact]
        public void MotionAloneHeatsAboveTheThreshold()
        {
            Assert.True(FrictionWatts(Worlds.Flight(ThickAir, 80f)) > 0f);
            Assert.Equal(0f, FrictionWatts(Worlds.Flight(ThickAir, 40f)));
        }

        [Fact]
        public void WindAloneHeatsAboveTheThreshold()
        {
            Assert.True(FrictionWatts(Worlds.Storm(ThickAir, 80f)) > 0f);
            Assert.Equal(0f, FrictionWatts(Worlds.Storm(ThickAir, 40f)));
        }

        /// <summary>
        /// Friction cannot tell which of the two moved: a parked hull in an 80 m/s gale and a hull
        /// doing 80 through still air are the same airflow, and must heat identically.
        /// </summary>
        [Fact]
        public void AStormAndAFlightAtTheSameAirspeedAreTheSameHeat()
        {
            float storm = FrictionWatts(Worlds.Storm(ThickAir, 80f));
            float flight = FrictionWatts(Worlds.Flight(ThickAir, 80f));

            Assert.True(storm > 0f);
            Assert.Equal(storm, flight, 2);
        }

        // ---- the compositions where the sum surprises ----------------------------------------

        /// <summary>
        /// Flying with the wind at the wind's own speed is calm air at full ground speed. This is
        /// the case that reads as "friction is broken" in play, and it is correct.
        /// </summary>
        [Fact]
        public void FlyingWithTheWindAtItsOwnSpeedIsCalmAir()
        {
            EnvironmentSample sample = Worlds.WindAndMotion(
                ThickAir, 80f, Vector3.Forward, Vector3.Forward * 80f);

            Assert.Equal(0f, sample.RelativeWindSpeed, 3);
            Assert.Equal(0f, FrictionWatts(sample));
        }

        /// <summary>
        /// A headwind sums: 40 m/s of wind against 40 m/s of speed trips the 50 m/s threshold that
        /// neither reaches alone, and heats exactly as 80 m/s of either would.
        /// </summary>
        [Fact]
        public void AHeadwindTripsTheThresholdNeitherPartReaches()
        {
            EnvironmentSample headwind = Worlds.WindAndMotion(
                ThickAir, 40f, Vector3.Forward, Vector3.Backward * 40f);

            Assert.Equal(80f, headwind.RelativeWindSpeed, 3);

            float watts = FrictionWatts(headwind);
            Assert.True(watts > 0f);
            Assert.Equal(FrictionWatts(Worlds.Flight(ThickAir, 80f)), watts, 2);
        }

        /// <summary>
        /// A downwind run subtracts: 80 m/s of speed in a 60 m/s tailwind is 20 m/s of airflow,
        /// below the threshold, so friction is off at a ground speed well above it.
        /// </summary>
        [Fact]
        public void ATailwindSwitchesFrictionOffAtFullSpeed()
        {
            EnvironmentSample downwind = Worlds.WindAndMotion(
                ThickAir, 60f, Vector3.Forward, Vector3.Forward * 80f);

            Assert.Equal(20f, downwind.RelativeWindSpeed, 3);
            Assert.Equal(0f, FrictionWatts(downwind));
        }

        [Fact]
        public void ACrosswindComposesByVectorNotByAddition()
        {
            EnvironmentSample crosswind = Worlds.WindAndMotion(
                ThickAir, 60f, Vector3.Forward, Vector3.Right * 60f);

            // sqrt(60^2 + 60^2), not 120: the two motions are at right angles.
            Assert.Equal(60f * (float)System.Math.Sqrt(2), crosswind.RelativeWindSpeed, 2);

            // Wind blows along -Z and the hull's own motion adds an equal -X of airflow: the
            // relative wind is the diagonal between them.
            Vector3 airflow = crosswind.RelativeWindDirectionLocal;
            Assert.Equal(airflow.Z, airflow.X, 3);
            Assert.True(airflow.X < 0f && airflow.Z < 0f);
        }

        // ---- what the airflow drives beyond friction -----------------------------------------

        /// <summary>Forced convection reads the same scalar, so it also cannot tell the two apart.</summary>
        [Fact]
        public void ForcedConvectionCannotTellWindFromMotion()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            EnvironmentState storm = EnvironmentSolver.Solve(settings, planet, Worlds.Storm(ThickAir, 30f));
            EnvironmentState flight = EnvironmentSolver.Solve(settings, planet, Worlds.Flight(ThickAir, 30f));
            EnvironmentState calm = EnvironmentSolver.Solve(
                settings, planet, Worlds.WindAndMotion(ThickAir, 30f, Vector3.Forward, Vector3.Forward * 30f));

            Assert.Equal(storm.ConvectionCoefficient, flight.ConvectionCoefficient, 3);
            Assert.True(calm.ConvectionCoefficient < storm.ConvectionCoefficient);
        }

        [Fact]
        public void FrictionGrowsWithTheCubeOfTheAirspeed()
        {
            float at60 = FrictionWatts(Worlds.Flight(ThickAir, 60f));
            float at120 = FrictionWatts(Worlds.Flight(ThickAir, 120f));

            Assert.True(at60 > 0f);
            Assert.Equal(8f, at120 / at60, 2);
        }

        /// <summary>
        /// The cube law and the airspeed-equivalence both still hold at 300 m/s, the speed limit
        /// the servers this mod is played on actually run.
        ///
        /// <para>
        /// **Vanilla's 100 m/s cap is not the world to balance against.** Friction goes as the cube
        /// of airspeed, so a raised limit is a twenty-sevenfold change in the one heating term that
        /// no ambient bounds, and a balance measured only at 100 has no evidence about it. The
        /// battery carries <c>flight-300</c> and <c>storm-300</c> for the same reason.
        /// </para>
        /// </summary>
        [Fact]
        public void TheFrictionLawStillHoldsAtARaisedSpeedLimit()
        {
            float at100 = FrictionWatts(Worlds.Flight(ThickAir, 100f));
            float at300 = FrictionWatts(Worlds.Flight(ThickAir, 300f));

            Assert.True(at100 > 0f, "100 m/s is above the friction threshold");
            Assert.Equal(27f, at300 / at100, 1);

            // Still the relative wind and nothing else: a parked hull in a 300 m/s gale is a hull
            // doing 300 through still air.
            Assert.Equal(at300, FrictionWatts(Worlds.Storm(ThickAir, 300f)), 0);

            // And the cancellation survives the speed: 300 downwind in a 300 m/s tailwind is calm.
            Assert.Equal(0f, FrictionWatts(
                Worlds.WindAndMotion(ThickAir, 300f, Vector3.Forward, Vector3.Forward * 300f)));
        }

        /// <summary>
        /// Forced convection at 300 m/s, which is where the cost of a raised speed limit lands.
        ///
        /// The coefficient carries a square-root wind bonus, <c>h = h0 * (1 + 0.1 * sqrt(v))</c>,
        /// so tripling the airspeed does not triple the cooling — it moves the bonus from 2.00 to
        /// 2.73. That flatness is the finding: the airflow term saturates while the friction term
        /// cubes, so past some speed the hull is being heated faster than the same air can cool it.
        /// </summary>
        [Fact]
        public void ForcedConvectionSaturatesWhileFrictionCubes()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            float calm = EnvironmentSolver.Solve(
                settings, planet, Worlds.Flight(ThickAir, 0f)).ConvectionCoefficient;
            float at100 = EnvironmentSolver.Solve(
                settings, planet, Worlds.Flight(ThickAir, 100f)).ConvectionCoefficient;
            float at300 = EnvironmentSolver.Solve(
                settings, planet, Worlds.Flight(ThickAir, 300f)).ConvectionCoefficient;

            Assert.Equal(2.000f, at100 / calm, 2);
            Assert.Equal(2.732f, at300 / calm, 2);

            // Tripling the speed buys 37 % more cooling against 27x the friction heat.
            Assert.True(at300 / at100 < 1.4f,
                "the airflow term saturates: three times the speed is under 1.4x the cooling");
        }

        /// <summary>
        /// The activation and the heat agree through the full solve, not just the sample: the
        /// environment state a composed sample produces carries FrictionActive exactly where the
        /// relative wind exceeds the threshold.
        /// </summary>
        [Fact]
        public void ActivationFollowsTheRelativeWindThroughTheSolve()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.Derive();
            PlanetThermalProperties planet = PlanetThermalProperties.Default();

            Assert.True(EnvironmentSolver.Solve(
                settings, planet, Worlds.WindAndMotion(ThickAir, 40f, Vector3.Forward, Vector3.Backward * 40f))
                .FrictionActive);

            Assert.False(EnvironmentSolver.Solve(
                settings, planet, Worlds.WindAndMotion(ThickAir, 60f, Vector3.Forward, Vector3.Forward * 60f))
                .FrictionActive);
        }
    }
}
