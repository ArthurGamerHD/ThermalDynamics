using System.Collections.Generic;
using Xunit;

namespace Thermodynamics.Tests
{
    public class WorldSettingsTests
    {
        private const string Sample =
            "<?xml version=\"1.0\"?>\n" +
            "<MyObjectBuilder_SessionSettings xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n" +
            "  <GameMode>Creative</GameMode>\n" +
            "  <MaxPlayers>2</MaxPlayers>\n" +
            "  <DestructibleBlocks>false</DestructibleBlocks>\n" +
            "  <EnableOxygen>true</EnableOxygen>\n" +
            "  <EnableOxygenPressurization>true</EnableOxygenPressurization>\n" +
            "  <EnableSaving>true</EnableSaving>\n" +
            "  <ScenarioName />\n" +
            "  <Description>a &amp; b</Description>\n" +
            "  <ExperimentalMode>\n" +
            "    <Enabled>true</Enabled>\n" +
            "  </ExperimentalMode>\n" +
            "</MyObjectBuilder_SessionSettings>\n";

/// <summary>Parsed operation.</summary>
        private static List<KeyValuePair<string, string>> Parsed()
        {
            return WorldSettings.Parse(Sample);
        }

        [Fact]
/// <summary>FlatValuesAreReadUnderTheirOwnName operation.</summary>
        public void FlatValuesAreReadUnderTheirOwnName()
        {
/// <summary>Parsed operation.</summary>
            List<KeyValuePair<string, string>> rows = Parsed();

            Assert.Equal("Creative", WorldSettings.Value(rows, "GameMode"));
            Assert.Equal("2", WorldSettings.Value(rows, "MaxPlayers"));
            Assert.Equal("false", WorldSettings.Value(rows, "DestructibleBlocks"));
        }

        [Fact]
/// <summary>TheSerialisedTypeIsNotAPrefixAndANestedBlockIs operation.</summary>
        public void TheSerialisedTypeIsNotAPrefixAndANestedBlockIs()
        {
/// <summary>Parsed operation.</summary>
            List<KeyValuePair<string, string>> rows = Parsed();

            Assert.Contains(rows, r => r.Key == "GameMode");
            Assert.Contains(rows, r => r.Key == "ExperimentalMode.Enabled");
            Assert.DoesNotContain(rows, r => r.Key.StartsWith("MyObjectBuilder_SessionSettings"));
        }

        [Fact]
/// <summary>AnEmptyElementIsRecordedRatherThanDropped operation.</summary>
        public void AnEmptyElementIsRecordedRatherThanDropped()
        {
/// <summary>Parsed operation.</summary>
            List<KeyValuePair<string, string>> rows = Parsed();

            Assert.Contains(rows, r => r.Key == "ScenarioName" && r.Value == "");
        }

        [Fact]
/// <summary>EntitiesAreDecoded operation.</summary>
        public void EntitiesAreDecoded()
        {
            Assert.Equal("a & b", WorldSettings.Value(Parsed(), "Description"));
        }

        [Fact]
/// <summary>EveryLeafIsKeptInDocumentOrder operation.</summary>
        public void EveryLeafIsKeptInDocumentOrder()
        {
/// <summary>Parsed operation.</summary>
            List<KeyValuePair<string, string>> rows = Parsed();

            Assert.Equal(9, rows.Count);
            Assert.Equal("GameMode", rows[0].Key);
            Assert.Equal("ExperimentalMode.Enabled", rows[8].Key);
        }

        [Fact]
/// <summary>AMalformedOrEmptyDocumentYieldsNothingRatherThanThrowing operation.</summary>
        public void AMalformedOrEmptyDocumentYieldsNothingRatherThanThrowing()
        {
            Assert.Empty(WorldSettings.Parse(null));
            Assert.Empty(WorldSettings.Parse(""));
            Assert.Empty(WorldSettings.Parse("<Settings><Broken"));
        }

