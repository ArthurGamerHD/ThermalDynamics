namespace Thermodynamics.Sim
{
    /// <summary>
    /// The command line, read one way. The runner and the fetcher each declared an identical
    /// pair of these under different names — the same shape the corpus tools' python `flag()`
    /// went through, caught before it drifted rather than after. Null for an absent or dangling
    /// value flag, so a mistyped command gets the caller's fallback rather than a crash.
    /// </summary>
    public static class Cli
    {
        /// <summary>The token after a value flag, or null when absent or dangling.</summary>
        public static string Value(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag) return args[i + 1];
            }
            return null;
        }

        /// <summary>Whether a bare flag is present, for options that take no value.</summary>
        public static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == flag) return true;
            }
            return false;
        }
    }
}
