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
    public class ThermalLoopDefinition
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalLoopProperties");
        // Renamed from "Mass" deliberately. The figure changed meaning from kilograms per loop to
        // kilograms per pipe, so a world still carrying the old element must not have 500 read as a
        // per-pipe charge — ten times the fluid it asked for. An unrecognised name falls to the field
        // default below instead, which is the safe outcome.
        private static readonly MyStringId CoolantMassPerPipeId = MyStringId.GetOrCompute("CoolantMassPerPipe");
        private static readonly MyStringId LargeGridFlowRateId = MyStringId.GetOrCompute("LargeGridFlowRate");
        private static readonly MyStringId SmallGridFlowRateId = MyStringId.GetOrCompute("SmallGridFlowRate");
        private static readonly MyStringId StagnantTransferId = MyStringId.GetOrCompute("StagnantTransferFraction");
        private static readonly MyStringId HeatTransferId = MyStringId.GetOrCompute("HeatTransferCoefficient");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId PipeContactMultiplierId = MyStringId.GetOrCompute("PipeContactMultiplier");
        private static readonly MyStringId SinkContactMultiplierId = MyStringId.GetOrCompute("SinkContactMultiplier");


        /// <summary>
        /// Names these properties used to carry, still read when the current name is absent. See
        /// the note on <c>ThermalCellDefinition</c>: Definition Extensions matches on the string,
        /// so dropping the old name would silently revert third-party loop definitions to defaults.
        /// </summary>
        private static readonly MyStringId LegacyParcelRateId = MyStringId.GetOrCompute("SegmentsPerSecondAtFullFlow");
        private static readonly MyStringId LegacyFlowRateId = MyStringId.GetOrCompute("FlowRate");
        private static readonly MyStringId LegacyMassPerPipeId = MyStringId.GetOrCompute("MassPerPipe");
        private static readonly MyStringId LegacyPipeContactId = MyStringId.GetOrCompute("PipeSurfaceAreaScaler");
        private static readonly MyStringId LegacySinkContactId = MyStringId.GetOrCompute("PlateSurfaceAreaScaler");

        /// <summary>Space Engineers' two cell sizes, used to convert the retired parcel-rate name.</summary>
        private const float LargeGridCellMetres = 2.5f;
        private const float SmallGridCellMetres = 0.5f;

        public static readonly MyDefinitionId DefaultLoopDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultLoopSubtypeId);

        /// <summary>
        /// Coolant per pipe block, kg. Defaults live on the fields rather than in a factory: an
        /// element added to the group after a world's Loops.xml was written is absent from that file,
        /// and a reader that finds nothing leaves the field alone. Zero here would clamp to 1 kg and
        /// silently give that world almost no coolant.
        /// </summary>
        [ProtoMember(1)]
        public float CoolantMassPerPipe = 50f;

        /// <summary>
        /// Thermal conductivity of the coolant, W/(m K). Reference values:
        /// https://www.engineeringtoolbox.com/thermal-conductivity-metals-d_858.html
        /// </summary>
        [ProtoMember(5)]
        /// <summary>Fluid-to-wall heat transfer coefficient, W/(m²·K). See thermal-model.md, Coolant loops.</summary>
        public float HeatTransferCoefficient = 160f;

        /// <summary>
        /// Specific heat capacity of the coolant, J/(kg K). Reference values:
        /// https://en.wikipedia.org/wiki/Table_of_specific_heat_capacities
        /// </summary>
        [ProtoMember(10)]
        public float SpecificHeat = 3400f;

        /// <summary>Contact area scaler between the coolant and a pipe segment.</summary>
        [ProtoMember(15)]
        public float PipeContactMultiplier = 1f;

        /// <summary>Contact area scaler between the coolant and a block on a sink face.</summary>
        [ProtoMember(20)]
        public float SinkContactMultiplier = 1f;

        /// <summary>Coolant parcels a full-flow pump pushes past a point each second.</summary>
        [ProtoMember(25)]
        public float LargeGridFlowRate = 10f;

        public float SmallGridFlowRate = 10f;

        /// <summary>Share of transfer that survives with no circulation, 0..1.</summary>
        [ProtoMember(30)]
        public float StagnantTransferFraction = 1f;

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

            // Flow, newest name first. Both retired spellings are still read, and neither means
            // quite the same thing, so each converts rather than being copied across.
            if (lookup.TryGetDouble(defId, GroupId, LargeGridFlowRateId, out dvalue))
            {
                def.LargeGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyFlowRateId, out dvalue))
            {
                // One rate for both grids, which is what the single dial meant.
                def.LargeGridFlowRate = (float)dvalue;
            }
            else if (lookup.TryGetDouble(defId, GroupId, LegacyParcelRateId, out dvalue))
            {
                // Parcels per second: one parcel is one pipe block, so the speed a definition
                // written this way actually produced depended on the grid it was built on.
                def.LargeGridFlowRate = (float)dvalue * LargeGridCellMetres;
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
                def.SmallGridFlowRate = (float)dvalue * SmallGridCellMetres;
            }

            if (lookup.TryGetDouble(defId, GroupId, StagnantTransferId, out dvalue))
                def.StagnantTransferFraction = (float)dvalue;


            def.CoolantMassPerPipe = Math.Max(1, def.CoolantMassPerPipe);

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

