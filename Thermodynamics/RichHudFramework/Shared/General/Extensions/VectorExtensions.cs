using VRageMath;

namespace RichHudFramework
{
    public static class VectorExtensions
    {
        private static Color lastRgbColor;
        private static Vector4 lastBbColor;

/// <summary>Returns the bbcolor.</summary>
        public static Vector4 GetBbColor(this Color color)
        {
            if (color == lastRgbColor)
                return lastBbColor;

            lastRgbColor = color;

            float opacity = color.A / 255f;
            color.R = (byte)(color.R * opacity);
            color.G = (byte)(color.G * opacity);
            color.B = (byte)(color.B * opacity);

            lastBbColor = ((Vector4)color).ToLinearRGB();
            return lastBbColor;
        }

/// <summary>ToDouble operation.</summary>
        public static Vector2D ToDouble(this Vector2 vec) =>
/// <summary>Vector2D operation.</summary>
            new Vector2D(vec.X, vec.Y);

/// <summary>ToSingle operation.</summary>
        public static Vector2 ToSingle(this Vector2D vec) =>
/// <summary>Vector2 operation.</summary>
            new Vector2((float)vec.X, (float)vec.Y);

/// <summary>Sets the alphapct.</summary>
        public static Color SetAlphaPct(this Color color, float alphaPercent) =>
/// <summary>Color operation.</summary>
            new Color(color.R, color.G, color.B, (byte)(alphaPercent * 255f));

/// <summary>Returns the channel.</summary>
        public static byte GetChannel(this Color color, int channel)
        {
            switch (channel)
            {
                case 0:
                    return color.R;
                case 1:
                    return color.G;
                case 2:
                    return color.B;
                case 3:
                    return color.A;
            }

            return 0;
        }

/// <summary>Sets the channel.</summary>
        public static Color SetChannel(this Color color, int channel, byte value)
        {
            switch(channel)
            {
                case 0:
                    color.R = value;
                    break;
                case 1:
                    color.G = value;
                    break;
                case 2:
                    color.B = value;
                    break;
                case 3:
                    color.A = value;
                    break;
            }

            return color;
        }
    }
}
