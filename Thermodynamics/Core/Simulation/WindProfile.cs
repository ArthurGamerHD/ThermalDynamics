using System;

namespace Thermodynamics.Core
{
    public static class WindProfile
    {
        public const float ReferenceHeight = 10f;

        public const float MinimumHeight = 0.5f;

/// <summary>GradientHeightIn operation.</summary>
        public static float GradientHeightIn(float configured, float airAboveGround)
        {
            if (airAboveGround <= 0f) return configured;
            if (airAboveGround >= configured) return configured;

            return airAboveGround < ReferenceHeight ? ReferenceHeight : airAboveGround;
        }

/// <summary>Multiplier operation.</summary>
        public static float Multiplier(float height, float roughness, float gradientHeight)
        {
            if (roughness <= 0f) roughness = 0.0002f;
            if (gradientHeight < ReferenceHeight) gradientHeight = ReferenceHeight;

            if (height < MinimumHeight) height = MinimumHeight;
            if (height > gradientHeight) height = gradientHeight;

            double reference = Math.Log((ReferenceHeight + roughness) / roughness);
            if (reference <= 0d) return 1f;

            return (float)(Math.Log((height + roughness) / roughness) / reference);
        }

/// <summary>Heating operation.</summary>
        public static float Heating(
            float previous, float sunElevationSine, float seconds, float lagSeconds)
        {
            float target = sunElevationSine <= 0f ? 0f : sunElevationSine;
            if (target > 1f) target = 1f;

            if (previous < 0f) return target;

            return ClimateModel.Follow(previous, target, seconds, lagSeconds);
        }

/// <summary>Diurnal operation.</summary>
        public static float Diurnal(
            float heating, float height, float amplitude, float crossover, float boundary)
        {
            if (amplitude <= 0f) return 1f;
            if (amplitude > 1f) amplitude = 1f;
            if (crossover <= 0f) return 1f;

            if (heating < 0f) heating = 0f;
            if (heating > 1f) heating = 1f;
            if (height < 0f) height = 0f;

            float side = 1f - (height / crossover);
            if (side < -1f) side = -1f;

            float swing = (2f * heating) - 1f;

            float reach = 1f;
            if (boundary > 0f && height > boundary)
            {
                reach = 2f - (height / boundary);
                if (reach < 0f) reach = 0f;
            }

            float factor = 1f + (amplitude * side * swing * reach);
            return factor < 0f ? 0f : factor;
        }
    }
}
