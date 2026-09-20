using System;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>
    /// The whole wind model in one call, and the <b>only</b> place the parts are composed: the game
    /// and the offline simulator both run exactly this, so a difference between them is a difference
    /// in the world rather than in a second implementation. Every factor is reported beside the
    /// answer, each 1 when it has nothing to say. Pure. See environment.md, What the solver computes.
    /// </summary>
    public static class WindSolver
    {
        /// <summary>Everything the model needs to know about one point at one moment.</summary>
        public struct Inputs
        {
            /// <summary>
            /// The game's own figure here: <c>MaxWindSpeed × airDensity</c>. Used as the per-planet
            /// scale and nothing else — see docs/environment.md for why it cannot be used as a wind.
            /// </summary>
            public float Ceiling;

            /// <summary>Away from the planet's centre, unit.</summary>
            public Vector3 Up;

            /// <summary>The planet's own north, unit.</summary>
            public Vector3 Axis;

            /// <summary>Weather intensity here, 0..1.</summary>
            public float WeatherIntensity;

            /// <summary>The weather effect's own wind modifier, already faded in with its intensity.</summary>
            public float WeatherWind;

            /// <summary>The steady per-place variation, 0..1. See <see cref="WindField.Variation"/>.</summary>
            public float Variation;

            /// <summary>
            /// Metres above the ground, signed. Negative below the surface, which is where a grid
            /// digging itself in sits.
            /// </summary>
            public float HeightAboveGround;

            /// <summary>
            /// Metres of descent below the surface over which the wind dies. Faded over the grid's own
            /// size rather than switched at its centre, because a ship in the trench it has dug is
            /// under the surface at its centre and open to the sky at its deck. Zero cuts exactly.
            /// </summary>
            public float BurialDepth;

            /// <summary>Lagged share of the day's heating, 0..1. See <see cref="WindProfile.Heating"/>.</summary>
            public float Heating;

            public float Roughness;
            public float GradientHeight;
            public float DiurnalAmplitude;
            public float DiurnalCrossover;

            /// <summary>0..1. Zero leaves the wind ignorant of the ground.</summary>
            public float TerrainInfluence;

            /// <summary>Radius the terrain ring was read at, m.</summary>
            public float TerrainRadius;

            /// <summary>
            /// How much slope wind to add, 0..1 — the air that runs up a mountain by day and drains
            /// back down it at night. Zero leaves it out entirely.
            /// </summary>
            public float SlopeStrength;

            /// <summary>
            /// Ground heights around this point relative to its own, m, as
            /// <see cref="WindTerrain.SampleCount"/> entries. Null where terrain is not being read.
            /// </summary>
            public float[] Terrain;
        }

        /// <summary>The wind, and every factor that made it.</summary>
        public struct Result
        {
            /// <summary>Where the wind blows, world space, unit. Zero where there is no wind.</summary>
            public Vector3 Direction;

            /// <summary>How hard, m/s.</summary>
            public float Speed;

            /// <summary>Share of the ceiling the band and the weather were blowing, 0..1.</summary>
            public float BandShare;

            /// <summary>Vertical profile times time of day, against the reference height.</summary>
            public float Profile;

            /// <summary>How much of the wind survives being under the surface, 0..1.</summary>
            public float Burial;

            /// <summary>Terrain exposure: above 1 on a rise, below 1 in a hollow.</summary>
            public float SpeedUp;

            /// <summary>Terrain sheltering: 1 in the open, less behind an obstruction.</summary>
            public float Shelter;

            /// <summary>Degrees the ground turned the wind from the band's own bearing.</summary>
            public float ChannelDegrees;

            /// <summary>Speed the slope wind contributed, m/s. Zero on flat ground or in a real wind.</summary>
            public float SlopeSpeed;

            /// <summary>+1 where that flow runs uphill, −1 downhill, 0 where there is none.</summary>
            public int SlopeSense;

            /// <summary>The gradient of the ground here, as a fraction. Zero where it has no fall line.</summary>
            public float Gradient;
        }

        /// <summary>
        /// The share of the wind that reaches a grid whose centre is <paramref name="height"/> above
        /// the ground: all of it at or above the surface, none of it once the whole body is under.
        /// </summary>
        public static float Burial(float height, float depth)
        {
            if (height >= 0f) return 1f;
            if (depth <= 0f) return 0f;

            float share = 1f + (height / depth);

            if (share <= 0f) return 0f;
            return share > 1f ? 1f : share;
        }

        public static Result Solve(ref Inputs inputs)
        {
            Result result = new Result();
            result.BandShare = 0f;
            result.Profile = 1f;
            result.Burial = 1f;
            result.SpeedUp = 1f;
            result.Shelter = 1f;
            result.ChannelDegrees = 0f;
            result.SlopeSpeed = 0f;
            result.SlopeSense = 0;
            result.Gradient = 0f;
            result.Direction = Vector3.Zero;
            result.Speed = 0f;

            if (inputs.Ceiling <= 0f) return result;

            Vector3 direction = WindField.Direction(inputs.Up, inputs.Axis);
            if (direction.LengthSquared() < 1e-8f) return result;

            // The band's own strength is what makes the field continuous where the direction
            // reverses: a band edge is a calm, not a seam. See WindField.BandStrength.
            float share = WindField.Speed(
                inputs.Ceiling, inputs.WeatherIntensity, inputs.Variation, inputs.WeatherWind)
                * WindField.BandStrength(inputs.Up, inputs.Axis);

            result.BandShare = share / inputs.Ceiling;

            float height = inputs.HeightAboveGround;

            // Under the surface. There is no wind in rock, and a grid that has dug itself in is
            // between the two: it keeps whatever share of itself is still above ground.
            result.Burial = Burial(height, inputs.BurialDepth);
            if (result.Burial <= 0f) return result;

            if (height < 0f) height = 0f;

            float profile = WindProfile.Multiplier(height, inputs.Roughness, inputs.GradientHeight)
                * WindProfile.Diurnal(
                    inputs.Heating, height,
                    inputs.DiurnalAmplitude, inputs.DiurnalCrossover, inputs.GradientHeight);

            result.Profile = profile;

            float speed = share * profile * result.Burial;

            float influence = Terrain(ref inputs, height);
            if (influence > 0f && inputs.Terrain != null)
            {
                Vector3 east = Vector3.Cross(inputs.Axis, inputs.Up);
                if (east.LengthSquared() > 1e-8f)
                {
                    east = Vector3.Normalize(east);
                    Vector3 north = Vector3.Normalize(Vector3.Cross(inputs.Up, east));

                    // Steered first, then sheltered: what stands upwind depends on which way the
                    // wind ends up going, and in a valley that is the valley's answer.
                    Vector3 free = direction;
                    direction = WindTerrain.Channel(
                        inputs.Terrain, inputs.TerrainRadius, direction, north, east, influence);

                    float turn = Vector3.Dot(free, direction);
                    if (turn > 1f) turn = 1f;
                    if (turn < -1f) turn = -1f;
                    result.ChannelDegrees = (float)(Math.Acos(turn) * 180d / Math.PI);

                    float speedUp = WindTerrain.SpeedUp(
                        WindTerrain.Relief(inputs.Terrain, inputs.TerrainRadius));
                    float shelter = WindTerrain.Shelter(
                        inputs.Terrain, inputs.TerrainRadius * 0.5f, inputs.TerrainRadius,
                        direction, north, east);

                    // Faded toward no effect rather than switched off, so a climbing ship leaves a
                    // valley's influence smoothly instead of stepping out of it.
                    //
                    // Floored at nothing: an influence outside 0..1 — which no caller should send
                    // and the contract sweep does — fades *past* no effect and turns a shelter
                    // factor negative, and a negative factor is a wind blowing backwards at a
                    // negative speed. The slope term used to hide it by overwriting the speed with
                    // a length; where there is no slope wind it escaped.
                    result.SpeedUp = 1f + ((speedUp - 1f) * influence);
                    result.Shelter = 1f + ((shelter - 1f) * influence);
                    if (result.SpeedUp < 0f) result.SpeedUp = 0f;
                    if (result.Shelter < 0f) result.Shelter = 0f;

                    speed *= result.SpeedUp * result.Shelter;

                    // The thermal half of what terrain does. Everything above is what the ground did
                    // to a wind that was already blowing; this is a wind the ground makes itself, so
                    // it is added as a velocity rather than multiplied in — it has its own direction
                    // and it blows on a day when nothing else does.
                    float gradient;
                    Vector3 downhill = WindTerrain.Downhill(
                        inputs.Terrain, inputs.TerrainRadius, north, east, out gradient);

                    result.Gradient = gradient;

                    Vector3 slope = WindSlope.Velocity(
                        downhill, gradient, inputs.Heating, height, speed,
                        inputs.SlopeStrength * influence * result.Burial);

                    // Bounded by the air there is. A drainage flow is a few metres a second of real
                    // air moving downhill, and near the top of an atmosphere — or on a world whose
                    // wind rating is small to begin with — the planet's own figure is already less
                    // than that. Without this, a slope wind is the one term that can blow in a
                    // near-vacuum, which is both wrong and a fresh way through the ceiling.
                    float slopeSpeed = slope.Length();
                    if (slopeSpeed > inputs.Ceiling)
                    {
                        slope *= inputs.Ceiling / slopeSpeed;
                    }

                    if (slope.LengthSquared() > 1e-8f)
                    {
                        result.SlopeSpeed = slope.Length();
                        result.SlopeSense = WindSlope.Sense(inputs.Heating, height);

                        Vector3 combined = (direction * speed) + slope;
                        float total = combined.Length();

                        // Slope wind may turn the wind freely but must not be a new way through the
                        // planet's own figure. The ceiling bounds the whole composed speed now, so
                        // this is the same bound the last line of this method applies rather than a
                        // second one — kept here because the slope term replaces the speed as well
                        // as the direction, and a total that is clamped afterwards would have been
                        // normalised out of a vector that was not.
                        if (total > inputs.Ceiling) total = inputs.Ceiling;

                        if (total > 1e-6f && combined.LengthSquared() > 1e-12f)
                        {
                            direction = Vector3.Normalize(combined);
                            speed = total;
                        }
                    }
                }
            }

            // **The engine's figure is a ceiling, and this is what makes that true.** It was
            // adopted for exactly one property — that it bounds the wind — and the composed model
            // had grown past it: a storm reached 187 m/s against a planet rating of 80, and 143 of
            // that within a hundred metres of the ground, where a grid can be standing still. Each
            // factor is reasonable alone, which is why no unit test could see it. Every one of them
            // still does its work below the bound; ordinary weather never comes near it.
            // See backlog B17, B18.
            if (speed > inputs.Ceiling) speed = inputs.Ceiling;

            result.Direction = direction;
            result.Speed = speed;
            return result;
        }

        /// <summary>
        /// How much say the ground gets here: the configured influence, faded out with height.
        ///
        /// Speed-up, shelter and channelling are surface-layer effects. A ship two kilometres up is
        /// in air that has forgotten what the ground under it looks like, and steering it along a
        /// valley it is nowhere near would be worse than not modelling terrain at all.
        /// </summary>
        private static float Terrain(ref Inputs inputs, float height)
        {
            float influence = inputs.TerrainInfluence;
            if (influence <= 0f || inputs.TerrainRadius <= 0f) return 0f;

            float reach = inputs.GradientHeight;
            if (reach <= 0f) return 0f;

            float weight = 1f - (height / reach);
            if (weight <= 0f) return 0f;
            if (weight > 1f) weight = 1f;

            return influence * weight;
        }
    }
}
