using System;
using VRageMath;

namespace Thermodynamics.Presentation
{
    public struct ThermalVisionTriangle
    {
        public Vector3 A, B, C, LocalNormal;
    }

    public interface IThermalVisionMeshSource
    {
        int TriangleCount { get; }

        bool TryRead(int index, out ThermalVisionTriangle triangle);
    }

    public sealed class ThermalVisionMeshBuild
    {
        public const int ModelLimit = 65536;
        private readonly IThermalVisionMeshSource source;
        private ThermalVisionTriangle[] scratch;
        private ThermalVisionTriangle[] result;
        private readonly ThermalVisionMeshBatch[] batches;
        public ThermalVisionMesh Mesh { get; private set; }
        public int Total { get; private set; }
        public int Read { get; private set; }
        public int Retained { get; private set; }
        public int Unsupported { get { return Read - Retained; } }
        public bool Complete { get { return Read == Total; } }
        public ThermalVisionTriangle[] Result { get { return Complete ? result : null; } }


        public ThermalVisionMeshBuild(IThermalVisionMeshSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            int count = source.TriangleCount;
            if (count < 0 || count > ModelLimit) throw new ArgumentException("Source triangle count exceeds the bounded model limit", "source");
            this.source = source;
            Total = count;
            scratch = new ThermalVisionTriangle[count];
            batches = new ThermalVisionMeshBatch[(count + ThermalVisionMeshBatch.Size - 1) / ThermalVisionMeshBatch.Size];
            if (count == 0) { result = scratch; scratch = null; Mesh = new ThermalVisionMesh(result, batches); }
        }


        public int Advance(int maxSourceTriangles)
        {
            if (maxSourceTriangles <= 0 || Complete) return 0;
            int amount = Math.Min(maxSourceTriangles, Total - Read);
            int end = Read + amount;
            while (Read < end)
            {
                ThermalVisionTriangle triangle;
                if (source.TryRead(Read, out triangle))
                {
                    triangle.LocalNormal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
                    batches[Retained / ThermalVisionMeshBatch.Size].Add(Retained, triangle);
                    scratch[Retained++] = triangle;
                }
                Read++;
            }
            if (Complete)
            {
                if (Retained == Total) result = scratch;
                else
                {
                    result = new ThermalVisionTriangle[Retained];
                    Array.Copy(scratch, result, Retained);
                }
                scratch = null;
                int used = (Retained + ThermalVisionMeshBatch.Size - 1) / ThermalVisionMeshBatch.Size;
                var publishedBatches = new ThermalVisionMeshBatch[used];
                Array.Copy(batches, publishedBatches, used);

                Mesh = new ThermalVisionMesh(result, publishedBatches);
            }
            return amount;
        }
    }
}
