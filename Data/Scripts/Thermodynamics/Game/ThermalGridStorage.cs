using System;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Thermodynamics
{
    /// <summary>
    /// Save and load, through the model's own codec.
    ///
    /// The version 1 format truncated every temperature to a whole kelvin and wrote the truncated
    /// value back onto the running simulation, so saving perturbed the world. The codec keeps the
    /// full value and does not write to live state.
    /// </summary>
    public partial class ThermalGrid
    {
        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

        /// <summary>
        /// Retired key. Version 1 wrote loop temperatures separately, indexed by list position; the
        /// codec carries them inside the main payload keyed by the loop's own signature, so a
        /// rebuilt loop keeps its temperature.
        /// </summary>
        private static readonly Guid LegacyLoopStorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619344");

        private void Save()
        {
            if (disabled || Simulation == null || Entity.Storage == null) return;

            try
            {
                if (Telemetry.Enabled && Stats != null) Stats.SaveTime.Begin();

                string data = Simulation.Save();

                MyModStorageComponentBase storage = Entity.Storage;
                if (storage.ContainsKey(StorageGuid))
                {
                    storage[StorageGuid] = data;
                }
                else
                {
                    storage.Add(StorageGuid, data);
                }

                if (Telemetry.Enabled && Stats != null)
                {
                    Stats.Saves++;
                    Stats.SaveBytes += data.Length;
                    Stats.SaveTime.End();
                }
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.Save", e);
                MyLog.Default.Error("[" + Settings.Name + "] save failed: " + e);
            }
        }

        private void Load()
        {
            if (disabled || Simulation == null || Entity.Storage == null) return;

            try
            {
                if (!Entity.Storage.ContainsKey(StorageGuid)) return;

                if (Telemetry.Enabled && Stats != null)
                {
                    Stats.Loads++;
                    Stats.LoadTime.Begin();
                }

                string data = Entity.Storage[StorageGuid];
                int restored = Simulation.Load(data);

                if (Telemetry.Enabled && Stats != null)
                {
                    Stats.LoadBytes += data.Length;
                    Stats.BlocksRestored += restored;
                    Stats.RoomsRestored += Simulation.RoomsRestored;
                    Stats.LoadTime.End();
                }
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.Load", e);
            }
        }
    }
}
