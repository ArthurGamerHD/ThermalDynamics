using VRageMath;

namespace RichHudFramework
{
    public static class VectorExtensions
    {
        private static Color lastRgbColor;
        private static Vector4 lastBbColor;


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


        public static Vector2D ToDouble(this Vector2 vec) =>

            new Vector2D(vec.X, vec.Y);


        public static Vector2 ToSingle(this Vector2D vec) =>

            new Vector2((float)vec.X, (float)vec.Y);


        public static Color SetAlphaPct(this Color color, float alphaPercent) =>

            new Color(color.R, color.G, color.B, (byte)(alphaPercent * 255f));


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
