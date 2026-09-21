namespace Thermodynamics.Presentation
{
    public static class ThermalVisionScenePolicy
    {
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
