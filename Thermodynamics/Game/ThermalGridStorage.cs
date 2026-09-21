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
        /// <summary>
        /// Everything this mod saves on a grid goes under this one key. **The key ending 344 is retired
        /// and must not be reused**: version 1 wrote loop temperatures under it, and a world saved by
        /// that build still carries the blob, which nothing reads.
        /// </summary>
        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

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
