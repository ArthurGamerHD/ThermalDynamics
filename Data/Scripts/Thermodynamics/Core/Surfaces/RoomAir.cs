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
    /// The air inside one sealed room, treated as a single well-mixed mass — the same lumped
    /// model a coolant loop uses, with the room's volume in place of the fluid's.
    ///
    /// Air is a poor conductor with very little mass, so it does not store much heat; what it
    /// does is couple every surface bounding the room to every other one. A reactor in a sealed
    /// compartment warms the compartment, and the compartment warms the far bulkhead, without
    /// any conduction path between them.
    ///
    /// A room only holds air if something says it does: <see cref="Pressure"/> starts at zero and
    /// the host sets it from whatever it uses to track pressurisation. A room at zero pressure
    /// has no air mass and no links, and costs nothing.
    /// </summary>
    public class RoomAirNode
    {
        /// <summary>Index into <see cref="RoomMap.Rooms"/> this air belongs to.</summary>
        public int RoomIndex;

        /// <summary>
        /// Lexicographically smallest cell of the room. Stable while the room's shape is, which
        /// is what carries a room's temperature across the map rebuilds that ordinary building
        /// triggers elsewhere on the grid.
        /// </summary>
        public Vector3I Anchor;

        /// <summary>Cells the room occupies.</summary>
        public int CellCount;

        /// <summary>Room volume, m^3.</summary>
        public float Volume;

        /// <summary>Air temperature, K.</summary>
        public float Temperature;

        /// <summary>
        /// How full of air the room is, 0..1. Set by the host; 0 means vacuum, and a vacuum
        /// exchanges nothing.
        /// </summary>
        public float Pressure;

        /// <summary>Heat capacity of the air in the room, J/K.</summary>
        public float ThermalMass { get; private set; }

        /// <summary>Conduction paths to the surfaces bounding the room.</summary>
        public readonly List<RoomLink> Links = new List<RoomLink>();

        /// <summary>
        /// True once this air has a temperature that means something — carried over from the room
        /// as it was before a rebuild, or taken from the surfaces around it the first time it was
        /// filled. Until then the figure is a placeholder, and a placeholder must not be allowed
        /// to become a heat sink: a compartment that materialises at the vacuum default would
        /// drain the ship it appeared in.
        /// </summary>
        public bool Initialised;

        private float heatTimeScale = 1f;

        /// <summary>
        /// Divides heat capacity, exactly as it does for a block and for coolant. The air has to
        /// run on the same clock as the walls it touches.
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
