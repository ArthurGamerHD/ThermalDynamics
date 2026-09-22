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
        private static readonly MyStringId IgnoreId = MyStringId.GetOrCompute("ExcludeFromSimulation");
        private static readonly MyStringId ConductivityId = MyStringId.GetOrCompute("Conductivity");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId EmissivityId = MyStringId.GetOrCompute("Emissivity");
        private static readonly MyStringId SolarAbsorptivityId = MyStringId.GetOrCompute("SolarAbsorptivity");
        private static readonly MyStringId SurfaceAreaScalerId = MyStringId.GetOrCompute("ExposedSurfaceMultiplier");
        private static readonly MyStringId ProducerWasteEnergyId = MyStringId.GetOrCompute("ProducerWasteEnergy");
        private static readonly MyStringId ConsumerWasteEnergyId = MyStringId.GetOrCompute("ConsumerWasteEnergy");
        private static readonly MyStringId CriticalTemperatureId = MyStringId.GetOrCompute("CriticalTemperature");
        private static readonly MyStringId CriticalTemperatureScalerId = MyStringId.GetOrCompute("OverheatDamagePerKelvin");
        private static readonly MyStringId HeatSourceWattsId = MyStringId.GetOrCompute("HeatSourceWatts");

        private static readonly MyStringId LegacyIgnoreId = MyStringId.GetOrCompute("IgnoreThermals");
        private static readonly MyStringId LegacyExposedSurfaceId = MyStringId.GetOrCompute("SurfaceAreaScaler");
        private static readonly MyStringId LegacyOverheatDamageId = MyStringId.GetOrCompute("CriticalTemperatureScaler");

        private static readonly MyDefinitionId DefaultCubeBlockDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultSubtypeId);

        [ProtoMember(1)]
        public bool ExcludeFromSimulation;

        [ProtoMember(5)]
        public float Conductivity;

        [ProtoMember(10)]
        public float SpecificHeat;

        [ProtoMember(15)]
        public float Emissivity;

        [ProtoMember(16)]
        public float SolarAbsorptivity;

        [ProtoMember(17)]
        public float ExposedSurfaceMultiplier;

        [ProtoMember(20)]
        public float ProducerWasteEnergy;

        [ProtoMember(30)]
        public float ConsumerWasteEnergy;

        [ProtoMember(40)]
        public float CriticalTemperature;

        [ProtoMember(45)]
        public float OverheatDamagePerKelvin;

        [ProtoMember(50)]
        public float HeatSourceWatts;

        public DeclaredProperties Declared;

        [Flags]
        public enum DeclaredProperties
        {
            None = 0,
            Conductivity = 1,
            SpecificHeat = 2,
            Emissivity = 4,

            SolarAbsorptivity = 1024,
            ExposedSurfaceMultiplier = 8,
            ProducerWasteEnergy = 16,
            ConsumerWasteEnergy = 32,
            CriticalTemperature = 64,
            OverheatDamagePerKelvin = 128,
            ExcludeFromSimulation = 256,
            HeatSourceWatts = 512,
        }

        public bool WasDeclared(DeclaredProperties property)
        {
            return (Declared & property) != 0;
        }

        public enum Resolution
        {
            Subtype,

            Type,

            Fallback,
        }

        public Resolution ResolvedAt;


        public static ThermalCellDefinition GetDefinition(MyDefinitionId defId)
        {
            ThermalCellDefinition def = new ThermalCellDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            bool isTrue;
            def.ResolvedAt = Resolution.Subtype;

            if (!lookup.DefinitionIdExists(defId) || !lookup.TryGetBool(defId, GroupId, IgnoreId, out isTrue))
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);
                def.ResolvedAt = Resolution.Type;

                if (!lookup.DefinitionIdExists(defId))
                {
                    defId = DefaultCubeBlockDefinitionId;
                    def.ResolvedAt = Resolution.Fallback;
                }
            }

            if (lookup.TryGetBool(defId, GroupId, IgnoreId, out isTrue)
                || lookup.TryGetBool(defId, GroupId, LegacyIgnoreId, out isTrue))
            {
                def.ExcludeFromSimulation = isTrue;
                def.Declared |= DeclaredProperties.ExcludeFromSimulation;
            }

            double dvalue;
            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
            {
                def.Conductivity = (float)dvalue;
                def.Declared |= DeclaredProperties.Conductivity;
            }

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
            {
                def.SpecificHeat = (float)dvalue;
                def.Declared |= DeclaredProperties.SpecificHeat;
            }

            if (lookup.TryGetDouble(defId, GroupId, EmissivityId, out dvalue))
            {
                def.Emissivity = (float)dvalue;
                def.Declared |= DeclaredProperties.Emissivity;
            }

            if (lookup.TryGetDouble(defId, GroupId, SolarAbsorptivityId, out dvalue))
            {
                def.SolarAbsorptivity = (float)dvalue;
                def.Declared |= DeclaredProperties.SolarAbsorptivity;
            }

            if (lookup.TryGetDouble(defId, GroupId, SurfaceAreaScalerId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacyExposedSurfaceId, out dvalue))
            {
                def.ExposedSurfaceMultiplier = (float)dvalue;
                def.Declared |= DeclaredProperties.ExposedSurfaceMultiplier;
            }

            if (lookup.TryGetDouble(defId, GroupId, ProducerWasteEnergyId, out dvalue))
            {
                def.ProducerWasteEnergy = (float)dvalue;
                def.Declared |= DeclaredProperties.ProducerWasteEnergy;
            }

            if (lookup.TryGetDouble(defId, GroupId, ConsumerWasteEnergyId, out dvalue))
            {
                def.ConsumerWasteEnergy = (float)dvalue;
                def.Declared |= DeclaredProperties.ConsumerWasteEnergy;
            }

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureId, out dvalue))
            {
                def.CriticalTemperature = (float)dvalue;
                def.Declared |= DeclaredProperties.CriticalTemperature;
            }

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureScalerId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacyOverheatDamageId, out dvalue))
            {
                def.OverheatDamagePerKelvin = (float)dvalue;
                def.Declared |= DeclaredProperties.OverheatDamagePerKelvin;
            }

            if (lookup.TryGetDouble(defId, GroupId, HeatSourceWattsId, out dvalue))
            {
                def.HeatSourceWatts = (float)dvalue;
                def.Declared |= DeclaredProperties.HeatSourceWatts;
            }

            def.Conductivity = Math.Max(0, def.Conductivity);

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.Emissivity = Math.Max(0, def.Emissivity);

            def.SolarAbsorptivity = Math.Max(0, def.SolarAbsorptivity);

            def.ExposedSurfaceMultiplier = Math.Max(0, def.ExposedSurfaceMultiplier);

            def.ProducerWasteEnergy = Math.Max(0, def.ProducerWasteEnergy);

            def.ConsumerWasteEnergy = Math.Max(0, def.ConsumerWasteEnergy);

            def.CriticalTemperature = Math.Max(0, def.CriticalTemperature);

            def.OverheatDamagePerKelvin = Math.Max(0, def.OverheatDamagePerKelvin);

            def.HeatSourceWatts = Math.Max(0, def.HeatSourceWatts);

            return def;
        }
    }
}
