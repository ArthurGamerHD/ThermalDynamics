using System;
using System.Collections.Generic;

namespace Thermodynamics
{
    /// <summary>
    /// The world's own settings, as the report sees them: the mod's say what the model was asked to do
    /// and these say whether the game allowed it. Read out of the serialised session settings rather
    /// than a hand-written list, so a setting the game adds appears in the next dump.
    /// See telemetry.md, Output.
    /// </summary>
    public static class WorldSettings
    {
        /// <summary>Rows kept from one serialised settings block.</summary>
        public const int MaxRows = 512;

        /// <summary>Which of the mod's own features a conflict is judged against.</summary>
        public struct ModFeatures
        {
            public bool Damage;
            public bool RoomAir;
            public bool Persistence;
        }

        /// <summary>
        /// Flattens serialised settings into name/value rows, in document order.
        ///
        /// Leaf elements only. A container contributes its name as a prefix — <c>A.B</c> — so a
        /// nested block reads unambiguously against a flat one.
        /// </summary>
        public static List<KeyValuePair<string, string>> Parse(string xml)
        {
            List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(xml)) return rows;

            List<string> path = new List<string>();
            string open = null;
            int textStart = -1;
            int cursor = 0;

            while (rows.Count < MaxRows)
            {
                int start = xml.IndexOf('<', cursor);
                if (start < 0) break;

                int end = xml.IndexOf('>', start + 1);
                if (end < 0) break;

                string tag = xml.Substring(start + 1, end - start - 1);
                cursor = end + 1;

                // Declarations, comments and doctypes carry no settings.
                if (tag.Length == 0 || tag[0] == '?' || tag[0] == '!') continue;

                bool closing = tag[0] == '/';
                bool empty = tag[tag.Length - 1] == '/';
                string name = TagName(tag, closing);
                if (name.Length == 0) continue;

                if (empty)
                {
                    // A value the game left null or blank. Recorded rather than skipped: which
                    // settings a world does not carry is itself an answer.
                    Add(rows, path, path.Count, name, "");
                    open = null;
                    continue;
                }

                if (closing)
                {
                    if (open == name && textStart >= 0)
                    {
                        // The element itself sits on the path; only its parents prefix it.
                        Add(rows, path, path.Count - 1, name, Decode(xml.Substring(textStart, start - textStart)));
                    }

                    if (path.Count > 0) path.RemoveAt(path.Count - 1);
                    open = null;
                    continue;
                }

                path.Add(name);
                open = name;
                textStart = cursor;
            }

            return rows;
        }

        /// <summary>The value of a leaf by name, or null. Matches the last path segment.</summary>
        public static string Value(IList<KeyValuePair<string, string>> rows, string name)
        {
            if (rows == null) return null;

            for (int i = 0; i < rows.Count; i++)
            {
                string key = rows[i].Key;
                if (key == name) return rows[i].Value;

                int dot = key.LastIndexOf('.');
                if (dot >= 0 && string.CompareOrdinal(key, dot + 1, name, 0, name.Length) == 0
                    && key.Length - dot - 1 == name.Length)
                {
                    return rows[i].Value;
                }
            }

            return null;
        }

        public static bool Flag(IList<KeyValuePair<string, string>> rows, string name, bool missing)
        {
            string value = Value(rows, name);
            if (string.IsNullOrEmpty(value)) return missing;

            return value == "true" || value == "True" || value == "1";
        }

        /// <summary>
        /// World settings that silence something the mod was asked to do.
        ///
        /// Each entry names the mod feature, the world setting overriding it, and what the dump will
        /// therefore not show. A conflict is not a fault — the world is entitled to its settings —
        /// but reading a dump without knowing about one wastes the reading.
        /// </summary>
        public static List<string> Conflicts(IList<KeyValuePair<string, string>> rows, ModFeatures features)
        {
            List<string> conflicts = new List<string>();
            if (rows == null || rows.Count == 0) return conflicts;

            if (features.Damage && !Flag(rows, "DestructibleBlocks", true))
            {
                conflicts.Add("EnableDamage is on but the world has DestructibleBlocks off: "
                    + "overheating blocks are recorded as damaged and the engine discards it");
            }

            if (features.RoomAir && !Flag(rows, "EnableOxygen", true))
            {
                conflicts.Add("EnableRoomAir is on but the world has EnableOxygen off: "
                    + "rooms hold no air and never convect");
            }
            else if (features.RoomAir && !Flag(rows, "EnableOxygenPressurization", true))
            {
                conflicts.Add("EnableRoomAir is on but the world has EnableOxygenPressurization off: "
                    + "rooms hold no air and never convect");
            }

            if (features.Persistence && !Flag(rows, "EnableSaving", true))
            {
                conflicts.Add("the world has EnableSaving off: block temperatures are not persisted, "
                    + "so every load starts from ambient");
            }

            return conflicts;
        }

        private static void Add(
            List<KeyValuePair<string, string>> rows, List<string> path, int parents, string name, string value)
        {
            rows.Add(new KeyValuePair<string, string>(Join(path, parents, name), value.Trim()));
        }

        /// <summary>
        /// The dotted name of a leaf. The outermost element is the serialised type itself and says
        /// nothing about any one setting, so it is dropped.
        /// </summary>
        private static string Join(List<string> path, int parents, string name)
        {
            if (parents <= 1) return name;

            string joined = "";
            for (int i = 1; i < parents; i++)
            {
                joined += path[i] + ".";
            }

            return joined + name;
        }

        private static string TagName(string tag, bool closing)
        {
            int start = closing ? 1 : 0;
            int i = start;

            while (i < tag.Length && tag[i] != ' ' && tag[i] != '\t' && tag[i] != '/' && tag[i] != '\r' && tag[i] != '\n')
            {
                i++;
            }

            return tag.Substring(start, i - start);
        }

        private static string Decode(string value)
        {
            if (value.IndexOf('&') < 0) return value;

            return value
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&");
        }
    }
}
