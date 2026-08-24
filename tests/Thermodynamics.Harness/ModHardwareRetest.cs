using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What the conversion did to the mod's own cooling hardware**, which the corpus cannot
    /// answer.
    ///
    /// <para>
    /// <see cref="ConductanceRetest"/> runs the retest set of real workshop hulls, and the corpus
    /// filters admit vanilla ships only — so its arm for the mod's blocks reaches zero of them and
    /// says so. The two largest single moves the conversion made are therefore invisible there:
    /// coolant pipes went 200 → 960 W/(m·K) effective (4.8×, copper) and radiators 200 → 568.8
    /// (2.84×, aluminium). This prices both on a rig.
    /// </para>
    ///
    /// <para>
    /// **One block moves per rig, and everything else stays shipped.** The composite is already
    /// measured on the corpus; what is wanted here is attribution, and a rig that moved the reactor
    /// as well could not say whether the radiator earned anything. So each rig restores the
    /// pre-conversion conductance of exactly the family it is about.
    /// </para>
    ///
    /// <para>
    /// **The rigs are built from <see cref="Catalog"/> and the delta is what is read.** Catalog is a
    /// stand-in and known to drift from the shipped definitions (backlog `C4`), but both arms of
    /// each comparison are built from the same stand-in, so the drift is common to them and cancels
    /// in the difference. What must not drift is the one figure under test, and
    /// <c>ModHardwareRetestTests</c> holds Catalog's pipe and radiator conductances against the
    /// shipped ones so this cannot quietly become a measurement of something else.
    /// See balance.md, What the real-unit conversion moved.
    /// </para>
    /// </summary>
    public static class ModHardwareRetest
    {
        /// <summary>Simulated seconds each rig is run for. Long enough for a stack to saturate.</summary>
        private const float Seconds = 14400f;

        /// <summary>
        /// Watts of *heat* the source puts into the rig.
        ///
        /// Stated as heat rather than as a reactor's output, because the rig is about what the
        /// cooling hardware does with a load and not about what a reactor wastes — and while it was
        /// stated as output it was quietly a quarter of it, on a catalogue reactor that wasted
        /// twenty-five times what the shipped one does ([backlog.md](../../docs/backlog.md) `C4`).
        /// 75 kW is what the old 300 kW at that fraction actually delivered, so the rigs carry the
        /// same load they always did.
        /// </summary>
        public const float SourceWatts = 75000f;

        /// <summary>One rig in one world.</summary>
        public class Row
        {
            public string Rig;

            /// <summary>How much cooling is bolted on: radiators, or pipes in the ring.</summary>
            public int Count;

            /// <summary>The world: "shipped", or "pre-units" for the family under test.</summary>
            public string World;

            /// <summary>Conductance the family under test carried, W/(m·K) effective.</summary>
            public float Conductance;

            /// <summary>Where the heat source settled. Lower is better cooling.</summary>
            public float SourceKelvin;

            /// <summary>The hottest block anywhere in the rig.</summary>
            public float PeakKelvin;

            /// <summary>The far end of the cooling, which says whether the heat arrived.</summary>
            public float FarKelvin;
        }

        /// <summary>
        /// A source with <paramref name="count"/> radiators stacked on it, in shadow, run to
        /// equilibrium — the same column shape <see cref="CoolingLadder"/> uses, and for the same
        /// reason: holding the mounting fixed is what makes two rungs comparable.
        /// </summary>
        public static Row RadiatorStack(int count, bool preConversion)
        {
            BlockModel radiator = Cooler(Catalog.Radiator, preConversion);

            GridBuilder builder = GridBuilder.Large();
            builder.Place(Catalog.LargeReactor(), Vector3I.Zero).Wasting(SourceWatts);
            BlockInstance source = builder.Last;

            int height = 3;
            BlockInstance top = null;

            for (int i = 0; i < count; i++)
            {
                builder.Place(radiator, new Vector3I(0, height, 0));
                top = builder.Last;
                height += radiator.Size.Y;
            }

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.StepExact((int)(Seconds / simulation.Settings.StepSeconds), Worlds.Shadow());

            return Read("radiator-stack", count, preConversion, radiator.Thermal.Conductivity,
                simulation, source, top);
        }

        /// <summary>
        /// A source bolted to a running coolant ring, in shadow, run to equilibrium.
        ///
        /// <para>
        /// The ring is what a player builds to move heat somewhere it can leave, and the pipe's
        /// material conductance is only one of the two paths through it: the fluid couples to the
        /// pipe through <c>LoopThermalProperties.Conductivity</c>, which the conversion never
        /// touched, while the pipe conducts to whatever is bolted to it through its own material.
        /// Which of the two the 4.8× actually reaches is the question.
        /// </para>
        /// </summary>
        public static Row CoolantRing(int width, int depth, bool preConversion)
        {
            List<Vector3I> cells = PipeFitter.RectangleXZ(Vector3I.Zero, width, depth);

            Dictionary<int, Vector3I> sinks = new Dictionary<int, Vector3I>();
            sinks[2] = Vector3I.Down;

            GridBuilder builder = GridBuilder.Large();

            float conductance;

            using (Pre(preConversion))
            {
                PipeFitter.BuildRing(builder, cells, -1, sinks);
                conductance = Catalog.CoolantThermal().Conductivity;
            }

            builder.Place(Catalog.LargeReactor(), cells[2] + Vector3I.Down * 3)
                .Wasting(SourceWatts);
            BlockInstance source = builder.Last;

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings(), 293.15f);
            simulation.StepExact((int)(Seconds / simulation.Settings.StepSeconds), Worlds.Shadow());

            BlockInstance far = null;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Block.Position == cells[cells.Count / 2]) far = nodes[i].Block;
            }

            Row row = Read("coolant-ring", cells.Count, preConversion, conductance,
                simulation, source, far);
            return row;
        }

        /// <summary>Radiator counts to try, and ring sizes. Doubling, because the interesting
        /// part is whether the gap grows with the stack or stays where one block puts it.</summary>
        private static readonly int[] Stacks = { 1, 2, 4, 8, 16, 32 };

        /// <summary>Ring side lengths, square. A bigger ring is a longer path for the fluid.</summary>
        private static readonly int[] Rings = { 4, 6, 8 };

        /// <summary>Every rig in both worlds, as a table.</summary>
        public static List<Row> Measure()
        {
            List<Row> rows = new List<Row>();

            for (int i = 0; i < Stacks.Length; i++)
            {
                rows.Add(RadiatorStack(Stacks[i], false));
                rows.Add(RadiatorStack(Stacks[i], true));
            }

            for (int i = 0; i < Rings.Length; i++)
            {
                rows.Add(CoolantRing(Rings[i], Rings[i], false));
                rows.Add(CoolantRing(Rings[i], Rings[i], true));
            }

            return rows;
        }


        /// <summary>
        /// What the conversion did to a spread of vanilla blocks, measured rather than argued.
        ///
        /// <para>
        /// It belongs beside the rigs because it is the same question one step out: the rigs price
        /// the two families <c>Cubes.xml</c> authors, and this is what the same change did to the
        /// blocks whose conductance is *derived* from what they are built out of — which is most of
        /// the game, and which no table anywhere stated until the retest went looking.
        /// </para>
        /// </summary>
        public static string Table()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            List<ConductanceRetest.Move> moves = ConductanceRetest.Moves();

            text.AppendLine("What the conversion to real W/(m K) did, block by block");
            text.AppendLine();
            text.AppendLine("Before it, every block took one of two conductances: 120 from the 0.6");
            text.AppendLine("fall-through, or 200 for Thrust, Reactor and the mod's own blocks at quality 1.");
            text.AppendLine();
            text.AppendLine(string.Format("{0,-36} {1,8} {2,8} {3,8}",
                "block", "before", "now", "ratio"));

            for (int i = 0; i < moves.Count; i++)
            {
                ConductanceRetest.Move move = moves[i];
                text.AppendLine(string.Format("{0,-36} {1,8:0} {2,8:0.0} {3,8:0.00}x",
                    move.Subtype, move.Before, move.After, move.Ratio));
            }

            List<string> missing = ConductanceRetest.Missing();
            if (missing.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("named here and not in the installed game: "
                    + string.Join(", ", missing.ToArray()));
            }
            else if (moves.Count == 0)
            {
                text.AppendLine("  (no game installed, so nothing could be derived)");
            }

            text.AppendLine();
            return text.ToString();
        }

        /// <summary>The table, with the shipped and pre-conversion rows paired so the delta reads.</summary>
        public static string Report()
        {
            List<Row> rows = Measure();
            System.Text.StringBuilder text = new System.Text.StringBuilder();

            text.Append(Table());
            text.AppendLine("What the real-unit conversion did to the mod's own cooling hardware");
            text.AppendLine();
            text.AppendLine("One family moves per rig and everything else stays shipped, because the");
            text.AppendLine("composite is already measured on the retest set of workshop hulls — and not");
            text.AppendLine("one of those forty carries a coolant pipe or a radiator, which is why these");
            text.AppendLine("two moves need a rig at all. Source is " + (SourceWatts / 1000f)
                + " kW in shadow, run to " + (Seconds / 3600f) + " h.");
            text.AppendLine();
            text.AppendLine(string.Format("{0,-15} {1,5} {2,10} {3,10} {4,10} {5,10} {6,9}",
                "rig", "n", "W/(m K)", "source K", "peak K", "far K", "saved K"));

            for (int i = 0; i + 1 < rows.Count; i += 2)
            {
                Row shipped = rows[i];
                Row before = rows[i + 1];

                text.AppendLine(string.Format("{0,-15} {1,5} {2,10:0} {3,10:0.0} {4,10:0.0} {5,10:0.0} {6,9}",
                    before.Rig, before.Count, before.Conductance, before.SourceKelvin,
                    before.PeakKelvin, before.FarKelvin, "-"));

                text.AppendLine(string.Format("{0,-15} {1,5} {2,10:0} {3,10:0.0} {4,10:0.0} {5,10:0.0} {6,9:+0.00;-0.00}",
                    shipped.Rig, shipped.Count, shipped.Conductance, shipped.SourceKelvin,
                    shipped.PeakKelvin, shipped.FarKelvin,
                    before.SourceKelvin - shipped.SourceKelvin));
            }

            text.AppendLine();
            text.AppendLine("saved K is the shipped world against the pre-conversion one: positive means");
            text.AppendLine("the conversion left the source cooler.");
            return text.ToString();
        }

        // ---- the two worlds ---------------------------------------------------------------------

        /// <summary>
        /// Builds one cooler model with the pre-conversion conductance installed, and takes the
        /// override straight back down.
        ///
        /// The override is a process-wide static on <see cref="Catalog"/>, so it is held for exactly
        /// as long as the one model is being made. Leaving it up would move the reactor as well and
        /// the rig would stop attributing anything.
        /// </summary>
        private static BlockModel Cooler(Func<BlockModel> make, bool preConversion)
        {
            using (Pre(preConversion)) return make();
        }

        /// <summary>The pre-conversion conductance, installed for the length of a using block.</summary>
        private static IDisposable Pre(bool preConversion)
        {
            if (!preConversion) return new Restore(null);

            Func<BlockThermalProperties, BlockThermalProperties> previous = Catalog.MaterialOverride;

            Catalog.MaterialOverride = properties =>
            {
                BlockThermalProperties copy = properties.Clone();
                copy.Conductivity = ConductanceRetest.Authored(ConductanceRetest.PreBest);
                return copy;
            };

            return new Restore(previous);
        }

        /// <summary>
        /// Puts the override back the way it was found. Also used for the shipped world, where it
        /// restores the null it was handed — a rig that skipped the restore in one branch and not
        /// the other would leave the two worlds built differently for a reason that has nothing to
        /// do with conductance.
        /// </summary>
        private class Restore : IDisposable
        {
            private readonly Func<BlockThermalProperties, BlockThermalProperties> previous;

            public Restore(Func<BlockThermalProperties, BlockThermalProperties> previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                Catalog.MaterialOverride = previous;
            }
        }

        private static Row Read(string rig, int count, bool preConversion, float conductance,
            ThermalSimulation simulation, BlockInstance source, BlockInstance far)
        {
            float peak = 0f;
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Temperature > peak) peak = nodes[i].Temperature;
            }

            ThermalNode sourceNode = simulation.Solver.GetNode(source);
            ThermalNode farNode = far == null ? null : simulation.Solver.GetNode(far);

            return new Row
            {
                Rig = rig,
                Count = count,
                World = preConversion ? "pre-units" : "shipped",
                Conductance = conductance * ThermalConstants.ConductionScale,
                SourceKelvin = sourceNode == null ? 0f : sourceNode.Temperature,
                PeakKelvin = peak,
                FarKelvin = farNode == null ? 0f : farNode.Temperature,
            };
        }
    }
}
