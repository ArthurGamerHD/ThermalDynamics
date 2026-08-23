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

        /// <summary>
        /// Names these properties used to carry, still read when the current name is absent: Definition
        /// Extensions matches on the string, so dropping one silently reverts every third-party
        /// definition to the defaults. See definitions.md, Retired property names.
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
        /// Grey-body emissivity, 0..1: what leaves the block as thermal radiation. Reference values:
        /// https://www.engineeringtoolbox.com/emissivity-coefficients-d_447.html
        /// </summary>
        [ProtoMember(15)]
        public float Emissivity;

        /// <summary>
        /// Solar absorptivity, 0..1: what the surface takes in from the sun and from point sources.
        /// Undeclared, it follows the emissivity, which is what every block did before this property
        /// existed. See definitions.md, Emissivity and absorptivity are two numbers.
        /// </summary>
        [ProtoMember(16)]
        public float SolarAbsorptivity;

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

        /// <summary>
        /// Watts a block makes because of what it is rather than because of power crossing it, W —
        /// decay heat, a forge, a wreck still burning. The one property where omission is the right
        /// default. See definitions.md, Every property but one must be declared.
        /// </summary>
        [ProtoMember(50)]
        public float HeatSourceWatts;

        /// <summary>
        /// Which properties an actual definition declared, one bit per property. An undeclared one
        /// arrives as zero, so this is what lets <c>ThermalBlockCatalog</c> fill the rest from the
        /// derivation rather than from zeros. See definitions.md, Lookup and fallback.
        /// </summary>
        public DeclaredProperties Declared;

        [Flags]
        public enum DeclaredProperties
        {
            None = 0,
            Conductivity = 1,
            SpecificHeat = 2,
            Emissivity = 4,

            // Beside the emissivity it follows, and at the next free bit rather than the next
            // number: the values are a wire format and moving one would re-read every other flag.
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

        /// <summary>Which of the three entries the lookup ended up reading.</summary>
        public enum Resolution
        {
            /// <summary>The block's own subtype entry. The most specific thing an author can write.</summary>
            Subtype,

            /// <summary>The <c>DefaultThermodynamics</c> entry for the block's type.</summary>
            Type,

            /// <summary>
            /// The environment-wide default: nothing here knows anything about this block. Taken as
            /// *no* answer rather than as an answer, since a block's own build cost describes it
            /// better than a global constant can.
            /// </summary>
            Fallback,
        }

        /// <summary>Which entry supplied <see cref="Declared"/>.</summary>
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
