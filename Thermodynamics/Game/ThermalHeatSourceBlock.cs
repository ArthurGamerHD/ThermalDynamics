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
    /// <summary>
    /// The debug heat source: a block that radiates like a small sun, so the point-source path can be
    /// seen in a session rather than only in the harness. See backlog.md <c>B11</c>.
    ///
    /// <para>
    /// **It registers through <see cref="ThermalHeatSources"/> and nothing else**, which is the whole
    /// point of it — it is the same entry point the mod API's <c>AddHeatSource</c> hands out and the
    /// same one <c>/thermal heat</c> drives, so a session that shows this block working has shown
    /// that path working for every consumer of it. A second implementation would have proved
    /// nothing about the first.
    /// </para>
    ///
    /// <para>
    /// **It conjures its energy, deliberately.** Every other heat term in this mod is conserved
    /// against something — power drawn, power produced, thrust, a gradient a pump paid to climb.
    /// This block has no resource sink and takes nothing from the grid: it is a test fixture whose
    /// job is to put a *known* number of watts into a world, and a fixture that browned out when the
    /// reactors did would be measuring the reactors. That is why its name says debug.
    /// </para>
    ///
    /// <para>
    /// **Every machine registers its own source.** Sources are sampled where grids are simulated and
    /// every machine simulates the same ship, so this component runs client and server alike; what
    /// crosses the wire is the two dial values, through <see cref="NetSync{T}"/>. The block's own
    /// on/off is the game's to replicate.
    /// </para>
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_HeatSource", "Gauge_SG_HeatSource")]
    public class ThermalHeatSourceBlock : MyGameLogicComponent
    {
        /// <summary>
        /// Where the dial is saved on the block. **A new GUID and never one that has been used**:
        /// the storage dictionary is shared with every other mod on the entity.
        /// </summary>
        private static readonly Guid StorageGuid = new Guid("b1f2c07a-6d43-4d1e-9a5e-2f1c8d3b4a67");

        private IMyCubeBlock block;
        private IMyFunctionalBlock functional;

        /// <summary>
        /// The dial, replicated both ways: a client turns it and every machine's registry has to
        /// agree, because every machine samples it.
        ///
        /// **Declaration order is the wire format.** A <see cref="NetSync{T}"/>'s address is its
        /// index on the entity, so these two are watts then range on every side and anything added
        /// later goes after them — the same rule the coolant pump's single property carries.
        /// </summary>
        private NetSync<float> watts;

        private NetSync<float> range;

        /// <summary>Registry id of the source this block owns, or 0 when it is registering none.</summary>
        private int sourceId;

        /// <summary>True once <see cref="Init"/> has run and the dials exist.</summary>
        private bool ready;

        /// <summary>The dial as it stands, held inside its limits whatever the network said.</summary>
        public HeatSourceBlockSetting Setting
        {
            get
            {
                if (watts == null || range == null) return HeatSourceBlockSetting.Default();
                return new HeatSourceBlockSetting(watts.Value, range.Value).Clamped();
            }
        }

        /// <summary>
        /// Whether the block is switched on and built far enough to work. An upgrade module with no
        /// sink is working exactly when it is enabled and functional, so this is the game's own
        /// answer rather than a second one (<c>C9</c>).
        /// </summary>
        public bool IsRunning
        {
            get { return functional != null && functional.Enabled && functional.IsFunctional; }
        }

        /// <summary>
        /// Whether the source this block registered is actually reaching anything, which is the one
        /// thing the terminal cannot show by echoing the dial back. **A world with
        /// `EnableHeatSources` off leaves a switched-on block delivering nothing**, and without this
        /// the readout would say 5 MW while the solver ignored it.
        ///
        /// <para>
        /// False at a dial of zero too, and there for the plainest reason: at zero there is no
        /// registered source to radiate. See <see cref="Sync"/>.
        /// </para>
        /// </summary>
        public bool IsRadiating
        {
            get
            {
                if (sourceId == 0) return false;
                return Settings.Instance == null || Settings.Instance.EnableHeatSources;
            }
        }

        /// <summary>
        /// Sets the output in watts and replicates it. Called by the terminal slider, which hands
        /// this the watts its position maps to rather than the position itself.
        /// </summary>
        public void SetWatts(float value)
        {
            if (watts == null) return;
            watts.Value = new HeatSourceBlockSetting(value, HeatSourceBlockSetting.DefaultRange)
                .Clamped().Watts;
        }

        /// <summary>Sets the reach in metres and replicates it. Called by the terminal slider.</summary>
        public void SetRange(float value)
        {
            if (range == null) return;
            range.Value = new HeatSourceBlockSetting(HeatSourceBlockSetting.DefaultWatts, value)
                .Clamped().Range;
        }

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

                // Two properties, watts then range. See the note on the fields.
                //
                // Constructed at the default rather than at what the save holds, because the
                // block's storage is not reliably deserialised by the time a game logic component's
                // Init runs. The saved dial is applied on the first frame instead, where the
                // component is on a block that is fully in the world.
                watts = new NetSync<float>(this, TransferType.Both, fresh.Watts).Coalesce();
                range = new NetSync<float>(this, TransferType.Both, fresh.Range).Coalesce();

                // A dragged slider is a stream of assignments, and the registry has to follow every
                // one of them — including the ones that arrive over the network on a machine whose
                // terminal is not open. ValueChanged fires for both, which is why the sync hangs off
                // it rather than off the setters above.
                watts.ValueChanged += OnDialChanged;
                range.ValueChanged += OnDialChanged;

                ready = true;

                // The block is not on the grid yet when Init runs, so the source is registered on
                // the first frame the entity is actually in the world.
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.Init", e);
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (functional != null) functional.IsWorkingChanged += OnWorkingChanged;

                // **The server owns the saved dial and hands it to everyone else.** Storage is the
                // server's copy of the world; a client applying its own would race the value
                // arriving over the wire and could win. Assigning here replicates, so a client that
                // joins later or reads a moment early converges on this.
                if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer)
                {
                    HeatSourceBlockSetting saved = Load();
                    watts.Value = saved.Watts;
                    range.Value = saved.Range;
                }

                Sync();

                // **A poll, in a mod whose stated goal is that nothing is polled, and it earns its
                // place.** The registry is shared and something else can empty it under this block
                // — `/thermal heat clear` does exactly that, and the two are debug tools somebody
                // uses in the same sitting. A source removed that way is gone with no event to
                // hear, the block goes on showing its dial, and the only symptom is a ship that
                // stopped warming up. Re-asserting costs one integer compare every hundred frames
                // on a block a player had to build on purpose.
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalHeatSourceBlock.UpdateOnceBeforeFrame", e);
            }
        }

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

        public override void Close()
        {
            try
            {
                if (functional != null) functional.IsWorkingChanged -= OnWorkingChanged;

                if (watts != null) watts.ValueChanged -= OnDialChanged;
                if (range != null) range.ValueChanged -= OnDialChanged;

                // The registry drops a source whose entity has gone, but only when something next
                // samples it. Removing here is what keeps `/thermal heat list` honest on a world
                // where nothing is being simulated.
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

        /// <summary>
        /// Makes the registry agree with the block: one source while it is switched on and asking
        /// for something, none while it is not.
        ///
        /// <para>
        /// **Removed rather than dialled to zero**, and that is the whole reason the output dial
        /// reaches zero at all. A registered source is charged one pass over the exposed blocks of
        /// every grid in its range, every sampled step, whether it is making a gigawatt or nothing
        /// — <c>Sample</c> skips a zero-watt source only *after* it has walked to it. So a dial at
        /// the bottom takes the entry out of the list, and the block costs the simulation exactly
        /// nothing (`P8`: switching a mechanism off removes its own cost).
        /// </para>
        ///
        /// <para>
        /// It also keeps the source list honest, which is what a person reading
        /// <c>/thermal heat list</c> to debug a cold ship is asking of it. The id changes each time
        /// the block comes back on and nothing outside this class holds it.
        /// </para>
        /// </summary>
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
                // Update carries the watts only, and it returns false when the registry no longer
                // holds the id at all — so this one call answers both *did the dial move* and *is
                // my source still there*. A changed range needs the entry rebuilt either way.
                if (ThermalHeatSources.Update(sourceId, setting.Watts)
                    && RangeOf(sourceId) == setting.Range)
                {
                    return;
                }

                ThermalHeatSources.Remove(sourceId);
                sourceId = 0;
            }

            sourceId = ThermalHeatSources.Add(Entity, setting.Watts, setting.Range);
        }

        /// <summary>The reach the registry currently holds for an id, or -1 when it holds none.</summary>
        private static float RangeOf(int id)
        {
            IList<ThermalHeatSources.HeatSource> all = ThermalHeatSources.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id) return all[i].Range;
            }
            return -1f;
        }

        /// <summary>
        /// Writes the dial into the block's own storage, which the game saves with the grid.
        ///
        /// <para>
        /// **Server only.** Storage is the server's copy of the world; a client writing its own
        /// would save nothing and confuse the next read. The value reached the server through
        /// <see cref="NetSync{T}"/> before this ran, and this runs there too.
        /// </para>
        ///
        /// <para>
        /// A <see cref="NetSync{T}"/> does not persist — the coolant pump's speed and the heat
        /// pump's throttle both return to their defaults when a world reloads. That is tolerable on
        /// a dial a player sets while flying and not on a debug rig, which is built once and then
        /// left, so this block carries the storage the other two do not.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// The dial a saved world left, or the default when there is none — which is what a freshly
        /// placed block and a block from a build before this storage existed both look like.
        /// </summary>
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
