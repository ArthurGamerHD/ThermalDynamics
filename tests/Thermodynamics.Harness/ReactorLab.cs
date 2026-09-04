using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// What a vanilla reactor settles at, against the waste fraction its definition carries.
    ///
    /// The producer fraction is the one definition figure that cannot be chosen by analogy. A
    /// thruster's 0.25 was picked against a 33 MW draw; a reactor's has to hold for a range that
    /// spans three orders of magnitude — 0.5 MW on a small-grid small generator against 300 MW on
    /// a large-grid large one — and the block it heats is the same size in both grids. A single
    /// fraction that is survivable at the top of that range is inert at the bottom, so the choice
    /// is made here, by running the range and reading where the block lands against its own
    /// critical temperature.
    ///
    /// Two rigs, because one bound is not a decision. **Bare** is the most favourable case there
    /// is: one reactor alone in shadow, every face radiating to a 2.7 K sky, nothing else on the
    /// grid. A fraction that overheats there overheats everywhere, so it sets the ceiling.
    /// **Skinned** wraps the same reactor in one cell of light armour, which is how a reactor is
    /// actually installed; it sets the floor, because a fraction the skinned rig survives at full
    /// rating is a fraction no player ever has to cool.
    /// </summary>
    public static class ReactorLab
    {
        /// <summary>Fractions the report sweeps. 0 is the shipped value, i.e. the defect.</summary>
        public static readonly float[] Fractions = { 0f, 0.01f, 0.02f, 0.05f, 0.1f, 0.25f };

        /// <summary>
        /// Share of rated output the reactor is delivering. A reactor supplies what the grid draws
        /// and no more, so full rating is a combat or heavy-thrust figure rather than a resting
        /// one; 10 % is a lit, powered ship going nowhere.
        /// </summary>
        public static readonly float[] Loads = { 0.1f, 0.5f, 1f };

        public class Row
        {
            public string Subtype;
            public float RatedMegawatts;
            public float WasteFraction;
            public float LoadFraction;

            /// <summary>Watts of heat the definition turns that load into.</summary>
            public float WasteWatts;

            /// <summary>Where the block settles, kelvin, alone in shadow.</summary>
            public float BareKelvin;

            /// <summary>Where it settles under one cell of light armour, kelvin.</summary>
            public float SkinnedKelvin;

            /// <summary>The block's own critical temperature, kelvin.</summary>
            public float CriticalKelvin;

            /// <summary>Kelvin below critical, bare. Negative is a reactor nothing can save.</summary>
            public float BareMarginKelvin
            {
                get { return CriticalKelvin - BareKelvin; }
            }

            /// <summary>Kelvin below critical, skinned. Negative is a reactor that needs cooling.</summary>
            public float SkinnedMarginKelvin
            {
                get { return CriticalKelvin - SkinnedKelvin; }
            }
        }

        /// <summary>
        /// Every reactor at every fraction and every load. Eight steady states per cell, so the
        /// full sweep is a few hundred runs: it is a report, not something to call in a loop.
        /// </summary>
        public static List<Row> Sweep()
        {
            return SweepAt(Fractions);
        }

        /// <summary>
        /// The sweep at the fraction Cubes.xml actually ships, which is the only column a test
        /// asserting on shipped balance can mean. A sixth of the work of <see cref="Sweep"/>.
        /// </summary>
        public static List<Row> Shipped()
        {
            return SweepAt(new float[] { ShippedBlocks.FunctionOf("Reactor").ProducerWasteEnergy });
        }

        /// <summary>
        /// The sweep at one stated fraction, for a test that has to ask what a different one would
        /// do — which is what `C28` needs, since the fraction the mod ships is no longer the one
        /// where a reactor's installation decides whether it survives.
        /// </summary>
        public static List<Row> Run(float fraction)
        {
            return SweepAt(new float[] { fraction });
        }

        /// <summary>
        /// Results are cached by fraction: every row is a pure function of the shipped XML and the
        /// two figures above it, and a suite that asserts on five of them should pay for one run.
        /// </summary>
        private static readonly FractionCache<Row> Cache = new FractionCache<Row>(At);

        private static List<Row> SweepAt(float[] fractions)
        {
            return Cache.SweepAt(fractions);
        }

        private static List<Row> At(float fraction)
        {
            List<Row> rows = new List<Row>();

            foreach (Vanilla.Block reactor in Vanilla.Reference)
            {
                if (reactor.TypeId != "Reactor") continue;

                // Each reactor is derived from its own build cost. They are close but not
                // identical — the large-grid large generator carries a hundred superconductors the
                // others do not — so using one figure for all four would report the family as more
                // uniform than it is.
                BlockThermalProperties shipped = reactor.Thermal;

                foreach (float load in Loads)
                {
                    float watts = reactor.PowerOutputMegawatts * ThermalConstants.MegawattsToWatts * load;

                    rows.Add(new Row
                    {
                        Subtype = reactor.Subtype,
                        RatedMegawatts = reactor.PowerOutputMegawatts,
                        WasteFraction = fraction,
                        LoadFraction = load,
                        WasteWatts = watts * fraction,
                        BareKelvin = Settled(reactor, shipped, fraction, watts, false),
                        SkinnedKelvin = Settled(reactor, shipped, fraction, watts, true),
                        CriticalKelvin = shipped.CriticalTemperature,
                    });
                }
            }

            return rows;
        }

        /// <summary>
        /// One reactor in shadow, skinned or not, run to steady state at the given output.
        ///
        /// The two bounds and the clock are <see cref="SoloBlockRig"/>'s, shared with
        /// <see cref="OxygenGeneratorLab"/> so a producer's kelvin and a consumer's are readable
        /// against each other. All this adds is which fraction the copy carries.
        /// </summary>
        private static float Settled(Vanilla.Block reactor, BlockThermalProperties shipped,
            float fraction, float producedWatts, bool skinned)
        {
            BlockThermalProperties thermal = SoloBlockRig.Clone(shipped);
            thermal.ProducerWasteEnergy = fraction;

            return SoloBlockRig.Settled(reactor, thermal, producedWatts,
                SoloBlockRig.Power.Produced, skinned);
        }

        public static string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("REACTOR WASTE HEAT  (one reactor, shadow, 4 h to steady state)");
            sb.AppendLine("  bare     alone on the grid, every face radiating: the best case there is");
            sb.AppendLine("  skinned  under one cell of light armour: how a reactor is actually installed");
            sb.AppendLine();
            sb.AppendLine("block                         rated MW  fraction  load     waste W    bare K  margin  skin K  margin");

            foreach (Row row in Sweep())
            {
                sb.Append(row.Subtype.PadRight(29));
                sb.Append(row.RatedMegawatts.ToString("n2").PadLeft(8));
                sb.Append(row.WasteFraction.ToString("n3").PadLeft(10));
                sb.Append((row.LoadFraction * 100f).ToString("n0").PadLeft(5)).Append('%');
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
