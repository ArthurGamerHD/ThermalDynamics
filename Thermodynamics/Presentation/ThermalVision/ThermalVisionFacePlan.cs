using System;
using System.Collections.Generic;
using VRageMath;
using Region = Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;

namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionFacePlan
    {
        private struct Key : IEquatable<Key>
        {
            public Vector3D Min, Max;
            public int Axis;
/// <summary>Equals operation.</summary>
            public bool Equals(Key other) { return Axis == other.Axis && Min == other.Min && Max == other.Max; }
            public override bool Equals(object other) { return other is Key && Equals((Key)other); }
/// <summary>Returns the hashcode.</summary>
            public override int GetHashCode() { return Min.GetHashCode() ^ (Max.GetHashCode() * 397) ^ Axis; }
        }
        private struct Owner { public int Index, Side; }
        private readonly Dictionary<Key, Owner> faces = new Dictionary<Key, Owner>();
/// <summary>List operation.</summary>
        private readonly List<int> masks = new List<int>();
        public int Removed { get; private set; }
/// <summary>Skip operation.</summary>
        public bool Skip(int region, int axis, bool upper)
        { return (masks[region] & (1 << (axis * 2 + (upper ? 1 : 0)))) != 0; }

/// <summary>Builds the API method table.</summary>
        public void Build(IList<Region> regions, Vector3D eye)
        {
            faces.Clear(); masks.Clear(); Removed = 0;
            for (int i = 0; i < regions.Count; i++) masks.Add(0);
            for (int i = 0; i < regions.Count; i++)
                for (int axis = 0; axis < 3; axis++) for (int side = 0; side < 2; side++)
                {
                    var region = regions[i];
                    Vector3D min = region.Min, max = region.Max;
/// <summary>Coordinate operation.</summary>
                    double plane = Coordinate(side == 0 ? min : max, axis);
                    if (axis == 0) min.X = max.X = plane;
                    else if (axis == 1) min.Y = max.Y = plane;
                    else min.Z = max.Z = plane;
                    var key = new Key { Min = min, Max = max, Axis = axis };
                    Owner other;
                    if (!faces.TryGetValue(key, out other))
                    { faces.Add(key, new Owner { Index = i, Side = side }); continue; }
                    if (other.Side == side) continue;
                    bool equal = regions[other.Index].Kelvin == region.Kelvin;
                    if (equal || !Entering(eye, axis, plane, side)) Remove(i, axis, side);
                    if (equal || !Entering(eye, axis, plane, other.Side)) Remove(other.Index, axis, other.Side);
                }
        }
/// <summary>PairedFaces operation.</summary>
        public static int[] PairedFaces(IList<Region> regions)
        {
/// <summary>Dictionary operation.</summary>
            var lookup=new Dictionary<Key,Owner>(); var result=new int[regions.Count];
            for(int i=0;i<regions.Count;i++) for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
            {
                Vector3D min=regions[i].Min, max=regions[i].Max;
                double plane=Coordinate(side==0?min:max,axis);
                if(axis==0) min.X=max.X=plane;
                else if(axis==1) min.Y=max.Y=plane;
                else min.Z=max.Z=plane;
                var key=new Key { Min=min,Max=max,Axis=axis }; Owner other;
                if(!lookup.TryGetValue(key,out other)) { lookup.Add(key,new Owner { Index=i,Side=side }); continue; }
                if(other.Side==side) continue;
                result[i]|=1<<(axis*2+side);
                result[other.Index]|=1<<(axis*2+other.Side);
            }
            return result;
        }

/// <summary>Removes the .</summary>
        private void Remove(int index, int axis, int side)
        {
            int flag = 1 << (axis * 2 + side);
            if ((masks[index] & flag) == 0) { masks[index] |= flag; Removed++; }
        }
/// <summary>Entering operation.</summary>
        private static bool Entering(Vector3D eye, int axis, double plane, int side)
        { return (Coordinate(eye, axis) - plane) * (side == 1 ? 1 : -1) > 0; }
/// <summary>Coordinate operation.</summary>
        private static double Coordinate(Vector3D v, int axis) { return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z; }
    }
}
