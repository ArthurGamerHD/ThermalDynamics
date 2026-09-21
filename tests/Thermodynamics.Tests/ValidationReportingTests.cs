using System.Collections.Generic;
using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    [Collection("ThermalValidation")]
    public class ValidationReportingTests
    {
/// <summary>Capture operation.</summary>
        private static List<string> Capture()
        {
            ThermalValidation.Reset();
/// <summary>List operation.</summary>
            List<string> written = new List<string>();
            ThermalValidation.Writer = written.Add;
            return written;
        }

        [Fact]
/// <summary>AnUnphysicalBlockPropertyIsNamedWithTheBlockThatCarriesIt operation.</summary>
        public void AnUnphysicalBlockPropertyIsNamedWithTheBlockThatCarriesIt()
        {
/// <summary>Capture operation.</summary>
            List<string> written = Capture();
            try
            {
                ThermalValidation.Check("SomeModsReactor", new BlockThermalProperties
                {
                    Emissivity = 2f,
                    ProducerWasteEnergy = 3f,
                });

                Assert.Equal(2, written.Count);
                foreach (string line in written)
                {
                    Assert.StartsWith("SomeModsReactor: ", line);
                }

                Assert.Equal(written, ThermalValidation.Problems);
            }
            finally
            {
                ThermalValidation.Writer = null;
                ThermalValidation.Reset();
            }
        }

        [Fact]
/// <summary>TheSameProblemIsSaidOnceHoweverOftenItIsFound operation.</summary>
        public void TheSameProblemIsSaidOnceHoweverOftenItIsFound()
        {
/// <summary>Capture operation.</summary>
            List<string> written = Capture();
            try
            {
                for (int i = 0; i < 500; i++)
                {
                    ThermalValidation.Check("Repeater",
                        new BlockThermalProperties { SpecificHeat = 0f });
                }

                Assert.Single(written);
                Assert.Single(ThermalValidation.Problems);

                ThermalValidation.Check("Another", new BlockThermalProperties { SpecificHeat = 0f });
                Assert.Equal(2, written.Count);
            }
            finally
            {
                ThermalValidation.Writer = null;
                ThermalValidation.Reset();
            }
        }

        [Fact]
/// <summary>NothingIsSaidAboutValuesThatAreFine operation.</summary>
        public void NothingIsSaidAboutValuesThatAreFine()
        {
/// <summary>Capture operation.</summary>
            List<string> written = Capture();
            try
            {
                ThermalValidation.Check("Fine", new BlockThermalProperties());
                ThermalValidation.Check(new ThermalSettings().Derive());

                Assert.Empty(written);
                Assert.Equal(0, ThermalValidation.Count);
            }
            finally
            {
                ThermalValidation.Writer = null;
                ThermalValidation.Reset();
            }
        }

        [Fact]
/// <summary>ASettingsFaultIsReportedAfterItHasBeenDerived operation.</summary>
        public void ASettingsFaultIsReportedAfterItHasBeenDerived()
        {
/// <summary>Capture operation.</summary>
            List<string> written = Capture();
            try
            {
/// <summary>ThermalSettings operation.</summary>
                ThermalSettings settings = new ThermalSettings();
                settings.Frequency = 2;
                settings.HeatTimeScale = 20000f;
                settings.Derive();

                ThermalValidation.Check(settings);

                Assert.NotEmpty(written);
                Assert.All(written, line => Assert.StartsWith("settings: ", line));
                Assert.Contains(written, line => line.Contains("HeatTimeScale / Frequency"));
            }
            finally
            {
                ThermalValidation.Writer = null;
                ThermalValidation.Reset();
            }
        }

        [Fact]
/// <summary>AProblemFoundBeforeAnybodyIsListeningIsStillRecorded operation.</summary>
        public void AProblemFoundBeforeAnybodyIsListeningIsStillRecorded()
        {
            ThermalValidation.Reset();
            ThermalValidation.Writer = null;
            try
            {
                ThermalValidation.Check("Early", new BlockThermalProperties { Emissivity = 4f });
                Assert.Equal(1, ThermalValidation.Count);
            }
            finally
            {
                ThermalValidation.Reset();
            }
        }

        [Fact]
/// <summary>AWriterThatThrowsIsNotTheReasonAWorldFailsToLoad operation.</summary>
        public void AWriterThatThrowsIsNotTheReasonAWorldFailsToLoad()
        {
            ThermalValidation.Reset();
            ThermalValidation.Writer = delegate { throw new System.InvalidOperationException(); };
            try
            {
                ThermalValidation.Check("Loud", new BlockThermalProperties { Emissivity = 4f });
                Assert.Equal(1, ThermalValidation.Count);
            }
            finally
            {
                ThermalValidation.Writer = null;
                ThermalValidation.Reset();
            }
        }
    }
}
