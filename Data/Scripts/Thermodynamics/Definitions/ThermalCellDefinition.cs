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
        private static readonly MyStringId SurfaceAreaScalerId = MyStringId.GetOrCompute("ExposedSurfaceMultiplier");
        private static readonly MyStringId ProducerWasteEnergyId = MyStringId.GetOrCompute("ProducerWasteEnergy");
        private static readonly MyStringId ConsumerWasteEnergyId = MyStringId.GetOrCompute("ConsumerWasteEnergy");
        private static readonly MyStringId CriticalTemperatureId = MyStringId.GetOrCompute("CriticalTemperature");
        private static readonly MyStringId CriticalTemperatureScalerId = MyStringId.GetOrCompute("OverheatDamagePerKelvin");

        /// <summary>
        /// Names these properties used to carry, still read when the current name is absent.
        ///
        /// Definition Extensions matches on the name string, so a rename that does not keep the old
        /// one silently reverts every third-party definition to the defaults — no error, no log
        /// line, just a mod whose blocks quietly stop being what they say they are. That is the
        /// same failure the whole of <c>ShippedDefinitionTests</c> exists because of.
        ///
        /// New definitions should use the current names; these stay so old ones keep working.
        /// </summary>
        private static readonly MyStringId LegacyIgnoreId = MyStringId.GetOrCompute("IgnoreThermals");
        private static readonly MyStringId LegacyExposedSurfaceId = MyStringId.GetOrCompute("SurfaceAreaScaler");
        private static readonly MyStringId LegacyOverheatDamageId = MyStringId.GetOrCompute("CriticalTemperatureScaler");

        private static readonly MyDefinitionId DefaultCubeBlockDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultSubtypeId);

        /// <summary>Exclude the block type from the simulation entirely.</summary>
        [ProtoMember(1)]
        public bool ExcludeFromSimulation;

        /// <summary>
        /// Thermal conductivity in real W/(m K) — the number a materials table gives.
        ///
        /// Mild steel 50, stainless 15, aluminium 237, copper 400. The game's pace is set once, in
        /// <see cref="ThermalConstants.ConductionScale"/>, so this stays a description of what the
        /// block is made of.
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
        public float ExposedSurfaceMultiplier;

        /// <summary>Fraction of produced power converted to heat, 0..1.</summary>
        [ProtoMember(20)]
        public float ProducerWasteEnergy;

        /// <summary>Fraction of consumed power converted to heat, 0..1.</summary>
        [ProtoMember(30)]
        public float ConsumerWasteEnergy;

        [ProtoMember(40)]
        public float CriticalTemperature;

        [ProtoMember(45)]
        public float OverheatDamagePerKelvin;


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

            if (lookup.TryGetBool(defId, GroupId, IgnoreId, out isTrue)
                || lookup.TryGetBool(defId, GroupId, LegacyIgnoreId, out isTrue))
                def.ExcludeFromSimulation = isTrue;

            double dvalue;
            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
                def.Conductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, EmissivityId, out dvalue))
                def.Emissivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SurfaceAreaScalerId, out dvalue))          
                def.ExposedSurfaceMultiplier = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ProducerWasteEnergyId, out dvalue))
                def.ProducerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConsumerWasteEnergyId, out dvalue))
                def.ConsumerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureId, out dvalue))
                def.CriticalTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureScalerId, out dvalue))
                def.OverheatDamagePerKelvin = (float)dvalue;

            def.Conductivity = Math.Max(0, def.Conductivity);

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.Emissivity = Math.Max(0, def.Emissivity);

            def.ExposedSurfaceMultiplier = Math.Max(0, def.ExposedSurfaceMultiplier);

            def.ProducerWasteEnergy = Math.Max(0, def.ProducerWasteEnergy);

            def.ConsumerWasteEnergy = Math.Max(0, def.ConsumerWasteEnergy);

            def.CriticalTemperature = Math.Max(0, def.CriticalTemperature);

            def.OverheatDamagePerKelvin = Math.Max(0, def.OverheatDamagePerKelvin);

            return def;
        }
    }
}
