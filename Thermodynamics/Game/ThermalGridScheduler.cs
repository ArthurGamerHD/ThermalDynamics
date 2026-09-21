using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Thermodynamics
{
    public static class ThermalGridScheduler
    {
        public const float FrameSeconds = 1f / 60f;

/// <summary>Tick operation.</summary>
        public static void Tick()
        {
            IList<ThermalGrid> grids = ThermalGrid.LiveGrids;

            if (Settings.Instance != null && Settings.Instance.ParallelGrids)
            {
                TickInParallel(grids);
                return;
            }

            for (int i = grids.Count - 1; i >= 0; i--)
            {
                ThermalGrid grid = grids[i];
                if (grid == null) continue;

                grid.Tick(FrameSeconds);
            }
        }

/// <summary>TickInParallel operation.</summary>
        private static void TickInParallel(IList<ThermalGrid> grids)
        {
            ready.Clear();

            for (int i = grids.Count - 1; i >= 0; i--)
            {
                ThermalGrid grid = grids[i];
                if (grid == null) continue;

                if (grid.PrepareTick(FrameSeconds)) ready.Add(grid);
            }

            if (ready.Count == 0) return;

            if (ready.Count == 1)
            {
                ready[0].SolveTick();
            }
/// <summary>if operation.</summary>
            else if (MyAPIGateway.Parallel != null)
            {
                MyAPIGateway.Parallel.ForEach(ready, Solve);
            }
            else
            {
                for (int i = 0; i < ready.Count; i++) ready[i].SolveTick();
            }

            for (int i = 0; i < ready.Count; i++) ready[i].PublishTick();
        }

        private static readonly Action<ThermalGrid> Solve = grid => grid.SolveTick();

/// <summary>List operation.</summary>
        private static readonly List<ThermalGrid> ready = new List<ThermalGrid>();
    }
}
