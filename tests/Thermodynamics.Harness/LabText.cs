namespace Thermodynamics.Harness
{
    public static class LabText
    {
/// <summary>Trim operation.</summary>
        public static string Trim(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Trim();
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
