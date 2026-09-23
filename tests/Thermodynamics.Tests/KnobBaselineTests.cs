using System;
using System.Collections.Generic;
using System.Reflection;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    public class KnobBaselineTests
    {
        private readonly ITestOutputHelper output;


        public KnobBaselineTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]

        public void EveryWorldKnobsShippedValueIsTheShippedValue()
        {
            List<KnobLab.Knob> knobs = KnobLab.Knobs();
            Assert.True(knobs.Count > 5, "only " + knobs.Count + " knobs, so this checked nothing");


            List<string> wrong = new List<string>();
            int checkedKnobs = 0;

            foreach (KnobLab.Knob knob in knobs)
            {
                if (knob.World == null) continue;

                checkedKnobs++;


                ThermalSettings applied = new ThermalSettings();
                knob.World(applied, knob.Shipped);


                ThermalSettings untouched = new ThermalSettings();

                foreach (string field in Differences(untouched, applied))
                {
                    wrong.Add(knob.Name + ": applying its shipped " + knob.Shipped
                        + " moves " + field);
                }
            }

            output.WriteLine("{0} of {1} knobs act on the world", checkedKnobs, knobs.Count);

            Assert.True(checkedKnobs > 3,
                "only " + checkedKnobs + " world knobs were found, so this test is reading the "
                + "wrong thing and would pass whatever a knob claimed");

            wrong.Sort(StringComparer.Ordinal);
            Assert.True(wrong.Count == 0,
                "a knob's shipped value is not what ships, so its sweep is read against a level "
                + "nobody runs:\n  " + string.Join("\n  ", wrong.ToArray()));
        }


        private static List<string> Differences(ThermalSettings before, ThermalSettings after)
        {

            List<string> moved = new List<string>();

            foreach (FieldInfo field in typeof(ThermalSettings).GetFields(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                object a = field.GetValue(before);
                object b = field.GetValue(after);

                if (Equals(a, b)) continue;

                moved.Add(field.Name + " from " + a + " to " + b);
            }

            return moved;
        }
    }
}
