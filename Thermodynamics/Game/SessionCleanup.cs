using System;

namespace Thermodynamics
{
    public static class SessionCleanup
    {
/// <summary>Run operation.</summary>
        public static void Run(Action[] steps, Action<int, Exception> report)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                try
                {
                    steps[i]();
                }
                catch (Exception error)
                {
                    report(i, error);
                }
            }
        }
    }
}
