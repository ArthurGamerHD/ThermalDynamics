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

namespace Thermodynamics.Sim
{
    /// <summary>
    /// Builds a blueprint corpus out of the Steam Workshop.
    ///
    /// <para>
    /// Two halves, because they have entirely different constraints. **Listing** is a public Web
    /// API call needing only a free key, and it is what decides *which* ten thousand ships — the
    /// corpus is meant to be a population of the designs people actually build, so it is ranked by
    /// subscriptions rather than taken at random. **Fetching** needs SteamCMD and an account that
    /// owns the game, because a paid title's workshop is not anonymously downloadable.
    /// </para>
    ///
    /// <para>
    /// **No credential is ever read, stored or logged by this tool.** SteamCMD is invoked with a
    /// user name and no password, which works once that account's credentials are cached by a
    /// single interactive <c>steamcmd +login &lt;user&gt;</c> run by hand. Anything else would mean
    /// this program handling a password, and it has no business doing that.
    /// </para>
    ///
    /// <para>
    /// Resumable and rate-limited throughout: a run that stops halfway is re-run, and anything
    /// already on disk is skipped. The listing pauses between pages and the fetch works in batches,
    /// because ten thousand items is a lot to ask of someone else's servers and there is no hurry.
    /// </para>
    /// </summary>
    public static class CorpusFetch
    {
        /// <summary>Space Engineers.</summary>
        private const int AppId = 244850;

        /// <summary>Items per API page. The endpoint's own maximum.</summary>
        private const int PageSize = 100;

        /// <summary>Workshop ids per SteamCMD invocation.</summary>
        private const int BatchSize = 100;

        /// <summary>Courtesy pause between API pages and between fetch batches.</summary>
        private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan BatchPause = TimeSpan.FromSeconds(2);

        public class Item
        {
            public long Id;
            public string Title;
            public long Subscriptions;
            public List<string> Tags = new List<string>();
        }

        public static int Run(string[] args)
        {
            string key = Value(args, "--key") ?? Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");
            // Anonymous works for this workshop, tested against a real item, so no credential is
            // needed at all — which is the best possible answer to a tool that would otherwise want
            // one. --user is kept for the case where an item turns out to need ownership.
            string user = Value(args, "--user") ?? "anonymous";
            string output = Value(args, "--out") ?? Thermodynamics.Harness.Blueprints.CorpusPath();
            string steamcmd = Value(args, "--steamcmd") ?? "steamcmd";
            int target = Int(Value(args, "--top"), 10000);
            bool listOnly = Has(args, "--list-only");

            Directory.CreateDirectory(output);
            string manifest = Path.Combine(output, "manifest.csv");

            List<Item> items = Load(manifest);
            if (items.Count >= target)
            {
                // The key buys the listing and nothing else, so a corpus with a manifest already
                // long enough can be fetched without one.
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
                items = List(key, target).GetAwaiter().GetResult();
                Save(manifest, items);
                Console.WriteLine("Listed " + items.Count.ToString("n0") + " blueprints to " + manifest);
            }

            if (listOnly) return 0;

            if (items.Count > target) items = items.GetRange(0, target);
            return Fetch(items, user, steamcmd, output);
        }

        // ---- listing ---------------------------------------------------------------------------

        /// <summary>
        /// The most-subscribed blueprints, newest cursor first.
        ///
        /// Ranked by total unique subscriptions rather than by votes or recency: the corpus is
        /// supposed to represent what people build and fly, and a subscription is the closest
        /// signal the workshop has to that. Ranking by vote would over-weight the spectacular, and
        /// by date the untested.
        /// </summary>
        private static async Task<List<Item>> List(string key, int target)
        {
            List<Item> items = new List<Item>();
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
                        // Redacted, because the key is in the query string and some transports put
                        // the whole URI in the message. A secret that reaches a log or a pasted
                        // error report has escaped as surely as one committed to a file.
                        Console.Error.WriteLine("Listing stopped: " + Redact(e.Message, key));
                        break;
                    }

                    int before = items.Count;
                    string next = ReadPage(body, items, seen, target);

                    Console.WriteLine("  listed " + items.Count.ToString("n0") + " of " + target.ToString("n0"));

