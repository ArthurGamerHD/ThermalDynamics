using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Thermodynamics.Harness;

namespace Thermodynamics.Sim
{
    public static class CorpusFetch
    {
        private const int AppId = 244850;

        private const int PageSize = 100;

        private const int BatchSize = 100;

        private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan BatchPause = TimeSpan.FromSeconds(2);

        public class Item
        {
            public long Id;
            public string Title;
            public long Subscriptions;
/// <summary>List operation.</summary>
            public List<string> Tags = new List<string>();
        }

/// <summary>Run operation.</summary>
        public static int Run(string[] args)
        {
/// <summary>Value operation.</summary>
            string key = Value(args, "--key") ?? Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");
/// <summary>Value operation.</summary>
            string user = Value(args, "--user") ?? "anonymous";
/// <summary>Value operation.</summary>
            string output = Value(args, "--out") ?? Thermodynamics.Harness.Blueprints.CorpusPath();
/// <summary>Value operation.</summary>
            string steamcmd = Value(args, "--steamcmd") ?? "steamcmd";
/// <summary>Int operation.</summary>
            int target = Int(Value(args, "--top"), 10000);
/// <summary>Has operation.</summary>
            bool listOnly = Has(args, "--list-only");

            Directory.CreateDirectory(output);
            string manifest = Path.Combine(output, "manifest.csv");

/// <summary>Load operation.</summary>
            List<Item> items = Load(manifest);
            if (items.Count >= target)
            {
                Console.WriteLine("Manifest already holds " + items.Count.ToString("n0") + " items; not re-listing.");
            }
            else if (string.IsNullOrEmpty(key))
            {
                Console.Error.WriteLine("A Steam Web API key is required to list the workshop.");
                Console.Error.WriteLine("Get one free at https://steamcommunity.com/dev/apikey, then pass");
                Console.Error.WriteLine("--key <key>, or set STEAM_WEB_API_KEY.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Only listing needs it. Fetching does not, and neither needs an account:");
                Console.Error.WriteLine("this workshop answers an anonymous SteamCMD login.");
                return 1;
            }
            else
            {
/// <summary>List operation.</summary>
                items = List(key, target).GetAwaiter().GetResult();
                Save(manifest, items);
                Console.WriteLine("Listed " + items.Count.ToString("n0") + " blueprints to " + manifest);
            }

            if (listOnly) return 0;

            if (items.Count > target) items = items.GetRange(0, target);
/// <summary>Fetch operation.</summary>
            return Fetch(items, user, steamcmd, output);
        }


/// <summary>List operation.</summary>
        private static async Task<List<Item>> List(string key, int target)
        {
/// <summary>List operation.</summary>
            List<Item> items = new List<Item>();
/// <summary>HashSet operation.</summary>
            HashSet<long> seen = new HashSet<long>();
            string cursor = "*";

            using (HttpClient http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(60);

                while (items.Count < target)
                {
                    string url =
                        "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/" +
                        "?key=" + Uri.EscapeDataString(key) +
                        "&appid=" + AppId +
                        "&query_type=9" +                     // RankedByTotalUniqueSubscriptions
                        "&filetype=0" +                       // Items
                        "&requiredtags%5B0%5D=Blueprint" +
                        "&match_all_tags=false" +
                        "&numperpage=" + PageSize +
                        "&cursor=" + Uri.EscapeDataString(cursor) +
                        "&return_tags=true&return_metadata=true&return_vote_data=true";

                    string body;
                    try
                    {
                        body = await http.GetStringAsync(url);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Listing stopped: " + Redact(e.Message, key));
                        break;
                    }

                    int before = items.Count;
/// <summary>ReadPage operation.</summary>
                    string next = ReadPage(body, items, seen, target);

                    Console.WriteLine("  listed " + items.Count.ToString("n0") + " of " + target.ToString("n0"));

                    if (next == null || next == cursor || items.Count == before) break;

                    cursor = next;
                    Thread.Sleep(PagePause);
                }
            }

            return items;
        }

/// <summary>Redact operation.</summary>
        private static string Redact(string text, string secret)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret)) return text;
            return text.Replace(secret, "<key>");
        }

/// <summary>ReadPage operation.</summary>
        private static string ReadPage(string body, List<Item> items, HashSet<long> seen, int target)
        {
            using (JsonDocument document = JsonDocument.Parse(body))
            {
                JsonElement response;
                if (!document.RootElement.TryGetProperty("response", out response)) return null;

                string next = null;
                JsonElement cursor;
                if (response.TryGetProperty("next_cursor", out cursor)) next = cursor.GetString();

                JsonElement details;
                if (!response.TryGetProperty("publishedfiledetails", out details)) return next;

                foreach (JsonElement element in details.EnumerateArray())
                {
                    if (items.Count >= target) break;

/// <summary>ReadItem operation.</summary>
                    Item item = ReadItem(element);
                    if (item == null || !seen.Add(item.Id)) continue;

                    if (item.Tags.Contains("Mod")) continue;

                    items.Add(item);
                }

                return next;
            }
        }

