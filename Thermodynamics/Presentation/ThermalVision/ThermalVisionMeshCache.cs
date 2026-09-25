using System;
using System.Collections.Generic;

namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionMeshCache<TKey>
    {
        private sealed class Entry
        {
            public TKey Key;
            public ThermalVisionMesh Mesh;
            public bool Partial;
        }
        private readonly int modelLimit, triangleLimit;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new Dictionary<TKey, LinkedListNode<Entry>>();

        private readonly LinkedList<Entry> order = new LinkedList<Entry>();
        public int Count { get { return entries.Count; } }
        public int Triangles { get; private set; }
        public long Evictions { get; private set; }


        public ThermalVisionMeshCache(int modelLimit, int triangleLimit)
        {
            if (modelLimit < 1 || triangleLimit < 1) throw new ArgumentException("Positive cache limits required");
            this.modelLimit = modelLimit;
            this.triangleLimit = triangleLimit;
        }


        public bool TryGetValue(TKey key, out ThermalVisionMesh mesh, out bool partial)
        {
            LinkedListNode<Entry> node;
            if (!entries.TryGetValue(key, out node)) { mesh = null; partial = false; return false; }
            order.Remove(node);
            order.AddLast(node);
            mesh = node.Value.Mesh;
            partial = node.Value.Partial;
            return true;
        }


        public void Add(TKey key, ThermalVisionMesh mesh, bool partial)
        {
            if (mesh == null || mesh.Triangles.Length > triangleLimit)
                throw new ArgumentException("Mesh exceeds cache capacity");
            if (entries.ContainsKey(key)) throw new ArgumentException("Duplicate cached model");
            while (Count >= modelLimit || mesh.Triangles.Length > triangleLimit - Triangles)
            {
                Entry oldest = order.First.Value;
                Triangles -= oldest.Mesh.Triangles.Length;
                entries.Remove(oldest.Key);
                order.RemoveFirst();
                Evictions++;
            }
            var node = order.AddLast(new Entry { Key = key, Mesh = mesh, Partial = partial });
            entries.Add(key, node);
            Triangles += mesh.Triangles.Length;
        }


        public void Clear()
        {
            entries.Clear(); order.Clear(); Triangles = 0; Evictions = 0;
        }
    }
}
