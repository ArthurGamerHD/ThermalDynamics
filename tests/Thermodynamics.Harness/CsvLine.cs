using System.Collections.Generic;
using System.Text;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// Splitting one CSV line, honouring quoted fields and the <c>""</c> escape the writers
    /// produce.
    ///
    /// Three readers carried this and one had drifted: `ShipSet` and `PerformanceReport` handled
    /// the escaped quote, and `BlockTriageLab`'s naive quote-toggle silently dropped it — so a
    /// ship name carrying a quote parsed differently depending on which lab read it, which is
    /// `D3`'s one-format-two-parsers drift with three. The escaping this honours is what
    /// `CorpusRecord.Text` and the performance report's own quoting write, and the round trip is
    /// pinned beside the writer.
    /// </summary>
    public static class CsvLine
    {
        /// <summary>
        /// A text field as the writers quote it: always quoted, quotes doubled, null as an empty
        /// quoted field. The escape <see cref="Split"/> honours, declared beside it — six sites
        /// stated this idiom independently before it lived here, which is the writer half of the
        /// drift the reader half already had.
        /// </summary>
        public static string Text(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static List<string> Split(string line)
        {
            List<string> fields = new List<string>();
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