        [Fact]
/// <summary>AValueSeenNowhereReadsAsItsDefault operation.</summary>
        public void AValueSeenNowhereReadsAsItsDefault()
        {
/// <summary>Parsed operation.</summary>
            List<KeyValuePair<string, string>> rows = Parsed();

            Assert.Null(WorldSettings.Value(rows, "NoSuchSetting"));
            Assert.True(WorldSettings.Flag(rows, "NoSuchSetting", true));
            Assert.False(WorldSettings.Flag(rows, "NoSuchSetting", false));
        }

        [Fact]
/// <summary>LookupDoesNotMatchASuffixOfAnotherName operation.</summary>
        public void LookupDoesNotMatchASuffixOfAnotherName()
        {
            List<KeyValuePair<string, string>> rows = WorldSettings.Parse(
                "<S><EnableOxygenPressurization>false</EnableOxygenPressurization></S>");

            Assert.Null(WorldSettings.Value(rows, "Pressurization"));
            Assert.Equal("false", WorldSettings.Value(rows, "EnableOxygenPressurization"));
        }

        [Fact]
/// <summary>DamageIsReportedAsSilencedWhenTheWorldKeepsBlocksIndestructible operation.</summary>
        public void DamageIsReportedAsSilencedWhenTheWorldKeepsBlocksIndestructible()
        {
            WorldSettings.ModFeatures features = new WorldSettings.ModFeatures();
            features.Damage = true;

            List<string> conflicts = WorldSettings.Conflicts(Parsed(), features);

            Assert.Single(conflicts);
            Assert.Contains("DestructibleBlocks", conflicts[0]);
        }

        [Fact]
/// <summary>RoomAirIsReportedAsSilencedByEitherOxygenSwitch operation.</summary>
        public void RoomAirIsReportedAsSilencedByEitherOxygenSwitch()
        {
            WorldSettings.ModFeatures features = new WorldSettings.ModFeatures();
            features.RoomAir = true;

            Assert.Empty(WorldSettings.Conflicts(Parsed(), features));

            List<KeyValuePair<string, string>> noPressure = WorldSettings.Parse(
                "<S><EnableOxygen>true</EnableOxygen>" +
                "<EnableOxygenPressurization>false</EnableOxygenPressurization></S>");
            Assert.Single(WorldSettings.Conflicts(noPressure, features));

            List<KeyValuePair<string, string>> noOxygen = WorldSettings.Parse(
                "<S><EnableOxygen>false</EnableOxygen>" +
                "<EnableOxygenPressurization>false</EnableOxygenPressurization></S>");

            Assert.Single(WorldSettings.Conflicts(noOxygen, features));
        }

        [Fact]
/// <summary>AFeatureTheModHasOffRaisesNoConflict operation.</summary>
        public void AFeatureTheModHasOffRaisesNoConflict()
        {
            Assert.Empty(WorldSettings.Conflicts(Parsed(), new WorldSettings.ModFeatures()));
        }

        [Fact]
/// <summary>SavingOffIsReportedAgainstPersistence operation.</summary>
        public void SavingOffIsReportedAgainstPersistence()
        {
            WorldSettings.ModFeatures features = new WorldSettings.ModFeatures();
            features.Persistence = true;

            List<KeyValuePair<string, string>> rows = WorldSettings.Parse(
                "<S><EnableSaving>false</EnableSaving></S>");

            Assert.Single(WorldSettings.Conflicts(rows, features));
        }

        [Fact]
/// <summary>NoSettingsMeansNoConflicts operation.</summary>
        public void NoSettingsMeansNoConflicts()
        {
            WorldSettings.ModFeatures features = new WorldSettings.ModFeatures();
            features.Damage = true;
            features.RoomAir = true;
            features.Persistence = true;

            Assert.Empty(WorldSettings.Conflicts(WorldSettings.Parse(""), features));
            Assert.Empty(WorldSettings.Conflicts(null, features));
        }
    }
}
