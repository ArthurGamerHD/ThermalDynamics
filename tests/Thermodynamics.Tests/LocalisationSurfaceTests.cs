using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class LocalisationSurfaceTests
    {
        private readonly ITestOutputHelper output;


        public LocalisationSurfaceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly KeyValuePair<string, string[]>[] Surfaces =
        {
            new KeyValuePair<string, string[]>("HUD in play", new[]
            {
                "ThermalHud.cs", "ThermalTerminal.cs",
                "Game/ThermalGridCues.cs", "Game/ThermalBlock.cs",
            }),
            new KeyValuePair<string, string[]>("settings menu", new[]
            {
                "ThermalSettingsMenu.cs", "ThermalSettingsWindow.cs",
            }),
            new KeyValuePair<string, string[]>("chat replies", new[]
            {
                "Game/HeatSourceCommand.cs", "Session.cs",
            }),
            new KeyValuePair<string, string[]>("telemetry files", new[] { "Telemetry" }),
            new KeyValuePair<string, string[]>("debug overlays", new[]
            {
                "ThermalDebugPanel.cs", "ThermalDebugView.cs", "Debug.cs",
                "WindOverlay.cs", "OverlayBudget.cs",
            }),
        };

        [Fact]

        public void TheSurfaceInsideTheGamesHudIsASmallFractionOfWhatTheModWrites()
        {
            string root = Path.Combine(
                ShippedBlocks.RepoRoot(), "Thermodynamics");

            Assert.True(Directory.Exists(root), "no mod sources at " + root);

            int total = 0;
            int inHud = 0;

            foreach (KeyValuePair<string, string[]> surface in Surfaces)
            {

                HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);

                foreach (string entry in surface.Value)
                {
                    string path = Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));

                    string[] files = File.Exists(path)
                        ? new[] { path }
                        : Directory.Exists(path)
                            ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
                            : new string[0];

                    Assert.True(files.Length > 0, "no sources for " + entry);

                    foreach (string file in files)
                    {
                        foreach (Match match in Regex.Matches(
                                     File.ReadAllText(file), "\"((?:[^\"\\\\]|\\\\.)*)\""))
                        {
                            if (Visible(match.Groups[1].Value)) distinct.Add(match.Groups[1].Value);
                        }
                    }
                }

                output.WriteLine("{0}: {1} distinct", surface.Key, distinct.Count);
                total += distinct.Count;
                if (surface.Key == "HUD in play") inHud = distinct.Count;
            }

            output.WriteLine("total {0}; the HUD is {1:n1} %", total, 100.0 * inHud / total);

            Assert.True(total > 500,
                "only " + total + " strings were found across every surface, so this test is "
                + "reading the wrong files and would pass whatever the code said");

            Assert.True(inHud > 10,
                "only " + inHud + " strings inside the game's HUD, which is too few to be the "
                + "readouts and means the surface list has gone stale");

            Assert.InRange(100.0 * inHud / total, 1.0, 10.0);
        }


        private static bool Visible(string value)
        {
            if (value.Length < 3) return false;
            if (Regex.IsMatch(value, @"^[nNfFeEgGxX]\d*$")) return false;
            if (!Regex.IsMatch(value, "[A-Za-z]")) return false;
            if (value.StartsWith("Gauge_", StringComparison.Ordinal)) return false;
            if (value.StartsWith("Thermal", StringComparison.Ordinal)) return false;

            if (Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$")
                && (char.IsUpper(value[0]) || value.IndexOf('_') >= 0))
            {
                return false;
            }

            return true;
        }
    }
}
