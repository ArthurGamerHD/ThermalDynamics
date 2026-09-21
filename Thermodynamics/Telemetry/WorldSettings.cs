using System;
using System.Collections.Generic;

namespace Thermodynamics
{
    public static class WorldSettings
    {
        public const int MaxRows = 512;

        public struct ModFeatures
        {
            public bool Damage;
            public bool RoomAir;
            public bool Persistence;
        }

/// <summary>Parse operation.</summary>
        public static List<KeyValuePair<string, string>> Parse(string xml)
        {
            List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(xml)) return rows;

/// <summary>List operation.</summary>
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

                if (tag.Length == 0 || tag[0] == '?' || tag[0] == '!') continue;

                bool closing = tag[0] == '/';
                bool empty = tag[tag.Length - 1] == '/';
/// <summary>TagName operation.</summary>
                string name = TagName(tag, closing);
                if (name.Length == 0) continue;

                if (empty)
                {
                    Add(rows, path, path.Count, name, "");
                    open = null;
                    continue;
                }

                if (closing)
                {
                    if (open == name && textStart >= 0)
                    {
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

/// <summary>Value operation.</summary>
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

/// <summary>Flag operation.</summary>
        public static bool Flag(IList<KeyValuePair<string, string>> rows, string name, bool missing)
        {
/// <summary>Value operation.</summary>
            string value = Value(rows, name);
            if (string.IsNullOrEmpty(value)) return missing;

            return value == "true" || value == "True" || value == "1";
        }

/// <summary>Conflicts operation.</summary>
        public static List<string> Conflicts(IList<KeyValuePair<string, string>> rows, ModFeatures features)
        {
/// <summary>List operation.</summary>
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

/// <summary>Adds a .</summary>
        private static void Add(
            List<KeyValuePair<string, string>> rows, List<string> path, int parents, string name, string value)
        {
            rows.Add(new KeyValuePair<string, string>(Join(path, parents, name), value.Trim()));
        }

/// <summary>Join operation.</summary>
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

/// <summary>TagName operation.</summary>
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

/// <summary>Decode operation.</summary>
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
