using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Thermodynamics
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_HeatSource", "Gauge_SG_HeatSource")]
    public class ThermalHeatSourceBlock : MyGameLogicComponent
    {
/// <summary>Guid operation.</summary>
        private static readonly Guid StorageGuid = new Guid("b1f2c07a-6d43-4d1e-9a5e-2f1c8d3b4a67");

        private IMyCubeBlock block;
        private IMyFunctionalBlock functional;

        private NetSync<float> watts;

        private NetSync<float> range;

        private int sourceId;

        private bool ready;

        public HeatSourceBlockSetting Setting
        {
            get
            {
                if (watts == null || range == null) return HeatSourceBlockSetting.Default();
                return new HeatSourceBlockSetting(watts.Value, range.Value).Clamped();
            }
        }

        public bool IsRunning
        {
            get { return functional != null && functional.Enabled && functional.IsFunctional; }
        }

        public bool IsRadiating
        {
            get
            {
                if (sourceId == 0) return false;
                return Settings.Instance == null || Settings.Instance.EnableHeatSources;
            }
        }

/// <summary>Sets the watts.</summary>
        public void SetWatts(float value)
        {
            if (watts == null) return;
/// <summary>HeatSourceBlockSetting operation.</summary>
            watts.Value = new HeatSourceBlockSetting(value, HeatSourceBlockSetting.DefaultRange)
                .Clamped().Watts;
        }

/// <summary>Sets the range.</summary>
        public void SetRange(float value)
        {
            if (range == null) return;
/// <summary>HeatSourceBlockSetting operation.</summary>
            range.Value = new HeatSourceBlockSetting(HeatSourceBlockSetting.DefaultWatts, value)
                .Clamped().Range;
        }

/// <summary>Init operation.</summary>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            block = Entity as IMyCubeBlock;
            functional = Entity as IMyFunctionalBlock;
            if (block == null) return;

            try
            {
                if (!NetworkAPI.IsInitialized)
                {
                    NetworkAPI.Init(Session.ModID, Settings.Name);
                }

                HeatSourceBlockSetting fresh = HeatSourceBlockSetting.Default();

/// <summary>NetSync operation.</summary>
                watts = new NetSync<float>(this, TransferType.Both, fresh.Watts).Coalesce();
/// <summary>NetSync operation.</summary>
                range = new NetSync<float>(this, TransferType.Both, fresh.Range).Coalesce();

                watts.ValueChanged += OnDialChanged;
                range.ValueChanged += OnDialChanged;

                ready = true;

                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.Init", e);
            }
        }

/// <summary>UpdateOnceBeforeFrame operation.</summary>
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (functional != null) functional.IsWorkingChanged += OnWorkingChanged;

                if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer)
                {
/// <summary>Load operation.</summary>
                    HeatSourceBlockSetting saved = Load();
                    watts.Value = saved.Watts;
                    range.Value = saved.Range;
                }

                Sync();

                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.UpdateOnceBeforeFrame", e);
            }
        }

/// <summary>UpdateAfterSimulation100 operation.</summary>
        public override void UpdateAfterSimulation100()
        {
            try
            {
                Sync();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.UpdateAfterSimulation100", e);
            }
        }

/// <summary>Close operation.</summary>
        public override void Close()
        {
            try
            {
                if (functional != null) functional.IsWorkingChanged -= OnWorkingChanged;

                if (watts != null) watts.ValueChanged -= OnDialChanged;
                if (range != null) range.ValueChanged -= OnDialChanged;

                if (sourceId != 0)
                {
                    ThermalHeatSources.Remove(sourceId);
                    sourceId = 0;
                }
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.Close", e);
            }

            base.Close();
        }

/// <summary>OnWorkingChanged operation.</summary>
        private void OnWorkingChanged(IMyCubeBlock changed)
        {
            try
            {
                Sync();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.OnWorkingChanged", e);
            }
        }

/// <summary>OnDialChanged operation.</summary>
        private void OnDialChanged(float previous, float current)
        {
            try
            {
                Sync();
                Save();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.OnDialChanged", e);
            }
        }

/// <summary>Sync operation.</summary>
        private void Sync()
        {
            if (!ready) return;

            if (!IsRunning || !Setting.HasOutput)
            {
                if (sourceId == 0) return;

                ThermalHeatSources.Remove(sourceId);
                sourceId = 0;
                return;
            }

            HeatSourceBlockSetting setting = Setting;

            if (sourceId != 0)
            {
                if (ThermalHeatSources.Update(sourceId, setting.Watts)
/// <summary>RangeOf operation.</summary>
                    && RangeOf(sourceId) == setting.Range)
                {
                    return;
                }

                ThermalHeatSources.Remove(sourceId);
                sourceId = 0;
            }

            sourceId = ThermalHeatSources.Add(Entity, setting.Watts, setting.Range);
        }

/// <summary>RangeOf operation.</summary>
        private static float RangeOf(int id)
        {
            IList<ThermalHeatSources.HeatSource> all = ThermalHeatSources.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id) return all[i].Range;
            }
            return -1f;
        }

/// <summary>Save operation.</summary>
        private void Save()
        {
            try
            {
                if (Entity == null || MyAPIGateway.Multiplayer == null
                    || !MyAPIGateway.Multiplayer.IsServer)
                {
                    return;
                }

                if (Entity.Storage == null) Entity.Storage = new MyModStorageComponent();

                string data = Setting.Save();
                if (Entity.Storage.ContainsKey(StorageGuid)) Entity.Storage[StorageGuid] = data;
                else Entity.Storage.Add(StorageGuid, data);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.Save", e);
            }
        }

/// <summary>Load operation.</summary>
        private HeatSourceBlockSetting Load()
        {
            try
            {
                string data;
                if (Entity == null || Entity.Storage == null
                    || !Entity.Storage.TryGetValue(StorageGuid, out data))
                {
                    return HeatSourceBlockSetting.Default();
                }

                HeatSourceBlockSetting setting;
                return HeatSourceBlockSetting.TryLoad(data, out setting)
                    ? setting
                    : HeatSourceBlockSetting.Default();
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.Load", e);
                return HeatSourceBlockSetting.Default();
            }
        }
    }
}
