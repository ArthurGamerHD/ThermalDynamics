using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// A ship digging straight down, from the surface to the planet's core.
    ///
    /// Everything about a grid's environment changes on the way down and each part changes at its
    /// own depth: the sun goes at the surface, the wind goes over the hull's own height, the day's
    /// swing damps out over tens of metres of rock, and the rock itself only starts warming below
    /// the sea-level deadzone. A single descent puts all four on one axis, which is the only way to
    /// see that each hands over to the next rather than one of them swallowing the others.
    ///
    /// The wind is solved rather than prescribed, since the question the descent exists to answer —
    /// whether a buried ship is still in a gale — is a question about the wind model.
    /// </summary>
    public static class Descent
    {
        /// <summary>One reading, at one depth.</summary>
        public struct Reading
        {
            /// <summary>Metres of the hull's centre above the surface. Negative once digging.</summary>
            public double Height;

            /// <summary>Metres below the surface, the way the model reports it.</summary>
            public float Depth;

            public float AmbientKelvin;
            public float WindSpeed;
            public float WindBurial;
            public float SolarWatts;
            public float SolarOcclusion;
            public float ConvectionCoefficient;

            /// <summary>What the game's own underground flag would say here.</summary>
            public bool GameUnderground;
        }

        /// <summary>Half the hull's extent, metres: how far it sinks before it is wholly buried.</summary>
        public const float HullReach = 5f;

        /// <summary>Wind at the reference height on the stand-in planet, m/s.</summary>
        public const float Ceiling = 80f;

        /// <summary>The planet's north, and a point at mid latitude on it.</summary>
        public static readonly Vector3 Axis = Vector3.Up;

        public static readonly Vector3 Up = Vector3.Normalize(new Vector3(1f, 1f, 0f));

        /// <summary>
        /// Heights to read at, metres above the surface: a hover, the dig, and then the shaft down
        /// to the core. Negative is below the surface.
        /// </summary>
        public static double[] Heights()
        {
            List<double> heights = new List<double>();

            for (double h = 40; h > 0; h -= 10) heights.Add(h);
            for (double h = 0; h >= -12; h -= 1) heights.Add(h);

            double[] deep = { -20, -50, -100, -200, -500, -1000, -2000, -5000, -10000, -20000, -40000, -59000 };
            heights.AddRange(deep);

            return heights.ToArray();
        }

        /// <summary>
        /// Reads the environment at every height, at midday, on an earthlike ball.
        ///
        /// Assembled exactly as the game adapter assembles it — signed height above ground, the
        /// hull's own reach as the burial depth — so what this measures is the model rather than a
        /// second copy of it.
        /// </summary>
        public static List<Reading> Run(ThermalSettings settings = null, PlanetThermalProperties planet = null)
        {
            if (settings == null) settings = new ThermalSettings();
            if (planet == null) planet = new PlanetThermalProperties();

            List<Reading> readings = new List<Reading>();
            double[] heights = Heights();

            for (int i = 0; i < heights.Length; i++)
            {
                double height = heights[i];

                EnvironmentSample sample = Worlds.PlanetSurface(1f, 0.5f);

                // Mid-latitude, sun overhead. The wind field is a circulation around the planet's
                // axis, so a point on the axis itself has no bearing for the wind to blow along.
                sample.UpDirection = Up;
                sample.SunDirection = Up;
                sample.SunDirectionLocal = Up;
                sample.LatitudeSine = Vector3.Dot(Up, Axis);
                sample.IsSolarOccluded = false;

                sample.Radius = (float)(Worlds.EarthlikeRadius + height);
                sample.Altitude = (float)height;
                sample.Depth = height < 0 ? (float)-height : 0f;

                // What the game would report. It answers for a point, so it turns true the moment
                // the hull's centre is under the surface — while the deck is still open to the sky.
                sample.IsUnderground = height < 0;

                WindSolver.Inputs inputs = new WindSolver.Inputs();
                inputs.Ceiling = Ceiling;
                inputs.Up = Up;
                inputs.Axis = Axis;
                inputs.Variation = 1f;
                inputs.WeatherWind = 1f;
                inputs.HeightAboveGround = (float)height;
                inputs.BurialDepth = HullReach;
                inputs.Heating = 1f;
                // The shipped wind figures. They live in the mod's settings file rather than in the
                // core's, so they are named here as the numbers a fresh install runs with.
                inputs.Roughness = 0.03f;
                inputs.GradientHeight = 600f;
                inputs.DiurnalAmplitude = 0.35f;
                inputs.DiurnalCrossover = 80f;

                WindSolver.Result wind = WindSolver.Solve(ref inputs);

                sample.WindSpeed = wind.Speed;
                sample.RelativeWindSpeed = wind.Speed;
                sample.RelativeWindDirectionLocal = wind.Speed > 0f ? Vector3.Forward : Vector3.Zero;

                EnvironmentState state = EnvironmentSolver.Solve(settings, planet, sample);

                Reading reading = new Reading();
                reading.Height = height;
                reading.Depth = sample.Depth;
                reading.AmbientKelvin = state.AmbientTemperature;
                reading.WindSpeed = wind.Speed;
                reading.WindBurial = wind.Burial;
                reading.SolarWatts = state.SolarEnergy;
                reading.SolarOcclusion = state.SolarOcclusion;
                reading.ConvectionCoefficient = state.EffectiveConvectionCoefficient;
                reading.GameUnderground = sample.IsUnderground;

                readings.Add(reading);
            }

            return readings;
        }

        public static string Csv(List<Reading> readings)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("height_m,depth_m,ambient_k,ambient_c,wind_speed,wind_burial,solar_w,solar_occlusion,");
            sb.Append("convection_coeff,game_underground\n");

            for (int i = 0; i < readings.Count; i++)
            {
                Reading r = readings[i];

                sb.Append(r.Height.ToString("0.###")).Append(',');
                sb.Append(r.Depth.ToString("0.###")).Append(',');
                sb.Append(r.AmbientKelvin.ToString("0.###")).Append(',');
                sb.Append(ThermalConstants.KelvinToCelsius(r.AmbientKelvin).ToString("0.###")).Append(',');
                sb.Append(r.WindSpeed.ToString("0.###")).Append(',');
                sb.Append(r.WindBurial.ToString("0.###")).Append(',');
                sb.Append(r.SolarWatts.ToString("0.###")).Append(',');
                sb.Append(r.SolarOcclusion.ToString("0.###")).Append(',');
                sb.Append(r.ConvectionCoefficient.ToString("0.###")).Append(',');
                sb.Append(r.GameUnderground ? 1 : 0).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>The reading nearest a given height, for a caller asking about one depth.</summary>
        public static Reading At(List<Reading> readings, double height)
        {
            Reading best = readings[0];
            double gap = double.MaxValue;

            for (int i = 0; i < readings.Count; i++)
            {
                double distance = Math.Abs(readings[i].Height - height);
                if (distance >= gap) continue;

                gap = distance;
                best = readings[i];
            }

            return best;
        }
    }
}
