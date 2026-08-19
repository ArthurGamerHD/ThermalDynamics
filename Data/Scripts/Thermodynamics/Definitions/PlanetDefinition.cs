using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class PlanetDefinition
    {

        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalPlanetProperties");
        private static readonly MyStringId NightTemperatureId = MyStringId.GetOrCompute("NightTemperature");
        private static readonly MyStringId DayTemperatureId = MyStringId.GetOrCompute("DayTemperature");
        private static readonly MyStringId UndergroundTemperatureId = MyStringId.GetOrCompute("UndergroundTemperature");
        private static readonly MyStringId CoreTemperatureId = MyStringId.GetOrCompute("CoreTemperature");
        private static readonly MyStringId SealevelDeadzoneId = MyStringId.GetOrCompute("SealevelDeadzone");
        private static readonly MyStringId PoleTemperatureDropId = MyStringId.GetOrCompute("PoleTemperatureDrop");
        private static readonly MyStringId AmbientLagSecondsId = MyStringId.GetOrCompute("AmbientLagSeconds");
        private static readonly MyStringId AmbientLapseRateId = MyStringId.GetOrCompute("AmbientLapseRate");
        private static readonly MyStringId UndergroundDampingDepthId = MyStringId.GetOrCompute("UndergroundDampingDepth");
        private static readonly MyStringId SolarDecayId = MyStringId.GetOrCompute("SolarDecay");
        private static readonly MyStringId ConvectionCoefficientId = MyStringId.GetOrCompute("ConvectionCoefficient");

        /// <summary>Ambient temperature with the sun on the far side of the planet, K.</summary>
        [ProtoMember(10)]
        public float NightTemperature;

        /// <summary>Ambient temperature with the sun directly overhead, K.</summary>
        [ProtoMember(15)]
        public float DayTemperature;

        /// <summary>Ambient temperature underground, K.</summary>
        [ProtoMember(17)]
        public float UndergroundTemperature;

        /// <summary>Temperature at the planet's centre, K.</summary>
        [ProtoMember(20)]
        public float CoreTemperature;

        /// <summary>
        /// Depth below sea level that stays at the underground temperature, m. Below it the rock
        /// warms towards the core temperature.
        /// </summary>
        [ProtoMember(25)]
        public float SealevelDeadzone;

        /// <summary>
        /// How much colder a pole is than the equator, K.
        ///
        /// This and the three fields below carry the model's defaults rather than zero. Zero is a
        /// valid setting for each of them — no latitude, no lag, no lapse, no damping — so a planet
        /// file written before they existed would otherwise disable all four.
        /// </summary>
        [ProtoMember(27)]
        public float PoleTemperatureDrop = 40f;

        /// <summary>How long the air takes to answer the sun, in seconds of play.</summary>
        [ProtoMember(28)]
        public float AmbientLagSeconds = 45f;

        /// <summary>How much colder a kilometre above sea level is, K.</summary>
        [ProtoMember(29)]
        public float AmbientLapseRate = 4f;

        /// <summary>Depth of rock over which the surface's day-night swing is damped to nothing, m.</summary>
        [ProtoMember(31)]
        public float UndergroundDampingDepth = 20f;

        /// <summary>Fraction of solar energy absorbed by a full-density atmosphere, 0..1.</summary>
        [ProtoMember(30)]
        public float SolarDecay;

        /// <summary>Convective heat transfer coefficient at rest, W/(m^2 K).</summary>
        [ProtoMember(40)]
        public float ConvectionCoefficient;

        public static PlanetDefinition GetDefinition(MyDefinitionId defId) 
        {
            MyLog.Default.Info($"[{Settings.Name}] Planet Definition: {defId}");

            PlanetDefinition def = new PlanetDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            if (!lookup.DefinitionIdExists(defId))
            {
                defId = new MyDefinitionId(typeof(MyObjectBuilder_PlanetGeneratorDefinition), Settings.DefaultSubtypeId);
            }

            double dvalue;

            if (lookup.TryGetDouble(defId, GroupId, NightTemperatureId, out dvalue))
                def.NightTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, DayTemperatureId, out dvalue))
                def.DayTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, UndergroundTemperatureId, out dvalue))
                def.UndergroundTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CoreTemperatureId, out dvalue))
                def.CoreTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SealevelDeadzoneId, out dvalue))
                def.SealevelDeadzone = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PoleTemperatureDropId, out dvalue))
                def.PoleTemperatureDrop = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, AmbientLagSecondsId, out dvalue))
                def.AmbientLagSeconds = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, AmbientLapseRateId, out dvalue))
                def.AmbientLapseRate = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, UndergroundDampingDepthId, out dvalue))
                def.UndergroundDampingDepth = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SolarDecayId, out dvalue))
                def.SolarDecay = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConvectionCoefficientId, out dvalue))
                def.ConvectionCoefficient = (float)dvalue;

            return def;

        }
    }
}
