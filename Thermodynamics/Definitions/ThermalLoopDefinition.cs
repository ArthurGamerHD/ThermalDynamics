using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class ThermalLoopDefinition
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalLoopProperties");
        private static readonly MyStringId CoolantMassPerPipeId = MyStringId.GetOrCompute("CoolantMassPerPipe");
        private static readonly MyStringId CoolantDensityId = MyStringId.GetOrCompute("CoolantKilogramsPerCubicMetre");
        private static readonly MyStringId LargeGridFlowRateId = MyStringId.GetOrCompute("LargeGridFlowRate");
        private static readonly MyStringId SmallGridFlowRateId = MyStringId.GetOrCompute("SmallGridFlowRate");
        private static readonly MyStringId StagnantTransferId = MyStringId.GetOrCompute("StagnantTransferFraction");
        private static readonly MyStringId HeatTransferId = MyStringId.GetOrCompute("HeatTransferCoefficient");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId PipeContactMultiplierId = MyStringId.GetOrCompute("PipeContactMultiplier");
        private static readonly MyStringId SinkContactMultiplierId = MyStringId.GetOrCompute("SinkContactMultiplier");


        private static readonly MyStringId LegacyParcelRateId = MyStringId.GetOrCompute("SegmentsPerSecondAtFullFlow");
        private static readonly MyStringId LegacyFlowRateId = MyStringId.GetOrCompute("FlowRate");
        private static readonly MyStringId LegacyMassPerPipeId = MyStringId.GetOrCompute("MassPerPipe");
        private static readonly MyStringId LegacyPipeContactId = MyStringId.GetOrCompute("PipeSurfaceAreaScaler");
        private static readonly MyStringId LegacySinkContactId = MyStringId.GetOrCompute("PlateSurfaceAreaScaler");



        public static readonly MyDefinitionId DefaultLoopDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultLoopSubtypeId);

        [ProtoMember(1)]
        public float CoolantMassPerPipe;

        [ProtoMember(35)]
        public float CoolantKilogramsPerCubicMetre = 33f;

        [ProtoMember(5)]
        public float HeatTransferCoefficient = 1000f;

        [ProtoMember(10)]
        public float SpecificHeat = 3400f;

        [ProtoMember(15)]
        public float PipeContactMultiplier = 1f;

        [ProtoMember(20)]
        public float SinkContactMultiplier = 1f;

        [ProtoMember(25)]
        public float LargeGridFlowRate = 10f;

        public float SmallGridFlowRate = 10f;

        [ProtoMember(30)]
        public float StagnantTransferFraction = 0.16f;

        public static ThermalLoopDefinition GetDefinition(MyDefinitionId defId)
        {
            ThermalLoopDefinition def = new ThermalLoopDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            double dvalue;
            bool carriesGroup = lookup.TryGetDouble(defId, GroupId, PipeContactMultiplierId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacyPipeContactId, out dvalue);

            if (!lookup.DefinitionIdExists(defId) || !carriesGroup)
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);

                if (!lookup.DefinitionIdExists(defId))
                {
                    defId = DefaultLoopDefinitionId;
                }
            }

            if (lookup.TryGetDouble(defId, GroupId, CoolantMassPerPipeId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacyMassPerPipeId, out dvalue))
                def.CoolantMassPerPipe = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CoolantDensityId, out dvalue))
                def.CoolantKilogramsPerCubicMetre = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, HeatTransferId, out dvalue))
                def.HeatTransferCoefficient = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PipeContactMultiplierId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacyPipeContactId, out dvalue))
                def.PipeContactMultiplier = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SinkContactMultiplierId, out dvalue)
                || lookup.TryGetDouble(defId, GroupId, LegacySinkContactId, out dvalue))
                def.SinkContactMultiplier = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, LargeGridFlowRateId, out dvalue))
            {
                def.LargeGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyFlowRateId, out dvalue))
            {
                def.LargeGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyParcelRateId, out dvalue))
            {
                def.LargeGridFlowRate = (float)dvalue * LoopThermalProperties.LargeGridCellMetres;
            }

            if (lookup.TryGetDouble(defId, GroupId, SmallGridFlowRateId, out dvalue))
            {
                def.SmallGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyFlowRateId, out dvalue))
            {
                def.SmallGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyParcelRateId, out dvalue))
            {
                def.SmallGridFlowRate = (float)dvalue * LoopThermalProperties.SmallGridCellMetres;
            }

            if (lookup.TryGetDouble(defId, GroupId, StagnantTransferId, out dvalue))
                def.StagnantTransferFraction = (float)dvalue;


            def.CoolantMassPerPipe = Math.Max(0, def.CoolantMassPerPipe);
            def.CoolantKilogramsPerCubicMetre = Math.Max(0, def.CoolantKilogramsPerCubicMetre);

            def.LargeGridFlowRate = Math.Max(0, def.LargeGridFlowRate);
            def.SmallGridFlowRate = Math.Max(0, def.SmallGridFlowRate);

            def.StagnantTransferFraction = Math.Min(1, Math.Max(0, def.StagnantTransferFraction));

            def.HeatTransferCoefficient = Math.Max(0, def.HeatTransferCoefficient);

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.PipeContactMultiplier = Math.Max(0, def.PipeContactMultiplier);

            def.SinkContactMultiplier = Math.Max(0, def.SinkContactMultiplier);

            return def;
        }
    }
}

