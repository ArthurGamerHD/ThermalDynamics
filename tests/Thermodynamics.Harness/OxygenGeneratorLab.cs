using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a vanilla oxygen generator settles at, against the waste fraction its definition
    /// carries — the measurement that decided it, on 2026-08-25.
    ///
    /// <para>
    /// `Cubes.xml` gave the type a `ConsumerWasteEnergy` of **0.6** under a comment admitting it was
    /// invented, against water electrolysis sourcing 0.20–0.40 — the largest gap between an authored
    /// fraction and a real one in the file. The question, the decision rule and the predictions were
    /// registered in balance.md, *Oxygen generator waste heat*, before this class produced a number,
    /// and what the number said is that **two of the six vanilla generators cannot be built at 0.6**:
    /// they sit past their own critical temperature alone in open space at their own rated draw. The
    /// file ships **0.40** since 2026-08-25.
    /// </para>
    ///
    /// <para>
    /// It stays runnable rather than being a one-off, because the fraction is only defensible while
    /// the six blocks still survive both rigs at it — `OxygenGeneratorWasteHeatTests` is what asks.
    /// </para>
    ///
    /// <para>
    /// **It is <see cref="ReactorLab"/>'s two bounds pointed at a consumer.** Both share
    /// <see cref="SoloBlockRig"/>, so *bare* means the same arrangement and the same four hours in
    /// both, and a producer's kelvin can be read against a consumer's. What differs is where the
    /// watts come from: a reactor wastes a fraction of what it *produces*, a generator a fraction of
    /// what it *draws*, and the draw is stated per definition rather than chosen here.
    /// </para>
    ///
    /// <para>
    /// **Three draws, and only one of them is anybody's opinion.** `standby` and `operational` are
    /// both figures the game's own definition states — `StandbyPowerConsumption` and
    /// `OperationalPowerConsumption` — so they bracket the block without an authored duty cycle in
    /// between. <see cref="ObservedDuty"/> is the third and is one field sample, labelled as one
    /// wherever it is quoted.
    /// </para>
    /// </summary>
    public static class OxygenGeneratorLab
    {
        /// <summary>
        /// Fractions the report sweeps: the sourced band's two ends and its midpoint, the 0.6 that
        /// was shipped until 2026-08-25, and 1.0 as the first law's bound on a device that exports
        /// nothing. The 0.6 is kept in the sweep because it is where the decision came from — a
        /// report that only shows the value in force cannot show why it is the value in force.
        /// </summary>
        public static readonly float[] Fractions = { 0.2f, 0.3f, 0.4f, 0.6f, 1f };

        /// <summary>The band water electrolysis sources, as waste rather than as efficiency.</summary>
        public const float SourcedLow = 0.2f;

        /// <summary>The high end of <see cref="SourcedLow"/>'s band.</summary>
        public const float SourcedHigh = 0.4f;

        /// <summary>
        /// Mean draw across the 346 large oxygen generators of the 2026-08-21 field dump, as a
        /// fraction of the 500 kW that definition rates: 31,481 W, a duty of 6.3 %.
        ///
        /// <para>
        /// **One sample, and it is quoted as one.** It is what players ran in one session on one
        /// set of ships, and a session's figures cannot be re-examined — the ships are gone. It is
        /// here because a rig with only *off* and *flat out* on it says nothing about where a
        /// generator actually sits, and inventing a duty cycle inside a rig deciding an invented
        /// fraction would be two opinions multiplied together.
        /// </para>
        /// </summary>
        public const float ObservedDuty = 0.063f;

        /// <summary>How hard the generator is being run, and what each level is.</summary>
        public enum Draw
        {
            /// <summary>`StandbyPowerConsumption`: switched on, converting nothing.</summary>
            Standby,

            /// <summary><see cref="ObservedDuty"/> of the operational rating. One field sample.</summary>
            Observed,

            /// <summary>`OperationalPowerConsumption`: converting ice as fast as it can.</summary>
            Operational,
        }

        /// <summary>The three draws, in the order a report reads them.</summary>
        public static readonly Draw[] Draws = { Draw.Standby, Draw.Observed, Draw.Operational };

        public class Row
        {
            /// <summary>The block's subtype, which is the empty string for the vanilla one.</summary>
            public string Subtype;

            /// <summary>What to call it in a report, since a subtype can be empty.</summary>
            public string Name;

            public bool Large;

            /// <summary>Cells the block occupies. Watts per cell is what decides this family.</summary>
            public int Cells;

            /// <summary>What the definition rates its operational draw at, megawatts.</summary>
            public float OperationalMegawatts;

            public float WasteFraction;
            public Draw Load;

            /// <summary>Watts the block draws at this load.</summary>
            public float DrawWatts;

            /// <summary>Watts of heat the fraction turns that draw into.</summary>
            public float WasteWatts;

            /// <summary>Where the block settles, kelvin, alone in shadow.</summary>
            public float BareKelvin;

            /// <summary>Where it settles under one cell of light armour, kelvin.</summary>
            public float SkinnedKelvin;

            /// <summary>The block's own critical temperature, kelvin.</summary>
            public float CriticalKelvin;

            /// <summary>Kelvin below critical, bare. Negative is a block nothing can save.</summary>
            public float BareMarginKelvin
            {
                get { return CriticalKelvin - BareKelvin; }
            }

            /// <summary>Kelvin below critical, skinned. Negative is a block that needs cooling.</summary>
            public float SkinnedMarginKelvin
            {
                get { return CriticalKelvin - SkinnedKelvin; }
            }
        }

        /// <summary>Every generator at every fraction and every draw. A report, not a loop body.</summary>
        public static List<Row> Sweep()
        {
            return SweepAt(Fractions);
        }

        /// <summary>The sweep at the fraction `Cubes.xml` ships, which is the only column a test
        /// asserting on shipped balance can mean.</summary>
        public static List<Row> Shipped()
        {
            return SweepAt(new float[]
                { ShippedBlocks.FunctionOf("OxygenGenerator").ConsumerWasteEnergy });
        }

        /// <summary>The sweep at one stated fraction, for a test asking what a different one does.</summary>
        public static List<Row> Run(float fraction)
        {
            return SweepAt(new float[] { fraction });
        }

        /// <summary>
        /// Results are cached by fraction: every row is a pure function of the shipped XML and the
        /// tables above it, and a suite asserting on several should pay for one run.
        /// </summary>
        private static readonly Dictionary<float, List<Row>> Cache = new Dictionary<float, List<Row>>();

        private static List<Row> SweepAt(float[] fractions)
        {
            List<Row> rows = new List<Row>();

            foreach (float fraction in fractions)
            {
                List<Row> cached;
                lock (Cache)
                {
                    if (Cache.TryGetValue(fraction, out cached))
                    {
                        rows.AddRange(cached);
                        continue;
                    }
                }

                cached = At(fraction);
                lock (Cache) Cache[fraction] = cached;
                rows.AddRange(cached);
            }

            return rows;
        }

        private static List<Row> At(float fraction)
        {
            List<Row> rows = new List<Row>();

            foreach (Vanilla.Block generator in Vanilla.Reference)
            {
                if (generator.TypeId != "OxygenGenerator") continue;

                // Each generator is derived from its own build cost, and they are not close: the
                // prototech one is a different material with a critical temperature five hundred
                // kelvin above the rest, so one figure for the family would report it as far more
                // uniform than it is.
                BlockThermalProperties shipped = generator.Thermal;

                foreach (Draw load in Draws)
                {
                    float watts = DrawWattsOf(generator, load);

                    rows.Add(new Row
                    {
                        Subtype = generator.Subtype,
                        Name = NameOf(generator),
                        Large = generator.Large,
                        Cells = generator.CellCount,
                        OperationalMegawatts = generator.PowerDrawMegawatts,
                        WasteFraction = fraction,
                        Load = load,
                        DrawWatts = watts,
                        WasteWatts = watts * fraction,
                        BareKelvin = Settled(generator, shipped, fraction, watts, false),
                        SkinnedKelvin = Settled(generator, shipped, fraction, watts, true),
                        CriticalKelvin = shipped.CriticalTemperature,
                    });
                }
            }

            return rows;
        }

        /// <summary>Watts the definition says this block draws at this load.</summary>
        public static float DrawWattsOf(Vanilla.Block generator, Draw load)
        {
            float operational = generator.PowerDrawMegawatts * ThermalConstants.MegawattsToWatts;

            switch (load)
            {
                case Draw.Standby:
                    return generator.StandbyDrawMegawatts * ThermalConstants.MegawattsToWatts;
                case Draw.Observed:
                    return operational * ObservedDuty;
                default:
                    return operational;
            }
        }

        /// <summary>The vanilla generator carries no subtype, so it is named by its type.</summary>
        public static string NameOf(Vanilla.Block generator)
        {
            return generator.Subtype.Length > 0
                ? generator.Subtype
                : generator.TypeId + " (no subtype)";
        }

        private static float Settled(Vanilla.Block generator, BlockThermalProperties shipped,
            float fraction, float drawnWatts, bool skinned)
        {
            BlockThermalProperties thermal = SoloBlockRig.Clone(shipped);
            thermal.ConsumerWasteEnergy = fraction;

            return SoloBlockRig.Settled(generator, thermal, drawnWatts,
                SoloBlockRig.Power.Consumed, skinned);
        }

        private static string LabelOf(Draw load)
        {
            switch (load)
            {
                case Draw.Standby: return "standby";
                case Draw.Observed: return "observed";
                default: return "operational";
            }
        }

        public static string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("OXYGEN GENERATOR WASTE HEAT  (one generator, shadow, 4 h to steady state)");
            sb.AppendLine("  bare     alone on the grid, every face radiating: the best case there is");
            sb.AppendLine("  skinned  under one cell of light armour: how a generator is actually installed");
            sb.AppendLine();
            sb.AppendLine("  standby      StandbyPowerConsumption, from the definition");
            sb.AppendLine("  observed     " + (ObservedDuty * 100f).ToString("n1")
                + " % of rating, the mean of 346 field instances — one sample");
            sb.AppendLine("  operational  OperationalPowerConsumption, from the definition");
            sb.AppendLine();
            sb.AppendLine("  shipped fraction 0.60 (invented); water electrolysis sources "
                + SourcedLow.ToString("n2") + "-" + SourcedHigh.ToString("n2"));
            sb.AppendLine();
            sb.AppendLine("block                             cells  rated MW  fraction  load           draw W"
                + "     waste W    bare K  margin  skin K  margin");

            foreach (Row row in Sweep())
            {
                sb.Append(row.Name.PadRight(34));
                sb.Append(row.Cells.ToString().PadLeft(5));
                sb.Append(row.OperationalMegawatts.ToString("n2").PadLeft(10));
                sb.Append(row.WasteFraction.ToString("n2").PadLeft(10));
                sb.Append("  ").Append(LabelOf(row.Load).PadRight(13));
                sb.Append(row.DrawWatts.ToString("n0").PadLeft(9));
                sb.Append(row.WasteWatts.ToString("n0").PadLeft(12));
                sb.Append(row.BareKelvin.ToString("n1").PadLeft(10));
                sb.Append(row.BareMarginKelvin.ToString("n1").PadLeft(8));
                sb.Append(row.SkinnedKelvin.ToString("n1").PadLeft(8));
                sb.Append(row.SkinnedMarginKelvin.ToString("n1").PadLeft(8));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
