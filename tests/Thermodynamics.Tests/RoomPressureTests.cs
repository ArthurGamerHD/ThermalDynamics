using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// Whether a room holds air, decided from the game's answers rather than this model's.
    ///
    /// Every one of those answers can veto air and none can insist on it, because the two mistakes
    /// are not equal: air is heat capacity, so a room wrongly given it warms and cools like a room
    /// with a tonne of gas in it and drags every surface around it along, while a room wrongly
    /// denied it only loses a little inertia.
    /// </summary>
    public class RoomPressureTests
    {
        [Fact]
        public void AWorldWithoutPressurisationHasNoAirAnywhere()
        {
            // The setting the player turned off is the whole answer: a sealed room with a vent
            // reporting a full tank still holds nothing.
            Assert.Equal(0f, RoomPressure.Level(false, true, 1f), 5);
        }

        [Fact]
        public void ARoomTheGameDoesNotCallSealedHoldsNothing()
        {
            // The game knows the real shape of a sloped block where this model knows a cell, so
            // where they disagree the game wins.
            Assert.Equal(0f, RoomPressure.Level(true, false, 1f), 5);
        }

        [Fact]
        public void ASealedRoomHoldsWhatItsVentReports()
        {
            Assert.Equal(1f, RoomPressure.Level(true, true, 1f), 5);
            Assert.Equal(0.4f, RoomPressure.Level(true, true, 0.4f), 5);
        }

        [Fact]
        public void NothingReportingIsNotTheSameAsReportingEmptyButGivesTheSameAnswer()
        {
            // A room with no vent cannot be measured, and an unmeasured room is treated as empty
            // rather than guessed at. Distinct in the input so the caller can tell the two apart.
            Assert.Equal(0f, RoomPressure.Level(true, true, RoomPressure.NotReported), 5);
            Assert.Equal(0f, RoomPressure.Level(true, true, 0f), 5);
            Assert.True(RoomPressure.NotReported < 0f);
        }

        [Fact]
        public void AnImpossibleReadingIsClampedRatherThanTrusted()
        {
            Assert.Equal(1f, RoomPressure.Level(true, true, 4f), 5);
            Assert.Equal(0f, RoomPressure.Level(true, true, -3f), 5);
        }
    }

    /// <summary>
    /// What pressure does to the air itself, once decided.
    /// </summary>
    public class RoomAirPressureTests
    {
        private static RoomAirNode Room(float pressure)
        {
            RoomAirNode air = new RoomAirNode();
            air.RoomIndex = 0;
            air.CellCount = 8;
            air.Volume = 8f * 2.5f * 2.5f * 2.5f;
            air.Temperature = 293.15f;
            air.Pressure = pressure;
            air.AirDensity = 1.225f;
            air.RefreshThermalMass();
            return air;
        }

        [Fact]
        public void AnEmptyRoomHasNoAirAndOnlyTheSolverFloorOfHeatCapacity()
        {
            RoomAirNode air = Room(0f);

            Assert.False(air.HasAir);
            Assert.Equal(0f, air.AirMass, 5);

            // Not zero: heat capacity is a divisor in the step, so it has a floor. The room still
            // exchanges nothing, because HasAir is what gates that.
            Assert.Equal(ThermalConstants.MinimumThermalMass, air.ThermalMass, 6);
        }

        [Fact]
        public void HalfPressureIsHalfTheAirAndHalfTheHeatCapacity()
        {
            RoomAirNode full = Room(1f);
            RoomAirNode half = Room(0.5f);

            Assert.True(half.HasAir);
            Assert.Equal(full.AirMass * 0.5f, half.AirMass, 3);
            Assert.Equal(full.ThermalMass * 0.5f, half.ThermalMass, 3);
        }
    
        // ---- what counts as a disagreement --------------------------------------------------

        /// <summary>
        /// The distinction the first version of the room diagnostic got wrong, and the reason it
        /// is a tested function rather than an inline condition.
        ///
        /// Sealed is not full. A cupboard nobody ever piped air into, on a ship in vacuum, is
        /// airtight and empty and both models are right about it. Testing airtightness instead of
        /// oxygen flagged eight such compartments on one ship and painted them all magenta in the
        /// overlay — burying the one room that was genuinely wrong among eight that were not.
        /// </summary>
        [Fact]
        public void ASealedEmptyRoomIsNotADisagreement()
        {
            Assert.False(RoomPressure.Disagrees(false, false, 0f));
            Assert.False(RoomPressure.Disagrees(false, false, RoomPressure.OxygenPresent));
        }

        [Fact]
        public void AirInTheGameAndNoneHereIsADisagreement()
        {
            Assert.True(RoomPressure.Disagrees(false, false, 1f));
            Assert.True(RoomPressure.Disagrees(false, false, 0.5f));
        }

        [Fact]
        public void ARoomThisModelHasFilledIsNeverADisagreement()
        {
            Assert.False(RoomPressure.Disagrees(true, false, 1f));
        }

        [Fact]
        public void AVentedRoomIsEmptyOnPurpose()
        {
            // Standing open through a door. Empty is the right answer, whatever the game reports
            // in the instant before its own fill catches up.
            Assert.False(RoomPressure.Disagrees(false, true, 1f));
        }

        [Fact]
        public void WhatCouldNotBeMeasuredIsNotAFault()
        {
            // Negative means the gas system could not be asked and no vent could answer either.
            // A diagnostic that cannot see has nothing to report.
            Assert.False(RoomPressure.Disagrees(false, false, RoomPressure.NotReported));
            Assert.False(RoomPressure.Disagrees(false, false, -1f));
        }
    
        // ---- the vent fallback ---------------------------------------------------------------

        /// <summary>
        /// Reading the air vents is a walk over every vent on the grid, so it is worth asking
        /// whether it can change an answer before paying for it. These pin the cases where it
        /// cannot.
        /// </summary>
        [Fact]
        public void AnUnansweredSealedRoomNeedsTheVentsRead()
        {
            Assert.True(RoomPressure.NeedsVentFallback(true, true, RoomPressure.NotReported));
        }

        [Fact]
        public void ARoomTheGameAnsweredForNeedsNoVents()
        {
            Assert.False(RoomPressure.NeedsVentFallback(true, true, 1f));
            Assert.False(RoomPressure.NeedsVentFallback(true, true, 0f));
        }

        /// <summary>
        /// The case that made this worth adding. A compartment this model finds and the game does
        /// not call sealed is emptied whatever a vent reports, so the vents were being walked
        /// every sweep to produce a number that <see cref="RoomPressure.Level"/> discards — and a
        /// field dump showed one such compartment on an ordinary ship, permanently.
        /// </summary>
        [Fact]
        public void ARoomTheGameDoesNotSealNeedsNoVentsBecauseItsAnswerIsAlreadyZero()
        {
            Assert.False(RoomPressure.NeedsVentFallback(true, false, RoomPressure.NotReported));

            // And the answer it would have produced is zero regardless of what a vent said.
            Assert.Equal(0f, RoomPressure.Level(true, false, 1f));
        }

        [Fact]
        public void AWorldWithoutPressurisationNeedsNoVentsAtAll()
        {
            Assert.False(RoomPressure.NeedsVentFallback(false, true, RoomPressure.NotReported));
            Assert.Equal(0f, RoomPressure.Level(false, true, 1f));
        }

        /// <summary>
        /// The fallback exists to decide a level, so it must be asked for exactly when the level
        /// is still open. Anything Level would resolve on its own is work not worth doing.
        /// </summary>
        [Fact]
        public void TheFallbackIsNeededExactlyWhenTheAnswerIsStillOpen()
        {
            float[] levels = { RoomPressure.NotReported, 0f, 0.5f, 1f };

            for (int w = 0; w < 2; w++)
            {
                for (int g = 0; g < 2; g++)
                {
                    for (int i = 0; i < levels.Length; i++)
                    {
                        bool world = w == 1;
                        bool sealedByGame = g == 1;
                        bool needs = RoomPressure.NeedsVentFallback(world, sealedByGame, levels[i]);

                        // Where the vents are not read, the level computed without them is the
                        // level that stands. It must not depend on what a vent would have said.
                        if (!needs)
                        {
                            Assert.Equal(
                                RoomPressure.Level(world, sealedByGame, levels[i]),
                                RoomPressure.Level(world, sealedByGame,
                                    levels[i] < 0f ? RoomPressure.NotReported : levels[i]));
                        }
                        else
                        {
                            // Only an unreported level in a room that could hold air.
                            Assert.True(world && sealedByGame && levels[i] < 0f);
                        }
                    }
                }
            }
        }
    }
}
