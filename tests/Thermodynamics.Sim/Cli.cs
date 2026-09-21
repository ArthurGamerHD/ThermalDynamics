namespace Thermodynamics.Sim
{
    public static class Cli
    {
/// <summary>Value operation.</summary>
        public static string Value(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag) return args[i + 1];
            }
            return null;
        }

/// <summary>Has operation.</summary>
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
