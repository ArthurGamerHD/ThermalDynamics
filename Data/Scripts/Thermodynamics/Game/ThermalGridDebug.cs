using System;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.Components;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The block-colouring debug overlays.
    ///
    /// All of it is behind the settings toggles and behind a server check, and the whole pass is
    /// skipped in one branch when nothing is switched on — colouring blocks is a grid-wide write
    /// and has no business running in ordinary play.
    /// </summary>
    public partial class ThermalGrid
    {
        private void DrawDebugColors()
        {
            Settings settings = Settings.Instance;

            bool any = settings.DebugTemperatureBlockColors
                || settings.DebugExposedSurfaceBlockColors
                || settings.DebugSolarRadiationBlockColors
                || settings.DebugFrictionColors;

            if (!any || !MyAPIGateway.Session.IsServer) return;

            foreach (ThermalBlock bound in blocks.Values)
            {
                ThermalNode node = bound.Node;
                if (node == null) continue;

                Vector3 color;
                if (settings.DebugTemperatureBlockColors)
                {
                    color = Tools.GetTemperatureColor(node.Temperature);
                }
                else if (settings.DebugExposedSurfaceBlockColors)
                {
                    color = Tools.GetTemperatureColor(node.TotalExposedFaces, 6, 0, 6);
                }
                else if (settings.DebugSolarRadiationBlockColors)
                {
                    color = Tools.GetTemperatureColor(Math.Abs(node.LastSolarWatts), 20000, 100, 5000);
                }
                else
                {
                    color = Tools.GetTemperatureColor(Math.Abs(node.LastFrictionWatts), 20000, 100, 5000);
                }

                if (bound.Block.ColorMaskHSV == color) continue;

                Grid.ColorBlocks(bound.Block.Min, bound.Block.Max, color, false);
            }
        }
    }
}
