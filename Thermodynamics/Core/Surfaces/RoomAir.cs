using System;
using System.Collections.Generic;
using VRageMath;

namespace Thermodynamics.Core
{
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

    public class RoomAirNode
    {
        public int RoomIndex;

        public Vector3I Anchor;

        public int CellCount;

        public float Volume;

        public float Temperature;

        public float Pressure;

        public float ThermalMass { get; private set; }


        public readonly List<RoomLink> Links = new List<RoomLink>();

        public bool Initialised;

        private float heatTimeScale = 1f;

        public float HeatTimeScale
        {
            get { return heatTimeScale; }
            set
            {
                heatTimeScale = value > 0f ? value : 1f;
                RefreshThermalMass();
            }
        }

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

        public bool HasAir
        {
            get { return Pressure > 0f && Volume > 0f && airDensity > 0f; }
        }

        public float AirMass
        {
            get { return Volume * airDensity * ThermalMath.Clamp01(Pressure); }
        }

        public float Energy
        {
            get { return Temperature * ThermalMass; }
        }


        public void RefreshThermalMass()
        {
            float capacity = (AirMass * ThermalConstants.AirSpecificHeat) / heatTimeScale;
            ThermalMass = Math.Max(ThermalConstants.MinimumThermalMass, capacity);
        }


        public override string ToString()
        {
            return "room " + RoomIndex + " " + Volume.ToString("n1") + "m3 "
                + Temperature.ToString("n1") + "K p=" + Pressure.ToString("n2");
        }
    }

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
