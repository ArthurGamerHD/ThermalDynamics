using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// One measured figure, named so that two reports can be lined up against each other.
    ///
    /// The key is <c>section / case / metric</c> and it is what a diff joins on, so it has to be
    /// stable across runs and across changes to the code. Renaming a case silently turns a
    /// regression into a pair of unrelated rows, which is the one way a benchmark suite lies.
    /// </summary>
    public class ReportRow
    {
        public string Section;
        public string Case;
        public string Metric;
        public double Value;
        public string Unit;

        /// <summary>Lower is better for cost; false for figures like substeps where it is neutral.</summary>
        public bool LowerIsBetter = true;

        public string Key
        {
            get { return Section + "/" + Case + "/" + Metric; }
        }
    }

    /// <summary>
    /// A performance report: what the simulation costs by size, by feature and by configuration, as one
    /// artefact per run — a table for a person and a CSV keyed for a diff. The feature breakdown is
    /// deliberately two-sided, **marginal** and **isolated**, and the gap between them is usually the
    /// interesting part. See benchmarks.md.
    /// </summary>
    public static class PerformanceReport
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>A feature that can be switched off, and how.</summary>
        private class Feature
        {
            public string Name;
            public Action<ThermalSettings, bool> Set;
        }

        private static readonly Feature[] Features =
        {
            new Feature { Name = "conduction",   Set = delegate (ThermalSettings s, bool on) { s.EnableConduction = on; } },
            new Feature { Name = "radiation",    Set = delegate (ThermalSettings s, bool on) { s.EnableRadiation = on; } },
            new Feature { Name = "convection",   Set = delegate (ThermalSettings s, bool on) { s.EnableConvection = on; } },
            new Feature { Name = "solar",        Set = delegate (ThermalSettings s, bool on) { s.EnableSolarHeat = on; } },
            new Feature { Name = "self shadow",  Set = delegate (ThermalSettings s, bool on) { s.SolarSelfShadowing = on; } },
            new Feature { Name = "waste heat",   Set = delegate (ThermalSettings s, bool on) { s.EnableWasteHeat = on; } },
            new Feature { Name = "heat sources", Set = delegate (ThermalSettings s, bool on) { s.EnableHeatSources = on; } },
            new Feature { Name = "friction",     Set = delegate (ThermalSettings s, bool on) { s.EnableFriction = on; } },
            new Feature { Name = "damage",       Set = delegate (ThermalSettings s, bool on) { s.EnableDamage = on; } },
            new Feature { Name = "coolant loops",Set = delegate (ThermalSettings s, bool on) { s.EnableCoolantLoops = on; } },
            new Feature { Name = "room air",     Set = delegate (ThermalSettings s, bool on) { s.EnableRoomAir = on; } },
            new Feature { Name = "heat pumps",   Set = delegate (ThermalSettings s, bool on) { s.EnableHeatPumps = on; } },
            new Feature { Name = "conduction clamp", Set = delegate (ThermalSettings s, bool on) { s.ClampConductionOvershoot = on; } },
            new Feature { Name = "environment clamp", Set = delegate (ThermalSettings s, bool on) { s.ClampEnvironmentOvershoot = on; } },
        };

        /// <summary>Substep caps the configuration matrix walks.</summary>
        private static readonly int[] Caps = { 0, 16, 8, 4, 2, 1 };

        /// <summary>
        /// The world every feature and configuration case is measured in: **deliberately the worst of
        /// the nine**. In vacuum <c>AtmosphereFactor</c> is zero, so convection and friction read as
        /// costing nothing in a report that never ran them, and flight rather than a parked base
        /// because friction engages only above its threshold. See benchmarks.md, The environments.
        /// </summary>
        private static EnvironmentSample Worst()
        {
            return Worlds.Flight(1f, 300f);
        }

        /// <summary>The worlds the environment section walks, cheapest first.</summary>
        private static readonly string[] WorldNames = { "vacuum", "atmosphere", "flight" };

        private static EnvironmentSample World(string name)
        {
            if (name == "vacuum") return Worlds.Space(new Vector3(0.3f, 0.9f, 0.2f));
            if (name == "atmosphere") return Worlds.PlanetSurface(1f, timeOfDay: 0.35f, windSpeed: 40f);
            return Worlds.Flight(1f, 300f);
        }

        /// <summary>Shapes the environment section walks. A truss inverts in an atmosphere.</summary>
        private static readonly string[] Shapes = { "ship", "cube", "truss" };

        // ----------------------------------------------------------------------------------
        // Running it
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Times each case this many times and keeps the fastest.
        ///
        /// The minimum, not the mean: a timing sample is the true cost plus whatever else the
        /// machine was doing, and that noise is one-sided. Averaging it in makes a benchmark
        /// measure the operating system. Three is enough to knock out a scheduler hiccup without
        /// tripling a report that already takes minutes.
        /// </summary>
        public static int Repeats = 3;

        public static List<ReportRow> Run(string shape, int size, int ticks, IList<int> ladder,
            Action<string> log = null)
        {
            List<ReportRow> rows = new List<ReportRow>();

            // The JIT compiles on first use, so the first thing measured is always the compiler.
            Measure(Configure(0, true), shape, 1000, 4);

            if (log != null) log("machine");
            Environment(rows);
            NoiseFloor(rows, shape, size, ticks, log);

            if (log != null) log("ladder");
            Ladder(rows, shape, ladder, ticks, log);

            if (log != null) log("features");
            FeatureBreakdown(rows, shape, size, ticks, log);

            if (log != null) log("configurations");
            Configurations(rows, shape, size, ticks, log);

            if (log != null) log("overshoot clamp");
            OvershootClamp(rows, shape, size, ticks, log);

            if (log != null) log("diagnostics");
            Diagnostics(rows, shape, size, ticks, log);

            if (log != null) log("environments");
            Environments(rows, ticks, log);

            if (log != null) log("scenarios");
            Scenarios(rows, ticks, log);

            if (log != null) log("fleets");
            Fleets(rows, ticks, log);

            return rows;
        }

        /// <summary>
        /// What the report was measured on, plus a calibration figure.
        ///
        /// Milliseconds are a property of the machine as much as of the code, so a report from one
        /// machine cannot be compared against another's without something to scale by. The
        /// calibration is a fixed, tiny simulation run a fixed number of times: two machines
        /// divide by it and their remaining differences are the code's.
        /// </summary>
        private static void Environment(List<ReportRow> rows)
        {
            // Neutral, not "lower is better": running the report on a smaller machine is not an
            // improvement, and a comparison that says so is a comparison nobody trusts.
            Add(rows, "machine", "host", "processors", System.Environment.ProcessorCount, "", false);
            Add(rows, "machine", "host", "64 bit", System.Environment.Is64BitProcess ? 1 : 0, "", false);

            // The hull is dealt before the clock starts, for the same reason as the ladder's build column.
            GridBuilder hull = Hull("ship", 4000);

            // **Timed `Repeats` times like every other case, and this one is the divisor.** Two
            // machines' reports are compared by dividing each by its own calibration, so a single
            // sample here puts a whole sample's noise into every cross-machine figure twice over —
            // and it was a single sample from the day this row was written. Found while fixing the
            // ladder's `build` column, which had the same defect; see performance.md, Pass 9,
            // Iteration 8.
            double best = double.MaxValue;

            for (int r = 0; r < (Repeats < 1 ? 1 : Repeats); r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                ThermalSimulation calibration = Build(Configure(0, true), hull);
                LoadBenchmarks.SeedSpread(calibration);

                EnvironmentState state = EnvironmentSolver.Solve(
                    calibration.Settings, calibration.Planet, Worst());

                for (int i = 0; i < 20; i++) calibration.Solver.Step(calibration.Settings.StepSeconds, state);
                watch.Stop();

                if (watch.Elapsed.TotalMilliseconds < best) best = watch.Elapsed.TotalMilliseconds;
            }

            Add(rows, "machine", "calibration", "4k ship, 20 steps", best, "ms");
        }

        /// <summary>
        /// How much the same measurement moves when nothing changes.
        ///
        /// Without this the feature table is unreadable: a feature that costs less than the run
        /// to run spread shows up as a negative cost, and a reader has no way to tell that from a
        /// feature that genuinely makes the solver faster. The spread is measured the same way
        /// everything else is — same hull, same settings, same repeats — so it is directly
        /// comparable to every figure below it, and anything smaller than it is noise.
        /// </summary>
        private static void NoiseFloor(List<ReportRow> rows, string shape, int size, int ticks,
            Action<string> log)
        {
            if (log != null) log("noise floor");

            ThermalSettings settings = Configure(0, true);

            double least = double.MaxValue;
            double most = 0;

            for (int i = 0; i < 5; i++)
            {
                double ms = Measure(settings, shape, size, ticks).StepMs;
                if (ms < least) least = ms;
                if (ms > most) most = ms;
            }

            Add(rows, "machine", "noise", "spread of five identical runs", most - least, "ms");
            Add(rows, "machine", "noise", "as a share of a step", least <= 0 ? 0 : (most - least) / least, "");
        }

        /// <summary>One rung's build, timed the number of times every other case is.</summary>
        public struct BuiltHull
        {
            /// <summary>The fastest of <see cref="Repeats"/> builds, in milliseconds.</summary>
            public double BuildMs;

            /// <summary>Builds actually timed, so a caller can assert the repeat happened.</summary>
            public int Builds;

            /// <summary>The last one, which is what the caller steps.</summary>
            public ThermalSimulation Simulation;
        }

        /// <summary>
        /// **Builds one dealt hull <see cref="Repeats"/> times and keeps the fastest**, like every
        /// other case in this report.
        ///
        /// <para>
        /// The ladder's `build` column was one stopwatch for the life of this report, under a page
        /// that says every case is timed three times and the fastest kept — so the one column a
        /// reader is most likely to compare between two runs was the one carrying a whole sample's
        /// worth of noise, and nothing said so (`D3`). See performance.md, Pass 9, Iteration 8.
        /// </para>
        ///
        /// <para>
        /// The hull is dealt once and built from repeatedly. Dealing it is the census generator,
        /// which is about ten times the build it feeds (`C26`) and is not what this column is
        /// about; that two builds of one hull are two builds of the *same graph* is checked here
        /// rather than assumed, because only a repeat can check it (`P6`).
        /// </para>
        /// </summary>
        public static BuiltHull RepeatBuild(ThermalSettings settings, GridBuilder hull)
        {
            BuiltHull built = new BuiltHull();
            built.BuildMs = double.MaxValue;

            int nodes = 0;
            int links = 0;

            for (int r = 0; r < (Repeats < 1 ? 1 : Repeats); r++)
            {
                Stopwatch build = Stopwatch.StartNew();
                ThermalSimulation candidate = Build(settings, hull);
                build.Stop();

                if (r == 0)
                {
                    nodes = candidate.Solver.Nodes.Count;
                    links = candidate.Solver.Links.Count;
                }
                else if (candidate.Solver.Nodes.Count != nodes
                    || candidate.Solver.Links.Count != links)
                {
                    throw new InvalidOperationException(
                        "repeat " + r + " built " + candidate.Solver.Nodes.Count + " nodes and "
                        + candidate.Solver.Links.Count + " links against " + nodes + " and " + links
                        + " the first time, so these are readings of different builds");
                }

                if (build.Elapsed.TotalMilliseconds < built.BuildMs)
                {
                    built.BuildMs = build.Elapsed.TotalMilliseconds;
                }

                built.Builds++;

                // The last one stands, so what the caller steps is a simulation nothing else has
                // touched.
                built.Simulation = candidate;
            }

            return built;
        }

        private static void Ladder(List<ReportRow> rows, string shape, IList<int> sizes, int ticks,
            Action<string> log)
        {
            for (int i = 0; i < sizes.Count; i++)
            {
                int size = sizes[i];
                if (log != null) log("  ladder " + size.ToString("n0"));

                ThermalSettings settings = Configure(0, true);
                GridBuilder hull = Hull(shape, size);

                BuiltHull built = RepeatBuild(settings, hull);
                ThermalSimulation simulation = built.Simulation;

                string name = size.ToString("n0");

                Add(rows, "ladder", name, "blocks", simulation.Solver.Nodes.Count, "");
                Add(rows, "ladder", name, "links", simulation.Solver.Links.Count, "");
                Add(rows, "ladder", name, "build", built.BuildMs, "ms");

                Sample sample = Measure(settings, shape, size, ticks, simulation);

                Add(rows, "ladder", name, "step", sample.StepMs, "ms");
                Add(rows, "ladder", name, "substeps granted", sample.Substeps, "", false);
                Add(rows, "ladder", name, "substeps demanded", sample.Demand, "", false);
                Add(rows, "ladder", name, "per simulated second", sample.MsPerSimulatedSecond, "ms");
                Add(rows, "ladder", name, "per element visit", sample.NsPerElement, "ns");
            }
        }

        /// <summary>
        /// Every feature twice: what removing it saves, and what it costs alone.
        /// </summary>
        private static void FeatureBreakdown(List<ReportRow> rows, string shape, int size, int ticks,
            Action<string> log)
        {
            Sample whole = Measure(Configure(0, true), shape, size, ticks);
            Add(rows, "features", "everything on", "step", whole.StepMs, "ms");

            Sample bare = Measure(Configure(0, false), shape, size, ticks);
            Add(rows, "features", "everything off", "step", bare.StepMs, "ms");

            for (int i = 0; i < Features.Length; i++)
            {
                Feature feature = Features[i];
                if (log != null) log("  feature " + feature.Name);

                // Marginal: everything on, this one off. What removing it from a working
                // configuration gives back.
                ThermalSettings without = Configure(0, true);
                feature.Set(without, false);
                without.Derive();
                Sample removed = Measure(without, shape, size, ticks);

                // Isolated: everything off, this one on. What the feature does by itself.
                ThermalSettings only = Configure(0, false);
                feature.Set(only, true);
                only.Derive();
                Sample alone = Measure(only, shape, size, ticks);

                Add(rows, "features", feature.Name, "marginal", whole.StepMs - removed.StepMs, "ms");
                Add(rows, "features", feature.Name, "isolated", alone.StepMs - bare.StepMs, "ms");

                // What the grid demands without it, not what it was granted: MaxSubsteps refuses
                // at 16 on every hull this report builds, so the granted count is the same number
                // in every row and says nothing about which features drive stiffness.
                Add(rows, "features", feature.Name, "demand without it", removed.Demand, "", false);
            }
        }

        /// <summary>
        /// The one configuration a server runs, and the substep cap across its useful range — the
        /// only dial that trades accuracy for throughput on a stiff grid.
        /// </summary>
        private static void Configurations(List<ReportRow> rows, string shape, int size, int ticks,
            Action<string> log)
        {
            double[] stepMs = new double[Caps.Length];
            float[] substeps = new float[Caps.Length];

            for (int i = 0; i < Caps.Length; i++)
            {
                int cap = Caps[i];
                if (log != null) log("  cap " + cap);

                Sample sample = Measure(Configure(cap, true), shape, size, ticks);
                string name = cap == 0 ? "cap off" : "cap " + cap;

                stepMs[i] = sample.StepMs;
                substeps[i] = sample.Substeps;

                Add(rows, "substep cap", name, "step", sample.StepMs, "ms");
                Add(rows, "substep cap", name, "substeps", sample.Substeps, "", false);
                Add(rows, "substep cap", name, "per simulated second", sample.MsPerSimulatedSecond, "ms");
                Add(rows, "substep cap", name, "blocks raised", sample.Floored, "", false);
                Add(rows, "substep cap", name, "clamped", sample.Clamped ? 1 : 0, "", false);
            }

            StepShape(rows, shape, size, ticks, stepMs, substeps);
        }

        /// <summary>
        /// A step split into the part that scales with substeps and the part that does not, fitted
        /// through the `cap 4` and `cap 16` rows because both are clamp-free — `cap 1` would put a
        /// clamped point against an unclamped one. **A step has three terms, not two**, so the fitted
        /// intercept carries the first substep's row fill as well as the prologue;
        /// <see cref="StepTerms"/> measures the ends directly.
        /// See benchmarks.md, The shape of a step.
        /// </summary>
        private static void StepShape(List<ReportRow> rows, string shape, int size, int ticks,
            double[] stepMs, float[] substeps)
        {
            int low = IndexOfCap(4);
            int high = IndexOfCap(16);
            if (low < 0 || high < 0) return;

            double span = substeps[high] - substeps[low];
            if (span <= 0d) return;

            double perSubstep = (stepMs[high] - stepMs[low]) / span;
            double fixedMs = stepMs[low] - (perSubstep * substeps[low]);

            int uncapped = IndexOfCap(0);
            double atDefault = uncapped >= 0 && stepMs[uncapped] > 0d
                ? 100d * fixedMs / stepMs[uncapped]
                : 0d;

            Add(rows, "step shape", "cap 4 to cap 16", "fixed per step", fixedMs, "ms");
            Add(rows, "step shape", "cap 4 to cap 16", "per substep", perSubstep, "ms");
            Add(rows, "step shape", "cap 4 to cap 16", "fixed share, uncapped", atDefault, "%");

            StepTerms(rows, shape, size, ticks, stepMs, perSubstep);
        }

        /// <summary>
        /// The three terms a step is made of, measured at `cap 1` where they separate without a fit.
        /// **`cap 1` is a refused grid, so the row fill measured here is the clamped one** — about
        /// twice what a grid granted its substeps pays, because the relaxation row is written only
        /// while the clamp is live. See benchmarks.md, The fill is two different fills.
        /// </summary>
        private static void StepTerms(List<ReportRow> rows, string shape, int size, int ticks,
            double[] stepMs, double perSubstep)
        {
            int one = IndexOfCap(1);
            if (one < 0) return;

            ThermalSettings settings = Configure(1, true);
            ThermalSimulation simulation = Build(settings, shape, size);
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            ThermalSolver solver = simulation.Solver;
            EnvironmentState state = EnvironmentSolver.Solve(settings, simulation.Planet, Worst());
            float step = settings.StepSeconds;

            for (int i = 0; i < 2; i++) solver.Step(step, state);

            long nodes = solver.Nodes.Count;
            int repeats = Repeats < 1 ? 1 : Repeats;

            double prepare = double.MaxValue;
            double publish = double.MaxValue;

            for (int r = 0; r < repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < ticks; i++) solver.RequiredSubsteps(step, state);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds / ticks;
                if (ms < prepare) prepare = ms;

                // Everything but the write-back, then the write-back on its own clock. The step
                // machine charges one element visit per node for that last stage, which is what
                // makes the boundary findable from outside.
                double total = 0d;
                for (int i = 0; i < ticks; i++)
                {
                    solver.BeginStep(step, state);
                    long upTo = solver.StepWorkUnits - nodes;
                    while (solver.StepWorkRemaining > nodes) solver.AdvanceStep(upTo);

                    Stopwatch last = Stopwatch.StartNew();
                    while (!solver.AdvanceStep(long.MaxValue)) { }
                    last.Stop();
                    total += last.Elapsed.TotalMilliseconds;
                }

                ms = total / ticks;
                if (ms < publish) publish = ms;
            }

            double firstSubstep = stepMs[one] - prepare - publish;

            Add(rows, "step shape", "terms", "prologue and estimate", prepare, "ms");
            Add(rows, "step shape", "terms", "write-back", publish, "ms");
            Add(rows, "step shape", "terms", "first substep", firstSubstep, "ms");
            Add(rows, "step shape", "terms", "later substep", perSubstep, "ms");
            // Named for the regime rather than for the term: `cap 1` is a refused grid, so this
            // is the fill with the relaxation row in it. `bench rowfill` reports both regimes.
            Add(rows, "step shape", "terms", "row fill, first substep only, clamp live",
                firstSubstep - perSubstep, "ms");
        }

        private static int IndexOfCap(int cap)
        {
            for (int i = 0; i < Caps.Length; i++)
            {
                if (Caps[i] == cap) return i;
            }
            return -1;
        }

        /// <summary>
        /// The conduction overshoot clamp, measured against itself in **both** regimes — resolved,
        /// where skipping it is a saving, and refused, where the test buys nothing — because one
        /// figure would hide whichever case it landed in. The <c>clamp live</c> rows are what say the
        /// two are different regimes rather than one measurement printed twice.
        /// See benchmarks.md, The overshoot clamp A/B.
        /// </summary>
        private static void OvershootClamp(List<ReportRow> rows, string shape, int size, int ticks,
            Action<string> log)
        {
            // Enough substeps that nothing is near its limit, then far too few, with the per-block
            // mass floor off in both so the floor cannot rescue the stiff blocks in the second.
            Regime(rows, "resolved", 4096, shape, size, ticks, log);
            Regime(rows, "refused", 4, shape, size, ticks, log);
        }

        /// <summary>
        /// What being measured costs. Every field dump in this repository was taken with the
        /// per-mechanism watt figures on, because taking a dump is what turns them on, so this row is
        /// what makes a dump and a benchmark comparable rather than leaving a reader to assume they
        /// already are. See benchmarks.md, What being measured costs.
        /// </summary>
        private static void Diagnostics(List<ReportRow> rows, string shape, int size, int ticks,
            Action<string> log)
        {
            if (log != null) log("  diagnostics");

            bool restore = LoadBenchmarks.CollectDiagnostics;
            try
            {
                LoadBenchmarks.CollectDiagnostics = false;
                Sample off = Measure(Configure(0, true), shape, size, ticks);

                LoadBenchmarks.CollectDiagnostics = true;
                Sample on = Measure(Configure(0, true), shape, size, ticks);

                Add(rows, "diagnostics", "per-mechanism watts", "step, off", off.StepMs, "ms");
                Add(rows, "diagnostics", "per-mechanism watts", "step, on", on.StepMs, "ms");
                Add(rows, "diagnostics", "per-mechanism watts", "cost of being measured",
                    on.StepMs - off.StepMs, "ms");

                // The batching, measured against itself. `every substep` is what the solver used
                // to do; the difference between it and `step, on` is what is saved by writing only
                // the substep anything reads.
                Sample all = Measure(Configure(0, true), shape, size, ticks,
                    null, null, true, everySubstep: true);

                Add(rows, "diagnostics", "per-mechanism watts", "step, every substep", all.StepMs, "ms");

                // Read off the node objects, so a case that claims to be collecting and is not
                // fails rather than reporting a suspiciously cheap millisecond figure.
                Add(rows, "diagnostics", "per-mechanism watts", "written, off",
                    off.DiagnosticsPublished ? 1 : 0, "", false);
                Add(rows, "diagnostics", "per-mechanism watts", "written, on",
                    on.DiagnosticsPublished ? 1 : 0, "", false);
                Add(rows, "diagnostics", "per-mechanism watts", "written, every substep",
                    all.DiagnosticsPublished ? 1 : 0, "", false);

                // The worst case for batching: one substep, so the last substep is the only
                // substep and there is nothing to skip.
                ThermalSettings single = Configure(0, true);
                single.MaxSubsteps = 1;
                single.Derive();

                Sample onceLast = Measure(single, shape, size, ticks);
                Sample onceEvery = Measure(single, shape, size, ticks, null, null, true, everySubstep: true);

                Add(rows, "diagnostics", "one substep", "step, last substep only", onceLast.StepMs, "ms");
                Add(rows, "diagnostics", "one substep", "step, every substep", onceEvery.StepMs, "ms");
            }
            finally
            {
                LoadBenchmarks.CollectDiagnostics = restore;
            }
        }

        private static void Regime(List<ReportRow> rows, string name, int maxSubsteps, string shape,
            int size, int ticks, Action<string> log)
        {
            if (log != null) log("  clamp " + name);

            ThermalSettings gated = Configure(0, true);
            gated.MaxSubsteps = maxSubsteps;
            gated.Derive();

            ThermalSettings ungated = Configure(0, true);
            ungated.MaxSubsteps = maxSubsteps;
            ungated.Derive();

            Sample with = Measure(gated, shape, size, ticks, null, null, true);
            Sample without = Measure(ungated, shape, size, ticks, null, null, false);

            Add(rows, "overshoot clamp", name, "step, gated", with.StepMs, "ms");
            Add(rows, "overshoot clamp", name, "step, always clamped", without.StepMs, "ms");
            Add(rows, "overshoot clamp", name, "substeps granted", with.Substeps, "", false);
            Add(rows, "overshoot clamp", name, "clamp live", with.ClampLive ? 1 : 0, "", false);

            // Signed, so the worst case reads as the cost it is rather than as a small saving.
            double change = without.StepMs <= 0.0
                ? 0.0
                : 100.0 * (with.StepMs - without.StepMs) / without.StepMs;
            Add(rows, "overshoot clamp", name, "gate change", change, "%");
        }

        /// <summary>
        /// Every shape in every world, because which is worst depends on which world.
        ///
        /// In vacuum a truss is the cheapest hull on the ladder — one link per node and nothing to
        /// convect with. In an atmosphere it is the most expensive, because every one of its nodes
        /// is exposed and convection is a per-exposed-node term that also raises the stability
        /// demand. A benchmark suite that measures one shape in one world will report whichever
        /// pair it happened to pick, and the pair this one had picked was the cheapest of nine.
        /// </summary>
        private static void Environments(List<ReportRow> rows, int ticks, Action<string> log)
        {
            const int Size = 8000;

            for (int s = 0; s < Shapes.Length; s++)
            {
                for (int w = 0; w < WorldNames.Length; w++)
                {
                    if (log != null) log("  " + Shapes[s] + " in " + WorldNames[w]);

                    Sample sample = Measure(Configure(0, true), Shapes[s], Size, ticks,
                        null, World(WorldNames[w]));

                    string name = Shapes[s] + " " + WorldNames[w];
                    Add(rows, "environments", name, "step", sample.StepMs, "ms");
                    Add(rows, "environments", name, "substeps demanded", sample.Demand, "", false);
                }
            }
        }

        /// <summary>
        /// The cases a plain hull in a plain world does not reach: air in the compartments,
        /// plumbing on the ship, and blocks past their rating.
        ///
        /// Each row carries what the scenario actually built beside what it cost, because a
        /// scenario that quietly builds nothing reports a cost of zero and is indistinguishable
        /// from a feature that is free. That is not hypothetical — it is what the report said
        /// about convection for its whole existence.
        /// </summary>
        private static void Scenarios(List<ReportRow> rows, int ticks, Action<string> log)
        {
            const int Size = 8000;

            Measured(rows, "plain", WorstCases.Hull("ship", Size, Configure(0, true)), ticks, log);
            Measured(rows, "pressurised", WorstCases.Pressurised("ship", Size, Configure(0, true)), ticks, log);
            Measured(rows, "plumbed", WorstCases.Plumbed("ship", Size, 8, Configure(0, true)), ticks, log);
            Measured(rows, "burning", WorstCases.Burning("ship", Size, Configure(0, true)), ticks, log);

            // The bound on the damage check rather than its ordinary cost: burning a ship through
            // its producers leaves most of the hull under its rating, so the expensive branch is
            // rarely taken. This takes it on every node of every substep.
            Measured(rows, "scorched", WorstCases.Scorched("ship", Size, Configure(0, true)), ticks, log);
        }

        private static void Measured(List<ReportRow> rows, string name, WorstCases.Built built,
            int ticks, Action<string> log)
        {
            if (log != null) log("  " + name + ": " + built);

            ThermalSimulation simulation = built.Simulation;
            EnvironmentState state = EnvironmentSolver.Solve(
                simulation.Settings, simulation.Planet, Worst());

            float step = simulation.Settings.StepSeconds;
            for (int i = 0; i < 2; i++) simulation.Solver.Step(step, state);

            double best = double.MaxValue;
            long overheats = 0;

            for (int r = 0; r < Math.Max(1, Repeats); r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < ticks; i++)
                {
                    simulation.Solver.Step(step, state);
                    overheats += simulation.Solver.Overheats.Count;
                }
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds / ticks;
                if (ms < best) best = ms;
            }

            Add(rows, "scenarios", name, "step", best, "ms");
            Add(rows, "scenarios", name, "substeps demanded", simulation.Solver.LastRequiredSubsteps, "", false);

            // The subject counts. A zero here says the row above measured nothing.
            Add(rows, "scenarios", name, "rooms with air", built.RoomsWithAir, "", false);
            Add(rows, "scenarios", name, "coolant loops", built.CoolantLoops, "", false);
            Add(rows, "scenarios", name, "heat pumps", built.HeatPumps, "", false);
            Add(rows, "scenarios", name, "overheat events", overheats / Math.Max(1, Repeats), "", false);
        }

        /// <summary>
        /// The same blocks as one grid, then as ten, then as a hundred.
        ///
        /// A world is many grids and every benchmark here has been one. Each grid pays its own
        /// fixed per-step cost — state sync, the stability estimate, the mass floor are each a
        /// pass over that grid's nodes — so a fleet of small grids is not the same price as one
        /// large one with the same block count. The largest finding of this project was about how
        /// a world of grids behaves, and it came from a telemetry dump because nothing here could
        /// see it.
        /// </summary>
        private static void Fleets(List<ReportRow> rows, int ticks, Action<string> log)
        {
            const int Total = 8000;
            int[] counts = { 1, 10, 100 };

            for (int c = 0; c < counts.Length; c++)
            {
                if (log != null) log("  fleet of " + counts[c]);

                List<WorstCases.Built> fleet =
                    WorstCases.Fleet("ship", Total, counts[c], Configure(0, true));

                EnvironmentSample sample = Worst();
                WorstCases.StepFleet(fleet, sample, 2);

                int nodes = 0;
                for (int i = 0; i < fleet.Count; i++) nodes += fleet[i].Nodes;

                double best = double.MaxValue;
                for (int r = 0; r < Math.Max(1, Repeats); r++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    WorstCases.StepFleet(fleet, sample, ticks);
                    watch.Stop();

                    double ms = watch.Elapsed.TotalMilliseconds / ticks;
                    if (ms < best) best = ms;
                }

                // Named by grid count rather than by block count, because the block count is not
                // controllable: the ship generator has a minimum size, so a hundred grids of
                // eighty blocks each are a hundred grids of eight hundred. The per-thousand-block
                // figure is what makes the rows comparable.
                string name = counts[c] + " grid" + (counts[c] == 1 ? "" : "s");
                Add(rows, "fleets", name, "step, whole fleet", best, "ms");
                Add(rows, "fleets", name, "blocks", nodes, "", false);
                Add(rows, "fleets", name, "blocks per grid", fleet.Count == 0 ? 0 : nodes / fleet.Count, "", false);
                Add(rows, "fleets", name, "per thousand blocks", nodes <= 0 ? 0 : best * 1000 / nodes, "ms");
            }
        }

        // ----------------------------------------------------------------------------------
        // The measurement itself
        // ----------------------------------------------------------------------------------

        private class Sample
        {
            public double StepMs;
            public double MsPerSimulatedSecond;
            public double NsPerElement;
            public float Substeps;

            /// <summary>What a full step asked for, before MaxSubsteps refused any of it.</summary>
            public float Demand;

            public int Floored;
            public bool Clamped;

            /// <summary>Whether the conduction overshoot clamp ran, as opposed to being skipped.</summary>
            public bool ClampLive;

            /// <summary>
            /// Whether any per-mechanism watt figure reached a node object.
            ///
            /// Read off the nodes rather than off the setting that asked for them: the setting is
            /// the input, and what a diagnostics case has to prove is the output.
            /// </summary>
            public bool DiagnosticsPublished;
        }

        /// <summary>
        /// Settings for one case: the shipped defaults, a substep cap, and whether the features start
        /// on or off.
        /// </summary>
        private static ThermalSettings Configure(int cap, bool featuresOn)
        {
            ThermalSettings settings = new ThermalSettings();

            if (!featuresOn)
            {
                for (int i = 0; i < Features.Length; i++) Features[i].Set(settings, false);

                // The clamps are not features in the sense the others are — they are what keeps a
                // refused step bounded — so a bare configuration keeps them.
                settings.ClampConductionOvershoot = true;
                settings.ClampEnvironmentOvershoot = true;
                settings.EnableEnvironment = true;
            }

            settings.MaxSubstepsPerBlock = cap;
            return settings.Derive();
        }

        /// <summary>
        /// The hull a case is measured on, dealt from the census and not yet simulated. Kept apart
        /// from <see cref="Build"/> so the ladder's `build` column times the simulation's own load
        /// path and not the generator: from `C26` the bolt search was ten times the build it fed,
        /// and the column reported the sum under a name that reads as the mod's.
        /// See performance.md, Iteration 2.
        /// </summary>
        private static GridBuilder Hull(string shape, int size)
        {
            GridBuilder builder = GridBuilder.Large();
            builder.PlaceCensus(LoadShapes.Build(shape, size));
            return builder;
        }

        private static ThermalSimulation Build(ThermalSettings settings, string shape, int size)
        {
            return Build(settings, Hull(shape, size));
        }

        /// <summary>Surfaces, links, loops, rooms and exposure for a hull already dealt.</summary>
        private static ThermalSimulation Build(ThermalSettings settings, GridBuilder builder)
        {
            ThermalSimulation simulation = builder.BuildSimulation(settings, 293.15f);
            simulation.RebuildAll();

            // `bench report --diagnostics` sets this, and until now nothing in this file read it:
            // the flag ran the whole report in the cheap configuration and printed it under a name
            // that claimed otherwise. Telemetry switches the per-mechanism watt figures on, so a
            // field dump is always measured with them on and a report that cannot reproduce that
            // cannot be compared against one.
            simulation.Solver.CollectDiagnostics = LoadBenchmarks.CollectDiagnostics;
            return simulation;
        }

        private static Sample Measure(ThermalSettings settings, string shape, int size, int ticks,
            ThermalSimulation prepared = null, EnvironmentSample? world = null, bool gateClamp = true,
            bool everySubstep = false)
        {
            ThermalSimulation simulation = prepared ?? Build(settings, shape, size);
            simulation.Solver.GateConductionClamp = gateClamp;
            simulation.Solver.DiagnosticsOnEverySubstep = everySubstep;
            LoadBenchmarks.SeedSpread(simulation);
            Census.DriveCensus(simulation);

            EnvironmentState state = EnvironmentSolver.Solve(
                settings, simulation.Planet, world ?? Worst());

            float step = settings.StepSeconds;

            // Warm the caches and let the substep count settle before the clock starts.
            for (int i = 0; i < 2; i++) simulation.Solver.Step(step, state);

            double best = double.MaxValue;
            int repeats = Repeats < 1 ? 1 : Repeats;

            for (int r = 0; r < repeats; r++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < ticks; i++) simulation.Solver.Step(step, state);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds / ticks;
                if (ms < best) best = ms;
            }

            Sample sample = new Sample();
            sample.StepMs = best;
            sample.MsPerSimulatedSecond = sample.StepMs * settings.StepsPerSecond;
            sample.Substeps = simulation.Solver.LastSubsteps;
            sample.Demand = simulation.Solver.LastRequiredSubsteps;
            sample.Floored = simulation.Solver.FlooredNodes;
            sample.Clamped = simulation.Solver.LastStepWasClamped;
            sample.ClampLive = simulation.Solver.ConductionClampLive;

            IList<ThermalNode> written = simulation.Solver.Nodes;
            for (int i = 0; i < written.Count; i++)
            {
                ThermalNode node = written[i];
                if (node.LastRadiationWatts == 0f && node.LastConvectionWatts == 0f
                    && node.LastSolarWatts == 0f && node.LastFrictionWatts == 0f
                    && node.LastConductionWatts == 0f)
                {
                    continue;
                }

                sample.DiagnosticsPublished = true;
                break;
            }

            double elements = simulation.Solver.Nodes.Count + simulation.Solver.Links.Count;
            double visits = elements * Math.Max(1, sample.Substeps);
            sample.NsPerElement = visits <= 0 ? 0 : sample.StepMs * 1e6 / visits;

            return sample;
        }

        private static void Add(List<ReportRow> rows, string section, string name, string metric,
            double value, string unit, bool lowerIsBetter = true)
        {
            rows.Add(new ReportRow
            {
                Section = section,
                Case = name,
                Metric = metric,
                Value = value,
                Unit = unit,
                LowerIsBetter = lowerIsBetter,
            });
        }

        // ----------------------------------------------------------------------------------
        // Reporting and comparing
        // ----------------------------------------------------------------------------------

        public static string Table(IList<ReportRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            string section = null;

            for (int i = 0; i < rows.Count; i++)
            {
                ReportRow row = rows[i];

                if (row.Section != section)
                {
                    section = row.Section;
                    sb.Append('\n').Append(section).Append('\n');
                    sb.Append(new string('-', section.Length)).Append('\n');
                }

                sb.Append("  ").Append(row.Case.PadRight(22))
                  .Append(row.Metric.PadRight(24))
                  .Append(Format(row.Value).PadLeft(14))
                  .Append(' ').Append(row.Unit)
                  .Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>The diffable artefact. One row per figure, keyed so a join is unambiguous.</summary>
        public static string Csv(IList<ReportRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("section,case,metric,value,unit,lower_is_better\n");

            for (int i = 0; i < rows.Count; i++)
            {
                ReportRow row = rows[i];
                sb.Append(Quote(row.Section)).Append(',')
                  .Append(Quote(row.Case)).Append(',')
                  .Append(Quote(row.Metric)).Append(',')
                  .Append(row.Value.ToString("r", Invariant)).Append(',')
                  .Append(Quote(row.Unit)).Append(',')
                  .Append(row.LowerIsBetter ? 1 : 0).Append('\n');
            }

            return sb.ToString();
        }

        public static List<ReportRow> ParseCsv(string text)
        {
            List<ReportRow> rows = new List<ReportRow>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;

                List<string> fields = SplitCsv(lines[i]);
                if (fields.Count < 6) continue;

                double value;
                if (!double.TryParse(fields[3], NumberStyles.Float, Invariant, out value)) continue;

                rows.Add(new ReportRow
                {
                    Section = fields[0],
                    Case = fields[1],
                    Metric = fields[2],
                    Value = value,
                    Unit = fields[4],
                    LowerIsBetter = fields[5] == "1",
                });
            }

            return rows;
        }

        /// <summary>
        /// This report against an earlier one, worst regression first.
        ///
        /// <paramref name="threshold"/> is the fractional change worth printing. Below it the
        /// difference is machine noise: two runs of the same binary on the same machine move by a
        /// few per cent, and a report that flags those is a report nobody reads.
        /// </summary>
        public static string Compare(IList<ReportRow> baseline, IList<ReportRow> current,
            double threshold = 0.05)
        {
            Dictionary<string, ReportRow> before = new Dictionary<string, ReportRow>();
            for (int i = 0; i < baseline.Count; i++) before[baseline[i].Key] = baseline[i];

            // A figure smaller than the machine's own run-to-run spread was never measured, so a
            // change in it is not a finding. This matters most when an optimisation lands: making
            // the solver leaner lifts several feature costs out of the noise at once, and every
            // one of them arrives in the diff as a large percentage increase over a number that
            // had meant nothing. Marking them is the difference between a report that explains
            // that and one that looks like fourteen regressions.
            double noise = NoiseOf(current);

            List<string> regressions = new List<string>();
            List<string> improvements = new List<string>();
            List<string> appeared = new List<string>();

            for (int i = 0; i < current.Count; i++)
            {
                ReportRow row = current[i];

                ReportRow old;
                if (!before.TryGetValue(row.Key, out old))
                {
                    appeared.Add(row.Key);
                    continue;
                }

                before.Remove(row.Key);

                if (Math.Abs(old.Value) < 1e-9) continue;

                double change = (row.Value - old.Value) / Math.Abs(old.Value);
                if (Math.Abs(change) < threshold) continue;

                bool underNoise = noise > 0
                    && Math.Abs(old.Value) < noise
                    && string.Equals(row.Unit, "ms", StringComparison.Ordinal);

                string line = "  " + row.Key.PadRight(52)
                    + Format(old.Value).PadLeft(12) + " ->"
                    + Format(row.Value).PadLeft(12) + "  "
                    + (change > 0 ? "+" : "") + (100 * change).ToString("n1", Invariant) + "%"
                    + (underNoise ? "   (was inside the noise floor)" : "");

                // Something that was never measurable cannot have regressed.
                bool worse = row.LowerIsBetter && change > 0 && !underNoise;
                if (worse) regressions.Add(line);
                else improvements.Add(line);
            }

            StringBuilder sb = new StringBuilder();

            sb.Append("\nregressions (").Append(regressions.Count).Append(")\n");
            if (regressions.Count == 0) sb.Append("  none beyond ")
                .Append((100 * threshold).ToString("n0", Invariant)).Append("%\n");
            for (int i = 0; i < regressions.Count; i++) sb.Append(regressions[i]).Append('\n');

            sb.Append("\nimprovements and neutral moves (").Append(improvements.Count).Append(")\n");
            for (int i = 0; i < improvements.Count; i++) sb.Append(improvements[i]).Append('\n');

            if (appeared.Count > 0)
            {
                sb.Append("\nnew figures, not in the baseline (").Append(appeared.Count).Append(")\n");
                for (int i = 0; i < appeared.Count; i++) sb.Append("  ").Append(appeared[i]).Append('\n');
            }

            if (before.Count > 0)
            {
                sb.Append("\ngone since the baseline (").Append(before.Count).Append(")\n");
                foreach (string key in before.Keys) sb.Append("  ").Append(key).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>The run-to-run spread the report measured on itself, in milliseconds.</summary>
        private static double NoiseOf(IList<ReportRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Section == "machine" && rows[i].Case == "noise"
                    && rows[i].Metric == "spread of five identical runs")
                {
                    return rows[i].Value;
                }
            }

            return 0;
        }

        private static string Format(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 1e9)
            {
                return value.ToString("n0", Invariant);
            }

            return Math.Abs(value) < 10
                ? value.ToString("n4", Invariant)
                : value.ToString("n2", Invariant);
        }

        private static string Quote(string value)
        {
            return CsvLine.Text(value);
        }

        private static List<string> SplitCsv(string line)
        {
            return CsvLine.Split(line);
        }
    }
}
