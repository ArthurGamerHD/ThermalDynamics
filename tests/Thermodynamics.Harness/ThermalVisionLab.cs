using Thermodynamics.Presentation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ThermalVisionLab
    {
        public sealed class Source : IThermalVisionMeshSource
        {
            public int TriangleCount { get; }
            public int Reads { get; private set; }
            private readonly int unsupportedEvery;
/// <summary>Source operation.</summary>
            public Source(int count, int unsupportedEvery = 0) { TriangleCount = count; this.unsupportedEvery = unsupportedEvery; }
/// <summary>TryRead operation.</summary>
            public bool TryRead(int index, out ThermalVisionTriangle triangle)
            {
                if (index != Reads) throw new InvalidOperationException("Extraction skipped or repeated a source index.");
                Reads++;
                float x = (index % 16) * .1f;
/// <summary>Vector3 operation.</summary>
                triangle = new ThermalVisionTriangle { A = new Vector3(x, 0, 0), B = new Vector3(x + .1f, 0, 0), C = new Vector3(x, .1f, 0) };
                return unsupportedEvery == 0 || index % unsupportedEvery != 0;
            }
        }

        public sealed class HullSource : IThermalVisionMeshSource
        {
            private readonly int divisions;
            public int TriangleCount { get { return 12 * divisions * divisions; } }
/// <summary>HullSource operation.</summary>
            public HullSource(int divisions) { this.divisions = divisions; }
/// <summary>TryRead operation.</summary>
            public bool TryRead(int index, out ThermalVisionTriangle triangle)
            {
                int faceCount = 2 * divisions * divisions;
                int face = index / faceCount, cell = (index % faceCount) / 2;
                Vector3 origin, u, v;
                switch (face)
                {
/// <summary>Vector3 operation.</summary>
                    case 0: origin = new Vector3(-1, -1, 1); u = Vector3.UnitX; v = Vector3.UnitY; break;
/// <summary>Vector3 operation.</summary>
                    case 1: origin = new Vector3(1, -1, -1); u = -Vector3.UnitX; v = Vector3.UnitY; break;
/// <summary>Vector3 operation.</summary>
                    case 2: origin = new Vector3(1, -1, 1); u = -Vector3.UnitZ; v = Vector3.UnitY; break;
/// <summary>Vector3 operation.</summary>
                    case 3: origin = new Vector3(-1, -1, -1); u = Vector3.UnitZ; v = Vector3.UnitY; break;
/// <summary>Vector3 operation.</summary>
                    case 4: origin = new Vector3(-1, 1, 1); u = Vector3.UnitX; v = -Vector3.UnitZ; break;
/// <summary>Vector3 operation.</summary>
                    default: origin = new Vector3(-1, -1, -1); u = Vector3.UnitX; v = Vector3.UnitZ; break;
                }
                float step = 2f / divisions;
                Vector3 a = origin + u * ((cell % divisions) * step) + v * ((cell / divisions) * step);
                Vector3 b = a + u * step, c = b + v * step, d = a + v * step;
                triangle = index % 2 == 0 ? new ThermalVisionTriangle { A = a, B = b, C = c }
                    : new ThermalVisionTriangle { A = a, B = c, C = d };
                return true;
            }
        }

/// <summary>BatchCandidates operation.</summary>
        public static int BatchCandidates(ThermalVisionMesh mesh, Vector3D eye)
        {
            int count = 0;
            foreach (var batch in mesh.Batches) if (!batch.IsEntirelyBackFacing(eye)) count += batch.Count;
            return count;
        }

        public sealed class Frame
        {
            public const int Width = 96, Height = 64;
            public readonly float[] Temperature = new float[Width * Height];
            private readonly double[] depth = new double[Width * Height];
/// <summary>Frame operation.</summary>
            public Frame()
            {
                for (int i = 0; i < depth.Length; i++) { depth[i] = double.NegativeInfinity; Temperature[i] = float.NaN; }
            }
/// <summary>Raster operation.</summary>
            public void Raster(ThermalVisionWorldTriangle triangle, float kelvin)
            {
                Vector3D a = triangle.A, b = triangle.B, c = triangle.C;
                double denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(denominator) < 1e-12) return;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    double px = (x + .5) / Width * 6 - 3, py = 2 - (y + .5) / Height * 4;
                    double u = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denominator;
                    double v = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denominator;
                    double w = 1 - u - v;
                    if (u < 0 || v < 0 || w < 0) continue;
                    double z = u * a.Z + v * b.Z + w * c.Z;
                    int index = y * Width + x;
                    if (z > depth[index]) { depth[index] = z; Temperature[index] = kelvin; }
                }
            }
