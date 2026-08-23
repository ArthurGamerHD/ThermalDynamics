using System;
using System.Collections.Generic;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **Two dials at once**, because the route to the significance window is a claim about their
    /// interaction and every sweep run so far moves one.
    ///
    /// <para>
    /// `G8` asks for the median crossing under full electrical load to land in 120–300 s while a
    /// hull still settles inside an hour. Neither dial reaches that alone: `HeatTimeScale` ≈ 11 puts
    /// the block in the window and the hull at eight hours, and conduction alone barely moves the
    /// crossing. The projection in [balance.md] is that raising conduction and lowering the clock
    /// *together* lands it at constant or lower cost — arrived at by multiplying two single-dial
    /// curves, which is exactly the step this grid exists to check.
    /// </para>
    ///
    /// <para>
    /// **The run length scales with the clock, and it has to.** Dividing `HeatTimeScale` by k
    /// multiplies every time constant by k, so a fixed 1,800 s ceiling measures a settled hull at
    /// 225 and a hull that is still climbing at 11 — and reports the second as though it had
    /// settled at the ceiling. That is [backlog.md] `C8`, and it is a prerequisite for `G8` rather
    /// than a refinement: the criterion's second half is a settling time, and a censored settling
    /// time cannot fail it.
    /// See balance-lab.md, G8, and balance.md, Two dials at once.
    /// </para>
    /// </summary>
    public static class PairLab
    {
        /// <summary>The shipped clock, which every other level is read against.</summary>
        public const float ShippedClock = 225f;

        /// <summary>
        /// **Twenty-five cells, not a full grid, and the choice is the design.** A full 4 × 6 sweep
        /// maps a surface; `G8` asks whether a point on it exists, and a panel run costs about
        /// thirteen minutes a cell at shipped cost, so a full grid is most of a day for
        /// resolution the question does not use.
        ///
        /// <para>
        /// So the grid is cut into parts that each answer something. The first three were laid out
        /// before anything ran; the two corners after them are searches guided by what the earlier
        /// cells measured, and each is described where it appears in the table below.
        /// </para>
        ///
        /// <list type="bullet">
        /// <item><description>
        /// <b>The two edges are the published single-dial curves.</b> Conductivity ×1 down the clock
        /// and the clock at 225 up the conductivity are exactly the rows
        /// [balance.md](../../docs/balance.md) already carries, so the grid re-measures them rather
        /// than assuming them — and the interaction is then testable as *is the interior what the
        /// two edges predict*, which is the actual open question.
        /// </description></item>
        /// <item><description>
        /// <b>The equal-cost diagonal.</b> Substep demand goes as conductivity × clock, so
        /// (2, 112), (4, 56) and (8, 25) all cost what the shipped pair costs while the clock falls
        /// by half, a quarter and a ninth. If the window is reachable for free, it is reachable
        /// along this line.
        /// </description></item>
        /// <item><description>
        /// <b>The projection and its neighbours.</b> (4, 15) is the pair
        /// [balance.md](../../docs/balance.md) projects lands a ~200 s crossing at ~0.7 substeps;
        /// (2, 15), (8, 15), (4, 25) and (4, 11) bracket it in both directions, so a projection
        /// that is wrong is wrong by a measurable amount rather than just wrong.
        /// </description></item>
        /// <item><description>
        /// <b>Two corners, each added after the cells above it were read.</b> The first went to
        /// conductivity ×8 and measured that ×8 has no crossing median at all; the second goes to
        /// ×4 at clocks 80–120, which is the only band the corrected curves leave.
        /// </description></item>
        /// </list>
        /// </summary>
        private static readonly float[][] Grid =
        {
            // control
            new[] { 1f, 225f },

            // edge: the clock alone, which is the published heat-time-scale curve
            new[] { 1f, 112f }, new[] { 1f, 56f }, new[] { 1f, 25f },
            new[] { 1f, 15f }, new[] { 1f, 11f },

            // edge: conduction alone, which is the published conductivity curve
            new[] { 2f, 225f }, new[] { 4f, 225f }, new[] { 8f, 225f },

            // the equal-cost diagonal: conductivity x clock is about 225 throughout
            new[] { 2f, 112f }, new[] { 4f, 56f }, new[] { 8f, 25f },

            // the projection, and the four cells that bracket it
            new[] { 4f, 15f },
            new[] { 2f, 15f }, new[] { 8f, 15f }, new[] { 4f, 25f }, new[] { 4f, 11f },

            // **The first corner, added after the sixteen above were read, and it measured its own
            // refutation.** The sixteen found no cell satisfying `G8`, and the crossing appeared to
            // respond to conduction far more strongly than recovery did, which put a satisfying
            // cell near conductivity ×8 with a clock in the seventies to low hundreds. Two of these
            // four then scored as satisfying it — and did not: the crossing median had been taken
            // over the hulls that crossed rather than over the hulls that were loaded, and at ×8
            // that is thirteen of forty, so a cell where two thirds of the population never
            // overheats read as the grid's best. Read as `E9` requires, all four are censored and
            // conductivity ×8 has no crossing median at any clock. They are kept because that is
            // the measurement: it is what says ×8 is out, and it is the only rung where the
            // crossing share falls below a half. The reading is pinned by `tools/corpus/test_scoring.py`.
            new[] { 8f, 112f }, new[] { 8f, 90f }, new[] { 8f, 80f }, new[] { 8f, 70f },

            // **The second corner, from the corrected curves**, and the same search done on a
            // statistic that survives its own censoring rule. Two measured curves fix it: the
            // crossing goes as one over the clock, so `crossing × clock` is a constant per
            // conductivity — 2,302 at ×1, 5,418 at ×2, 14,869 at ×4, each held to within 1.5 %
            // across every cell that has a median — and recovery is set by the clock alone, under
            // the hour at clock 80 and over it at 70. Only ×4 has a window band (clock 50–124)
            // that reaches the clock the recovery bound needs, so the four cells below are where
            // `G8` can be satisfied if it can be satisfied at all.
            //
            // **A search guided by the data inside a criterion that has not moved** (`E1`, `E11`):
            // `G8` is unchanged and was written before any of this ran.
            new[] { 4f, 120f }, new[] { 4f, 100f }, new[] { 4f, 90f }, new[] { 4f, 80f },
        };

        /// <summary>
        /// The scenarios this grid runs, and every criterion the panel can answer from them: `G1`
        /// from idle, `G2` and `G8`'s first half from full electrical load, `G5` and `G8`'s second
        /// half from recovery, and `G6` from the substep demand of all three. Idle is run and its
        /// settling column is printed rather than scored, because a hull in vacuum shadow has no
        /// equilibrium and the column is a floor at low clock (`SettleReadingTests`).
        /// </summary>
        public static readonly string[] Scenarios = { "idle", "full-electrical", "recovery" };

        /// <summary>
        /// **The four environments the cost question is actually decided in.** `G6` is a cost
        /// criterion and the three scenarios above are all vacuum, where substeps are cheap: corpus
        /// p99 is 6.02 against 64 granted. In air they are not — the panel measures 36.71 at p95
        /// under reentry, and about 41 is projected at the 300 m/s servers run — so whether a
        /// retune is affordable is a question about air and the grid never asked it.
        ///
        /// <para>
        /// Four rather than the panel's six, and each earns its place. `vacuum-shadow` ties this
        /// pass to the vacuum data it has to be read beside. The other three are the two anchors of
        /// the convection fit [balance.md](../../docs/balance.md) validated — `h = 1` at
        /// `surface-hot-noon` and `h = 2` at `storm-parked` — and the held-out point it was tested
        /// against, `reentry` at 200 m/s, which is also the worst measured case. Keeping the fit's
        /// own three points means this pass can be read as *what the retune does to that fit*
        /// rather than as four unrelated numbers, and the 300 m/s figure is a projection either way.
        /// </para>
        /// </summary>
        public static readonly string[] AirScenarios =
        {
            "vacuum-shadow", "surface-hot-noon", "storm-parked", "reentry",
        };

        /// <summary>
        /// The cells the air pass prices: the shipped control, and the four
        /// [balance.md](../../docs/balance.md) found satisfy `G8`.
        ///
        /// **Only these five, because only these five are decidable.** Every other cell in the grid
        /// either fails `G8` or is excluded by it, so what it costs in air changes nothing. The
        /// control is here for the same reason it is in the main grid: a cost is a ratio, and a
        /// ratio needs a denominator measured the same way.
        /// </summary>
        private static readonly float[][] AirGrid =
        {
            new[] { 1f, 225f },
            new[] { 4f, 120f }, new[] { 4f, 100f }, new[] { 4f, 90f }, new[] { 4f, 80f },
        };

        /// <summary>One cell of the grid.</summary>
        public class Cell
        {
            /// <summary>Multiplier on every block's conductivity.</summary>
            public float Conductivity;

            /// <summary>`HeatTimeScale`, absolute.</summary>
            public float Clock;

            /// <summary>True for the shipped pair, which is the control the grid is read against.</summary>
            public bool IsShipped
            {
                get { return Conductivity == 1f && Clock == ShippedClock; }
            }

            /// <summary>
            /// Substep demand is conductance over capacity, so it goes as conductivity × clock.
            /// **This is the projection under test, not a measurement** — it is printed beside the
            /// measured demand so the two can be compared rather than confused.
            /// </summary>
            public float ProjectedDemandRatio
            {
                get { return Conductivity * Clock / ShippedClock; }
            }

            public string Name
            {
                get
                {
                    return "k" + Conductivity.ToString("0.##",
                               System.Globalization.CultureInfo.InvariantCulture)
                        + "-h" + Clock.ToString("0.##",
                               System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            public ThermalSettings Settings()
            {
                ThermalSettings settings = new ThermalSettings();
                settings.HeatTimeScale = Clock;
                return settings.Derive();
            }

            /// <summary>
            /// The material override for this cell, or null at the shipped conductivity — null
            /// rather than an identity function, so the control shares the model cache with nothing
            /// installed and cannot differ from the shipped world by a rounding.
            /// </summary>
            public Func<string, string, BlockThermalProperties, BlockThermalProperties> Material()
            {
                if (Conductivity == 1f) return null;

                float multiplier = Conductivity;
                return (typeId, subtype, source) =>
                {
                    BlockThermalProperties copy = source.Clone();
                    copy.Conductivity *= multiplier;
                    return copy;
                };
            }

            /// <summary>
            /// How much longer a run has to be at this clock for it to reach the same physical
            /// state. Exactly the clock ratio, because `HeatTimeScale` divides every heat capacity
            /// and nothing else — every rate in the model is a rate over that capacity.
            /// </summary>
            public float ClockStretch
            {
                get { return ShippedClock / Clock; }
            }
        }

        /// <summary>Every cell, control first, then in the order above.</summary>
        public static List<Cell> All()
        {
            return Cells(Grid);
        }

        /// <summary>The five cells the air pass prices, control first.</summary>
        public static List<Cell> Decision()
        {
            return Cells(AirGrid);
        }

        private static List<Cell> Cells(float[][] grid)
        {
            List<Cell> cells = new List<Cell>();

            for (int i = 0; i < grid.Length; i++)
            {
                cells.Add(new Cell { Conductivity = grid[i][0], Clock = grid[i][1] });
            }

            return cells;
        }

        /// <summary>
        /// The longest any run in this grid is given, simulated seconds.
        ///
        /// <para>
        /// **Twice `G8`'s own bound, and that is what makes it enough.** The criterion asks for a
        /// hull to settle inside 3,600 s; a run that is still climbing at 7,200 s has failed that by
        /// a factor of two and nothing longer would change the verdict. Stretching all the way — the
        /// clock ratio is 20.45 at `HeatTimeScale` 11, so 36,818 s for an idle run — would multiply
        /// the grid's cost by twenty to sharpen a number on the wrong side of the line.
        /// </para>
        /// </summary>
        public const float Ceiling = 7200f;

        /// <summary>
        /// One scenario with its clock stretched to match the cell, so the same physical question is
        /// asked at every rung (`M1`: compare only runs that were stopped the same way).
        ///
        /// <para>
        /// The settle test stops a run early when the hottest block stops moving, so stretching the
        /// ceiling costs nothing on a hull that has settled and is the whole measurement on one that
        /// has not. A run that reaches <see cref="Ceiling"/> reports no settling time at all, which
        /// is the honest reading: it did not settle inside the clock it was given.
        /// </para>
        /// </summary>
        public static Battery.Scenario Stretch(Battery.Scenario scenario, Cell cell)
        {
            return new Battery.Scenario
            {
                Name = scenario.Name,
                Question = scenario.Question,
                Environment = scenario.Environment,
                Load = scenario.Load,
                Seconds = Math.Min(Ceiling, scenario.Seconds * cell.ClockStretch),
            };
        }
    }
}
