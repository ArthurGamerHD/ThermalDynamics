using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using Thermodynamics.Core;
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
        private static readonly MyStringId AmbientLagShareOfDayId = MyStringId.GetOrCompute("AmbientLagShareOfDay");
        private static readonly MyStringId AmbientLapseRateId = MyStringId.GetOrCompute("AmbientLapseRate");
        private static readonly MyStringId UndergroundDampingDepthId = MyStringId.GetOrCompute("UndergroundDampingDepth");
        private static readonly MyStringId SolarDecayId = MyStringId.GetOrCompute("SolarDecay");
        private static readonly MyStringId ConvectionCoefficientId = MyStringId.GetOrCompute("ConvectionCoefficient");
        private static readonly MyStringId UndergroundConvectionCoefficientId = MyStringId.GetOrCompute("UndergroundConvectionCoefficient");

        public PlanetField Supplied;

/// <summary>Has operation.</summary>
        public bool Has(PlanetField field)
        {
            return (Supplied & field) != 0;
        }

        [ProtoMember(10)]
        public float NightTemperature;

        [ProtoMember(15)]
        public float DayTemperature;

        [ProtoMember(17)]
        public float UndergroundTemperature;

        [ProtoMember(20)]
        public float CoreTemperature;

        [ProtoMember(25)]
        public float SealevelDeadzone;

        [ProtoMember(27)]
        public float PoleTemperatureDrop = 40f;

        [ProtoMember(28)]
        public float AmbientLagSeconds = 45f;

        public float AmbientLagShareOfDay;

        [ProtoMember(29)]
        public float AmbientLapseRate = 4f;

        [ProtoMember(31)]
        public float UndergroundDampingDepth = 20f;

        [ProtoMember(30)]
        public float SolarDecay;

        [ProtoMember(40)]
        public float ConvectionCoefficient;

        public float UndergroundConvectionCoefficient;

/// <summary>Returns the definition.</summary>
        public static PlanetDefinition GetDefinition(MyDefinitionId defId) 
        {
            MyLog.Default.Info($"[{Settings.Name}] Planet Definition: {defId}");

/// <summary>PlanetDefinition operation.</summary>
            PlanetDefinition def = new PlanetDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            if (lookup == null || !lookup.Init) return null;

            if (!lookup.DefinitionIdExists(defId))
            {
/// <summary>MyDefinitionId operation.</summary>
                defId = new MyDefinitionId(typeof(MyObjectBuilder_PlanetGeneratorDefinition), Settings.DefaultSubtypeId);
            }

            double dvalue;

            if (lookup.TryGetDouble(defId, GroupId, NightTemperatureId, out dvalue))
            {
                def.NightTemperature = (float)dvalue;
                def.Supplied |= PlanetField.NightTemperature;
            }

            if (lookup.TryGetDouble(defId, GroupId, DayTemperatureId, out dvalue))
            {
                def.DayTemperature = (float)dvalue;
                def.Supplied |= PlanetField.DayTemperature;
            }

            if (lookup.TryGetDouble(defId, GroupId, UndergroundTemperatureId, out dvalue))
            {
                def.UndergroundTemperature = (float)dvalue;
                def.Supplied |= PlanetField.UndergroundTemperature;
            }

            if (lookup.TryGetDouble(defId, GroupId, CoreTemperatureId, out dvalue))
            {
                def.CoreTemperature = (float)dvalue;
                def.Supplied |= PlanetField.CoreTemperature;
            }

            if (lookup.TryGetDouble(defId, GroupId, SealevelDeadzoneId, out dvalue))
            {
                def.SealevelDeadzone = (float)dvalue;
                def.Supplied |= PlanetField.SealevelDeadzone;
            }

            if (lookup.TryGetDouble(defId, GroupId, PoleTemperatureDropId, out dvalue))
            {
                def.PoleTemperatureDrop = (float)dvalue;
                def.Supplied |= PlanetField.PoleTemperatureDrop;
            }

            if (lookup.TryGetDouble(defId, GroupId, AmbientLagSecondsId, out dvalue))
            {
                def.AmbientLagSeconds = (float)dvalue;
                def.Supplied |= PlanetField.AmbientLagSeconds;
            }

            if (lookup.TryGetDouble(defId, GroupId, AmbientLagShareOfDayId, out dvalue))
            {
                def.AmbientLagShareOfDay = (float)dvalue;
                def.Supplied |= PlanetField.AmbientLagShareOfDay;
            }

            if (lookup.TryGetDouble(defId, GroupId, AmbientLapseRateId, out dvalue))
            {
                def.AmbientLapseRate = (float)dvalue;
                def.Supplied |= PlanetField.AmbientLapseRate;
            }

            if (lookup.TryGetDouble(defId, GroupId, UndergroundDampingDepthId, out dvalue))
            {
                def.UndergroundDampingDepth = (float)dvalue;
                def.Supplied |= PlanetField.UndergroundDampingDepth;
            }

            if (lookup.TryGetDouble(defId, GroupId, SolarDecayId, out dvalue))
            {
                def.SolarDecay = (float)dvalue;
                def.Supplied |= PlanetField.SolarDecay;
            }

            if (lookup.TryGetDouble(defId, GroupId, ConvectionCoefficientId, out dvalue))
            {
                def.ConvectionCoefficient = (float)dvalue;
                def.Supplied |= PlanetField.ConvectionCoefficient;
            }


            if (lookup.TryGetDouble(defId, GroupId, UndergroundConvectionCoefficientId, out dvalue))
            {
                def.UndergroundConvectionCoefficient = (float)dvalue;
                def.Supplied |= PlanetField.UndergroundConvectionCoefficient;
            }
            return def;

        }
    }
}