/// <summary>ReadItem operation.</summary>
        private static Item ReadItem(JsonElement element)
        {
            JsonElement id;
            if (!element.TryGetProperty("publishedfileid", out id)) return null;

            long parsed;
            if (!long.TryParse(id.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return null;
            }

            Item item = new Item { Id = parsed, Title = "" };

            JsonElement title;
            if (element.TryGetProperty("title", out title)) item.Title = title.GetString() ?? "";

            JsonElement subscriptions;
            if (element.TryGetProperty("subscriptions", out subscriptions)
                && subscriptions.ValueKind == JsonValueKind.Number)
            {
                item.Subscriptions = subscriptions.GetInt64();
            }

            JsonElement tags;
            if (element.TryGetProperty("tags", out tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tag in tags.EnumerateArray())
                {
                    JsonElement name;
                    if (tag.TryGetProperty("tag", out name)) item.Tags.Add(name.GetString() ?? "");
                }
            }

            return item;
        }


/// <summary>Fetch operation.</summary>
        private static int Fetch(List<Item> items, string user, string steamcmd, string output)
        {
/// <summary>List operation.</summary>
            List<long> wanted = new List<long>();
            foreach (Item item in items)
            {
                if (!Directory.Exists(ItemPath(output, item.Id))) wanted.Add(item.Id);
            }

            Console.WriteLine();
            Console.WriteLine(wanted.Count.ToString("n0") + " of " + items.Count.ToString("n0")
                + " items still to fetch.");

            if (wanted.Count == 0) return 0;

            int done = 0;
            for (int i = 0; i < wanted.Count; i += BatchSize)
            {
/// <summary>StringBuilder operation.</summary>
                StringBuilder arguments = new StringBuilder();
                arguments.Append("+force_install_dir ").Append(Quote(Path.GetFullPath(output)));
                arguments.Append(" +login ").Append(user);

                int end = Math.Min(i + BatchSize, wanted.Count);
                for (int j = i; j < end; j++)
                {
                    arguments.Append(" +workshop_download_item ").Append(AppId).Append(' ').Append(wanted[j]);
                }
                arguments.Append(" +quit");

                if (!RunSteamCmd(steamcmd, arguments.ToString())) return 1;

                done = end;
                Console.WriteLine("  fetched " + done.ToString("n0") + " of " + wanted.Count.ToString("n0"));

                if (end < wanted.Count) Thread.Sleep(BatchPause);
            }

/// <summary>Unpack operation.</summary>
            int unpacked = Unpack(output);

            Console.WriteLine();
            Console.WriteLine(unpacked.ToString("n0") + " legacy archives unpacked to bp.sbc");
            Console.WriteLine("Corpus at " + Path.GetFullPath(output));
            Console.WriteLine("Read it with:  corpus");
            return 0;
        }

/// <summary>ItemPath operation.</summary>
        private static string ItemPath(string output, long id)
        {
            return Path.Combine(output, "steamapps", "workshop", "content", "244850",
                id.ToString(CultureInfo.InvariantCulture));
        }



/// <summary>Unpack operation.</summary>
        private static int Unpack(string output)
        {
            string root = Path.Combine(output, "steamapps", "workshop", "content", "244850");
            if (!Directory.Exists(root)) return 0;

            int unpacked = 0;

            foreach (string archive in Directory.GetFiles(root, "*_legacy.bin", SearchOption.AllDirectories))
            {
                string folder = Path.GetDirectoryName(archive);
                if (folder == null || File.Exists(Path.Combine(folder, "bp.sbc"))) continue;

                try
                {
                    using (ZipArchive zip = ZipFile.OpenRead(archive))
                    {
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            if (!Blueprints.IsLegacyBlueprintEntry(entry.Name)) continue;

                            entry.ExtractToFile(Path.Combine(folder, "bp.sbc"), true);
                            unpacked++;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return unpacked;
        }

/// <summary>RunSteamCmd operation.</summary>
        private static bool RunSteamCmd(string steamcmd, string arguments)
        {
            try
            {
/// <summary>ProcessStartInfo operation.</summary>
                ProcessStartInfo start = new ProcessStartInfo(steamcmd, arguments)
                {
                    UseShellExecute = false,
                };

                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();

                    return true;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Could not run '" + steamcmd + "': " + e.Message);
                Console.Error.WriteLine("Install SteamCMD, or pass --steamcmd <path>.");
                return false;
            }
        }


/// <summary>Save operation.</summary>
        private static void Save(string path, List<Item> items)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("id,subscriptions,title");
                foreach (Item item in items)
                {
                    writer.Write(item.Id);
                    writer.Write(',');
                    writer.Write(item.Subscriptions);
                    writer.Write(',');
                    writer.WriteLine(Csv(item.Title));
                }
            }
        }

/// <summary>Load operation.</summary>
        private static List<Item> Load(string path)
        {
/// <summary>List operation.</summary>
            List<Item> items = new List<Item>();
            if (!File.Exists(path)) return items;

            bool first = true;
            foreach (string line in File.ReadAllLines(path))
            {
                if (first) { first = false; continue; }
                if (line.Length == 0) continue;

                string[] parts = line.Split(new char[] { ',' }, 3);
                long id;
                if (parts.Length < 2 || !long.TryParse(parts[0], out id)) continue;

                long subscriptions;
                long.TryParse(parts[1], out subscriptions);

                items.Add(new Item
                {
                    Id = id,
                    Subscriptions = subscriptions,
                    Title = parts.Length > 2 ? parts[2] : "",
                });
            }

            return items;
        }

/// <summary>Csv operation.</summary>
        private static string Csv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.IndexOf(',') < 0 && text.IndexOf('"') < 0
                ? text
                : "\"" + text.Replace("\"", "\"\"") + "\"";
        }

/// <summary>Quote operation.</summary>
        private static string Quote(string path)
        {
            return path.IndexOf(' ') < 0 ? path : "\"" + path + "\"";
        }

/// <summary>Value operation.</summary>
        private static string Value(string[] args, string name)
        {
            return Cli.Value(args, name);
        }

/// <summary>Has operation.</summary>
        private static bool Has(string[] args, string name)
        {
            return Cli.Has(args, name);
        }

/// <summary>Int operation.</summary>
        private static int Int(string text, int fallback)
        {
            int value;
            return text != null && int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}
