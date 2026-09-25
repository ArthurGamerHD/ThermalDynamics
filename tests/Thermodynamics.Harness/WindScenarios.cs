using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class WindScenarios
    {
        public class Scenario
        {
            public string Name;
            public string Asks;
            public WindLab.Planet Planet;
            public WindLab.Options Options;

            public double[] Latitudes;

            public double[] Heights;
        }

        public static readonly double[] InterestingLatitudes =
        {
            -89d, -75d, -60d, -45d, -30d, -15d, -1d, 0d, 1d, 15d, 30d, 45d, 60d, 75d, 89d,
        };

        public static readonly double[] InterestingHeights =
        {
            0d, 0.5d, 2d, 10d, 40d, 80d, 160d, 400d, 600d, 1200d, 5000d, 20000d,
        };


        public static List<Scenario> All()
        {

            List<Scenario> list = new List<Scenario>();

            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
                string name = WindLab.Planet.VanillaNames[i];
                list.Add(new Scenario
                {
                    Name = "vanilla:" + name,
                    Asks = "the model on a shipped world at its usual size",
                    Planet = WindLab.Planet.Vanilla(name),
                    Options = new WindLab.Options(),
                    Latitudes = InterestingLatitudes,
                    Heights = InterestingHeights,
                });
            }

            double[] diameters = { 19000d, 60000d, 80000d, 120000d };
            for (int i = 0; i < diameters.Length; i++)
            {
                list.Add(new Scenario
                {
                    Name = "size:" + (diameters[i] / 1000d).ToString("n0") + "km",
                    Asks = "whether world size alone changes the answer",
                    Planet = WindLab.Planet.Vanilla("EarthLike", diameters[i]),
                    Options = new WindLab.Options(),
                    Latitudes = InterestingLatitudes,
                    Heights = InterestingHeights,
                });
            }

            list.Add(new Scenario
            {
                Name = "size:2km-moddedtiny",
                Asks = "a world so small the terrain ring wraps a measurable arc of it",
                Planet = WindLab.Planet.Vanilla("EarthLike", 2000d),
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            list.Add(new Scenario
            {
                Name = "size:1000km-moddedhuge",
                Asks = "a world large enough that the bands are continents again",
                Planet = WindLab.Planet.Vanilla("EarthLike", 1000000d),
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            list.Add(Tuned("settings:glass", "the smoothest ground the setting allows",
                o => { o.Roughness = 0.0001f; }));

            list.Add(Tuned("settings:forest", "the roughest",
                o => { o.Roughness = 2f; }));

            list.Add(Tuned("settings:shallow-layer", "a boundary layer barely above the reference height",
                o => { o.GradientHeight = 10f; }));

            list.Add(Tuned("settings:deep-layer", "a boundary layer taller than most SE atmospheres",
                o => { o.GradientHeight = 3000f; }));

            list.Add(Tuned("settings:no-diurnal", "the daily cycle switched off",
                o => { o.DiurnalAmplitude = 0f; }));

            list.Add(Tuned("settings:full-diurnal", "the daily cycle at its maximum",
                o => { o.DiurnalAmplitude = 1f; }));

            list.Add(Tuned("settings:no-terrain", "the ground ignored",
                o => { o.TerrainInfluence = 0f; }));

            list.Add(Tuned("settings:wide-terrain", "a terrain ring five kilometres across",
                o => { o.TerrainRadius = 5000f; }));

            list.Add(Tuned("settings:tight-terrain", "a terrain ring inside one landform",
                o => { o.TerrainRadius = 50f; }));

            list.Add(Tuned("settings:no-slope-wind", "the thermal slope flow switched off",
                o => { o.SlopeStrength = 0f; }));

            list.Add(Tuned("settings:storm", "the worst weather the game reports",
                o => { o.WeatherIntensity = 1f; o.WeatherWind = 2f; }));

            WindLab.Planet still = WindLab.Planet.Vanilla("EarthLike");
            still.MaxWindSpeed = 0f;
            list.Add(new Scenario
            {
                Name = "degenerate:no-wind-rating",
                Asks = "a planet whose definition says the wind never blows",
                Planet = still,
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            WindLab.Planet airless = WindLab.Planet.Vanilla("EarthLike");
            airless.HasAtmosphere = false;
            list.Add(new Scenario
            {
                Name = "degenerate:no-atmosphere",
                Asks = "an earthlike world with the air taken away",
                Planet = airless,
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            WindLab.Planet flat = WindLab.Planet.Vanilla("EarthLike");
            flat.Ground = new WindLab.FlatTerrain();
            list.Add(new Scenario
            {
                Name = "degenerate:flat-world",
                Asks = "ground with no shape, the control for every terrain factor",
                Planet = flat,
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            WindLab.Planet fast = WindLab.Planet.Vanilla("EarthLike");
            fast.DayLength = 240d;
            list.Add(new Scenario
            {
                Name = "degenerate:four-minute-day",
                Asks = "a day shorter than the climate lag it drives",
                Planet = fast,
                Options = new WindLab.Options(),
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            });

            return list;
        }


        private static Scenario Tuned(string name, string asks, Action<WindLab.Options> tune)
        {
            WindLab.Options options = new WindLab.Options();
            tune(options);

            return new Scenario
            {
                Name = name,
                Asks = asks,
                Planet = WindLab.Planet.Vanilla("EarthLike"),
                Options = options,
                Latitudes = InterestingLatitudes,
                Heights = InterestingHeights,
            };
        }

        public struct Outcome
        {
            public string Name;
            public string Asks;
            public int Samples;

            public double PlanetRadius;
            public double AtmosphereAltitude;
            public double MaxHill;
            public double BandWidthMetres;
            public double HorizonMetres;

            public float MinSpeed, MaxSpeed, MeanSpeed;
            public float MinSpeedUp, MaxSpeedUp;
            public float MinShelter;
            public float MaxChannelDegrees;
            public float MaxProfile;
            public int OverCeiling;
            public int Bad;

            public int OverFriction;

            public int OverFrictionNearGround;

            public float MaxSpeedNearGround;
        }

        public const float ParkedHeightMetres = 100f;

        public const float FrictionThreshold = 50f;


        public static Outcome Run(Scenario scenario)
        {
            WindLab.Options options = scenario.Options;
            if (scenario.Heights != null) options.Heights = scenario.Heights;

            List<WindLab.Row> rows = scenario.Latitudes != null

                ? RunAt(scenario.Planet, options, scenario.Latitudes)
                : WindLab.Run(scenario.Planet, options);


            Outcome outcome = new Outcome();
            outcome.Name = scenario.Name;
            outcome.Asks = scenario.Asks;
            outcome.Samples = rows.Count;

            outcome.PlanetRadius = scenario.Planet.AverageRadius;
            outcome.AtmosphereAltitude = scenario.Planet.AtmosphereAltitude;
            outcome.MaxHill = scenario.Planet.MaxHillHeight;
            outcome.BandWidthMetres = scenario.Planet.MetresPerDegree * 30d;
            outcome.HorizonMetres = scenario.Planet.HorizonFrom(2d);

            outcome.MinSpeed = float.MaxValue;
            outcome.MinSpeedUp = float.MaxValue;
            outcome.MinShelter = float.MaxValue;

            double total = 0d;
            float threshold = FrictionThreshold;

            for (int i = 0; i < rows.Count; i++)
            {
                WindLab.Row r = rows[i];

                if (float.IsNaN(r.Speed) || float.IsInfinity(r.Speed) || r.Speed < 0f) outcome.Bad++;
                if (float.IsNaN(r.SpeedUp) || float.IsNaN(r.Shelter)) outcome.Bad++;
                if (float.IsNaN(r.BearingDegrees) || float.IsInfinity(r.BearingDegrees)) outcome.Bad++;

                if (r.Speed < outcome.MinSpeed) outcome.MinSpeed = r.Speed;
                if (r.Speed > outcome.MaxSpeed) outcome.MaxSpeed = r.Speed;
                total += r.Speed;

                if (r.SpeedUp < outcome.MinSpeedUp) outcome.MinSpeedUp = r.SpeedUp;
                if (r.SpeedUp > outcome.MaxSpeedUp) outcome.MaxSpeedUp = r.SpeedUp;
                if (r.Shelter < outcome.MinShelter) outcome.MinShelter = r.Shelter;
                if (r.ChannelDegrees > outcome.MaxChannelDegrees) outcome.MaxChannelDegrees = r.ChannelDegrees;
                if (r.Profile > outcome.MaxProfile) outcome.MaxProfile = r.Profile;

                if (r.Speed > r.Ceiling + 1e-4f) outcome.OverCeiling++;
                if (r.Speed > threshold) outcome.OverFriction++;

                if (r.HeightAboveGround <= ParkedHeightMetres)
                {
                    if (r.Speed > outcome.MaxSpeedNearGround) outcome.MaxSpeedNearGround = r.Speed;
                    if (r.Speed > threshold) outcome.OverFrictionNearGround++;
                }
            }

            if (rows.Count == 0)
            {
                outcome.MinSpeed = 0f;
                outcome.MinSpeedUp = 0f;
                outcome.MinShelter = 0f;
            }
            else
            {
                outcome.MeanSpeed = (float)(total / rows.Count);
            }

            return outcome;
        }


        public static List<WindLab.Row> RunAt(
            WindLab.Planet planet, WindLab.Options options, double[] latitudes)
        {

            List<WindLab.Row> all = new List<WindLab.Row>();

            for (int i = 0; i < latitudes.Length; i++)
            {

                WindLab.Options one = Copy(options);
                one.LatitudeLimit = Math.Abs(latitudes[i]);

                one.LatitudeStep = one.LatitudeLimit > 0d ? one.LatitudeLimit * 2d : 1d;

                List<WindLab.Row> rows = WindLab.Run(planet, one);

                for (int r = 0; r < rows.Count; r++)
                {
                    if (Math.Abs(rows[r].Latitude - latitudes[i]) < 1e-6d) all.Add(rows[r]);
                }
            }

            return all;
        }


        private static WindLab.Options Copy(WindLab.Options options)
        {
            return new WindLab.Options
            {
                Roughness = options.Roughness,
                GradientHeight = options.GradientHeight,
                DiurnalAmplitude = options.DiurnalAmplitude,
                DiurnalCrossover = options.DiurnalCrossover,
                TerrainInfluence = options.TerrainInfluence,
                TerrainRadius = options.TerrainRadius,
                SlopeStrength = options.SlopeStrength,
                WeatherIntensity = options.WeatherIntensity,
                WeatherWind = options.WeatherWind,
                AmbientLagSeconds = options.AmbientLagSeconds,
                LatitudeLimit = options.LatitudeLimit,
                LatitudeStep = options.LatitudeStep,
                LongitudeStep = options.LongitudeStep,
                Heights = options.Heights,
                StepsPerDay = options.StepsPerDay,
            };
        }


        public static string Report()
        {

            List<Scenario> scenarios = All();

            StringBuilder sb = new StringBuilder();

            sb.Append("Wind model across every shipped world, every size and every corner\n\n");

            sb.Append("The worlds, as the engine derives them\n");
            sb.Append("  world            radius km   hills m        atmosphere m   band km   horizon m\n");

            for (int i = 0; i < WindLab.Planet.VanillaNames.Length; i++)
            {
                WindLab.Planet planet = WindLab.Planet.Vanilla(WindLab.Planet.VanillaNames[i]);

                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-14} {1,9:n1}   {2,6:n0}..{3,-7:n0} {4,12:n0}   {5,7:n1}   {6,9:n0}{7}\n",
                    planet.Name,
                    planet.AverageRadius / 1000d,
                    planet.MinHillHeight, planet.MaxHillHeight,
                    planet.AtmosphereAltitude,
                    planet.MetresPerDegree * 30d / 1000d,
                    planet.HorizonFrom(2d),
                    planet.PeaksAboveAir ? "   PEAKS IN VACUUM" : (planet.HasAtmosphere ? "" : "   AIRLESS")));
            }

            sb.Append("\n  Earth, for scale:  6,371.0        -11,000..8,849        ~100,000   3,336.0       5,048\n");

            sb.Append("\nScenarios\n");
            sb.Append("  name                         samples   speed m/s          speed-up      shelter  chan   >ceil  >fric  parked >fric  bad\n");

            for (int i = 0; i < scenarios.Count; i++)
            {

                Outcome o = Run(scenarios[i]);

                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-26} {1,8:n0}   {2,5:n1}..{3,-6:n1} ({4,4:n1})  {5,4:n2}..{6,-4:n2}  {7,7:n3}  {8,4:n0}  {9,6:n0} {10,6:n0} {11,6:n1} {12,5:n0} {13,4:n0}\n",
                    o.Name, o.Samples, o.MinSpeed, o.MaxSpeed, o.MeanSpeed,
                    o.MinSpeedUp, o.MaxSpeedUp, o.MinShelter, o.MaxChannelDegrees,
                    o.OverCeiling, o.OverFriction, o.MaxSpeedNearGround,
                    o.OverFrictionNearGround, o.Bad));
            }

            sb.Append("\n  >ceil  samples where the modelled wind exceeded the engine's own figure\n");
            sb.Append("  >fric  samples over the friction threshold at any height\n");
            sb.Append("  parked the fastest wind, and the samples over the threshold, within "
                + ParkedHeightMetres.ToString("n0", CultureInfo.InvariantCulture)
                + " m of the ground — where a grid can be standing still\n");
            sb.Append("  bad    NaN, infinite or negative results — must be zero everywhere\n");

            return sb.ToString();
        }
    }
}
