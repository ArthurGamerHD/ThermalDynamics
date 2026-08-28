using System;
using System.Collections.Generic;
using System.Reflection;
using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;
using Xunit.Abstractions;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// **Every number describing a planet reaches the environment the solver is handed.**
    ///
    /// <para>
    /// <see cref="LoopDialReachTests"/> makes this check for the coolant and found a dial that was
    /// authored, documented, exposed on a slider and multiplied by nothing.
    /// <see cref="SettingsDialReachTests"/> makes it for the world settings. The planet's fourteen
    /// are the third group and had neither: they are parsed from `ThermalPlanetProperties`, carried
    /// through <c>Settings</c>, listed in the in-game menu and clamped, and nothing asked whether
    /// the model ever read them. `C33` counts them as the largest unswept subsystem.
    /// </para>
    ///
    /// <para>
    /// **The reading is the environment rather than a temperature.**
    /// <see cref="EnvironmentSolver.Solve"/> is where every one of these lands — it takes the planet
    /// and a sample and produces the state a step integrates against — so a fingerprint of that
    /// state over a spread of places is both the cheapest instrument and the complete one. A dial
    /// that moved a temperature without moving the state would be a dial reaching the solver by
    /// some path other than the one it is documented to take.
    /// </para>
    ///
    /// <para>
    /// **It is a reach test.** It asserts a field changes what the environment solve produces and
    /// says nothing about the direction or the size, which is what lets it survive a retune
    /// untouched. `C7` is where the planet numbers are argued, and it says they are opinions rather
    /// than fits; this says only that the opinions are connected.
    /// </para>
    /// </summary>
    public class PlanetDialReachTests
    {
        private readonly ITestOutputHelper output;

        public PlanetDialReachTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static ThermalSettings Settings()
        {
            return new ThermalSettings().Derive();
        }

        /// <summary>
        /// The places a planet dial can act, and every one of them is needed by at least one field:
        /// day and night for the two ambients, a pole for the drop, altitude for the lapse rate,
        /// two depths for the underground pair and the deadzone, sun and shadow for the decay, and
        /// weather for the response. A dial invisible in all of them is invisible.
        /// </summary>
        private static List<EnvironmentSample> Places()
        {
            List<EnvironmentSample> places = new List<EnvironmentSample>();

            places.Add(Worlds.PlanetSurface(1f, 0.5f));
            places.Add(Worlds.PlanetSurface(1f, 0f));
            places.Add(Worlds.PlanetSurface(0.3f, 0.25f, 30f));
            places.Add(Worlds.Storm(1f, 120f));
            places.Add(Worlds.Flight(1f, 300f));

            // A pole, which is the only place PoleTemperatureDrop is anything.
            EnvironmentSample pole = Worlds.PlanetSurface(1f, 0.5f);
            pole.LatitudeSine = 0.98f;
            places.Add(pole);

            // High in the air, for the lapse rate, and a long way down for the underground pair.
            EnvironmentSample high = Worlds.PlanetSurface(0.4f, 0.5f);
            high.Altitude = 8000f;
            places.Add(high);

            // **Shallow, because the damping depth is 20 m and clamps at one.** At a hundred metres
            // the day has died out completely whatever the depth is set to, so the deep places
            // below report `UndergroundDampingDepth` inert however far they are moved.
            places.Add(Worlds.Underground(1f, 10f));
            places.Add(Worlds.Underground(1f, 100f));
            places.Add(Worlds.Underground(1f, 40000f));

            // **A morning that is still warming.** The ambient lag is a first-order filter on the
            // target, so it does nothing at all unless the sample carries where ambient was and how
            // long ago — a place with no previous ambient starts at the target by definition, and
            // that is every other place here.
            //
            // A short day and a long one, because the lag is a share of the day as well as a
            // duration and one of the two is invisible unless the day length moves.
            places.Add(Warming(600f));
            places.Add(Warming(200000f));

            // **And a world whose rotation the mod could not read**, which is the only place the
            // absolute lag is used: `LagSecondsFor` is a share of the day where a day is known and
            // the authored seconds where it is not, so a rig that always knows the day length
            // reports the fallback inert. `ThermalGridEnvironment` passes zero for exactly this.
            places.Add(Warming(0f));

            return places;
        }

        /// <summary>Mid-morning on a planet whose day is this long, with ambient still catching up.</summary>
        private static EnvironmentSample Warming(float dayLengthSeconds)
        {
            EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.3f);

            sample.DayLengthSeconds = dayLengthSeconds;
            sample.HasPreviousAmbient = true;
            sample.PreviousAmbient = 240f;
            sample.SecondsSincePrevious = 30f;

            return sample;
        }

        /// <summary>What the environment solve produced, everything the state exposes.</summary>
        private static void Read(EnvironmentState state, List<float> into)
        {
            into.Add(state.AmbientTemperature);
            into.Add(state.AmbientTemperaturePow4);
            into.Add(state.AirDensity);
            into.Add(state.AtmosphereFactor);
            into.Add(state.ConvectionCoefficient);
            into.Add(state.EffectiveConvectionCoefficient);
            into.Add(state.SolarEnergy);
            into.Add(state.IsSolarOccluded ? 1f : 0f);
            into.Add(state.SolarOcclusion);
            into.Add(state.WindSpeed);
            into.Add(state.FrictionActive ? 1f : 0f);
            into.Add(state.WeatherIntensity);
            into.Add(state.WeatherTemperatureOffset);
        }

        private static List<float> Fingerprint(PlanetThermalProperties planet)
        {
            List<float> readings = new List<float>();
            ThermalSettings settings = Settings();

            List<EnvironmentSample> places = Places();
            for (int i = 0; i < places.Count; i++)
            {
                Read(EnvironmentSolver.Solve(settings, planet, places[i]), readings);
            }

            return readings;
        }

        /// <summary>
        /// The levels a field is tried at, both ways where its value allows it. A temperature is
        /// moved by a fixed span rather than scaled, because quartering a kelvin figure lands
        /// somewhere the clamps may have an opinion about.
        /// </summary>
        private static float[] Levels(string name, float shipped)
        {
            if (name.EndsWith("Temperature", StringComparison.Ordinal))
            {
                return new float[] { shipped - 60f, shipped + 60f };
            }

            if (shipped == 0f) return new float[] { 1f, 10f };
            return new float[] { shipped * 0.25f, shipped * 4f };
        }

        private static bool Same(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                float scale = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
                float tolerance = scale < 1f ? 1e-5f : scale * 1e-6f;
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            }

            return true;
        }

        /// <summary>
        /// **Every float on <see cref="PlanetThermalProperties"/> changes the environment the solver
        /// is handed.** Enumerated rather than listed, so a field added after this is checked
        /// without anyone remembering the file exists.
        /// </summary>
        [Fact]
        public void EveryPlanetDialReachesTheEnvironment()
        {
            List<FieldInfo> fields = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(PlanetThermalProperties)
                .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(float)) fields.Add(field);
            }

            Assert.True(fields.Count >= 12,
                "only " + fields.Count + " planet dials were found, so this test would pass on a"
                + " definition that had lost most of them");

            List<float> shipped = Fingerprint(PlanetThermalProperties.Default());

            List<string> inert = new List<string>();

            foreach (FieldInfo field in fields)
            {
                float value = (float)field.GetValue(PlanetThermalProperties.Default());
                bool moved = false;
                string at = "";

                foreach (float level in Levels(field.Name, value))
                {
                    PlanetThermalProperties planet = PlanetThermalProperties.Default();
                    field.SetValue(planet, level);

                    if (Same(shipped, Fingerprint(planet))) continue;

                    moved = true;
                    at = level.ToString("n3");
                    break;
                }

                if (!moved) inert.Add(field.Name);

                output.WriteLine("{0,-34} {1}", field.Name,
                    moved ? "reaches, at " + at : "REACHES NOTHING");
            }

            output.WriteLine("{0} planet dials over {1} places", fields.Count, Places().Count);

            Assert.True(inert.Count == 0,
                "planet dials that changed nothing the environment solve produces:\n  "
                + string.Join("\n  ", inert)
                + "\nEither the dial is wired to nothing — which is the defect this exists for —"
                + " or no place in Places() can see it, and a place is cheap to add.");
        }
    }
}
