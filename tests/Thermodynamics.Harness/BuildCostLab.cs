using System;
using System.Collections.Generic;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **What the mod's blocks cost to build, against what the game charges for its own.**
    ///
    /// <para>
    /// backlog.md `B33`: nineteen definitions carry components, build times
    /// and PCU, and none of the three is derived or defended anywhere. This is what can be checked
    /// about them, and the first finding is about what *cannot*.
    /// </para>
    ///
    /// <para>
    /// **There is no comparator, so the cost cannot be derived from one.** A radiator, a coolant
    /// pipe, a pump and a heat pump have no vanilla counterpart — the game has no block whose job
    /// is to move heat — so the usual move of pricing a new block against the one it competes with
    /// is unavailable, and pretending otherwise would be picking a comparator to reach a number
    /// (`M10`). What is available is the *population*: the game prices thousands of blocks, and how
    /// it prices matter and volume is a distribution a new block can be placed inside.
    /// </para>
    ///
    /// <para>
    /// So the three ratios below are deliberately shape-free — they say nothing about what a block
    /// does and everything about how much of a ship it is. A mod block outside the vanilla range on
    /// one of them is charging for something the game does not charge for, which is a finding
    /// whichever direction it points (`P7` — the game is the authority).
    /// </para>
    /// </summary>
    public static class BuildCostLab
    {
        /// <summary>One block's build cost, normalised by the space it takes up.</summary>
        public class Row
        {
            public string Subtype;
            public bool Large;

            public float Kilograms;
            public int Pcu;
            public float BuildSeconds;
            public float CubicMetres;

            /// <summary>Kilograms of components a cubic metre of this block costs.</summary>
            public float KilogramsPerCubicMetre
            {
                get { return CubicMetres <= 0f ? 0f : Kilograms / CubicMetres; }
            }

            /// <summary>Declared PCU a cubic metre of this block costs.</summary>
            public float PcuPerCubicMetre
            {
                get { return CubicMetres <= 0f ? 0f : Pcu / CubicMetres; }
            }

            /// <summary>
            /// Declared PCU the whole block costs, which is the unit a server budget is actually
            /// spent in — a limit is on the count, not on the volume.
            /// </summary>
            public float PcuPerBlock
            {
                get { return Pcu; }
            }

            /// <summary>
            /// Seconds of welding a kilogram of this block costs.
            ///
            /// **Per kilogram rather than per block**, because the game's own build time tracks the
            /// components far more closely than it tracks the size: a block is slow to weld because
            /// it is made of a lot of things.
            /// </summary>
            public float SecondsPerKilogram
            {
                get { return Kilograms <= 0f ? 0f : BuildSeconds / Kilograms; }
            }
        }

        /// <summary>
        /// Every installed block the game prices: one with mass, volume, a declared PCU and a
        /// build time.
        ///
        /// <para>
        /// **Blocks missing any of the four are dropped and counted rather than defaulted.** An
        /// absent `PCU` means the engine uses 1, and folding those in would put thousands of
        /// blocks at the same value and make every percentile a statement about the default (`C8`,
        /// `E9`).
        /// </para>
        /// </summary>
        public static List<Row> Vanilla(out int dropped)
        {
            List<Row> rows = new List<Row>();
            dropped = 0;

            foreach (KeyValuePair<string, GameBlocks.Definition> entry in GameBlocks.BySubtype())
            {
                GameBlocks.Definition block = entry.Value;

                if (block.Mass <= 0f || block.VolumeCubicMetres <= 0f
                    || block.Pcu <= 0 || block.BuildSeconds <= 0f)
                {
                    dropped++;
                    continue;
                }

                rows.Add(new Row
                {
                    Subtype = block.SubtypeId,
                    Large = block.Large,
                    Kilograms = block.Mass,
                    Pcu = block.Pcu,
                    BuildSeconds = block.BuildSeconds,
                    CubicMetres = block.VolumeCubicMetres
                });
            }

            return rows;
        }

        /// <summary>Every block this mod ships that carries a component list.</summary>
        public static List<Row> Shipped()
        {
            List<Row> rows = new List<Row>();

            foreach (KeyValuePair<string, ShippedBlocks.Definition> entry in ShippedBlocks.All())
            {
                ShippedBlocks.Definition block = entry.Value;
                if (block.Components.Count == 0) continue;

                rows.Add(new Row
                {
                    Subtype = block.Subtype,
                    Large = block.Large,
                    Kilograms = block.Mass,
                    Pcu = block.Pcu,
                    BuildSeconds = block.BuildSeconds,
                    CubicMetres = block.CellCount * block.GridSize * block.GridSize * block.GridSize
                });
            }

            rows.Sort(delegate (Row a, Row b)
            {
                return string.CompareOrdinal(a.Subtype, b.Subtype);
            });

            return rows;
        }

        /// <summary>
        /// Where a value falls in a reference population, 0 to 1, by the share of it at or below.
        ///
        /// **Stated as a share rather than as a pass or a fail**, because the question `B33` asks
        /// is whether a price is defensible and a percentile is the evidence for that rather than
        /// the verdict.
        /// </summary>
        public static double Percentile(List<float> sortedAscending, float value)
        {
            if (sortedAscending.Count == 0) return 0d;

            int below = 0;
            while (below < sortedAscending.Count && sortedAscending[below] <= value) below++;

            return below / (double)sortedAscending.Count;
        }

        /// <summary>A sorted column of one ratio over a set of rows.</summary>
        public static List<float> Column(List<Row> rows, Func<Row, float> of)
        {
            List<float> values = new List<float>(rows.Count);
            foreach (Row row in rows)
            {
                float value = of(row);
                if (value > 0f) values.Add(value);
            }

            values.Sort();
            return values;
        }

        /// <summary>How a recipe's mass changes what the block does. One rung of the ladder.</summary>
        public class MassRung
        {
            /// <summary>What the radiator's authored mass was multiplied by.</summary>
            public float Factor;

            public float Kilograms;

            /// <summary>Where the reactor settled with those radiators on it, K.</summary>
            public float SettledKelvin;

            /// <summary>Where the far end of the radiator stack settled, K.</summary>
            public float TopSettledKelvin;

            /// <summary>
            /// Simulated seconds for the far radiator to come within a kelvin of where it settled
            /// — the transient, which is the half a recipe is expected to move.
            ///
            /// **Read off the radiator rather than the reactor**, because the radiators are what
            /// the recipe changes: the reactor's own capacity is untouched and dominates the
            /// source's curve whatever is bolted to it.
            /// </summary>
            public float SecondsToSettle;
        }

        /// <summary>
        /// **What doubling a block's recipe does to what the block is for.**
        ///
        /// <para>
        /// `B33`'s sharpest point is that a component list is already a thermal dial: mass is the
        /// sum of the components and capacity is mass times specific heat, so the two purposes are
        /// one number. This measures which half of the block's behaviour that number reaches.
        /// </para>
        ///
        /// <para>
        /// The rig is the biggest reactor the game ships with eight of the mod's large radiators
        /// stacked on it in one column, in shadow — the same shape `CoolingLadder` uses, so the
        /// two are readable together — run with the radiators' mass multiplied by each factor and
        /// nothing else changed (`P6`).
        /// </para>
        /// </summary>
        public static List<MassRung> MassLadder(float[] factors, int radiators = 8,
            float seconds = 3600f)
        {
            List<MassRung> rungs = new List<MassRung>();

            GameBlocks.Definition reactor = null;
            foreach (GameBlocks.Definition definition in GameBlocks.All())
            {
                if (definition.TypeId != "Reactor") continue;
                if (!definition.SubtypeId.StartsWith("Large", StringComparison.Ordinal)) continue;

                if (reactor == null || definition.PowerOutputWatts > reactor.PowerOutputWatts)
                {
                    reactor = definition;
                }
            }

            if (reactor == null) return rungs;

            BlockModel radiator;
            try
            {
                radiator = ShippedBlocks.Model("Gauge_LG_Radiator");
            }
            catch (Exception)
            {
                return rungs;
            }

            foreach (float factor in factors)
            {
                GridBuilder builder = GridBuilder.Large();
                builder.Place(Blueprints.Model(reactor), Vector3I.Zero);
                builder.Last.PowerProducedWatts = reactor.PowerOutputWatts;
                BlockInstance source = builder.Last;

                int height = Math.Max(1, reactor.Size.Y);
                float kilograms = 0f;
                BlockInstance top = null;

                for (int i = 0; i < radiators; i++)
                {
                    builder.Place(radiator, new Vector3I(0, height, 0));
                    builder.Last.Mass = radiator.Mass * factor;
                    kilograms += builder.Last.Mass;
                    top = builder.Last;
                    height += Math.Max(1, radiator.Size.Y);
                }

                ThermalSimulation simulation =
                    builder.BuildSimulation(new ThermalSettings(), 293.15f);

                // **Sampled every simulated second.** A minute was the first choice and it read
                // the same 300 s at every factor, which is the sample interval rather than the
                // rig: at the shipped clock this stack settles in a few hundred seconds, so a
                // sixty-second grid has five points to resolve the whole transient on.
                const float Interval = 1f;
                int perSample = Math.Max(1, (int)(Interval / simulation.Settings.StepSeconds));
                int samples = Math.Max(1, (int)(seconds / Interval));

                List<float> trace = new List<float>(samples);
                List<float> hot = new List<float>(samples);

                for (int i = 0; i < samples; i++)
                {
                    simulation.StepExact(perSample, Worlds.Shadow());

                    ThermalNode reading = simulation.Solver.GetNode(source);
                    trace.Add(reading == null ? 0f : reading.Temperature);

                    ThermalNode far = top == null ? null : simulation.Solver.GetNode(top);
                    hot.Add(far == null ? 0f : far.Temperature);
                }

                float settled = trace[trace.Count - 1];
                float topSettled = hot[hot.Count - 1];

                // **The first sample within a kelvin of where it ends**, read forwards, so a rig
                // that passes through the band on its way up is not credited with having settled
                // there. Never, where the run is too short to get within a kelvin — which is a
                // reading and not a failure.
                float reached = float.PositiveInfinity;
                for (int i = 0; i < hot.Count; i++)
                {
                    if (Math.Abs(hot[i] - topSettled) > 1f) continue;

                    reached = (i + 1) * Interval;
                    break;
                }

                rungs.Add(new MassRung
                {
                    Factor = factor,
                    Kilograms = kilograms,
                    SettledKelvin = settled,
                    TopSettledKelvin = topSettled,
                    SecondsToSettle = reached
                });
            }

            return rungs;
        }

        /// <summary>The value at a quantile of a sorted column, by nearest rank.</summary>
        public static float Quantile(List<float> sortedAscending, double quantile)
        {
            if (sortedAscending.Count == 0) return 0f;

            int index = (int)Math.Ceiling(quantile * sortedAscending.Count) - 1;
            if (index < 0) index = 0;
            if (index >= sortedAscending.Count) index = sortedAscending.Count - 1;

            return sortedAscending[index];
        }
    }
}
