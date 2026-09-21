using System;
using System.Collections.Generic;
using VRageMath;
using Region = Thermodynamics.Presentation.ThermalVisionRegionPartition.Region;

namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionRegionLod : IDisposable
    {
        public sealed class Band
        {
            public Vector3D Min, Max;
            public readonly ThermalVisionRegionScan Scan;
/// <summary>List operation.</summary>
            internal readonly List<Region> Samples = new List<Region>();
            public bool Overflow { get; internal set; }
            public bool Applied { get; internal set; }
/// <summary>Band operation.</summary>
            internal Band(Vector3D eye, double width, double cell, int capacity)
            {
/// <summary>Vector3D operation.</summary>
                Min = new Vector3D(Math.Floor((eye.X - width / 2) / cell) * cell,
                    Math.Floor((eye.Y - width / 2) / cell) * cell, Math.Floor((eye.Z - width / 2) / cell) * cell);
/// <summary>Vector3D operation.</summary>
                Max = Min + new Vector3D(width);
/// <summary>ThermalVisionRegionScan operation.</summary>
                Scan = new ThermalVisionRegionScan(cell, capacity, 1, width);
            }
        }
        public readonly Band[] Bands;
        private bool started;
/// <summary>ThermalVisionRegionLod operation.</summary>
        public ThermalVisionRegionLod(Vector3D eye)
        {
/// <summary>Band operation.</summary>
            Bands = new[] { new Band(eye, 320, 40, 192), new Band(eye, 80, 10, 192), new Band(eye, 20, 2.5, 512) };
        }
/// <summary>ThermalVisionRegionLod operation.</summary>
        public ThermalVisionRegionLod(Vector3D eye, Vector3D surface) : this(eye)
        {
            double factor = 1;
            double distance = Vector3D.Distance(eye, surface);
            while (distance > 40 * factor && factor < 16) factor *= 2;
            Bands[2].Scan.Dispose();
/// <summary>Band operation.</summary>
            Bands[2] = new Band(surface, 20 * factor, 2.5 * factor, 512);
        }
/// <summary>Observe operation.</summary>
        public void Observe(Region sample)
        {
            foreach (Band band in Bands)
            {
                Vector3D lo = Vector3D.Max(sample.Min, band.Min), hi = Vector3D.Min(sample.Max, band.Max);
                if (lo.X >= hi.X || lo.Y >= hi.Y || lo.Z >= hi.Z) continue;
                if (band.Samples.Count < 32768) band.Samples.Add(new Region(lo, hi, sample.Kelvin));
                else band.Overflow = true;
            }
        }
        public bool Running
        {
            get
            {
                if (!started) return true;
                foreach (Band band in Bands) if (band.Scan.Running) return true;
                return false;
            }
        }
/// <summary>Advance operation.</summary>
        public int Advance(int work)
        {
            if (!started)
            {
                foreach (Band band in Bands)
                    if (!band.Overflow) band.Scan.Start(new List<IEnumerable<Region>> { band.Samples });
                started = true;
            }
            for (int i = Bands.Length - 1; i >= 0; i--)
                if (Bands[i].Scan.Running) return Bands[i].Scan.Advance(work);
            return 0;
        }
/// <summary>TryBuild operation.</summary>
        public bool TryBuild(ThermalVisionRegionScan coarse, int limit, out ThermalVisionRegionOrder order)
        {
            order = null;
            if (Running) return false;
            var basis = ThermalVisionRegionPartition.FromScan(coarse, limit);
            if (basis == null) return false;
            for (int skip = 0; skip <= Bands.Length; skip++)
            {
                var field = basis;
                bool valid = true;
                for (int i = skip; i < Bands.Length; i++)
                {
                    Band band = Bands[i];
                    if (band.Overflow || band.Scan.Failure != null) continue;
                    ThermalVisionRegionPartition next;
                    if (!ThermalVisionRegionPartition.TryReplace(field, band.Scan, band.Min, band.Max, limit, out next))
                    { valid = false; break; }
                    field = next;
                }
                if (!valid || !ThermalVisionRegionOrder.TryBuild(field, limit, out order)) continue;
                for (int i = 0; i < Bands.Length; i++)
                    Bands[i].Applied = i >= skip && !Bands[i].Overflow && Bands[i].Scan.Failure == null;
                return true;
            }
            return false;
        }
/// <summary>Dispose operation.</summary>
        public void Dispose() { foreach (Band band in Bands) band.Scan.Dispose(); }
    }
}
