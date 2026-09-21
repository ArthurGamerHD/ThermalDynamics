using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class PlanetPropertyMergeTests
    {
/// <summary>Read operation.</summary>
        private static PlanetThermalProperties Read()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties read = new PlanetThermalProperties();
            read.DayTemperature = 400f;
            read.NightTemperature = 300f;
            read.ConvectionCoefficient = 12f;
            read.SolarDecay = 0.8f;
            read.CoreTemperature = 1234f;
            return read;
        }

        [Fact]
/// <summary>ADefinitionThatSuppliedNothingLeavesTheModelsOwnClimate operation.</summary>
        public void ADefinitionThatSuppliedNothingLeavesTheModelsOwnClimate()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties defaults = new PlanetThermalProperties();

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>PlanetThermalProperties operation.</summary>
                defaults, new PlanetThermalProperties(), PlanetField.None);

            Assert.Equal(defaults.DayTemperature, merged.DayTemperature);
            Assert.Equal(defaults.NightTemperature, merged.NightTemperature);
            Assert.Equal(defaults.ConvectionCoefficient, merged.ConvectionCoefficient);
            Assert.False(PlanetProperties.IsVacuum(merged));
        }

        [Fact]
/// <summary>ABlankDefinitionCannotTurnBreathableAirIntoSpace operation.</summary>
        public void ABlankDefinitionCannotTurnBreathableAirIntoSpace()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties blank = new PlanetThermalProperties();
            blank.DayTemperature = 0f;
            blank.NightTemperature = 0f;
            blank.UndergroundTemperature = 0f;
            blank.ConvectionCoefficient = 0f;

            Assert.True(PlanetProperties.IsVacuum(blank));

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>PlanetThermalProperties operation.</summary>
                new PlanetThermalProperties(), blank, PlanetField.None);

            Assert.False(PlanetProperties.IsVacuum(merged));
            Assert.True(merged.ConvectionCoefficient > 0f);
        }

        [Fact]
/// <summary>OnlyTheFieldsNamedAreTaken operation.</summary>
        public void OnlyTheFieldsNamedAreTaken()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties defaults = new PlanetThermalProperties();

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>Read operation.</summary>
                defaults, Read(), PlanetField.DayTemperature | PlanetField.ConvectionCoefficient);

            Assert.Equal(400f, merged.DayTemperature);
            Assert.Equal(12f, merged.ConvectionCoefficient);

            Assert.Equal(defaults.NightTemperature, merged.NightTemperature);
            Assert.Equal(defaults.CoreTemperature, merged.CoreTemperature);
            Assert.Equal(defaults.SolarDecay, merged.SolarDecay);
        }

        [Fact]
/// <summary>AFullDefinitionIsTakenWhole operation.</summary>
        public void AFullDefinitionIsTakenWhole()
        {
/// <summary>Read operation.</summary>
            PlanetThermalProperties read = Read();

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>PlanetThermalProperties operation.</summary>
                new PlanetThermalProperties(), read, PlanetField.All);

            Assert.Equal(read.DayTemperature, merged.DayTemperature);
            Assert.Equal(read.NightTemperature, merged.NightTemperature);
            Assert.Equal(read.ConvectionCoefficient, merged.ConvectionCoefficient);
            Assert.Equal(read.SolarDecay, merged.SolarDecay);
            Assert.Equal(read.CoreTemperature, merged.CoreTemperature);
        }

        [Fact]
/// <summary>ADefinitionMaySupplyZeroDeliberately operation.</summary>
        public void ADefinitionMaySupplyZeroDeliberately()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties read = new PlanetThermalProperties();
            read.ConvectionCoefficient = 0f;
            read.PoleTemperatureDrop = 0f;

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>PlanetThermalProperties operation.</summary>
                new PlanetThermalProperties(), read,
                PlanetField.ConvectionCoefficient | PlanetField.PoleTemperatureDrop);

            Assert.Equal(0f, merged.ConvectionCoefficient);
            Assert.Equal(0f, merged.PoleTemperatureDrop);
        }

        [Fact]
/// <summary>NeitherArgumentIsModified operation.</summary>
        public void NeitherArgumentIsModified()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties defaults = new PlanetThermalProperties();
            float day = defaults.DayTemperature;

/// <summary>Read operation.</summary>
            PlanetThermalProperties read = Read();
            PlanetProperties.Merge(defaults, read, PlanetField.All);

            Assert.Equal(day, defaults.DayTemperature);
            Assert.Equal(400f, read.DayTemperature);
        }

        [Fact]
/// <summary>TheResultIsClampedToWhatTheSolverCanUse operation.</summary>
        public void TheResultIsClampedToWhatTheSolverCanUse()
        {
/// <summary>PlanetThermalProperties operation.</summary>
            PlanetThermalProperties read = new PlanetThermalProperties();
            read.DayTemperature = -50f;
            read.SolarDecay = 4f;

            PlanetThermalProperties merged = PlanetProperties.Merge(
/// <summary>PlanetThermalProperties operation.</summary>
                new PlanetThermalProperties(), read,
                PlanetField.DayTemperature | PlanetField.SolarDecay);

            Assert.Equal(0f, merged.DayTemperature);
            Assert.Equal(1f, merged.SolarDecay);
        }

        [Fact]
/// <summary>AMissingBaselineIsTheModelsOwnDefaults operation.</summary>
        public void AMissingBaselineIsTheModelsOwnDefaults()
        {
            PlanetThermalProperties merged = PlanetProperties.Merge(null, null, PlanetField.All);

            Assert.False(PlanetProperties.IsVacuum(merged));
            Assert.Equal(new PlanetThermalProperties().DayTemperature, merged.DayTemperature);
        }
    }
}
