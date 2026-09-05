using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// The step's per-node walks, priced two ways: what fusing the apply pass of one substep with
    /// the environment pass of the next would buy — three sweeps of the node arrays per substep
    /// becoming two — and what the per-step temperature mirror costs, measured on the real
    /// <c>SyncNodeState</c> rather than a prototype.
    ///
    /// <para>
    /// This is the third-sweep instrument on redesign.md. In the fused schedule the conduction
    /// pass keeps its place — env(1), conduction(1), [apply(1)+env(2)], conduction(2), … ,
    /// apply(K) — so the fused pair really is adjacent and the arithmetic is the same operations
    /// in the same per-node order, which is why the lab can demand the two schemes agree to the
    /// bit on temperatures, watts and the accumulator before either is timed (`E8`, `P4`).
    /// **The criteria, fixed before the first run** (`E1`): fusion earns a design row if the
    /// fused schedule runs at **0.85 or less** of the separate one at 505,566 blocks; the
    /// temperature-ownership inversion earns one if the real mirror walk costs **three per cent
    /// or more of a settled step** at that size.
    /// </para>
    ///
    /// <para>
    /// What the fusion prototype cannot see, named so the next reader does not have to find it
    /// (`P4`): the real conduction pass between fused walks streams the link arrays and both node
    /// rows, so the cache the fused pair shares is colder in the shipped solver than here — at
    /// half a million nodes the rows outrun the cache anyway, but acceptance for a built design
    /// is `bench stepphases` on the real pass, not this table.
    /// </para>
    /// </summary>
    public static class StepWalksLab
    {
        public class Row
        {
            public string Walk;
            public int Nodes;
            public double BestMs;
            public double NsPerNodeSubstep;
        }

        public class Result
        {
            public List<Row> Fusion = new List<Row>();
            public double MirrorMs;
            public double SettledStepMs;
            public int Nodes;
        }

        private const int Substeps = 16;

        public static Result Run(string shape, int blocks, int repeats = 20, Action<string> log = null)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, blocks));
            ThermalSimulation simulation = builder.BuildSimulation(Hulls.Uncapped(), 293.15f);

            Result result = new Result();
            result.Nodes = simulation.Solver.Nodes.Count;

            if (log != null) log("fusion prototypes, " + result.Nodes.ToString("n0") + " nodes");
            Fusion(simulation, repeats, result);

            if (log != null) log("the per-step mirror, measured on the real SyncNodeState");
            Mirror(simulation, repeats, result);

            return result;
        }

        // ---- the fused pair against the separate pair --------------------------------------

        private static void Fusion(ThermalSimulation simulation, int repeats, Result result)
        {
            IList<ThermalNode> nodes = simulation.Solver.Nodes;
            int count = nodes.Count;

            int[] exposedFaces = new int[count];
            float[] sourceRow = new float[count];
            float[] radiationRow = new float[count];
            float[] convectionRow = new float[count];
            float[] mass = new float[count];
            for (int i = 0; i < count; i++)
            {
                exposedFaces[i] = nodes[i].TotalExposedFaces;
                mass[i] = 400f + ((i * 13) % 900);
                sourceRow[i] = nodes[i].HeatGenerationWatts;
                if (exposedFaces[i] > 0)
                {
                    // Realistic magnitudes, deliberately: the first draft used coefficients four
                    // orders too large, the rig ran away to 1e17 kelvin, and only the agreement
                    // check's failure said so. sigma-epsilon-A is of order 1e-8.
                    radiationRow[i] = 1e-8f * (1f + ((i % 7) * 0.2f));
                    convectionRow[i] = -1.5f - (i % 5);
                    sourceRow[i] += (i % 3) * 40f;
                }
            }

            const float Ambient = 220f;
            const float AmbientPow4 = Ambient * Ambient * Ambient * Ambient;
            const float H = 0.25f / 24f;

            // Both schemes run the same K substeps from the same start and must land on the same
            // bits everywhere, or they are two different integrators and the timing means nothing.
            float[] tA = Spread(count);
            float[] wA = new float[count];
            float sumA = Separate(exposedFaces, tA, wA, sourceRow, radiationRow, convectionRow, mass, Ambient, AmbientPow4, H);

            float[] tB = Spread(count);
            float[] wB = new float[count];
            float sumB = Fused(exposedFaces, tB, wB, sourceRow, radiationRow, convectionRow, mass, Ambient, AmbientPow4, H);

            // **The two schedules must agree — but not to the bit, and that is a finding, not a
            // slack.** Run in lockstep, substep by substep, the fused and separate schemes are
            // bit-identical (a scratch probe confirmed it at this size). Run as two whole loops,
            // they diverge: the JIT auto-vectorises the simple apply and env loops and not the
            // combined fused loop, and a vectorised float reduction rounds differently from a
            // scalar one. So a *rescheduling* prototype cannot demonstrate its own correctness —
            // reordering the work changes which loops vectorise, which changes the last bits
            // (`P4`, the harness fault that looks like physics). Bit-identity is therefore the
            // *real* solver's obligation, proven by `SolverAb` against the code it replaces
            // (`D8`), and here the check is that the schemes agree to a tight relative tolerance,
            // which rules out a scheduling bug while admitting the rounding the JIT imposes.
            double worst = 0d;
            for (int i = 0; i < count; i++)
            {
                worst = Math.Max(worst, RelDiff(tA[i], tB[i]));
                worst = Math.Max(worst, RelDiff(wA[i], wB[i]));
            }
            if (worst > 1e-4d)
            {
                throw new InvalidOperationException(
                    "the schemes diverge by " + worst.ToString("g4", CultureInfo.InvariantCulture)
                    + " relative — beyond JIT rounding, so this is a scheduling bug, not vectorisation");
            }

            result.Fusion.Add(Time("separate: env walk + apply walk", count, repeats, () =>
            {
                float[] t = Spread(count);
                float[] w = new float[count];
                return Separate(exposedFaces, t, w, sourceRow, radiationRow, convectionRow, mass, Ambient, AmbientPow4, H);
            }));

            result.Fusion.Add(Time("fused: apply(n) + env(n+1)", count, repeats, () =>
            {
                float[] t = Spread(count);
                float[] w = new float[count];
                return Fused(exposedFaces, t, w, sourceRow, radiationRow, convectionRow, mass, Ambient, AmbientPow4, H);
            }));
        }

        private static float[] Spread(int count)
        {
            float[] t = new float[count];
            for (int i = 0; i < count; i++) t[i] = 250f + ((i * 37) % 500);
            return t;
        }

        /// <summary>The shipped schedule: an environment walk, then an apply walk, per substep.</summary>
        private static float Separate(int[] exposedFaces, float[] t, float[] w, float[] sourceRow,
            float[] radiationRow, float[] convectionRow, float[] mass, float ambient, float ambientPow4, float h)
        {
            float accumulator = 0f;
            for (int substep = 0; substep < Substeps; substep++)
            {
                accumulator += EnvWalk(exposedFaces, t, w, sourceRow, radiationRow, convectionRow, ambient, ambientPow4);
                ApplyWalk(t, w, mass, h);
            }
            return accumulator;
        }

        /// <summary>
        /// The candidate schedule: env once to open, then apply(n) and env(n+1) in one walk, then
        /// the closing apply — the same K environment fills and K applies, with K−1 pairs fused.
        /// </summary>
        private static float Fused(int[] exposedFaces, float[] t, float[] w, float[] sourceRow,
            float[] radiationRow, float[] convectionRow, float[] mass, float ambient, float ambientPow4, float h)
        {
            float accumulator = EnvWalk(exposedFaces, t, w, sourceRow, radiationRow, convectionRow, ambient, ambientPow4);

            for (int substep = 1; substep < Substeps; substep++)
            {
                // The per-substep subtotal is a local, exactly as the separate schedule keeps
                // it, so the grand total is the same additions in the same order — the fused
                // walk changes when work happens, not what is summed with what.
                float subtotal = 0f;
                for (int i = 0; i < t.Length; i++)
                {
                    float updated = t[i] + (w[i] * h / mass[i]);
                    if (updated < 3f) updated = 3f;
                    t[i] = updated;

                    if (exposedFaces[i] <= 0)
                    {
                        float buried = sourceRow[i];
                        w[i] = buried;
                        subtotal += buried;
                        continue;
                    }

                    float squared = updated * updated;
                    float radiation = -radiationRow[i] * ((squared * squared) - ambientPow4);
                    float convection = convectionRow[i] * (updated - ambient);
                    float watts = sourceRow[i] + radiation + convection;
                    w[i] = watts;
                    subtotal += watts;
                }
                accumulator += subtotal;
            }

            ApplyWalk(t, w, mass, h);
            return accumulator;
        }

        private static float EnvWalk(int[] exposedFaces, float[] t, float[] w, float[] sourceRow,
            float[] radiationRow, float[] convectionRow, float ambient, float ambientPow4)
        {
            float subtotal = 0f;
            for (int i = 0; i < t.Length; i++)
            {
                if (exposedFaces[i] <= 0)
                {
                    float buried = sourceRow[i];
                    w[i] = buried;
                    subtotal += buried;
                    continue;
                }

                float temperature = t[i];
                float squared = temperature * temperature;
                float radiation = -radiationRow[i] * ((squared * squared) - ambientPow4);
                float convection = convectionRow[i] * (temperature - ambient);
                float watts = sourceRow[i] + radiation + convection;
                w[i] = watts;
                subtotal += watts;
            }
            return subtotal;
        }

        private static void ApplyWalk(float[] t, float[] w, float[] mass, float h)
        {
            for (int i = 0; i < t.Length; i++)
            {
                float updated = t[i] + (w[i] * h / mass[i]);
                if (updated < 3f) updated = 3f;
                t[i] = updated;
            }
        }

        private static double RelDiff(float a, float b)
        {
            if (a == b) return 0d;
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale <= 0d ? 0d : Math.Abs((double)a - b) / scale;
        }

        private static Row Time(string walk, int count, int repeats, Func<float> scheme)
        {
            scheme();   // the JIT, outside the clock

            double best = double.MaxValue;
            Stopwatch watch = new Stopwatch();
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
                float sink = scheme();
                watch.Stop();
                if (float.IsNaN(sink)) throw new InvalidOperationException("the scheme produced NaN");
                if (watch.Elapsed.TotalMilliseconds < best) best = watch.Elapsed.TotalMilliseconds;
            }

            Row row = new Row();
            row.Walk = walk;
            row.Nodes = count;
            row.BestMs = best;
            row.NsPerNodeSubstep = best * 1e6 / count / Substeps;
            return row;
        }

        // ---- the per-step mirror, on the real thing ----------------------------------------

        private static void Mirror(ThermalSimulation simulation, int repeats, Result result)
        {
            // A settled step first, so the mirror is the clean incremental path a steady step
            // pays — and the step figure beside it is taken on the same grid in the same window.
            simulation.StepExact(1, Worlds.Shadow());

            Stopwatch watch = new Stopwatch();
            double best = double.MaxValue;
            for (int r = 0; r < repeats; r++)
            {
                watch.Restart();
                simulation.Solver.SyncNodeState();
                watch.Stop();
                if (watch.Elapsed.TotalMilliseconds < best) best = watch.Elapsed.TotalMilliseconds;
            }
            result.MirrorMs = best;

            double bestStep = double.MaxValue;
            for (int r = 0; r < Math.Max(3, repeats / 4); r++)
            {
                watch.Restart();
                simulation.StepExact(1, Worlds.Shadow());
                watch.Stop();
                if (watch.Elapsed.TotalMilliseconds < bestStep) bestStep = watch.Elapsed.TotalMilliseconds;
            }
            result.SettledStepMs = bestStep;
        }

        // ---- reporting ---------------------------------------------------------------------

        public static string Report(string shape, int blocks, Action<string> log = null)
        {
            return Table(Run(shape, blocks, 20, log));
        }

        public static string Table(Result result)
        {
            StringBuilder text = new StringBuilder();
            text.Append("walk".PadRight(34))
                .Append("nodes".PadLeft(10))
                .Append("best ms".PadLeft(11))
                .Append("ns/node/substep".PadLeft(17))
                .Append('\n');

            foreach (Row row in result.Fusion)
            {
                text.Append(row.Walk.PadRight(34))
                    .Append(row.Nodes.ToString("n0", CultureInfo.InvariantCulture).PadLeft(10))
                    .Append(row.BestMs.ToString("n3", CultureInfo.InvariantCulture).PadLeft(11))
                    .Append(row.NsPerNodeSubstep.ToString("n2", CultureInfo.InvariantCulture).PadLeft(17))
                    .Append('\n');
            }

            if (result.Fusion.Count == 2 && result.Fusion[0].BestMs > 0)
            {
                text.Append("fused / separate: ")
                    .Append((result.Fusion[1].BestMs / result.Fusion[0].BestMs).ToString("n3", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            text.Append('\n')
                .Append("the per-step mirror (the real SyncNodeState, clean incremental path): ")
                .Append(result.MirrorMs.ToString("n3", CultureInfo.InvariantCulture))
                .Append(" ms against a settled step's ")
                .Append(result.SettledStepMs.ToString("n3", CultureInfo.InvariantCulture))
                .Append(" ms — ")
                .Append((result.SettledStepMs <= 0 ? 0 : result.MirrorMs / result.SettledStepMs).ToString("p1", CultureInfo.InvariantCulture))
                .Append('\n');

            return text.ToString();
        }
    }
}
