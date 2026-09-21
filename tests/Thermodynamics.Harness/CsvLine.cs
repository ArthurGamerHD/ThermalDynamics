using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    public static class CsvLine
    {
/// <summary>Text operation.</summary>
        public static string Text(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

/// <summary>Split operation.</summary>
        public static List<string> Split(string line)
        {
/// <summary>List operation.</summary>
            List<string> fields = new List<string>();
/// <summary>StringBuilder operation.</summary>
            StringBuilder current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (quoted)
                {
                    if (c != '"') { current.Append(c); continue; }

                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; continue; }
                    quoted = false;
                    continue;
                }

                if (c == '"') { quoted = true; continue; }
                if (c == ',') { fields.Add(current.ToString()); current.Length = 0; continue; }
                current.Append(c);
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
