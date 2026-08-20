using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SENetworkAPI;
using VRage.Game.Components;
using VRage.ObjectBuilders;

namespace Thermodynamics
{
    /// <summary>
    /// The game-side half of a coolant pump: its switch and its speed setting.
    ///
    /// <para>
    /// <see cref="Core.CoolantPump"/> has read <c>Enabled</c>, <c>Speed</c> and
    /// <c>PowerAvailable</c> from the host since it was written — the field comments name the
    /// switch and the terminal explicitly — and nothing ever wrote them. A pump therefore ran at
    /// full speed whatever its terminal said, which is what made the on/off toggle look like a
    /// bug: the toggle was real, the wiring behind it was missing.
    /// </para>
    ///
    /// <para>
    /// <c>Enabled</c> comes from the block's own functional state, which the game replicates
    /// itself. The speed setting does not exist in the game's model at all, so it is carried by a
    /// <see cref="NetSync{T}"/> of this mod's own — one property, declared here so its address is
    /// the same on every side.
    /// </para>
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false,
        "Gauge_LG_CoolantPump", "Gauge_SG_CoolantPump")]
    public class ThermalCoolantPumpBlock : MyGameLogicComponent
    {
        private IMyFunctionalBlock functional;

        /// <summary>
        /// Speed the terminal is set to, 0..1, replicated both ways: a client turns the dial and
        /// the server is the one whose value the simulation reads.
        /// </summary>
        private NetSync<float> speed;

        /// <summary>
        /// Whether the block is switched on and working. A pump that is off or damaged past
        /// functional drives nothing — the ring keeps its coolant and stops carrying it anywhere.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (functional == null) return false;
                return functional.Enabled && functional.IsFunctional;
            }
        }

        /// <summary>Speed setting, 0..1. Defaults to full, so a pump built and left alone works.</summary>
        public float Speed
        {
            get { return speed == null ? 1f : Clamp01(speed.Value); }
        }

        /// <summary>Sets the speed and replicates it. Called by the terminal slider.</summary>
        public void SetSpeed(float value)
        {
            if (speed == null) return;
            speed.Value = Clamp01(value);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            functional = Entity as IMyFunctionalBlock;

            try
            {
                if (!NetworkAPI.IsInitialized)
                {
                    NetworkAPI.Init(Session.ModID, Settings.Name);
                }

                // One property, so its index on this entity is zero on every side. Anything added
                // here later must go after it.
                speed = new NetSync<float>(this, TransferType.Both, 1f);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalCoolantPumpBlock.Init", e);
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
