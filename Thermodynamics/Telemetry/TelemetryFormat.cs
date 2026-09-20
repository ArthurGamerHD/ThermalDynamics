using System.Globalization;
using System.Text;

namespace Thermodynamics
{
    /// <summary>
    /// Formatting for the report and the CSVs.
    ///
    /// Every number written to a file goes through here in the invariant culture. A client whose
    /// locale formats 1.5 as "1,5" would otherwise corrupt every CSV it wrote, since the decimal
    /// separator and the delimiter would be the same character.
    ///
    /// Free of any Space Engineers type, so it can be tested outside the game.
    /// </summary>
    public static class TelemetryFormat
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>Blank rather than "NaN" or "Infinity", neither of which a spreadsheet reads as a number.</summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return value.ToString("0.######", Invariant);
        }

        public static string Integer(long value)
        {
            return value.ToString(Invariant);
        }

        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Clips to a column width, marking the clip so a truncated name is not mistaken for a
        /// complete one. The result is never longer than <paramref name="length"/>.
        /// </summary>
        public static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            if (length < 1) return "";
            if (value.Length <= length) return value;
            return value.Substring(0, length - 1) + "~";
        }

        /// <summary>Formats a peak that was never set as absent rather than as -3.4e38.</summary>
        public static string Peak(float value)
        {
            return value == float.MinValue ? "-" : value.ToString("n1");
        }

        /// <summary>
        /// The label for one histogram bucket. Index <paramref name="edges"/>.Length is the
        /// overflow bucket above the last edge.
        /// </summary>
        public static string BucketLabel(float[] edges, int index, string unit)
        {
            if (edges == null || edges.Length == 0) return "all";

            if (index >= edges.Length)
            {
                return ">= " + edges[edges.Length - 1].ToString("0.###", Invariant) + unit;
            }

            string upper = edges[index].ToString("0.###", Invariant) + unit;

            return index == 0
                ? "< " + upper
                : edges[index - 1].ToString("0.###", Invariant) + unit + " - " + upper;
        }

        public static void AppendCsv(StringBuilder sb, string value)
        {
            sb.Append(Quote(value)).Append(',');
        }

        public static void AppendCsv(StringBuilder sb, long value)
        {
            sb.Append(Integer(value)).Append(',');
        }

        public static void AppendCsv(StringBuilder sb, double value)
        {
            sb.Append(Number(value)).Append(',');
        }

        public static void AppendCsvLast(StringBuilder sb, double value)
        {
            sb.Append(Number(value)).Append('\n');
        }

        public static void AppendCsvLast(StringBuilder sb, long value)
        {
            sb.Append(Integer(value)).Append('\n');
        }

        /// <summary>A quoted string as the last column of a row.</summary>
        public static void AppendCsvLast(StringBuilder sb, string value)
        {
            AppendCsv(sb, value);

            // AppendCsv appends a separator; the last column ends the line instead.
            sb.Length--;
            sb.Append('\n');
        }
    }
}