/// <summary>Svg operation.</summary>
            public string Svg(ThermalVisionState.Mode mode)
            {
/// <summary>StringBuilder operation.</summary>
                var sb = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 64' shape-rendering='crispEdges'><rect width='96' height='64' fill='#18212b'/>");
                for (int i = 0; i < Temperature.Length; i++)
                {
                    Vector3 rgb;
                    if (!ThermalVisionPalette.TrySample(Temperature[i], mode, 200, 800, out rgb)) continue;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "<rect x='{0}' y='{1}' width='1' height='1' fill='rgb({2},{3},{4})'/>",
                        i % Width, i / Width, (int)(rgb.X * 255), (int)(rgb.Y * 255), (int)(rgb.Z * 255));
                }
                return sb.Append("</svg>").ToString();
            }
        }

/// <summary>HullFrame operation.</summary>
        public static Frame HullFrame(bool batched)
        {
/// <summary>ThermalVisionMeshBuild operation.</summary>
            var build = new ThermalVisionMeshBuild(new HullSource(8));
            build.Advance(ThermalVisionMeshBuild.ModelLimit);
/// <summary>Frame operation.</summary>
            var frame = new Frame();
            var world = MatrixD.CreateRotationY(.6) * MatrixD.CreateRotationX(.25);
/// <summary>Vector3D operation.</summary>
            var eye = new Vector3D(0, 0, 5);
            Vector3D localEye;
            bool localCull = ThermalVisionGeometry.TryGetLocalEye(world, eye, out localEye);
            foreach (var batch in build.Mesh.Batches)
            {
                if (batched && localCull && batch.IsEntirelyBackFacing(localEye)) continue;
                for (int i = batch.Start; i < batch.Start + batch.Count; i++)
                {
                    ThermalVisionWorldTriangle projected;
                    if (ThermalVisionGeometry.Project(build.Result[i], world, eye, localCull, localEye, out projected) == ThermalVisionProjection.Visible)
                        frame.Raster(projected, 450);
                }
            }
            return frame;
        }

/// <summary>OcclusionScene operation.</summary>
        public static Frame OcclusionScene(bool reverse)
        {
/// <summary>Frame operation.</summary>
            var frame = new Frame();
            if (reverse) { Quad(frame, 300, 0); Quad(frame, 750, -1); }
            else { Quad(frame, 750, -1); Quad(frame, 300, 0); }
            Submit(frame, new Vector3(1.2f, -1, 0), new Vector3(2.7f, -1, 0), new Vector3(2.7f, 1, 0), 650);
            return frame;
        }

/// <summary>Quad operation.</summary>
        private static void Quad(Frame frame, float kelvin, float z)
        {
            Submit(frame, new Vector3(-1, -1, z), new Vector3(1, -1, z), new Vector3(1, 1, z), kelvin);
            Submit(frame, new Vector3(-1, -1, z), new Vector3(1, 1, z), new Vector3(-1, 1, z), kelvin);
        }
/// <summary>Submit operation.</summary>
        private static void Submit(Frame frame, Vector3 a, Vector3 b, Vector3 c, float kelvin)
        {
            var triangle = new ThermalVisionTriangle { A = a, B = b, C = c, LocalNormal = Vector3.Cross(b - a, c - a) };
            ThermalVisionWorldTriangle projected;
/// <summary>Vector3D operation.</summary>
            var eye = new Vector3D(0, 0, 5);
            if (ThermalVisionGeometry.Project(triangle, MatrixD.Identity, eye, true, eye, out projected) == ThermalVisionProjection.Visible)
                frame.Raster(projected, kelvin);
        }

