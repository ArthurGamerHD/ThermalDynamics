using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Thermodynamics.Core;
using Thermodynamics.Harness;

namespace Thermodynamics.Tests
{
    public class OptimisedBuildTests
    {

        private static bool OptimiserDisabled(Assembly assembly)
        {
            DebuggableAttribute debuggable = assembly
                .GetCustomAttributes(typeof(DebuggableAttribute), false)
                .Cast<DebuggableAttribute>()
                .FirstOrDefault();

            return debuggable != null && debuggable.IsJITOptimizerDisabled;
        }

        [Theory]
        [InlineData(typeof(ThermalSolver))]
        [InlineData(typeof(PerformanceReport))]
        [InlineData(typeof(OptimisedBuildTests))]

        public void EveryAssemblyTheHarnessTimesIsJitOptimised(System.Type inside)
        {
            Assembly assembly = inside.Assembly;
            Assert.False(OptimiserDisabled(assembly),
                assembly.GetName().Name + " was built with the JIT optimiser disabled, so every"
                + " millisecond it reports is of code the game never runs");
        }
    }
}
