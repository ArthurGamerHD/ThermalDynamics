using System;

namespace Thermodynamics.Presentation
{
    /// <summary>Camera-derived thermal reach; visibility itself uses the game's full bounding frustum.</summary>
    public static class ThermalVisionViewPolicy
    {
        /// <summary>The selected full-screen mode persists independently of measurement acquisition.</summary>
        public static bool DrawContext(bool fleetMode, bool hasField)
        { return fleetMode || hasField; }

        /// <summary>Chat and cursor do not suppress passive rendering; input handling is gated separately.</summary>
        public static bool SuppressForUi(bool guiAvailable, bool gameChat, bool frameworkChat, bool cursor)
        { return !guiAvailable; }

        public static double FarDistance(double cameraFar, double sessionFar, double near)
        {
            if (Valid(cameraFar, near)) return cameraFar;
            if (Valid(sessionFar, near)) return sessionFar;
            return Math.Max(15000, near + 1);
        }

        public static double BackdropDistance(double far)
        {
            // Keep the sky-clearing quad just inside the far clipping plane.
            return far * .999999;
        }

        /// <summary>Spatial preparation detail from apparent diameter, not a billboard quota.
        /// Hysteresis holds the existing level until a substantial screen-size change.</summary>
        public static int DetailBudget(double diameterPixels, int previous, bool interior)
        {
            if(interior || double.IsNaN(diameterPixels)) return 512;
            double cells=Math.Max(0,diameterPixels)/8;
            double demand=Math.Max(16,Math.Min(512,cells*cells));
            if(previous>=16 && previous<=512 && (previous & (previous-1))==0
                && demand>=previous*.6 && demand<=previous*1.4) return previous;
            int budget=16;
            while(budget<demand && budget<512) budget*=2;
            return budget;
        }

        private static bool Valid(double value, double near)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value > near + 1; }
    }
}
