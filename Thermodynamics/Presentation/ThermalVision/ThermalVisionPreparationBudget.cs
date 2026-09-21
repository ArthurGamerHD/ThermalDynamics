namespace Thermodynamics.Presentation
{
    public static class ThermalVisionPreparationBudget
    {
/// <summary>PreferBackground operation.</summary>
        public static bool PreferBackground(double priorityMilliseconds,double backgroundMilliseconds)
        {
            return priorityMilliseconds>4*backgroundMilliseconds;
        }

/// <summary>CanAdvance operation.</summary>
        public static bool CanAdvance(double elapsedMilliseconds, double preparationStart,
            int visited, int visibleGrids, int idle)
        {
            return visibleGrids>0 && elapsedMilliseconds-preparationStart<2
                && (long)visited<System.Math.Max(4096L,(long)visibleGrids*128) && idle<visibleGrids;
        }
    }
}
