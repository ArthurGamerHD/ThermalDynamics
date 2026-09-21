using System;

namespace Thermodynamics
{
    /// <summary>Runs every teardown step even when an earlier step fails; reports each failure to the caller.</summary>
    public static class SessionCleanup
    {
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
