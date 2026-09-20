namespace Thermodynamics.Presentation
{
    /// <summary>Bounds cooperative preparation independently of preceding visibility work.</summary>
    public static class ThermalVisionPreparationBudget
    {
        /// <summary>Reserve one fifth of measured preparation time for other visible grids.
        /// Account for time rather than iterator count: different steps have very different costs.</summary>
        public static bool PreferBackground(double priorityMilliseconds,double backgroundMilliseconds)
        {
            return priorityMilliseconds>4*backgroundMilliseconds;
        }

        /// <summary>The deadline is relative to preparation start, so costly discovery cannot
        /// permanently starve refreshes. One iterator step can still exceed this soft deadline.</summary>
        public static bool CanAdvance(double elapsedMilliseconds, double preparationStart,
            int visited, int visibleGrids, int idle)
        {
            return visibleGrids>0 && elapsedMilliseconds-preparationStart<2
                // Cheap lattice samples should use the available time even when only
                // one ship is visible. Keep a finite guard for timer granularity/stalls.
                && (long)visited<System.Math.Max(4096L,(long)visibleGrids*128) && idle<visibleGrids;
        }
    }
}
