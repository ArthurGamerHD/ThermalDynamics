using System;
using System.Collections.Generic;
using System.IO;
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
        /// </summary>
        private static int BenchCommand(string[] args)
        {
            string name = args.Length > 1 ? args[1] : "scale";
            string shape = Option(args, "--shape", "ship");
            int size = OptionInt(args, "--size", 125000);
            int max = OptionInt(args, "--max", int.MaxValue);
            int ticks = OptionInt(args, "--ticks", 0);
            string csvDirectory = Option(args, "--csv", null);

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
            Console.WriteLine("  bench spike --size N    one block placed, split by stage");
            Console.WriteLine("    --shape ship|cube|truss   --max N   --ticks N   --csv <dir>");
        }
    }
}
