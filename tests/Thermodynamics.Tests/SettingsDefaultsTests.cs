using System.Reflection;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SettingsDefaultsTests
    {
        [Fact]

        public void AFreshSettingsObjectIsAlreadyUsable()
        {

            ThermalSettings settings = new ThermalSettings();

            Assert.True(settings.EnableConduction);
            Assert.True(settings.EnableRadiation);
            Assert.True(settings.EnableSolarHeat);
            Assert.True(settings.SolarSelfShadowing);
            Assert.True(settings.Frequency > 0);
            Assert.True(settings.SimulationSpeed > 0f);
            Assert.True(settings.HeatTimeScale > 0f);
            Assert.True(settings.SolarEnergy > 0f);
        }

        [Fact]

        public void EveryNumericSettingStartsAtSomethingUsable()
        {

            ThermalSettings settings = new ThermalSettings();

            foreach (FieldInfo field in typeof(ThermalSettings).GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(float) && field.FieldType != typeof(int)) continue;

                if (field.Name != "Frequency"
                    && field.Name != "SimulationSpeed"
                    && field.Name != "HeatTimeScale"
                    && field.Name != "SolarEnergy"
                    && field.Name != "RoomAirDensity"
                    && field.Name != "RoomConvectionCoefficient") continue;


                double value = field.FieldType == typeof(float)
                    ? (float)field.GetValue(settings)
                    : (int)field.GetValue(settings);

                Assert.True(value > 0d, field.Name + " should not start at zero");
            }
        }
    }
}