                    // No cursor movement and no new items means the workshop has no more to give.
                    if (next == null || next == cursor || items.Count == before) break;

                    cursor = next;
                    Thread.Sleep(PagePause);
                }
            }

            return items;
        }

        /// <summary>Removes a secret from anything about to be printed.</summary>
        private static string Redact(string text, string secret)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret)) return text;
            return text.Replace(secret, "<key>");
        }

        /// <summary>Reads one API page, returning the cursor for the next.</summary>
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

                    Item item = ReadItem(element);
                    if (item == null || !seen.Add(item.Id)) continue;

                    // A workshop item tagged both Blueprint and Mod is a mod that ships a
                    // blueprint, and its ship is built out of its own blocks. Not a measurement of
                    // vanilla balance, and cheaper to drop here than to download and reject.
                    if (item.Tags.Contains("Mod")) continue;

                    items.Add(item);
                }

                return next;
            }
        }

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

        // ---- fetching --------------------------------------------------------------------------

        /// <summary>
        /// Downloads whatever is not already on disk, in batches, through SteamCMD.
        ///
        /// SteamCMD unpacks into its own <c>steamapps/workshop/content/244850/&lt;id&gt;</c>, which
        /// is the layout <c>Blueprints</c> already reads, so nothing is copied or rewritten: the
        /// corpus directory is pointed at rather than assembled.
        /// </summary>
        private static int Fetch(List<Item> items, string user, string steamcmd, string output)
        {
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

            int unpacked = Unpack(output);

            Console.WriteLine();
            Console.WriteLine(unpacked.ToString("n0") + " legacy archives unpacked to bp.sbc");
            Console.WriteLine("Corpus at " + Path.GetFullPath(output));
            Console.WriteLine("Read it with:  corpus");
            return 0;
        }

        /// <summary>
        /// Where SteamCMD unpacks one workshop item.
        ///
        /// The <c>workshop</c> segment is not optional and leaving it out is not harmless: the
        /// resume check then never finds anything already on disk, and a re-run downloads all ten
        /// thousand items again rather than none of them.
        /// </summary>
        private static string ItemPath(string output, long id)
        {
            return Path.Combine(output, "steamapps", "workshop", "content", "244850",
                id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Unpacks the legacy workshop format.
        ///
        /// An item published before Steam's current UGC system arrives as a single
        /// <c>*_legacy.bin</c>, which is a zip holding the <c>bp.sbc</c> and a thumbnail rather than
        /// the unpacked folder a newer item gives. Both shapes end up in the corpus, so the reader
        /// finds a blueprint either way and never has to know which era an item came from.
        /// </summary>
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
                            if (entry.Name != "bp.sbc") continue;

                            entry.ExtractToFile(Path.Combine(folder, "bp.sbc"), true);
                            unpacked++;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // A corpus of ten thousand will contain a truncated or unreadable archive. It
                    // is one ship, and the yield reports it rather than the pass failing.
                }
            }

            return unpacked;
        }

        private static bool RunSteamCmd(string steamcmd, string arguments)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(steamcmd, arguments)
                {
                    UseShellExecute = false,
                };

                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();

                    // SteamCMD reports a nonzero code for a batch in which any single item failed,
                    // which on a corpus this size is routine — an item is deleted or made private
                    // between listing and fetching. The corpus reader ignores what is not there.
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

        // ---- the manifest ----------------------------------------------------------------------

        /// <summary>
        /// The listing, checked in beside the corpus.
        ///
        /// It is what makes a corpus reproducible and what lets a later pass weight a ship by how
        /// many people actually use it — a design with fifty thousand subscribers is worth more to
        /// a balance decision than one with fifty.
        /// </summary>
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

        private static List<Item> Load(string path)
        {
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

        private static string Csv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.IndexOf(',') < 0 && text.IndexOf('"') < 0
                ? text
                : "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string Quote(string path)
        {
            return path.IndexOf(' ') < 0 ? path : "\"" + path + "\"";
        }

        private static string Value(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        private static bool Has(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name) return true;
            }
            return false;
        }

        private static int Int(string text, int fallback)
        {
            int value;
            return text != null && int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}
