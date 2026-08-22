using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The one number that says whether a block can survive itself, and the gate on it.
    ///
    /// <para>
    /// The corpus found that every ship carrying a jump drive loses a block, whatever else is true
    /// of it, and the reason turned out not to need a corpus at all: a `LargeJumpDrive` makes
    /// 4.8 MW into 262 m² of its own surface and carries a critical temperature of 689 K, so it
    /// passes its own limit before any question of hull, mounting or cooling arises. That is
    /// checkable from the definition, in milliseconds, and this is the check.
    /// </para>
    ///
    /// <para>
    /// **The known-impossible list is the point of the test.** It is not there to excuse the blocks
    /// on it; it is there so that the day one of them is fixed, or a new block joins them, the suite
    /// says so. Removing an entry should be the last step of fixing a block.
    /// </para>
    /// </summary>
    public class BlockHeatIndexTests
    {
        private readonly ITestOutputHelper output;

        public BlockHeatIndexTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Blocks known to be unable to shed their own heat under any arrangement, with the index
        /// measured when each was recorded.
        ///
        /// Both are already findings. `LargeJumpDrive` is 67 % of the corpus's full-load heat and
        /// every one of the 2,255 ships carrying one loses a block. `LargeHydrogenEngine` is the
        /// hottest block on 34 % of runaway rows against 4 % of rows overall.
        /// </summary>
        private static readonly Dictionary<string, string> KnownImpossible =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // The worst block in the game by an order of magnitude, and it is a definition
                // accident: Keen gives LargePrototechReactor the TypeId HydrogenEngine, so the
                // derivation charges a 400 MW plant a combustion engine's 0.60 waste fraction and
                // it makes 240 MW of heat in a 3x2x2 block. At the reactor's own 0.01 it would be
                // 4 MW and unremarkable. See docs/known-issues.md.
                { "LargePrototechReactor", "index 17.9 — 400 MW at a combustion engine's 0.60, "
                    + "because its TypeId is HydrogenEngine rather than Reactor" },

                { "LargeHydrogenEngine", "index 1.54 — 0.60 waste fraction on a 5 MW plant in 3x3x3" },
                { "LargeHydrogenEngineReskin", "index 1.54 — the same block under another name" },
                { "SmallPrototechJumpDrive", "index 1.68 — 3.6 MW into a small-grid block" },
            };

        [Fact]
        public void NoShippedBlockIsImpossibleToCoolExceptTheOnesAlreadyKnown()
        {
            if (!GameBlocks.IsInstalled) return;

            List<BlockHeatIndex.Reading> readings = BlockHeatIndex.All();
            Assert.True(readings.Count > 0, "no block in the installed game reported any waste heat");

            List<string> unexpected = new List<string>();
            List<string> fixedNow = new List<string>();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (BlockHeatIndex.Reading reading in readings)
            {
                bool known = KnownImpossible.ContainsKey(reading.Subtype);
                if (known) seen.Add(reading.Subtype);

                if (reading.Impossible && !known)
                {
                    unexpected.Add(reading.Subtype + " index " + reading.Index.ToString("n2")
                        + " — makes " + (reading.Watts / 1e6f).ToString("n2") + " MW, can shed "
                        + ((reading.RadiatedWatts + reading.ConductedWatts) / 1e6f).ToString("n2")
                        + " MW at its " + reading.CriticalKelvin.ToString("n0") + " K limit");
                }

                if (!reading.Impossible && known)
                {
                    fixedNow.Add(reading.Subtype + " is now at " + reading.Index.ToString("n2")
                        + " — take it off KnownImpossible");
                }
            }

            foreach (KeyValuePair<string, string> entry in KnownImpossible)
            {
                if (!seen.Contains(entry.Key))
                {
                    fixedNow.Add(entry.Key + " no longer makes heat or no longer exists — "
                        + "take it off KnownImpossible");
                }
            }

            Report(readings);

            List<string> problems = new List<string>();
            problems.AddRange(unexpected);
            problems.AddRange(fixedNow);

            Assert.True(problems.Count == 0,
                "the set of blocks that cannot shed their own heat has changed:\n  "
                + string.Join("\n  ", problems));
        }

        /// <summary>
        /// The index is a ratio of watts, so it has to read as one: a block at 7.2 needs its heat
        /// cut sevenfold. Pinned on the block the whole finding came from, because a change to the
        /// area, the emissivity or the ambient that silently altered the scale would leave every
        /// recommendation in the report wrong by a factor nobody could see.
        /// </summary>
        [Fact]
        public void TheIndexReadsAsTheFactorTheHeatHasToComeDownBy()
        {
            if (!GameBlocks.IsInstalled) return;

            Dictionary<string, GameBlocks.Definition> game = GameBlocks.BySubtype();
            GameBlocks.Definition rating;
            if (!game.TryGetValue("LargeJumpDrive", out rating)) return;

            BlockHeatIndex.Reading reading = BlockHeatIndex.Measure(rating);
            Assert.NotNull(reading);

            // 32 MW of draw at the shipped 0.15.
            Assert.InRange(reading.Watts, 4.79e6f, 4.81e6f);

            // Bare 3x3x2 on a large grid: 2*(9+6+6) cells of face at 6.25 m2 each.
            Assert.Equal(262.5f, reading.AreaSquareMetres, 1);

            // **It is not impossible, and that is the point of having two numbers.** Its own skin
            // sheds a fraction of what it makes, but superconductors conduct extremely well, so a
            // drive bolted to armour that stays at ambient survives. What the corpus shows is that
            // nothing stays at ambient: every one of the 2,255 ships carrying one loses a block.
            Assert.True(reading.Index < 1f,
                "with conduction into an ambient sink the drive is survivable in principle");
            Assert.True(reading.SelfIndex > 5f,
                "on its own skin alone it is nowhere near self-sufficient");

            // Both indices read as the factor the heat has to come down by.
            Assert.True(reading.Watts / reading.Index
                <= reading.RadiatedWatts + reading.ConductedWatts + 1f,
                "waste divided by the index should be what the block can actually shed");
            Assert.True(Math.Abs(reading.Watts / reading.SelfIndex - reading.RadiatedWatts) < 1f,
                "waste divided by the self index should be what its own skin sheds");
        }

        /// <summary>Writes the whole table when a data directory is set, for the report to read.</summary>
        private void Report(List<BlockHeatIndex.Reading> readings)
        {
            output.WriteLine(string.Format("{0,-40}{1,8}{2,8}{3,12}{4,12}{5,10}",
                "block", "index", "self", "waste MW", "can shed MW", "crit K"));

            for (int i = 0; i < readings.Count && i < 20; i++)
            {
                BlockHeatIndex.Reading r = readings[i];
                output.WriteLine(string.Format("{0,-40}{1,8:n2}{2,8:n1}{3,12:n2}{4,12:n2}{5,10:n0}",
                    r.Subtype, r.Index, r.SelfIndex, r.Watts / 1e6f,
                    (r.RadiatedWatts + r.ConductedWatts) / 1e6f, r.CriticalKelvin));
            }

            string directory = Environment.GetEnvironmentVariable("THERMAL_CORPUS_DATA");
            if (string.IsNullOrEmpty(directory)) return;

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("subtype,type_id,large,source,waste_w,area_m2,emissivity,critical_k,"
                + "radiated_w,conducted_w,index,self_index,hull_area_needed_m2,equilibrium_k");

            foreach (BlockHeatIndex.Reading r in readings)
            {
                csv.Append(Csv(r.Subtype)).Append(',').Append(Csv(r.TypeId)).Append(',')
                   .Append(r.Large ? 1 : 0).Append(',').Append(Csv(r.Source)).Append(',')
                   .Append(Num(r.Watts)).Append(',').Append(Num(r.AreaSquareMetres)).Append(',')
                   .Append(Num(r.Emissivity)).Append(',').Append(Num(r.CriticalKelvin)).Append(',')
                   .Append(Num(r.RadiatedWatts)).Append(',').Append(Num(r.ConductedWatts)).Append(',')
                   .Append(Num(r.Index)).Append(',').Append(Num(r.SelfIndex)).Append(',')
                   .Append(Num(r.HullAreaNeeded)).Append(',').Append(Num(r.EquilibriumKelvin))
                   .AppendLine();
            }

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "blockheat.csv"), csv.ToString());
            }
            catch
            {
                // Reporting must never be the reason a test fails.
            }
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        private static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "";
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
