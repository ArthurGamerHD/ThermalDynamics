using System;
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
        }
    }
}
