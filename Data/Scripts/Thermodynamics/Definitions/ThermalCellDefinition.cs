using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class ThermalCellDefinition
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalBlockProperties");
        private static readonly MyStringId IgnoreId = MyStringId.GetOrCompute("IgnoreThermals");
        private static readonly MyStringId ConductivityId = MyStringId.GetOrCompute("Conductivity");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId EmissivityId = MyStringId.GetOrCompute("Emissivity");
        private static readonly MyStringId SurfaceAreaScalerId = MyStringId.GetOrCompute("SurfaceAreaScaler");
        private static readonly MyStringId ProducerWasteEnergyId = MyStringId.GetOrCompute("ProducerWasteEnergy");
        private static readonly MyStringId ConsumerWasteEnergyId = MyStringId.GetOrCompute("ConsumerWasteEnergy");
        private static readonly MyStringId CriticalTemperatureId = MyStringId.GetOrCompute("CriticalTemperature");
        private static readonly MyStringId CriticalTemperatureScalerId = MyStringId.GetOrCompute("CriticalTemperatureScaler");
        private static readonly MyDefinitionId DefaultCubeBlockDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultSubtypeId);

        /// <summary>Exclude the block type from the simulation entirely.</summary>
        [ProtoMember(1)]
        public bool IgnoreThermals;

        /// <summary>
        /// Thermal conductivity, W/(m K). Reference values:
        /// https://www.engineeringtoolbox.com/thermal-conductivity-metals-d_858.html
        /// </summary>
        [ProtoMember(5)]
        public float Conductivity;

        /// <summary>
        /// Specific heat capacity, J/(kg K). Reference values:
        /// https://en.wikipedia.org/wiki/Table_of_specific_heat_capacities
        /// </summary>
        [ProtoMember(10)]
        public float SpecificHeat;

        /// <summary>
        /// Grey-body emissivity, 0..1, also used as solar absorptivity. Reference values:
        /// https://www.engineeringtoolbox.com/emissivity-coefficients-d_447.html
        /// </summary>
        [ProtoMember(15)]
        public float Emissivity;

        [ProtoMember(17)]
        public float SurfaceAreaScaler;

        /// <summary>Fraction of produced power converted to heat, 0..1.</summary>
        [ProtoMember(20)]
        public float ProducerWasteEnergy;

        /// <summary>Fraction of consumed power converted to heat, 0..1.</summary>
        [ProtoMember(30)]
        public float ConsumerWasteEnergy;

        [ProtoMember(40)]
        public float CriticalTemperature;

        [ProtoMember(45)]
        public float CriticalTemperatureScaler;


        public static ThermalCellDefinition GetDefinition(MyDefinitionId defId)
        {
            ThermalCellDefinition def = new ThermalCellDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            bool isTrue;
            if (!lookup.DefinitionIdExists(defId) || !lookup.TryGetBool(defId, GroupId, IgnoreId, out isTrue))
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);

                if (!lookup.DefinitionIdExists(defId))
                {
                    defId = DefaultCubeBlockDefinitionId;
                }
            }

            if (lookup.TryGetBool(defId, GroupId, IgnoreId, out isTrue))
                def.IgnoreThermals = isTrue;

            double dvalue;
            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
                def.Conductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, EmissivityId, out dvalue))
                def.Emissivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SurfaceAreaScalerId, out dvalue))          
                def.SurfaceAreaScaler = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ProducerWasteEnergyId, out dvalue))
                def.ProducerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConsumerWasteEnergyId, out dvalue))
                def.ConsumerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureId, out dvalue))
                def.CriticalTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureScalerId, out dvalue))
                def.CriticalTemperatureScaler = (float)dvalue;

            def.Conductivity = Math.Min(1, Math.Max(0, def.Conductivity));

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.Emissivity = Math.Max(0, def.Emissivity);

            def.SurfaceAreaScaler = Math.Max(0, def.SurfaceAreaScaler);

            def.ProducerWasteEnergy = Math.Max(0, def.ProducerWasteEnergy);

            def.ConsumerWasteEnergy = Math.Max(0, def.ConsumerWasteEnergy);

            def.CriticalTemperature = Math.Max(0, def.CriticalTemperature);

            def.CriticalTemperatureScaler = Math.Max(0, def.CriticalTemperatureScaler);

            return def;
        }
    }
}
