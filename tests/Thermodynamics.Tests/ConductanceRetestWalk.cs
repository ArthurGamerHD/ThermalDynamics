using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **The retest set, run in the world before real conductances and in the world after it**, so
    /// backlog `C2` is answered with a measurement rather than an argument.
    ///
    /// <para>
    /// The standing panel is the wrong instrument for this question. It picks extremes because a
    /// dial sweep needs the largest lever it can find; a regression asks whether a change broke the
    /// ships people fly, which is what <c>tools/corpus/typical.csv</c> is for — forty hulls carrying
    /// thrust, power, an airtight room and 100 kW of load, nearest the population median on the four
    /// quantities the census found decide an outcome.
    /// </para>
    ///
    /// <code>
    ///     THERMAL_CORPUS_TESTS=1 THERMAL_CORPUS_DATA=out/retest \
    ///         dotnet test --filter ConductanceRetestWalk
    /// </code>
    ///
    /// <para>
    /// **The worlds run in sequence, and that is not a tuning choice.** A material override is a
    /// process-wide static that empties the shared model cache, so two worlds in flight at once
    /// would build ships out of each other's materials. The parallelism is inside a world, across
    /// the set, which is where it was anyway.
    /// </para>
    ///
    /// <para>
    /// **Reach is measured before anything is run and written to its own file.** An arm that reaches
    /// no block on this set reports "no change" in exactly the shape of an arm that reached every
    /// block and changed nothing, and the corpus filters admit vanilla hulls only — so the arm for
    /// the mod's own blocks is expected to reach zero here, and has to say so rather than read as a
    /// finding (`E8`).
    /// </para>
    /// </summary>
    public class ConductanceRetestWalk
    {
        public const string Header =
            "world,scenario,ship,workshop_id,large,blocks,retuned_blocks,"
            + "peak_k,mean_k,median_k,p95_k,min_k,hotspot_k,gradient_k,over_critical,over_share,"
            + "seconds_to_settle,seconds_to_critical,seconds_to_first_loss,"
            + "made_w,vented_w,radiation_w,convection_w,friction_w,solar_w,"
            + "substeps_demanded,hottest_block";

        public const string ReachHeader =
            "world,ship,workshop_id,blocks,retuned_blocks,retuned_share,subtypes,retuned_subtypes";

        /// <summary>
        /// The scenarios this retest runs, and why each is here.
        ///
        /// <para>
        /// The first five are the ones membership of the retest set already required an outcome
        /// for, so a ship that is in the set is a ship all five could be read from. The two flight
        /// cases are the half of `C2` that was named as unmeasured: the conversion left friction
        /// heating the leading face where it used to be computed and discarded, and a conductance
        /// change decides whether that face keeps the heat or spreads it.
        /// </para>
        /// </summary>
        private static readonly string[] Wanted =
        {
            "idle", "full-electrical", "burn-forward", "vacuum-sunlit", "recovery",
            "flight-100", "flight-300",
        };

        private static int rowsWritten;

        [Fact]
        public void EveryArmOfTheConversionGetsAMeasuredEffect()
        {
            if (CorpusFixture.Files().Count == 0) return;

            List<ShipSet.Entry> set = ShipSet.Read("THERMAL_RETEST", "tools/corpus/typical.csv");
            Assert.True(set.Count > 0,
                "the corpus is opted in but no retest set was read. Build one with "
                + "tools/corpus/typical.py, or point THERMAL_RETEST at it.");

            List<Blueprints.Ship> ships = ShipSet.Load(set, Progress);
            Assert.True(ships.Count > 0, "the retest set named ships but none of them could be read");

            Dictionary<string, Battery.Scenario> scenarios =
                new Dictionary<string, Battery.Scenario>(StringComparer.Ordinal);
            foreach (Battery.Scenario scenario in Battery.All()) scenarios[scenario.Name] = scenario;

            foreach (string name in Wanted)
            {
                Assert.True(scenarios.ContainsKey(name),
                    "the retest asks for scenario '" + name + "' and the battery has no such case");
            }

            List<ConductanceRetest.World> worlds = ConductanceRetest.All();

            // Exactly one control, or the comparison has no baseline — or two, in which case one of
            // the arms is silently being read against itself.
            int controls = 0;
            foreach (ConductanceRetest.World world in worlds)
            {
                if (world.IsShipped) controls++;
            }

            Assert.Equal(1, controls);

            Dictionary<string, int> reach = Reach(worlds, ships);

            foreach (ConductanceRetest.World world in worlds)
            {
                Run(world, reach, ships, scenarios);
            }

            Assert.True(rowsWritten > 0 || Record.Done().Count >= worlds.Count * ships.Count,
                "the retest produced no rows and had nothing recorded as already finished");
        }

        // ---- reach -----------------------------------------------------------------------------

        /// <summary>
        /// How many blocks each world actually retunes, counted by building every ship in every
        /// world and comparing each block's conductance against the shipped one for the same
        /// subtype.
        ///
        /// <para>
        /// Building is a small fraction of settling, so this costs little and buys the one thing the
        /// result columns cannot say on their own: whether an arm that moved nothing had anything to
        /// move.
        /// </para>
        /// </summary>
        private static Dictionary<string, int> Reach(List<ConductanceRetest.World> worlds,
            List<Blueprints.Ship> ships)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> rows = new List<string>();

            Dictionary<string, float> shippedBySubtype =
                new Dictionary<string, float>(StringComparer.Ordinal);

            object gate = new object();

            System.Threading.Tasks.ParallelOptions options =
                new System.Threading.Tasks.ParallelOptions
                { MaxDegreeOfParallelism = LabRun.Workers };

            try
            {
                Blueprints.MaterialOverride = null;
                Each(ships, options, ship =>
                {
                    Dictionary<string, float> mine =
                        new Dictionary<string, float>(StringComparer.Ordinal);
                    Conductances(ship, mine);

                    lock (gate)
                    {
                        foreach (KeyValuePair<string, float> pair in mine)
                        {
                            shippedBySubtype[pair.Key] = pair.Value;
                        }
                    }
                });

                foreach (ConductanceRetest.World world in worlds)
                {
                    // **Installed once per world, outside the fan-out.** The override is a
                    // process-wide static that empties the model cache, so setting it from a worker
                    // would clear the cache under the ships already in flight.
                    Blueprints.MaterialOverride = world.Material();

                    int total = 0;
                    ConductanceRetest.World current = world;

                    Each(ships, options, ship =>
                    {
                        Dictionary<string, float> mine =
                            new Dictionary<string, float>(StringComparer.Ordinal);
                        Dictionary<string, int> counts = Conductances(ship, mine);

                        int retunedBlocks = 0;
                        int retunedSubtypes = 0;

                        foreach (KeyValuePair<string, float> pair in mine)
                        {
                            float shipped;
                            lock (gate)
                            {
                                if (!shippedBySubtype.TryGetValue(pair.Key, out shipped)) continue;
                            }

                            if (Math.Abs(shipped - pair.Value) <= 1e-4f) continue;

                            retunedSubtypes++;
                            retunedBlocks += counts[pair.Key];
                        }

                        StringBuilder row = new StringBuilder();
                        row.Append(CorpusRecord.Text(current.Name)).Append(',');
                        row.Append(CorpusRecord.Text(ship.Name)).Append(',');
                        row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
                        row.Append(ship.Blocks).Append(',');
                        row.Append(retunedBlocks).Append(',');
                        row.Append(CorpusRecord.Num(
                            ship.Blocks == 0 ? 0f : (float)retunedBlocks / ship.Blocks)).Append(',');
                        row.Append(mine.Count).Append(',');
                        row.Append(retunedSubtypes);

                        lock (gate)
                        {
                            total += retunedBlocks;
                            rows.Add(row.ToString());
                        }
                    });

                    totals[world.Name] = total;
                    Progress(world.Name + " retunes " + total + " blocks across the set");
                }
            }
            finally
            {
                Blueprints.MaterialOverride = null;
            }

            if (CorpusRecord.On && rows.Count > 0) CorpusRecord.Write("reach", ReachHeader, rows);

            // **The composite has to have reached something, and it has to be the union of the
            // arms.** It is deliberately not asserted to reach every block: armour is where the
            // conversion was calibrated to land exactly, and on a typical hull most blocks derive
            // as steel and therefore conduct at 120 either side of the change. What must hold is
            // that the composite retunes at least as much as any single arm, because it is the same
            // rewrite with a wider reach — an arm that beat it would mean the arms are not subsets
            // and nothing below can be attributed.
            int composite;
            Assert.True(totals.TryGetValue("pre-units", out composite) && composite > 0,
                "the composite arm retuned no block at all, so nothing below is a comparison");

            foreach (KeyValuePair<string, int> arm in totals)
            {
                Assert.True(arm.Value <= composite,
                    "arm '" + arm.Key + "' retuned " + arm.Value + " blocks against the composite's "
                    + composite + "; the arms must be subsets of it");
            }

            return totals;
        }

        /// <summary>
        /// Every distinct subtype on a ship, its conductance in the installed world, and how many
        /// blocks carry it. Read off the built simulation rather than the definitions, because the
        /// override applies as the model is built and that is the value the solver will use.
        /// </summary>
        private static Dictionary<string, int> Conductances(Blueprints.Ship ship,
            Dictionary<string, float> into)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

            // **Re-read, because a ship resolves its models as it is parsed.** A blueprint read
            // before an override was installed carries the models it resolved then, and the model
            // cache being cleared behind it changes nothing — so counting off the ship as loaded
            // reported that every arm retuned nothing at all. This is the same reason the run below
            // reloads per world rather than reusing the parsed ship.
            ShipAssembly assembly = ship.Reload().Build();

            for (int s = 0; s < assembly.Simulations.Count; s++)
            {
                IList<ThermalNode> nodes = assembly.Simulations[s].Solver.Nodes;

                for (int n = 0; n < nodes.Count; n++)
                {
                    BlockModel model = nodes[n].Block.Model;
                    if (model == null) continue;

                    into[model.Name] = model.Thermal.Conductivity;

                    int count;
                    counts[model.Name] = counts.TryGetValue(model.Name, out count) ? count + 1 : 1;
                }
            }

            return counts;
        }

        // ---- the run ---------------------------------------------------------------------------

        /// <summary>
        /// One world: install it, run the set through every scenario, take it down again.
        ///
        /// The override is cleared in a finally, because leaving one installed contaminates every
        /// world after it and the contamination reads as a result rather than as an error.
        /// </summary>
        private static void Run(ConductanceRetest.World world, Dictionary<string, int> reach,
            List<Blueprints.Ship> ships, Dictionary<string, Battery.Scenario> scenarios)
        {
            object gate = new object();
            int written = 0;
            int skipped = 0;

            int retuned;
            reach.TryGetValue(world.Name, out retuned);

            HashSet<string> done = Record.Done();

            try
            {
                Blueprints.MaterialOverride = world.Material();

                System.Threading.Tasks.ParallelOptions options =
                    new System.Threading.Tasks.ParallelOptions
                    { MaxDegreeOfParallelism = LabRun.Workers };

                Each(ships, options, source =>
                {
                    // **A ship's rows are written the moment its ship is finished, and the ship is
                    // recorded as done.** Five worlds over forty hulls is hours, and a pass that
                    // flushed at the end of a world lost every row it had when it was killed
                    // (`O3`). Relaunching resumes; deleting done-retest.txt starts over.
                    string mark = world.Name + "|" + source.Name + "|" + source.WorkshopId;
                    if (done.Contains(mark))
                    {
                        lock (gate) skipped++;
                        return;
                    }

                    List<string> mine = new List<string>();

                    // Each worker owns its own copy, exactly as the survey does: block instances
                    // carry the applied load, so two scenarios sharing one instance would write
                    // over each other. It is also what re-reads the ship under this world's
                    // override, since a blueprint resolves its models as it is parsed.
                    Blueprints.Ship ship = source.Reload();

                    for (int s = 0; s < Wanted.Length; s++)
                    {
                        Battery.Scenario scenario;
                        if (!scenarios.TryGetValue(Wanted[s], out scenario)) continue;

                        try
                        {
                            ScenarioOutcome outcome = Battery.Run(ship, scenario);
                            mine.Add(Row(world, retuned, ship, outcome));
                        }
                        catch
                        {
                            // A ship this world cannot run must not lose the walk.
                        }
                    }

                    lock (gate)
                    {
                        if (CorpusRecord.On && mine.Count > 0)
                        {
                            CorpusRecord.Write("retest", Header, mine);
                        }

                        Record.Mark(mark);
                        written += mine.Count;
                        rowsWritten += mine.Count;
                    }
                });
            }
            finally
            {
                Blueprints.MaterialOverride = null;
            }

            Progress(world.Name + " (" + world.Restores + ") wrote " + written + " rows, "
                + "resumed past " + skipped + " ships");
        }

        private static string Row(ConductanceRetest.World world, int retuned,
            Blueprints.Ship ship, ScenarioOutcome o)
        {
            StringBuilder row = new StringBuilder();
            row.Append(CorpusRecord.Text(world.Name)).Append(',');
            row.Append(CorpusRecord.Text(o.Scenario)).Append(',');
            row.Append(CorpusRecord.Text(ship.Name)).Append(',');
            row.Append(ship.WorkshopId.ToString(CultureInfo.InvariantCulture)).Append(',');
            row.Append(ship.Large ? 1 : 0).Append(',');
            row.Append(ship.Blocks).Append(',');
            row.Append(retuned).Append(',');
            row.Append(CorpusRecord.Num(o.PeakKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MeanKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MedianKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.P95Kelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.MinKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.HotSpotKelvin)).Append(',');
            row.Append(CorpusRecord.Num(o.GradientKelvin)).Append(',');
            row.Append(o.BlocksOverCritical).Append(',');
            row.Append(CorpusRecord.Num(o.OverCriticalShare)).Append(',');
            row.Append(CorpusRecord.Num(o.SecondsToSettle)).Append(',');
            row.Append(CorpusRecord.Num(o.SecondsToCritical)).Append(',');
            row.Append(CorpusRecord.Num(o.SecondsToFirstLoss)).Append(',');
            row.Append(CorpusRecord.Num(o.MadeWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.VentedWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.RadiationWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.ConvectionWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.FrictionWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.SolarWatts)).Append(',');
            row.Append(CorpusRecord.Num(o.SubstepsDemanded)).Append(',');
            row.Append(CorpusRecord.Text(o.HottestBlock));
            return row.ToString();
        }

        // ---- the set and the resume record -----------------------------------------------

        /// <summary>The resume record for this walk. One per walk, beside its data.</summary>
        private static readonly ShipSet.Resume Record = new ShipSet.Resume("retest");

        /// <summary>
        /// Runs <paramref name="work"/> over every ship, one ship per chunk. The default range
        /// partitioner assumes items cost about the same and these do not — the largest hull on the
        /// set is over a hundred times the smallest — so a worker dealt a run of big ones finishes
        /// long after the rest.
        /// </summary>
        private static void Each(List<Blueprints.Ship> ships,
            System.Threading.Tasks.ParallelOptions options, Action<Blueprints.Ship> work)
        {
            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, ships.Count, 1),
                options,
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++) work(ships[i]);
                });
        }

        private static void Progress(string line)
        {
            ShipSet.Progress("retest", line);
        }
    }
}
