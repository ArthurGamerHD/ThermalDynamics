﻿using System;

namespace RichHudFramework
{
    public static class MathExtensions
    {
/// <summary>Round operation.</summary>
        public static double Round(this double value, int digits = 0) => Math.Round(value, digits);

/// <summary>Round operation.</summary>
        public static float Round(this float value, int digits = 0) => (float)Math.Round(value, digits);

/// <summary>Abs operation.</summary>
        public static float Abs(this float value) => Math.Abs(value);

/// <summary>RadiansToDegrees operation.</summary>
        public static float RadiansToDegrees(this float value) => value * 180f / (float)Math.PI;

/// <summary>DegreesToRadians operation.</summary>
        public static float DegreesToRadians(this float value) => value * (float)Math.PI / 180f;
    }
}