using System.Globalization;
using System.Text;

namespace Thermodynamics
{
    public static class TelemetryFormat
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;


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


        public static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            if (length < 1) return "";
            if (value.Length <= length) return value;
            return value.Substring(0, length - 1) + "~";
        }


        public static string Peak(float value)
        {
            return value == float.MinValue ? "-" : value.ToString("n1");
        }


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


        public static void AppendCsvLast(StringBuilder sb, string value)
        {
            AppendCsv(sb, value);

            sb.Length--;
            sb.Append('\n');
        }
    }
}
