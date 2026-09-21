using System;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SuitThermalTests
    {
/// <summary>Shipped operation.</summary>
        private static ThermalSettings Shipped()
        {
            return new ThermalSettings().Derive();
        }

/// <summary>DamageOverSeconds operation.</summary>
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

        [Fact]
/// <summary>TheSuitHoldsUpToTheTemperatureItsRatingImplies operation.</summary>
        public void TheSuitHoldsUpToTheTemperatureItsRatingImplies()
        {
/// <summary>Shipped operation.</summary>
            ThermalSettings settings = Shipped();
            float survivable = SuitThermal.SurvivableKelvin(settings);

            Assert.Equal(SuitThermal.ComfortKelvin
                + (settings.SuitCoolingWatts / settings.SuitConductance), survivable, 2);

            float interior;
            int hurt;

            Assert.Equal(0f, DamageOverSeconds(settings, survivable - 10f, false, 3600,
                out interior, out hurt), 4);
            Assert.True(Math.Abs(interior - SuitThermal.ComfortKelvin) < 1f,
                "the suit should still be holding its occupant at comfort, not drifting: " + interior);
            Assert.Equal(-1, hurt);
        }

        [Fact]
/// <summary>BeingOverwhelmedComesBeforeBeingHurt operation.</summary>
        public void BeingOverwhelmedComesBeforeBeingHurt()
        {
/// <summary>Shipped operation.</summary>
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

        [Fact]
/// <summary>AHotterRoomLeavesLessTime operation.</summary>
        public void AHotterRoomLeavesLessTime()
        {
/// <summary>Shipped operation.</summary>
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

        [Fact]
/// <summary>AnOpenHelmetIsUnsafeWhereAClosedOneIsNot operation.</summary>
        public void AnOpenHelmetIsUnsafeWhereAClosedOneIsNot()
        {
/// <summary>Shipped operation.</summary>
            ThermalSettings settings = Shipped();

            float closed = SuitThermal.SurvivableKelvin(settings, false);
            float open = SuitThermal.SurvivableKelvin(settings, true);

            Assert.True(open < closed, "an open helmet cannot be the safer state");
            Assert.Equal(closed - SuitThermal.ComfortKelvin,
                (open - SuitThermal.ComfortKelvin) * SuitThermal.OpenHelmetConductanceFactor, 1);

            float room = (open + closed) * 0.5f;
            float interior;
            int hurt;

            Assert.Equal(0f, DamageOverSeconds(settings, room, false, 600, out interior, out hurt), 4);
            Assert.True(DamageOverSeconds(settings, room, true, 600, out interior, out hurt) > 0f,
                "an open helmet at " + room.ToString("n0") + " K should hurt");
        }

        [Fact]
/// <summary>AFlatSuitDoesNotRegulate operation.</summary>
        public void AFlatSuitDoesNotRegulate()
        {
/// <summary>Shipped operation.</summary>
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

        [Fact]
/// <summary>TheSuitHeatsAsWellAsCools operation.</summary>
        public void TheSuitHeatsAsWellAsCools()
        {
/// <summary>Shipped operation.</summary>
            ThermalSettings settings = Shipped();

            SuitStepResult result = SuitThermal.Step(settings, SuitThermal.ComfortKelvin,
                settings.VacuumTemperature, false, true, 1f);

            Assert.True(result.RegulatedWatts < 0f, "in vacuum the suit should be putting heat in");
            Assert.False(result.Overwhelmed, "a cold environment is not a cooling failure");
            Assert.Equal(0f, result.Damage, 5);
            Assert.True(Math.Abs(result.InteriorKelvin - SuitThermal.ComfortKelvin) < 1f);
        }

        [Fact]
/// <summary>TheColdEndOfTheSuitsWindowIsAnOrdinaryRoomAndTheHotEndIsNot operation.</summary>
        public void TheColdEndOfTheSuitsWindowIsAnOrdinaryRoomAndTheHotEndIsNot()
        {
/// <summary>Shipped operation.</summary>
            ThermalSettings settings = Shipped();

            float sealed_ = settings.SuitCoolingWatts / settings.SuitConductance;
            float open = sealed_ / SuitThermal.OpenHelmetConductanceFactor;

            Assert.Equal(SuitThermal.ComfortKelvin + sealed_,
                SuitThermal.SurvivableKelvin(settings), 2);
            Assert.Equal(SuitThermal.ComfortKelvin + open,
                SuitThermal.SurvivableKelvin(settings, true), 2);

            float coldOpen = SuitThermal.ComfortKelvin - open;

            Assert.InRange(coldOpen, 283.15f, 296.15f);

            Assert.True(SuitThermal.SurvivableKelvin(settings, true) > 323.15f);

            Assert.True(coldOpen < SuitThermal.ComfortKelvin);
            Assert.True(coldOpen > 273.15f,
                "the cold end has moved below freezing, so it is no longer an ordinary room and"
                + " `C18`'s reason for having no floor no longer holds");
        }

        [Fact]
/// <summary>TheModelSurvivesBeingHandedNothing operation.</summary>
        public void TheModelSurvivesBeingHandedNothing()
        {
            Assert.Equal(0f, SuitThermal.Step(null, 300f, 900f, false, true, 1f).Damage, 5);
            Assert.Equal(300f, SuitThermal.Step(Shipped(), 300f, 900f, false, true, 0f).InteriorKelvin, 5);

/// <summary>ThermalSettings operation.</summary>
            ThermalSettings zeroed = new ThermalSettings();
            zeroed.SuitHeatCapacity = 0f;
            zeroed.SuitConductance = 0f;
            zeroed.Derive();

            SuitStepResult result = SuitThermal.Step(zeroed, 300f, 5000f, false, true, 1f);
            Assert.False(float.IsNaN(result.InteriorKelvin));
            Assert.False(float.IsInfinity(result.InteriorKelvin));
        }

        [Fact]
/// <summary>TheSwitchIsHonouredByTheModelItself operation.</summary>
        public void TheSwitchIsHonouredByTheModelItself()
        {
/// <summary>Shipped operation.</summary>
            ThermalSettings settings = Shipped();
            settings.EnableSuitDamage = false;

            SuitStepResult result = SuitThermal.Step(settings, SuitThermal.ComfortKelvin,
                5000f, true, true, 10f);

            Assert.Equal(SuitThermal.ComfortKelvin, result.InteriorKelvin, 5);
            Assert.Equal(0f, result.Damage, 5);
            Assert.False(result.Overwhelmed);
        }

        [Fact]
/// <summary>ASuitLimitBelowComfortIsReportedAsAProblem operation.</summary>
        public void ASuitLimitBelowComfortIsReportedAsAProblem()
        {
/// <summary>ThermalSettings operation.</summary>
            ThermalSettings settings = new ThermalSettings();
            settings.SuitCriticalTemperature = SuitThermal.ComfortKelvin - 5f;
            settings.Derive();

            Assert.Contains(settings.Validate(),
                problem => problem.Contains("SuitCriticalTemperature"));
        }
    }
}
