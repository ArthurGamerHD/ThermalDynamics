using System;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What a hot room does to a player.
    ///
    /// <para>
    /// The one place heat stopped being consequential: every block on a burning ship could be past
    /// its limit while the person standing between them was untouched. Backlog `B10`.
    /// </para>
    ///
    /// <para>
    /// The model is a suit as a refrigerator rather than as a threshold, so what these check is
    /// that the *consequences* follow from the machine: where it holds, where it gives up, how long
    /// a player has once it does, and that an open helmet costs them most of that margin. The
    /// numbers are derived from the settings rather than transcribed, so a world that moves a dial
    /// moves the assertion with it.
    /// </para>
    /// </summary>
    public class SuitThermalTests
    {
        private static ThermalSettings Shipped()
        {
            return new ThermalSettings().Derive();
        }

        /// <summary>
        /// A minute in a room at a given temperature, and what the occupant has taken by the end.
        /// One-second steps, which is the cadence the session runs the pass on.
        /// </summary>
        private static float DamageOverSeconds(ThermalSettings settings, float roomKelvin,
            bool helmetOpen, int seconds, out float interior, out int firstHurtAt)
        {
            interior = SuitThermal.ComfortKelvin;
            firstHurtAt = -1;
            float damage = 0f;

            for (int second = 1; second <= seconds; second++)
            {
                SuitStepResult result =
                    SuitThermal.Step(settings, interior, roomKelvin, helmetOpen, true, 1f);

                interior = result.InteriorKelvin;
                damage += result.Damage;
                if (firstHurtAt < 0 && result.Damage > 0f) firstHurtAt = second;
            }

            return damage;
        }

        /// <summary>
        /// The shipped suit holds its occupant in an ordinary warm compartment indefinitely, and
        /// the temperature it stops holding at is the one the settings imply rather than one
        /// somebody typed.
        /// </summary>
        [Fact]
        public void TheSuitHoldsUpToTheTemperatureItsRatingImplies()
        {
            ThermalSettings settings = Shipped();
            float survivable = SuitThermal.SurvivableKelvin(settings);

            Assert.Equal(SuitThermal.ComfortKelvin
                + (settings.SuitCoolingWatts / settings.SuitConductance), survivable, 2);

            float interior;
            int hurt;

            // Ten degrees under it, for an hour of one-second steps: nothing.
            Assert.Equal(0f, DamageOverSeconds(settings, survivable - 10f, false, 3600,
                out interior, out hurt), 4);
            Assert.True(Math.Abs(interior - SuitThermal.ComfortKelvin) < 1f,
                "the suit should still be holding its occupant at comfort, not drifting: " + interior);
            Assert.Equal(-1, hurt);
        }

        /// <summary>
        /// Past it the suit is overwhelmed, and **the warning comes before the damage**: the
        /// interior has the whole gap between comfort and the limit to cross first, which is the
        /// seconds a player has to get out. That gap is the point of the heat capacity.
        /// </summary>
        [Fact]
        public void BeingOverwhelmedComesBeforeBeingHurt()
        {
            ThermalSettings settings = Shipped();
            float room = SuitThermal.SurvivableKelvin(settings) + 100f;

            SuitStepResult first =
                SuitThermal.Step(settings, SuitThermal.ComfortKelvin, room, false, true, 1f);

            Assert.True(first.Overwhelmed, "the suit is over its rating and should say so");
            Assert.Equal(0f, first.Damage, 5);

            float interior;
            int hurt;
            DamageOverSeconds(settings, room, false, 120, out interior, out hurt);

            Assert.True(hurt > 5, "a player should have more than five seconds, got " + hurt);
            Assert.True(hurt < 60, "and not a minute of them, got " + hurt);
        }

        /// <summary>
        /// The hotter the room the less time there is, monotonically. A player learns a rule from
        /// this — get out faster when it is worse — and a model that did not hold it would teach
        /// them nothing.
        /// </summary>
        [Fact]
        public void AHotterRoomLeavesLessTime()
        {
            ThermalSettings settings = Shipped();
            float survivable = SuitThermal.SurvivableKelvin(settings);

            int previous = int.MaxValue;
            for (float over = 50f; over <= 600f; over += 50f)
            {
                float interior;
                int hurt;
                DamageOverSeconds(settings, survivable + over, false, 600, out interior, out hurt);

                Assert.True(hurt > 0, "nothing happened at " + over + " K over the rating");
                Assert.True(hurt <= previous,
                    "a hotter room must not buy time: " + hurt + "s at " + over + " K over");
                previous = hurt;
            }
        }

        /// <summary>
        /// An open helmet is unsafe at temperatures a closed one survives, and it is one number
        /// that does it — the breathing path — rather than a second threshold.
        /// </summary>
        [Fact]
        public void AnOpenHelmetIsUnsafeWhereAClosedOneIsNot()
        {
            ThermalSettings settings = Shipped();

            float closed = SuitThermal.SurvivableKelvin(settings, false);
            float open = SuitThermal.SurvivableKelvin(settings, true);

            Assert.True(open < closed, "an open helmet cannot be the safer state");
            Assert.Equal(closed - SuitThermal.ComfortKelvin,
                (open - SuitThermal.ComfortKelvin) * SuitThermal.OpenHelmetConductanceFactor, 1);

            // A room between the two: safe closed, and not safe open.
            float room = (open + closed) * 0.5f;
            float interior;
            int hurt;

            Assert.Equal(0f, DamageOverSeconds(settings, room, false, 600, out interior, out hurt), 4);
            Assert.True(DamageOverSeconds(settings, room, true, 600, out interior, out hurt) > 0f,
                "an open helmet at " + room.ToString("n0") + " K should hurt");
        }

        /// <summary>
        /// A flat suit does not regulate. It is the one thing the game lets this mod see about the
        /// suit's power, and it is what makes a hot room a reason to keep the batteries up.
        /// </summary>
        [Fact]
        public void AFlatSuitDoesNotRegulate()
        {
            ThermalSettings settings = Shipped();
            float room = SuitThermal.SurvivableKelvin(settings) - 100f;

            SuitStepResult powered =
                SuitThermal.Step(settings, SuitThermal.ComfortKelvin, room, false, true, 1f);
            SuitStepResult flat =
                SuitThermal.Step(settings, SuitThermal.ComfortKelvin, room, false, false, 1f);

            Assert.True(powered.RegulatedWatts > 0f, "a powered suit in a hot room should be working");
            Assert.Equal(0f, flat.RegulatedWatts, 5);
            Assert.True(flat.InteriorKelvin > powered.InteriorKelvin,
                "a flat suit must let its occupant heat up");
        }

        /// <summary>
        /// The suit works in both directions. A cold environment is the other half of what a suit
        /// is for, and a model that only cooled would hold nobody anywhere cold.
        /// </summary>
        [Fact]
        public void TheSuitHeatsAsWellAsCools()
        {
            ThermalSettings settings = Shipped();

            SuitStepResult result = SuitThermal.Step(settings, SuitThermal.ComfortKelvin,
                settings.VacuumTemperature, false, true, 1f);

            Assert.True(result.RegulatedWatts < 0f, "in vacuum the suit should be putting heat in");
            Assert.False(result.Overwhelmed, "a cold environment is not a cooling failure");
            Assert.Equal(0f, result.Damage, 5);
            Assert.True(Math.Abs(result.InteriorKelvin - SuitThermal.ComfortKelvin) < 1f);
        }

        /// <summary>
        /// Off is off (`C7`): with the switch down the pass is not run at all, and the model itself
        /// still answers rather than throwing, because the settings it is handed can be anything.
        /// </summary>
        [Fact]
        public void TheModelSurvivesBeingHandedNothing()
        {
            Assert.Equal(0f, SuitThermal.Step(null, 300f, 900f, false, true, 1f).Damage, 5);
            Assert.Equal(300f, SuitThermal.Step(Shipped(), 300f, 900f, false, true, 0f).InteriorKelvin, 5);

            ThermalSettings zeroed = new ThermalSettings();
            zeroed.SuitHeatCapacity = 0f;
            zeroed.SuitConductance = 0f;
            zeroed.Derive();

            SuitStepResult result = SuitThermal.Step(zeroed, 300f, 5000f, false, true, 1f);
            Assert.False(float.IsNaN(result.InteriorKelvin));
            Assert.False(float.IsInfinity(result.InteriorKelvin));
        }

        /// <summary>
        /// Off means off wherever the model is reached from. The session pass returns before it
        /// looks up a player, which is what makes it free; this is the other half (`C7`).
        /// </summary>
        [Fact]
        public void TheSwitchIsHonouredByTheModelItself()
        {
            ThermalSettings settings = Shipped();
            settings.EnableSuitDamage = false;

            SuitStepResult result = SuitThermal.Step(settings, SuitThermal.ComfortKelvin,
                5000f, true, true, 10f);

            Assert.Equal(SuitThermal.ComfortKelvin, result.InteriorKelvin, 5);
            Assert.Equal(0f, result.Damage, 5);
            Assert.False(result.Overwhelmed);
        }

        /// <summary>
        /// A critical temperature at or below the temperature the suit aims at would damage a
        /// player the suit is holding perfectly, which is a world nobody meant to configure.
        /// </summary>
        [Fact]
        public void ASuitLimitBelowComfortIsReportedAsAProblem()
        {
            ThermalSettings settings = new ThermalSettings();
            settings.SuitCriticalTemperature = SuitThermal.ComfortKelvin - 5f;
            settings.Derive();

            Assert.Contains(settings.Validate(),
                problem => problem.Contains("SuitCriticalTemperature"));
        }
    }
}
