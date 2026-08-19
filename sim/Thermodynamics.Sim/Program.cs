using System;
using System.Collections.Generic;
using System.IO;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Sim
{
    /// <summary>
    /// Command line front end for the scenario library.
    ///
    ///   dotnet run --project sim/Thermodynamics.Sim -- list
    ///   dotnet run --project sim/Thermodynamics.Sim -- run reactor
    ///   dotnet run --project sim/Thermodynamics.Sim -- run all --csv out/
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                PrintUsage();
                return 0;
            }

            switch (args[0])
            {
                case "list":
                    foreach (string name in Scenarios.Names) Console.WriteLine("  " + name);
                    return 0;

                case "run":
                    return RunCommand(args);

                case "bench":
                    return BenchCommand(args);

                default:
                    Console.Error.WriteLine("Unknown command: " + args[0]);
                    PrintUsage();
                    return 1;
            }
        }

        private static int RunCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("run needs a scenario name, or 'all'.");
                return 1;
            }

            string csvDirectory = null;
            for (int i = 2; i < args.Length - 1; i++)
            {
                if (args[i] == "--csv") csvDirectory = args[i + 1];
            }

            string[] names = args[1] == "all" ? Scenarios.Names : new string[] { args[1] };

            foreach (string name in names)
            {
                ScenarioResult result;
                try
                {
                    result = Scenarios.Run(name);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(name + ": FAILED - " + error.Message);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("== " + result.Name + " ==");
                Console.WriteLine(result.Summary);

                if (result.Runner != null && result.Runner.Samples.Count > 0)
                {
                    PrintTable(result);
                }

                if (csvDirectory != null && !string.IsNullOrEmpty(result.Csv))
                {
                    Directory.CreateDirectory(csvDirectory);
                    string path = Path.Combine(csvDirectory, result.Name + ".csv");
                    File.WriteAllText(path, result.Csv);
                    Console.WriteLine("csv -> " + path);
                }
            }

            return 0;
        }

        /// <summary>
        /// The load benchmarks. Separate from <c>run</c> because they answer a different
        /// question — not what the simulation does, but what it costs at sizes no scenario
        /// would sit through.
        ///
        ///   bench scale                       the ladder, ship shape, up to 10^6 blocks
        ///   bench scale --shape truss         the same ladder on a station spine
        ///   bench scale --max 125000          stop the ladder early
        ///   bench hitch --size 250000         per-tick cost with a block welded mid-run
        ///   bench weld  --size 250000         a block welded on every tick
        ///   bench load  --size 1000000        what building the grid costs before tick one
        ///   bench floor --size 42000           what a per-block substep cap buys, and costs
        ///   bench report --csv out/            the full performance report, as a CSV to diff
        ///   bench report --baseline out/performance.csv   the same, against an earlier one
        /// </summary>
        private static int BenchCommand(string[] args)
        {
            string name = args.Length > 1 ? args[1] : "scale";
            string shape = Option(args, "--shape", "ship");
            int size = OptionInt(args, "--size", 125000);
            int max = OptionInt(args, "--max", int.MaxValue);
            int ticks = OptionInt(args, "--ticks", 0);
            string csvDirectory = Option(args, "--csv", null);

            // Telemetry switches the solver's per-mechanism watt figures on, so a field report
            // includes the cost of being measured. This makes that comparable.
            LoadBenchmarks.CollectDiagnostics = Has(args, "--diagnostics");

            switch (name)
            {
                case "scale":
                {
                    List<int> sizes = new List<int>();
                    foreach (int rung in LoadBenchmarks.DefaultSizes)
                    {
                        if (rung <= max) sizes.Add(rung);
                    }
                    if (sizes.Count == 0) sizes.Add(max);

                    Console.WriteLine("Scale ladder, " + shape + " shape. Building "
                        + sizes[sizes.Count - 1].ToString("n0") + " blocks takes a while.");
                    Console.WriteLine();

                    List<ScaleRow> rows = LoadBenchmarks.Scale(
                        shape, sizes, message => Console.Error.WriteLine("  " + message));

                    Console.WriteLine(LoadBenchmarks.Table(rows));

                    if (csvDirectory != null)
                    {
                        Directory.CreateDirectory(csvDirectory);
                        string path = Path.Combine(csvDirectory, "scale-" + shape + ".csv");
                        File.WriteAllText(path, LoadBenchmarks.Csv(rows));
                        Console.WriteLine("csv -> " + path);
                    }
                    return 0;
                }

                case "hitch":
                    PrintHitch(LoadBenchmarks.Hitch(shape, size, ticks > 0 ? ticks : 400));
                    return 0;

                case "weld":
                    PrintHitch(LoadBenchmarks.Weld(shape, size, ticks > 0 ? ticks : 120));
                    return 0;

                case "load":
                    PrintHitch(LoadBenchmarks.Load(shape, size));
                    return 0;

                case "reach":
                {
                    float seconds = OptionInt(args, "--seconds", 10);
                    int length = OptionInt(args, "--length", 200);

                    List<LoadBenchmarks.ReachRow> rows = new List<LoadBenchmarks.ReachRow>();

                    // Two sweeps. First the accuracy-first end, raising transfer with enough
                    // substeps that nothing clamps; then the arcade end, where a single substep
                    // per step leans on the overshoot clamp to stay bounded.
                    rows.Add(LoadBenchmarks.Reach("sim f8 h225", 8, 1f, 225f, 64, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("def f4 h225", 4, 1f, 225f, 16, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f4 h3600", 4, 1f, 3600f, 16, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f4 h20k", 4, 1f, 20000f, 16, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f4 h100k", 4, 1f, 100000f, 16, seconds, length));

                    rows.Add(LoadBenchmarks.Reach("f4 h20k x1", 4, 1f, 20000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f4 h100k x1", 4, 1f, 100000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f4 h1M x1", 4, 1f, 1000000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f8 h1M x1", 8, 1f, 1000000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f16 h1M x1", 16, 1f, 1000000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f16 h10M x1", 16, 1f, 10000000f, 1, seconds, length));
                    rows.Add(LoadBenchmarks.Reach("f1 h1M x1", 1, 1f, 1000000f, 1, seconds, length));

                    Console.WriteLine();
                    Console.WriteLine("== reach, " + length + " blocks, " + seconds + " real seconds ==");
                    Console.WriteLine();
                    Console.WriteLine(LoadBenchmarks.ReachTable(rows));
                    return 0;
                }

                case "memory":
                {
                    Console.WriteLine();
                    Console.WriteLine("== memory, " + shape + " " + size.ToString("n0") + " ==");
                    Console.WriteLine();
                    Console.WriteLine(LoadBenchmarks.MemoryTable(LoadBenchmarks.Memory(shape, size)));
                    return 0;
                }

                case "profiles":
                {
                    float seconds = OptionInt(args, "--seconds", 20);
                    int length = OptionInt(args, "--length", 200);

                    List<LoadBenchmarks.ReachRow> reach = new List<LoadBenchmarks.ReachRow>();
                    List<LoadBenchmarks.StabilityRow> stable = new List<LoadBenchmarks.StabilityRow>();

                    for (int i = 0; i < ThermalProfiles.Names.Length; i++)
                    {
                        string profile = ThermalProfiles.Names[i];

                        ThermalSettings forReach = new ThermalSettings();
                        ThermalProfiles.Apply(forReach, profile);
                        reach.Add(LoadBenchmarks.Reach(profile, forReach, seconds, length));

                        ThermalSettings forStability = new ThermalSettings();
                        ThermalProfiles.Apply(forStability, profile);
                        forStability.MaxLinkVisitsPerStep = 0;
                        forStability.Derive();
                        stable.Add(LoadBenchmarks.Stability(profile, forStability, seconds));
                    }

                    Console.WriteLine();
                    Console.WriteLine("== profiles ==");
                    Console.WriteLine();
                    for (int i = 0; i < ThermalProfiles.Names.Length; i++)
                    {
                        Console.WriteLine("  " + ThermalProfiles.Names[i].PadRight(12)
                            + ThermalProfiles.Describe(ThermalProfiles.Names[i]));
                    }
                    Console.WriteLine();
                    Console.WriteLine("  how fast heat crosses " + length + " blocks, conduction only:");
                    Console.WriteLine(LoadBenchmarks.ReachTable(reach));
                    Console.WriteLine("  and how each behaves with the environment on:");
                    Console.WriteLine(LoadBenchmarks.StabilityTable(stable));
                    return 0;
                }

                case "stability":
                {
                    float seconds = OptionInt(args, "--seconds", 20);
                    List<LoadBenchmarks.StabilityRow> rows = new List<LoadBenchmarks.StabilityRow>();

                    rows.Add(LoadBenchmarks.Stability("default", 4, 225f, 16, seconds));
                    rows.Add(LoadBenchmarks.Stability("h3600 x16", 4, 3600f, 16, seconds));
                    rows.Add(LoadBenchmarks.Stability("h3600 x1", 4, 3600f, 1, seconds));
                    rows.Add(LoadBenchmarks.Stability("h20k x1", 4, 20000f, 1, seconds));
                    rows.Add(LoadBenchmarks.Stability("h100k x1", 4, 100000f, 1, seconds));
                    rows.Add(LoadBenchmarks.Stability("h1M x1", 4, 1000000f, 1, seconds));
                    rows.Add(LoadBenchmarks.Stability("h20k x1 f16", 16, 20000f, 1, seconds));
                    rows.Add(LoadBenchmarks.Stability("h20k x4", 4, 20000f, 4, seconds));

                    Console.WriteLine();
                    Console.WriteLine("== stability, environment on, " + seconds + " real seconds ==");
                    Console.WriteLine();
                    Console.WriteLine(LoadBenchmarks.StabilityTable(rows));
                    return 0;
                }

                case "pace":
                {
                    float seconds = OptionInt(args, "--seconds", 20);
                    Console.WriteLine();
                    Console.WriteLine("== pace " + shape + " " + size.ToString("n0") + " ==");
                    Console.WriteLine("  Constant SimulationSpeed x HeatTimeScale, then Frequency alone.");
                    Console.WriteLine();
                    Console.WriteLine(LoadBenchmarks.PaceTable(
                        LoadBenchmarks.Pace(shape, size, seconds), seconds));
                    return 0;
                }

                case "report":
                {
                    List<int> ladder = new List<int>();
                    foreach (int rung in LoadBenchmarks.DefaultSizes)
                    {
                        if (rung <= max) ladder.Add(rung);
                    }
                    if (ladder.Count == 0) ladder.Add(max);

                    int reportTicks = ticks > 0 ? ticks : 20;
                    string baseline = Option(args, "--baseline", null);
                    string outDir = csvDirectory ?? "out";

                    Console.WriteLine();
                    Console.WriteLine("== performance report ==");
                    Console.WriteLine("  " + shape + ", feature and configuration cases on "
                        + size.ToString("n0") + " blocks, " + reportTicks + " steps each.");
                    Console.WriteLine("  Ladder: " + string.Join(", ", ladder.ConvertAll(
                        delegate (int n) { return n.ToString("n0"); }).ToArray()));
                    Console.WriteLine();

                    List<ReportRow> rows = PerformanceReport.Run(shape, size, reportTicks, ladder,
                        message => Console.Error.WriteLine("  " + message));

                    Console.WriteLine(PerformanceReport.Table(rows));

                    Directory.CreateDirectory(outDir);
                    string path = Path.Combine(outDir, "performance.csv");
                    File.WriteAllText(path, PerformanceReport.Csv(rows));
                    Console.WriteLine("csv -> " + path);

                    if (baseline != null && File.Exists(baseline))
                    {
                        Console.WriteLine();
                        Console.WriteLine("== against " + baseline + " ==");
                        Console.WriteLine(PerformanceReport.Compare(
                            PerformanceReport.ParseCsv(File.ReadAllText(baseline)), rows));
                    }
                    else if (baseline != null)
                    {
                        Console.Error.WriteLine("  baseline not found: " + baseline);
                    }

                    return 0;
                }

                case "floor":
                {
                    int[] caps = { 0, 32, 16, 8, 6, 4, 3, 2, 1 };

                    Console.WriteLine();
                    Console.WriteLine("== substep floor, " + shape + " " + size.ToString("n0") + " ==");
                    Console.WriteLine("  MaxSubstepsPerBlock swept, on a hull built from the measured"
                        + " block census.");
                    Console.WriteLine("  " + (ticks > 0 ? ticks : 200) + " steps of a quarter second, "
                        + (Has(args, "--driven")
                            ? "the census share of heat producers run to equilibrium"
                            : "temperatures spread 250-750 K")
                        + ", vacuum. Error is against the uncapped run.");
                    Console.WriteLine();

                    Console.WriteLine(LoadBenchmarks.FloorTable(LoadBenchmarks.SubstepFloor(
                        shape, size, ticks > 0 ? ticks : 200, caps,
                        message => Console.Error.WriteLine("  " + message),
                        Has(args, "--driven"))));
                    return 0;
                }

                case "spike":
                {
                    LoadBenchmarks.SpikeReport report = LoadBenchmarks.Spike(shape, size);
                    Console.WriteLine();
                    Console.WriteLine("== spike " + shape + " " + size.ToString("n0") + " ==");
                    Console.WriteLine("  " + report.Blocks.ToString("n0") + " blocks, "
                        + report.Links.ToString("n0") + " links, one block placed.");
                    Console.WriteLine("  " + report.Describe());
                    return 0;
                }

                default:
                    Console.Error.WriteLine("Unknown benchmark: " + name);
                    Console.Error.WriteLine("  one of: " + string.Join(", ", LoadBenchmarks.Names));
                    return 1;
            }
        }

        private static void PrintHitch(HitchResult result)
        {
            Console.WriteLine();
            Console.WriteLine("== " + result.Name + " ==");
            Console.WriteLine("  " + result.Blocks.ToString("n0") + " blocks, "
                + result.Links.ToString("n0") + " links, built in "
                + result.BuildMs.ToString("n0") + " ms.");
            if (result.Notes.Length > 0) Console.WriteLine("  " + result.Notes);
            Console.WriteLine("  " + result.Trace.Describe());
            if (result.SolverTick >= 0) Console.WriteLine("  " + result.DescribeStages());
            Console.WriteLine("  " + result.DescribeGc());
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == flag) return true;
            }
            return false;
        }

        private static string Option(string[] args, string flag, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag) return args[i + 1];
            }
            return fallback;
        }

        private static int OptionInt(string[] args, string flag, int fallback)
        {
            string raw = Option(args, flag, null);
            int value;
            return raw != null && int.TryParse(raw, out value) ? value : fallback;
        }

        private static void PrintTable(ScenarioResult result)
        {
            string[] lines = result.Csv.Split('\n');
            int shown = Math.Min(lines.Length, 14);
            for (int i = 0; i < shown; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0) continue;
                Console.WriteLine("  " + line);
            }
            if (lines.Length > shown) Console.WriteLine("  ... " + (lines.Length - shown) + " more rows");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Thermal Dynamics simulation harness");
            Console.WriteLine();
            Console.WriteLine("  list                    show available scenarios");
            Console.WriteLine("  run <name|all>          run a scenario");
            Console.WriteLine("  run <name> --csv <dir>  also write full results as CSV");
            Console.WriteLine();
            Console.WriteLine("  bench scale             cost per stage as the grid grows");
            Console.WriteLine("  bench hitch --size N    per-tick cost, with a block welded mid-run");
            Console.WriteLine("  bench weld  --size N    a block welded on every tick");
            Console.WriteLine("  bench load  --size N    what building the grid costs before tick one");
            Console.WriteLine("  bench floor --size N    what a per-block substep cap buys, and costs");
            Console.WriteLine("  bench report            full performance report; --baseline <csv> to compare");
            Console.WriteLine("  bench spike --size N    one block placed, split by stage");
            Console.WriteLine("  bench pace  --size N    does slowing sim and raising transfer save anything");
            Console.WriteLine("  bench reach --length N  how fast heat crosses a grid, against what it costs");
            Console.WriteLine("  bench profiles          the named profiles, measured side by side");
            Console.WriteLine("  bench memory --size N   where a grid's memory goes, by structure");
            Console.WriteLine("    --shape ship|cube|truss   --max N   --ticks N   --csv <dir>");
            Console.WriteLine("    --diagnostics             as telemetry runs it: per-node watts on");
        }
    }
}
