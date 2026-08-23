using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// That the committed retest set is a set something can actually run.
    ///
    /// <para>
    /// <c>tools/corpus/typical.csv</c> was written by <c>typical.py</c> and then read by nothing for
    /// a while, which is the defect class this repository keeps finding: built, documented, and
    /// reached by nothing (`D2`). These checks are the cheap half of wiring it — they need neither
    /// the corpus opt-in nor a game install, so a set that has gone stale fails in the ordinary
    /// suite rather than eight hours into a walk.
    /// </para>
    /// </summary>
    public class RetestSetTests
    {
        /// <summary>
        /// The set is present, has the count its own tool documents, and carries a blueprint path
        /// per ship — without which the walk falls back to parsing all 9,981 corpus blueprints to
        /// find forty ships, which is how earlier runs died.
        /// </summary>
        [Fact]
        public void TheRetestSetIsReadableAndCarriesAPathPerShip()
        {
            List<ShipSet.Entry> rows = ShipSet.Read("THERMAL_RETEST", "tools/corpus/typical.csv");
            Assert.True(rows.Count > 0,
                "tools/corpus/typical.csv is missing or empty; rebuild it with typical.py");

            Assert.Equal(40, rows.Count);

            foreach (ShipSet.Entry entry in rows)
            {
                Assert.False(string.IsNullOrEmpty(entry.Name), "a retest row has no ship name");
                Assert.False(string.IsNullOrEmpty(entry.Path),
                    "retest ship '" + entry.Name + "' carries no blueprint path");
            }
        }

        /// <summary>
        /// **Both grid sizes and every size band are represented.** The set is drawn per grid size
        /// and across four size bands on purpose, so that a retest is not accidentally all frigates;
        /// a regenerated set that lost a band would still be forty ships and would still run.
        /// </summary>
        [Fact]
        public void TheRetestSetSpansBothGridSizesAndEveryBand()
        {
            HashSet<string> large = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> bands = new HashSet<string>(StringComparer.Ordinal);

            foreach (ShipSet.Entry entry in
                ShipSet.Read("THERMAL_RETEST", "tools/corpus/typical.csv"))
            {
                if (entry.Fields.Length < 6) continue;
                large.Add(entry.Fields[3]);
                bands.Add(entry.Fields[3] + "/" + entry.Fields[4]);
            }

            Assert.Equal(2, large.Count);
            Assert.Equal(8, bands.Count);
        }

        /// <summary>
        /// The membership rule says every ship in the set had all five of the survey's scenarios
        /// read from it, and the walk adds the two flight cases on top. All seven have to be names
        /// the battery answers to, or the walk quietly measures fewer scenarios than it reports.
        /// </summary>
        [Fact]
        public void EveryScenarioTheRetestAsksForExists()
        {
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) known.Add(scenario.Name);

            string[] wanted =
            {
                "idle", "full-electrical", "burn-forward", "vacuum-sunlit", "recovery",
                "flight-100", "flight-300",
            };

            foreach (string name in wanted)
            {
                Assert.True(known.Contains(name),
                    "the retest asks for scenario '" + name + "' and the battery has no such case");
            }
        }

        /// <summary>
        /// The pre-conversion table is the one the pre-conversion file held, and the calibration
        /// point holds: mild steel comes out where it was, which is the whole reason the conversion
        /// could be made without moving armour.
        /// </summary>
        [Fact]
        public void ThePreConversionTableIsTheOneTheOldFileHeld()
        {
            Assert.Equal(120f, ConductanceRetest.PreDefault, 3);
            Assert.Equal(200f, ConductanceRetest.PreBest, 3);

            // Mild steel, the calibration point: 50 x 2.4 is exactly the old 0.6 x 200.
            Assert.Equal(ConductanceRetest.PreDefault,
                ReferenceMaterials.MildSteel.Conductivity * ThermalConstants.ConductionScale, 3);

            // The four families that were authored at quality 1, and nothing else.
            Assert.True(ConductanceRetest.WasBest("Thrust", "LargeBlockLargeThrust"));
            Assert.True(ConductanceRetest.WasBest("Reactor", "LargeBlockLargeGenerator"));
            Assert.True(ConductanceRetest.WasBest("CubeBlock", "Gauge_LG_Radiator"));
            Assert.True(ConductanceRetest.WasBest("UpgradeModule", "Gauge_LG_CoolantPump"));

            Assert.False(ConductanceRetest.WasBest("CubeBlock", "LargeBlockArmorBlock"));
            Assert.False(ConductanceRetest.WasBest("SolarPanel", "LargeBlockSolarPanel"));
        }

        /// <summary>
        /// Every arm rewrites the family it names and leaves the rest alone. Without this a typo in
        /// a predicate produces a world that is neither the shipped one nor the old one, and the
        /// walk reports its difference as a finding.
        /// </summary>
        [Fact]
        public void EachArmRewritesOnlyWhatItNames()
        {
            Dictionary<string, ConductanceRetest.World> worlds =
                new Dictionary<string, ConductanceRetest.World>(StringComparer.Ordinal);
            foreach (ConductanceRetest.World world in ConductanceRetest.All())
            {
                worlds[world.Name] = world;
            }

            Assert.Null(worlds["shipped"].Material());

            float armour = Conductivity(worlds["vanilla-flat"], "CubeBlock", "LargeBlockArmorBlock");
            Assert.Equal(ConductanceRetest.Authored(ConductanceRetest.PreDefault), armour, 3);

            // …and the same arm leaves a thruster where the shipped world put it.
            Assert.True(float.IsNaN(
                Conductivity(worlds["vanilla-flat"], "Thrust", "LargeBlockLargeThrust")));

            Assert.True(float.IsNaN(
                Conductivity(worlds["thrust-and-reactors"], "CubeBlock", "LargeBlockArmorBlock")));
            Assert.Equal(ConductanceRetest.Authored(ConductanceRetest.PreBest),
                Conductivity(worlds["thrust-and-reactors"], "Thrust", "LargeBlockLargeThrust"), 3);

            Assert.True(float.IsNaN(
                Conductivity(worlds["mod-blocks"], "Thrust", "LargeBlockLargeThrust")));
            Assert.Equal(ConductanceRetest.Authored(ConductanceRetest.PreBest),
                Conductivity(worlds["mod-blocks"], "CubeBlock", "Gauge_LG_Radiator"), 3);

            // The composite reaches both halves.
            Assert.Equal(ConductanceRetest.Authored(ConductanceRetest.PreDefault),
                Conductivity(worlds["pre-units"], "CubeBlock", "LargeBlockArmorBlock"), 3);
            Assert.Equal(ConductanceRetest.Authored(ConductanceRetest.PreBest),
                Conductivity(worlds["pre-units"], "Thrust", "LargeBlockLargeThrust"), 3);
        }

        /// <summary>
        /// What an arm makes of one block, or NaN where it hands the block straight back — which is
        /// how "this arm does not reach here" is told apart from "this arm sets it to the same
        /// value".
        /// </summary>
        private static float Conductivity(ConductanceRetest.World world, string typeId, string subtype)
        {
            BlockThermalProperties source = new BlockThermalProperties { Conductivity = 12.5f };
            BlockThermalProperties result = world.Material()(typeId, subtype, source);
            return ReferenceEquals(result, source) ? float.NaN : result.Conductivity;
        }
    }
}
