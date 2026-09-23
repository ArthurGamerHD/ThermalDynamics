using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    public class SuspendedRulesTests
    {
        private static readonly string[] Routes =
        {
            "CreativeMode",
            "IsCreative",
            "CreativeTools",
            "MyGameModeEnum",
            "GameMode",
            "EnableInstantBuildingCreative",
            "EnableGodMode",
            "InfiniteAmmo",
        };

        [Fact]

        public void NothingInTheModAsksWhetherTheWorldsRulesAreSuspended()
        {
            string scripts = Path.Combine(
                ShippedBlocks.RepoRoot(), "Thermodynamics");

            Assert.True(Directory.Exists(scripts), "no mod sources at " + scripts);


            List<string> offenders = new List<string>();
            int scanned = 0;

            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/RichHudFramework/")) continue;

                scanned++;
                string text = File.ReadAllText(file);

                foreach (string route in Routes)
                {
                    if (!text.Contains(route)) continue;

                    offenders.Add(route + " in " + Path.GetFileName(file));
                }
            }

            Assert.True(scanned > 50,
                "only " + scanned + " mod sources were scanned, so this check is looking in the "
                + "wrong place and would pass whatever the code said");

            offenders.Sort(StringComparer.Ordinal);
            Assert.True(offenders.Count == 0,
                "the simulation now behaves differently when the game's rules are suspended, which "
                + "document-of-intent.md says it does not:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]

        public void TurningOffTheConsequenceIsASettingRatherThanAMode()
        {
            ThermalSettingsFields fields = ThermalSettingsFields.Read();

            Assert.True(fields.Has("EnableDamage"),
                "there is no world setting that stops heat from destroying blocks, so a creative "
                + "builder's only lever would be the world's mode");

            Assert.True(fields.Has("EnableSuitDamage"),
                "there is no world setting that stops heat from hurting a player");
        }
    }

    internal class ThermalSettingsFields
    {
        private readonly string text;


        private ThermalSettingsFields(string text)
        {
            this.text = text;
        }


        public static ThermalSettingsFields Read()
        {
            string path = Path.Combine(
                ShippedBlocks.RepoRoot(), "Thermodynamics", "Settings.cs");

            Assert.True(File.Exists(path), "no Settings.cs at " + path);
            return new ThermalSettingsFields(File.ReadAllText(path));
        }


        public bool Has(string field)
        {
            return text.Contains(" " + field + " =") || text.Contains(" " + field + ";");
        }
    }
}