/// <summary>WriteReport operation.</summary>
        public static string WriteReport(string directory)
        {
            Directory.CreateDirectory(directory);
/// <summary>StringBuilder operation.</summary>
            var sb = new StringBuilder("# Thermal vision synthetic lab\n\nScope: production extraction, projection, palettes and exposure; synthetic source triangles and an independent software depth oracle. No game GPU, shader, material, occlusion-query or exact camera-trace replay.\n\n");
            sb.Append("Machine: ").Append(Environment.MachineName).Append("; runtime: ").Append(Environment.Version)
                .Append("; OS: ").Append(Environment.OSVersion).Append(". Timings are this offline process, include cold/JIT effects and do not estimate engine frame time.\n\n");
/// <summary>ThermalVisionAutoRange operation.</summary>
            var exposure = new ThermalVisionAutoRange();
/// <summary>ThermalVisionState operation.</summary>
            var optics = new ThermalVisionState();
/// <summary>StringBuilder operation.</summary>
            var trace = new StringBuilder("frame,viewpoint,mode,low_kelvin,high_kelvin\n");
            optics.Enable(ThermalVisionState.Mode.Cividis, 42);
            for (int frame = 0; frame < 360; frame++)
            {
                long viewpoint = frame >= 120 && frame < 150 ? 0 : 42;
                if (frame == 180) optics.Enable(ThermalVisionState.Mode.WhiteHot, 42);
                bool active = optics.Validate(viewpoint);
                if (frame >= 120 && frame < 180 && active) throw new InvalidOperationException("Optics reactivated without request.");
                if (active)
                {
                    exposure.BeginSamples(); exposure.Observe(230);
                    if (frame >= 60 && frame < 120 || frame >= 180 && frame < 240) exposure.Observe(900);
                    exposure.Update(1.0 / 60);
                }
                trace.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3:0.000},{4:0.000}\n", frame, viewpoint, optics.Current, exposure.Low, exposure.High);
            }
            File.WriteAllText(Path.Combine(directory, "adaptation.csv"), trace.ToString());
            sb.Append("Adaptation/state trace: 360 synthetic frames at 60 Hz; cold scene, hot entry, viewpoint loss, explicit greyscale reactivation, hot departure. Automatic reactivation is asserted absent. See adaptation.csv.\n\n");
            sb.Append("Model counts include the 26,914 and 61,199 source-triangle cases from the September 18 game dumps; their geometry is synthetic.\n\n| Source triangles | Build slices | Retained | Offline build ms |\n| --- | --- | --- | --- |\n");
            foreach (int count in new[] { 12, 8192, 8193, 26914, 61199, 65536 })
            {
/// <summary>Source operation.</summary>
                var source = new Source(count, 7);
/// <summary>ThermalVisionMeshBuild operation.</summary>
                var build = new ThermalVisionMeshBuild(source);
                int slices = 0;
                var watch = Stopwatch.StartNew();
                while (!build.Complete)
                {
                    build.Advance(ThermalVisionScenePolicy.BuildTriangleLimit); slices++;
                    if (!build.Complete && build.Result != null) throw new InvalidOperationException("Partial model published.");
                }
                watch.Stop();
                if (source.Reads != count || build.Retained + build.Unsupported != count) throw new InvalidOperationException("Lost triangles.");
                sb.AppendFormat(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3:0.000} |\n", count, slices, build.Retained, watch.Elapsed.TotalMilliseconds);
            }
/// <summary>ThermalVisionMeshBuild operation.</summary>
            var hull = new ThermalVisionMeshBuild(new HullSource(72));
            while (!hull.Complete) hull.Advance(ThermalVisionScenePolicy.BuildTriangleLimit);
