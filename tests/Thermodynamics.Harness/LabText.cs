namespace Thermodynamics.Harness
{
    /// <summary>
    /// Text helpers for the labs' fixed-width console reports. Three labs carried an identical
    /// column trim; one statement, so a report convention cannot drift per lab.
    /// </summary>
    public static class LabText
    {
        /// <summary>One line, at most this wide, ellipsised — a report column's cell.</summary>
        public static string Trim(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Trim();
            return text.Length <= width ? text : text.Substring(0, width - 1) + "…";
        }
    }
}
