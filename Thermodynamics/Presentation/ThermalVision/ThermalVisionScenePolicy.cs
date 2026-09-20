namespace Thermodynamics.Presentation
{
    /// <summary>Shared work limits, so the offline lab evaluates the actual scene policy.</summary>
    public static class ThermalVisionScenePolicy
    {
        // Region mode shares the native 32768-entry billboard buffer with the game and other mods.
        // At most 12 box triangles + 8 clipped near-cap triangles per cell, plus two backdrops.
        public const int RegionCellLimit = 1536;
        public const int RegionWorstCaseBillboards = RegionCellLimit * 20 + 2;
        public const int TriangleLimit = 32768;
        public const int BuildTriangleLimit = 8192;
        public const int BlockLimit = 512;
        public const int ArmourBlockLimit = 384;
        public const double MillisecondLimit = 4;
        public const double Reach = 100;
    }
}
