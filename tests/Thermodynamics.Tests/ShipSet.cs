using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    internal static class ShipSet
    {
        public class Entry
        {
            public string Name;
            public string WorkshopId;

            public string Path;

            public string[] Fields;
        }


        public static List<Entry> Read(string variable, string fallback)
        {

            List<Entry> entries = new List<Entry>();

            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) path = Find(fallback);
            if (path == null || !File.Exists(path)) return entries;

            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;


                List<string> fields = Split(lines[i]);
                if (fields.Count < 3) continue;

                entries.Add(new Entry
                {
                    Name = fields[0],
                    WorkshopId = fields[1],
                    Path = fields[2],
                    Fields = fields.ToArray(),
                });
            }

            return entries;
        }


        public static List<Blueprints.Ship> Load(List<Entry> entries, Action<string> progress)
        {

            List<Blueprints.Ship> found = new List<Blueprints.Ship>();

            object gate = new object();
            int unresolved = 0;

            GameBlocks.BySubtype();

            System.Threading.Tasks.ParallelOptions options =
                new System.Threading.Tasks.ParallelOptions
                { MaxDegreeOfParallelism = LabRun.Workers };

            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, entries.Count, 1),
                options,
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        Entry wanted = entries[i];

                        if (string.IsNullOrEmpty(wanted.Path) || !File.Exists(wanted.Path))
                        {
                            lock (gate) unresolved++;
                            continue;
                        }

                        List<Blueprints.Ship> read;
                        try { read = Blueprints.Read(wanted.Path); }
                        catch { lock (gate) unresolved++; continue; }

                        bool matched = false;
                        foreach (Blueprints.Ship ship in read)
                        {
                            if (ship.Name != wanted.Name) continue;
                            if (ship.WorkshopId.ToString(CultureInfo.InvariantCulture)
                                != wanted.WorkshopId) continue;

                            lock (gate) found.Add(ship);
                            matched = true;
                            break;
                        }

                        if (!matched) lock (gate) unresolved++;
                    }
                });

            if (progress != null)
            {
                progress("set resolved " + found.Count + " of " + entries.Count + " ships, "
                    + unresolved + " unresolved");
            }

            return found;
        }


        public static List<string> Split(string line)
        {
            return CsvLine.Split(line);
        }


        public static string Find(string relative)
        {
            try
            {
                string candidate = Path.Combine(ShippedBlocks.RepoRoot(), relative);
                return File.Exists(candidate) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }

        public class Resume
        {
            private readonly string label;


            public Resume(string label)
            {
                this.label = label;
            }


            private string PathOrNull()
            {
                string directory = CorpusRecord.Directory();
                return directory == null ? null : Path.Combine(directory, "done-" + label + ".txt");
            }


            public HashSet<string> Done()
            {

                HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);


                string path = PathOrNull();
                if (path == null || !File.Exists(path)) return done;

                try
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0) done.Add(trimmed);
                    }
                }
                catch (IOException)
                {
                }

                return done;
            }


            public void Mark(string mark)
            {

                string path = PathOrNull();
                if (path == null) return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, mark + Environment.NewLine);
                }
                catch (IOException)
                {
                }
            }
        }


        public static void Progress(string sweep, string line)
        {
            string path = Environment.GetEnvironmentVariable("THERMAL_CORPUS_PROGRESS");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss") + " " + sweep + " " + line
                    + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
