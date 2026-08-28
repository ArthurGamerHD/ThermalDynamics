using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// What every ship in the corpus *is*, before anything is stepped — the terms G4 needs and the
    /// survey's outcome rows do not carry. **This costs a build, not a simulation**, so the full
    /// corpus is minutes rather than the survey's eight hours and can be re-run whenever a definition
    /// moves.
    ///
    /// <para>
    /// <c>census.csv</c> is one row per ship, <c>composition.csv</c> one per ship and heat-making block
    /// type. See balance.md, The datasets.
    /// </para>
    /// </summary>
    [Collection("alone")]
    public class CorpusCensus
    {
        /// <summary>Everything the census learned about one ship.</summary>
        private class Censused
        {
            public string Row;
            public List<string> Composition = new List<string>();
        }

        [Fact]
        public void EveryShipInTheCorpusIsCounted()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<Censused> results = CorpusFixture.Sweep("census", Count);

            Assert.True(results.Count > 0, "the corpus yielded no censusable ships");

            // The census exists to make G4 answerable, and it cannot do that if the terms it was
            // added for are absent. A run where nothing reported exposure has a build or a mount
            // table broken upstream, and would otherwise write a full-looking file of zeroes.
            int withExposure = 0;
            int withPower = 0;
            foreach (Censused ship in results)
            {
                string[] fields = ship.Row.Split(',');
                if (Positive(fields, ExposedAreaColumn)) withExposure++;
                if (Positive(fields, InstalledPowerColumn)) withPower++;
            }

            Assert.True(withExposure > 0, "not one ship reported any exposed area");
            Assert.True(withPower > 0, "not one ship reported any installed power");

            // The geometry terms are what the hot-spot question rests on, and a broken link graph
            // or an unapplied load would write a full-looking file of zeroes for all of them.
            int withSources = 0;
            foreach (Censused ship in results)
            {
                if (Positive(ship.Row.Split(','), HeatSourcesColumn)) withSources++;
            }
            Assert.True(withSources > 0, "not one ship reported a single heat source");
        }

        /// <summary>Column of <c>exposed_area_m2</c> in <see cref="Header"/>.</summary>
        private const int ExposedAreaColumn = 11;

        /// <summary>Column of <c>installed_power_w</c> in <see cref="Header"/>.</summary>
        private const int InstalledPowerColumn = 16;

        /// <summary>Column of <c>heat_sources</c>, the first of the geometry terms.</summary>
        private const int HeatSourcesColumn = 31;

        private static bool Positive(string[] fields, int column)
        {
            float value;
            return column < fields.Length
                && float.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out value) && value > 0f;
        }

        public const string Header =
            "ship,workshop_id,large,blocks,grids,joints,rooms,"
            + "exposed_blocks,buried_blocks,buried_share,sealed_blocks,exposed_area_m2,"
            + "thermal_mass_j_per_k,mean_capacity_j_per_k,"
            + "armor_n,producer_n,installed_power_w,store_n,store_power_w,"
            + "thruster_n,thrust_n,tool_n,consumer_n,consumer_draw_w,other_n,"
            + "waste_idle_w,waste_full_w,waste_burn_w,"
            + "exposure_m2_per_kw,capacity_j_per_k_per_w,top_source,top_source_w,top_source_n,"
            + "heat_sources,w_per_m2,max_depth,heat_depth_mean,heat_depth_max,clumping,heat_gini,"
            + "local_w_max,local_w_per_m2_max,heat_spread_m,hottest_conductance_w_per_k";

        public const string CompositionHeader =
            "ship,workshop_id,subtype,type_id,count,waste_full_w,share_of_waste";

        /// <summary>
        /// Builds one ship, measures it under three load states, and returns its rows.
        ///
        /// The three states are the same ones the survey runs, so a census row and an outcome row
        /// for the same ship describe the same configuration. Nothing is stepped: applying a load
        /// settles every block's heat generation, which is all the census reads.
        /// </summary>
        private static Censused Count(Blueprints.Ship ship)
        {
            ShipAssembly assembly = ship.Build();
            if (assembly.NodeCount == 0) return null;

            // By model name: a placed block carries its type where the game states no subtype,
            // and a subtype index resolves all thirteen of those to one arbitrary definition.
            Dictionary<string, GameBlocks.Definition> definitions = GameBlocks.ByModelName();

            // ---- the hull, as built ------------------------------------------------------------

            int exposedBlocks = 0;
            int buried = 0;
            long sealedBlocks = 0;
            double exposedArea = 0d;
            double thermalMass = 0d;

            for (int g = 0; g < assembly.Simulations.Count; g++)
            {
                ThermalSolver solver = assembly.Simulations[g].Solver;
                for (int i = 0; i < solver.Nodes.Count; i++)
                {
                    ThermalNode node = solver.Nodes[i];
                    exposedArea += node.ExposedArea;
                    thermalMass += node.ThermalMass;

                    if (node.ExposedArea > 0f) exposedBlocks++;
                    else
                    {
                        buried++;

                        // A block with neither a face to radiate from nor a link to conduct along
                        // cannot shed heat by any route. The survey counts these too; repeated here
                        // so the census stands on its own.
                        if (solver.Nodes.Count >= 2 && solver.NodeConductanceTotal(i) <= 0f)
                        {
                            sealedBlocks++;
                        }
                    }
                }
            }

            // ---- what it installs ---------------------------------------------------------------

            int armor = 0, producers = 0, stores = 0, thrusters = 0, tools = 0, consumers = 0, other = 0;
            double installed = 0d, storePower = 0d, thrust = 0d, draw = 0d;
            Dictionary<string, int> counts = new Dictionary<string, int>();

            foreach (ThermalNode node in assembly.Nodes)
            {
                string name = node.Block.Name;
                int seen;
                counts[name] = counts.TryGetValue(name, out seen) ? seen + 1 : 1;

                GameBlocks.Definition definition;
                if (!definitions.TryGetValue(name, out definition)) { other++; continue; }

                if (definition.PowerOutputWatts > 0f)
                {
                    if (ShipLoad.IsStore(definition.TypeId))
                    {
                        stores++;
                        storePower += definition.PowerOutputWatts;
                    }
                    else
                    {
                        producers++;
                        installed += definition.PowerOutputWatts;
                    }
                }
                else if (definition.ThrustNewtons > 0f)
                {
                    thrusters++;
                    thrust += definition.ThrustNewtons;
                }
                else if (definition.TypeId == "CubeBlock")
                {
                    armor++;
                }
                else if (definition.PowerDrawWatts > 0f)
                {
                    consumers++;
                    draw += definition.PowerDrawWatts;
                }
                else other++;

                if (IsToolType(definition.TypeId)) tools++;
            }

            // ---- what it makes, at each load ---------------------------------------------------

            float idle = ShipLoad.Apply(assembly, ShipLoad.State.Idle);
            float burn = ShipLoad.Apply(assembly, ShipLoad.State.Burn(Face.Forward));

            // Full last, so the per-block figures the composition rows and the geometry read are
            // the full-load ones.
            float full = ShipLoad.Apply(assembly, ShipLoad.State.Full);

            // Where the heat is put, rather than how much of it there is. Measured after the load
            // is applied, because every figure in it reads HeatGenerationWatts.
            HeatGeometry.Result geometry = HeatGeometry.Measure(assembly, ship.Large);

            Dictionary<string, double> wasteBySubtype = new Dictionary<string, double>();
            foreach (ThermalNode node in assembly.Nodes)
            {
                float watts = node.HeatGenerationWatts;
                if (watts <= 0f) continue;

                string name = node.Block.Name;
                double seen;
                wasteBySubtype[name] = wasteBySubtype.TryGetValue(name, out seen)
                    ? seen + watts : watts;
            }

            string topSource = "";
            double topWatts = 0d;
            foreach (KeyValuePair<string, double> entry in wasteBySubtype)
            {
                if (entry.Value <= topWatts) continue;
                topWatts = entry.Value;
                topSource = entry.Key;
            }

            // ---- the two derived terms the criteria wanted --------------------------------------
            //
            // Exposure per kilowatt is G4's missing side: how much radiating surface a design gives
            // each kilowatt it makes. Capacity per watt is the time constant in disguise — joules
            // per kelvin against watts arriving is, to a factor, the seconds a hull takes to move a
            // kelvin, which is what the survey's six-second failures are really about.
            double exposurePerKilowatt = full > 0f ? exposedArea / (full / 1000d) : 0d;
            double capacityPerWatt = full > 0f ? thermalMass / full : 0d;

            Censused censused = new Censused();

            StringBuilder row = new StringBuilder();
            row.Append(CorpusRecord.Text(ship.Name)).Append(',');
            row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(ship.Large ? 1 : 0).Append(',');
            row.Append(assembly.NodeCount).Append(',');
            row.Append(ship.Grids.Count).Append(',');
            row.Append(assembly.Bridges.Count).Append(',');
            row.Append(assembly.RoomCount).Append(',');
            row.Append(exposedBlocks).Append(',');
            row.Append(buried).Append(',');
            row.Append(CorpusRecord.Num(assembly.NodeCount > 0
                ? (float)buried / assembly.NodeCount : 0f)).Append(',');
            row.Append(sealedBlocks).Append(',');
            row.Append(CorpusRecord.Num((float)exposedArea)).Append(',');
            row.Append(CorpusRecord.Num((float)thermalMass)).Append(',');
            row.Append(CorpusRecord.Num(assembly.NodeCount > 0
                ? (float)(thermalMass / assembly.NodeCount) : 0f)).Append(',');
            row.Append(armor).Append(',');
            row.Append(producers).Append(',');
            row.Append(CorpusRecord.Num((float)installed)).Append(',');
            row.Append(stores).Append(',');
            row.Append(CorpusRecord.Num((float)storePower)).Append(',');
            row.Append(thrusters).Append(',');
            row.Append(CorpusRecord.Num((float)thrust)).Append(',');
            row.Append(tools).Append(',');
            row.Append(consumers).Append(',');
            row.Append(CorpusRecord.Num((float)draw)).Append(',');
            row.Append(other).Append(',');
            row.Append(CorpusRecord.Num(idle)).Append(',');
            row.Append(CorpusRecord.Num(full)).Append(',');
            row.Append(CorpusRecord.Num(burn)).Append(',');
            row.Append(CorpusRecord.Num((float)exposurePerKilowatt)).Append(',');
            row.Append(CorpusRecord.Num((float)capacityPerWatt)).Append(',');
            row.Append(CorpusRecord.Text(topSource)).Append(',');
            row.Append(CorpusRecord.Num((float)topWatts)).Append(',');
            row.Append(topSource.Length == 0 || !counts.ContainsKey(topSource)
                ? 0 : counts[topSource]).Append(',');
            row.Append(geometry.Sources).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.WattsPerSquareMetre)).Append(',');
            row.Append(geometry.MaxDepth).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.HeatDepthMean)).Append(',');
            row.Append(geometry.HeatDepthMax).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.Clumping)).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.Gini)).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.LocalWattsMax)).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.LocalWattsPerAreaMax)).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.SpreadMetres)).Append(',');
            row.Append(CorpusRecord.Num((float)geometry.HottestSourceConductance));

            censused.Row = row.ToString();

            // ---- composition, for the block types that carry heat --------------------------------
            //
            // Every subtype would be six hundred columns of mostly armour. What a balance question
            // needs is the types that make the heat, so a distribution can be read against the
            // heating: the rows are the heat-making types plus their counts, and their share of the
            // hull's total waste.
            foreach (KeyValuePair<string, double> entry in wasteBySubtype)
            {
                GameBlocks.Definition definition;
                definitions.TryGetValue(entry.Key, out definition);

                StringBuilder line = new StringBuilder();
                line.Append(CorpusRecord.Text(ship.Name)).Append(',');
                line.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
                line.Append(CorpusRecord.Text(entry.Key)).Append(',');
                line.Append(CorpusRecord.Text(definition == null ? "" : definition.TypeId)).Append(',');
                line.Append(counts.ContainsKey(entry.Key) ? counts[entry.Key] : 0).Append(',');
                line.Append(CorpusRecord.Num((float)entry.Value)).Append(',');
                line.Append(CorpusRecord.Num(full > 0f ? (float)(entry.Value / full) : 0f));
                censused.Composition.Add(line.ToString());
            }

            if (CorpusRecord.On)
            {
                CorpusRecord.Write("census", Header, new List<string> { censused.Row });
                if (censused.Composition.Count > 0)
                {
                    CorpusRecord.Write("composition", CompositionHeader, censused.Composition);
                }
            }

            return censused;
        }

        /// <summary>
        /// Blocks that run only when a player is using them. Mirrors <c>ShipLoad</c>'s own list,
        /// which is private to it; a tool is counted here as well as in its power category, because
        /// "how many tools" and "how many consumers" are different questions about the same block.
        /// </summary>
        private static bool IsToolType(string typeId)
        {
            switch (typeId)
            {
                case "Drill":
                case "ShipGrinder":
                case "ShipWelder":
                case "Refinery":
                case "Assembler":
                case "SmallGatlingGun":
                case "LargeGatlingTurret":
                case "InteriorTurret":
                case "SmallMissileLauncher":
                case "SmallMissileLauncherReload":
                case "LargeMissileTurret":
                case "JumpDrive":
                    return true;
                default:
                    return false;
            }
        }
    }
}
