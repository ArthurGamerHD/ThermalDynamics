using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
    /// <summary>A conduction path between a room's air and one block node.</summary>
    public struct RoomLink
    {
        public int NodeIndex;
        public float Conductance;

        public RoomLink(int nodeIndex, float conductance)
        {
            NodeIndex = nodeIndex;
            Conductance = conductance;
        }
    }

    /// <summary>
    /// The air inside one sealed room as a single well-mixed mass. It stores little heat; what it does
    /// is couple every surface bounding the room to every other, which nothing else can. A room holds
    /// air only when the host says so, and one at zero pressure has no mass, no links and no cost.
    /// See thermal-model.md, Room air.
    /// </summary>
    public class RoomAirNode
    {
        /// <summary>The room this air belongs to, in the numbering <see cref="RoomMap.CellsOf"/> reads.</summary>
        public int RoomIndex;

        /// <summary>
        /// Lexicographically smallest cell of the room. Stable while the room's shape is, which is
        /// what carries a room's temperature across map rebuilds triggered elsewhere on the grid.
        /// </summary>
        public Vector3I Anchor;

        /// <summary>Cells the room occupies.</summary>
        public int CellCount;

        /// <summary>Room volume, m^3.</summary>
        public float Volume;

        /// <summary>Air temperature, K.</summary>
        public float Temperature;

        /// <summary>
        /// How full of air the room is, 0..1. Set by the host. Zero is vacuum, which exchanges
        /// nothing.
        /// </summary>
        public float Pressure;

        /// <summary>Heat capacity of the air in the room, J/K.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>Conduction paths to the surfaces bounding the room.</summary>
        public readonly List<RoomLink> Links = new List<RoomLink>();

        /// <summary>
        /// True once this air holds a meaningful temperature: carried over from the room before a
        /// rebuild, or taken from the surrounding surfaces the first time it was filled. Until then
        /// the value is a placeholder, and a compartment appearing at the vacuum default would act
        /// as a heat sink on the grid around it.
        /// </summary>
        public bool Initialised;

        private float heatTimeScale = 1f;

        /// <summary>
        /// Divisor applied to heat capacity, as for a block and for coolant, so the air runs on the
        /// same clock as the walls it touches.
        /// </summary>
        public float HeatTimeScale
        {
            get { return heatTimeScale; }
            set
            {
                heatTimeScale = value > 0f ? value : 1f;
                RefreshThermalMass();
            }
        }

        /// <summary>Air density at full pressure, kg/m^3.</summary>
        public float AirDensity
        {
            get { return airDensity; }
            set
            {
                airDensity = value > 0f ? value : 0f;
                RefreshThermalMass();
            }
        }

        private float airDensity = 1.225f;

        /// <summary>True when the room holds enough air to exchange anything.</summary>
        public bool HasAir
        {
            get { return Pressure > 0f && Volume > 0f && airDensity > 0f; }
        }

        /// <summary>Air mass in the room, kg.</summary>
        public float AirMass
        {
            get { return Volume * airDensity * Clamp01(Pressure); }
        }

        /// <summary>Total stored energy, J. Part of the grid's conservation check.</summary>
        public float Energy
        {
            get { return Temperature * ThermalMass; }
        }

        public void RefreshThermalMass()
        {
            float capacity = (AirMass * ThermalConstants.AirSpecificHeat) / heatTimeScale;
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, capacity);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        public override string ToString()
        {
            return "room " + RoomIndex + " " + Volume.ToString("n1") + "m3 "
                + Temperature.ToString("n1") + "K p=" + Pressure.ToString("n2");
        }
    }

    /// <summary>
    /// How many faces of one block look onto one room. Produced by
    /// <see cref="SurfaceMap.GetRoomContacts"/>.
    /// </summary>
    public struct RoomContact
    {
        public int RoomIndex;
        public int Faces;

        public RoomContact(int roomIndex, int faces)
        {
            RoomIndex = roomIndex;
            Faces = faces;
        }
    }
}
