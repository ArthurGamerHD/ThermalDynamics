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
    public class ThermalLoopDefintion
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalLoopProperties");
        // Renamed from "Mass" deliberately. The figure changed meaning from kilograms per loop to
        // kilograms per pipe, so a world still carrying the old element must not have 500 read as a
        // per-pipe charge — ten times the fluid it asked for. An unrecognised name falls to the field
        // default below instead, which is the safe outcome.
        private static readonly MyStringId MassPerPipeId = MyStringId.GetOrCompute("MassPerPipe");
        private static readonly MyStringId SegmentsPerSecondId = MyStringId.GetOrCompute("SegmentsPerSecondAtFullFlow");
        private static readonly MyStringId StagnantTransferId = MyStringId.GetOrCompute("StagnantTransferFraction");
        private static readonly MyStringId ConductivityId = MyStringId.GetOrCompute("Conductivity");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId PipeSurfaceAreaScalerId = MyStringId.GetOrCompute("PipeSurfaceAreaScaler");
        private static readonly MyStringId PlateSurfaceAreaScalerId = MyStringId.GetOrCompute("PlateSurfaceAreaScaler");

        public static readonly MyDefinitionId DefaultLoopDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultLoopSubtypeId);

        /// <summary>
        /// Coolant per pipe block, kg. Defaults live on the fields rather than in a factory: an
        /// element added to the group after a world's Loops.xml was written is absent from that file,
        /// and a reader that finds nothing leaves the field alone. Zero here would clamp to 1 kg and
        /// silently give that world almost no coolant.
        /// </summary>
        [ProtoMember(1)]
        public float MassPerPipe = 50f;

        /// <summary>
        /// Thermal conductivity of the coolant, W/(m K). Reference values:
        /// https://www.engineeringtoolbox.com/thermal-conductivity-metals-d_858.html
        /// </summary>
        [ProtoMember(5)]
        public float Conductivity = 1f;

        /// <summary>
        /// Specific heat capacity of the coolant, J/(kg K). Reference values:
        /// https://en.wikipedia.org/wiki/Table_of_specific_heat_capacities
        /// </summary>
        [ProtoMember(10)]
        public float SpecificHeat = 3400f;

        /// <summary>Contact area scaler between the coolant and a pipe segment.</summary>
        [ProtoMember(15)]
        public float PipeSurfaceAreaScaler = 1f;

        /// <summary>Contact area scaler between the coolant and a block on a sink face.</summary>
        [ProtoMember(20)]
        public float PlateSurfaceAreaScaler = 1f;

        /// <summary>Coolant parcels a full-flow pump pushes past a point each second.</summary>
        [ProtoMember(25)]
        public float SegmentsPerSecondAtFullFlow = 4f;

        /// <summary>Share of transfer that survives with no circulation, 0..1.</summary>
        [ProtoMember(30)]
        public float StagnantTransferFraction = 1f;

        public static ThermalLoopDefintion GetDefinition(MyDefinitionId defId)
        {
            ThermalLoopDefintion def = new ThermalLoopDefintion();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            double dvalue;
            if (!lookup.DefinitionIdExists(defId) || !lookup.TryGetDouble(defId, GroupId, PipeSurfaceAreaScalerId, out dvalue))
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);

                if (!lookup.DefinitionIdExists(defId))
                {
                    defId = DefaultLoopDefinitionId;
                }
            }

            if (lookup.TryGetDouble(defId, GroupId, MassPerPipeId, out dvalue))
                def.MassPerPipe = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
                def.Conductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PipeSurfaceAreaScalerId, out dvalue))
                def.PipeSurfaceAreaScaler = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, PlateSurfaceAreaScalerId, out dvalue))
                def.PlateSurfaceAreaScaler = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SegmentsPerSecondId, out dvalue))
                def.SegmentsPerSecondAtFullFlow = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, StagnantTransferId, out dvalue))
                def.StagnantTransferFraction = (float)dvalue;


            def.MassPerPipe = Math.Max(1, def.MassPerPipe);

            def.SegmentsPerSecondAtFullFlow = Math.Max(0, def.SegmentsPerSecondAtFullFlow);

            def.StagnantTransferFraction = Math.Min(1, Math.Max(0, def.StagnantTransferFraction));

            def.Conductivity = Math.Min(1, Math.Max(0, def.Conductivity));

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.PipeSurfaceAreaScaler = Math.Max(0, def.PipeSurfaceAreaScaler);

            def.PlateSurfaceAreaScaler = Math.Max(0, def.PlateSurfaceAreaScaler);

            return def;
        }
    }
}