/// <summary>BatchCandidates operation.</summary>
            int hullCandidates = BatchCandidates(hull.Mesh, new Vector3D(0, 0, 5));
            if (hullCandidates >= hull.Total || hullCandidates > ThermalVisionScenePolicy.TriangleLimit)
                throw new InvalidOperationException("Dense closed-hull fixture did not fit after conservative batch rejection.");
            sb.Append("\nDense closed-hull fixture: ").Append(hull.Total).Append(" retained triangles, ")
                .Append(hull.Mesh.Batches.Length).Append(" batch tests, ").Append(hullCandidates)
                .Append(" candidate triangles from an axial view; no surfaces approximated. This fixture fits the scene triangle cap after batch rejection. Fully front-facing dense meshes can still exceed it.\n");
/// <summary>HullFrame operation.</summary>
            var hullReference = HullFrame(false); var hullBatched = HullFrame(true);
            for (int i = 0; i < hullReference.Temperature.Length; i++)
                if (!hullReference.Temperature[i].Equals(hullBatched.Temperature[i])) throw new InvalidOperationException("Batched hull differs from reference raster.");
            File.WriteAllText(Path.Combine(directory, "hull.svg"), hullBatched.Svg(ThermalVisionState.Mode.Cividis));
            sb.Append("Batched rotated-hull raster equals the unbatched reference at every pixel (hull.svg).\n");
/// <summary>OcclusionScene operation.</summary>
            var first = OcclusionScene(false); var second = OcclusionScene(true);
            int visible = 0;
            for (int i = 0; i < first.Temperature.Length; i++)
            {
                if (!first.Temperature[i].Equals(second.Temperature[i])) throw new InvalidOperationException("Depth depends on submission order.");
                if (first.Temperature[i] == 750) throw new InvalidOperationException("Hidden hot surface leaked.");
                if (!float.IsNaN(first.Temperature[i])) visible++;
            }
            if (visible == 0) throw new InvalidOperationException("Empty render passed the oracle.");
            File.WriteAllText(Path.Combine(directory, "colour.svg"), first.Svg(ThermalVisionState.Mode.Cividis));
            File.WriteAllText(Path.Combine(directory, "grey.svg"), first.Svg(ThermalVisionState.Mode.WhiteHot));
            sb.Append("\nDepth oracle: front opaque surface wins in both submission orders; hidden hot surface contributes no pixels. Palette previews: colour.svg and grey.svg. These are synthetic fixtures, not game screenshots.\n\n");
            sb.Append("## Product readiness: INCOMPLETE\n\n- A fully front-facing 61,199-triangle opaque model still exceeds the production scene triangle budget of ")
                .Append(ThermalVisionScenePolicy.TriangleLimit).Append("; resumable extraction alone does not fix its draw coverage.\n")
                .Append("- A scene with 2,252 candidate blocks exceeds the production block-attempt limit of ").Append(ThermalVisionScenePolicy.BlockLimit)
                .Append(". These are candidates, not a visible-pixel coverage count.\n")
                .Append("- Terrain, characters, sky, transparent/cutout surfaces and deformed armour remain unsupported.\n")
                .Append("- Native depth/material behavior, whitelist acceptance of new calls and GPU cost require eventual engine validation.\n")
                .Append("- Aggregate dumps cannot reconstruct the original view or explain a 211 ms elapsed-time stall.\n\n")
                .Append("Do not request another routine user reload solely because this lab passes. Resolve offline failures and coverage gates first.\n");
            string report = Path.Combine(directory, "report.md");
            File.WriteAllText(report, sb.ToString());
            File.WriteAllText(Path.Combine(directory, "preview.html"), "<!doctype html><meta charset='utf-8'><title>Thermal vision synthetic lab</title><style>body{background:#101923;color:#dde8ee;font:18px system-ui;max-width:960px;margin:40px auto}img{width:46%;image-rendering:pixelated}p{line-height:1.5}</style><h1>Synthetic thermal fixtures</h1><p>Production palette and triangle projection; software depth. Cold front plate hides a hotter rear plate. Hot slope remains visible. No native game rendering is simulated here.</p><img src='colour.svg' alt='Cividis fixture'><img src='grey.svg' alt='White-hot fixture'><p>Full-scene product readiness: incomplete. See report.md for explicit coverage gates.</p>");
            return report;
        }
    }
}
