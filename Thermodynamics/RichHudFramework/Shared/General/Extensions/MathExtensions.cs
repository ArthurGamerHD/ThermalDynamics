﻿using System;

namespace RichHudFramework
{
    public static class MathExtensions
    {

        public static double Round(this double value, int digits = 0) => Math.Round(value, digits);


        public static float Round(this float value, int digits = 0) => (float)Math.Round(value, digits);


        public static float Abs(this float value) => Math.Abs(value);


        public static float RadiansToDegrees(this float value) => value * 180f / (float)Math.PI;


        public static float DegreesToRadians(this float value) => value * (float)Math.PI / 180f;
    }
}