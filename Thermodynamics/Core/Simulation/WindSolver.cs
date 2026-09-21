using System;
using VRageMath;

namespace Thermodynamics.Core
{
    public static class WindSolver
    {
        public struct Inputs
        {
            public float Ceiling;

            public Vector3 Up;

            public Vector3 Axis;

            public float WeatherIntensity;

            public float WeatherWind;

            public float Variation;

            public float HeightAboveGround;

            public float BurialDepth;

            public float Heating;

            public float Roughness;
            public float GradientHeight;
            public float DiurnalAmplitude;
            public float DiurnalCrossover;

            public float TerrainInfluence;

            public float TerrainRadius;

            public float SlopeStrength;

            public float[] Terrain;
        }

        public struct Result
        {
            public Vector3 Direction;

            public float Speed;

            public float BandShare;

            public float Profile;

            public float Burial;

            public float SpeedUp;

            public float Shelter;

            public float ChannelDegrees;

            public float SlopeSpeed;

            public int SlopeSense;

            public float Gradient;
        }

/// <summary>Burial operation.</summary>
        public static float Burial(float height, float depth)
        {
            if (height >= 0f) return 1f;
            if (depth <= 0f) return 0f;

            float share = 1f + (height / depth);

            if (share <= 0f) return 0f;
            return share > 1f ? 1f : share;
        }

/// <summary>Solve operation.</summary>
        public static Result Solve(ref Inputs inputs)
        {
/// <summary>Result operation.</summary>
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

            float share = WindField.Speed(
                inputs.Ceiling, inputs.WeatherIntensity, inputs.Variation, inputs.WeatherWind)
                * WindField.BandStrength(inputs.Up, inputs.Axis);

            result.BandShare = share / inputs.Ceiling;

            float height = inputs.HeightAboveGround;

/// <summary>Burial operation.</summary>
            result.Burial = Burial(height, inputs.BurialDepth);
            if (result.Burial <= 0f) return result;

            if (height < 0f) height = 0f;

            float profile = WindProfile.Multiplier(height, inputs.Roughness, inputs.GradientHeight)
                * WindProfile.Diurnal(
                    inputs.Heating, height,
                    inputs.DiurnalAmplitude, inputs.DiurnalCrossover, inputs.GradientHeight);

            result.Profile = profile;

            float speed = share * profile * result.Burial;

/// <summary>Terrain operation.</summary>
            float influence = Terrain(ref inputs, height);
            if (influence > 0f && inputs.Terrain != null)
            {
                Vector3 east = Vector3.Cross(inputs.Axis, inputs.Up);
                if (east.LengthSquared() > 1e-8f)
                {
                    east = Vector3.Normalize(east);
                    Vector3 north = Vector3.Normalize(Vector3.Cross(inputs.Up, east));

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

                    result.SpeedUp = 1f + ((speedUp - 1f) * influence);
                    result.Shelter = 1f + ((shelter - 1f) * influence);
                    if (result.SpeedUp < 0f) result.SpeedUp = 0f;
                    if (result.Shelter < 0f) result.Shelter = 0f;

                    speed *= result.SpeedUp * result.Shelter;

                    float gradient;
                    Vector3 downhill = WindTerrain.Downhill(
                        inputs.Terrain, inputs.TerrainRadius, north, east, out gradient);

                    result.Gradient = gradient;

                    Vector3 slope = WindSlope.Velocity(
                        downhill, gradient, inputs.Heating, height, speed,
                        inputs.SlopeStrength * influence * result.Burial);

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

                        if (total > inputs.Ceiling) total = inputs.Ceiling;

                        if (total > 1e-6f && combined.LengthSquared() > 1e-12f)
                        {
                            direction = Vector3.Normalize(combined);
                            speed = total;
                        }
                    }
                }
            }

            if (speed > inputs.Ceiling) speed = inputs.Ceiling;

            result.Direction = direction;
            result.Speed = speed;
            return result;
        }

/// <summary>Terrain operation.</summary>
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
