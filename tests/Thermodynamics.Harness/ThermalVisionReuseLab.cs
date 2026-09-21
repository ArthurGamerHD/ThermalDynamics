using Thermodynamics.Presentation;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ThermalVisionReuseLab
    {
        private struct Cached
        {
            public bool Valid;
            public Vector3D Point;
            public ThermalVisionRayLab.Sample Sample;
            public int Updated;
        }

        public sealed class Result
        {
            public int MaxQueries, TotalQueries, WarmupQueries;
            public int PeakHoles, PeakWrongSurface, PeakFalseHot, FinalWrongSurface;
            public int PixelCount;
            public string Csv;
        }

/// <summary>Trace operation.</summary>
        private static ThermalVisionRayLab.Sample Trace(Vector3D ray, bool occluder)
        {
            var sample = ThermalVisionRayLab.Trace(ray);
            if (sample.Distance > 150)
                sample = new ThermalVisionRayLab.Sample { Distance = double.PositiveInfinity, Kelvin = float.NaN };
            if (occluder && ray.Z < 0)
            {
                double t = -4 / ray.Z;
                Vector3D p = ray * t;
                if (Math.Abs(p.X) < 1.3 && Math.Abs(p.Y) < 1.4 && t < sample.Distance)
                    sample = new ThermalVisionRayLab.Sample { Surface = 7, Distance = t, Kelvin = 250 };
            }
            return sample;
        }

/// <summary>Run operation.</summary>
        public static Result Run(int width, int height, bool pan, bool occluder, int queryBudget = 128, int footprintRadius = 0)
        {
            if (width < 1 || height < 1 || width > 160 || height > 90 || queryBudget < 1
                || footprintRadius < 0 || footprintRadius > 1)
                throw new ArgumentException("Invalid reuse fixture dimensions or budget.");
            int count = width * height;
            var current = new Cached[count];
            var next = new Cached[count];
            var depth = new double[count];
            var result = new Result { PixelCount = count, WarmupQueries = count };
/// <summary>StringBuilder operation.</summary>
            var csv = new StringBuilder("frame,queries,holes,wrong_surface,false_hot\n");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector3D ray = ThermalVisionRayLab.Direction(x, y, width, height);
/// <summary>Trace operation.</summary>
                    var sample = Trace(ray, false);
                    current[y * width + x] = new Cached { Valid = true, Sample = sample,
                        Point = ray * (sample.Surface == 0 ? 150 : sample.Distance) };
                }
            double tangent = Math.Tan(Math.PI / 6), aspect = (double)width / height;
            int refreshCursor = 0;
            for (int frame = 1; frame <= 60; frame++)
            {
                MatrixD world = MatrixD.CreateRotationY(pan ? frame * Math.PI / 120 : 0);
                MatrixD inverse = MatrixD.Transpose(world);
                Array.Clear(next, 0, count);
                for (int i = 0; i < count; i++) depth[i] = double.PositiveInfinity;
                foreach (Cached cached in current)
                {
                    if (!cached.Valid) continue;
                    Vector3D local = Vector3D.TransformNormal(cached.Point, inverse);
                    if (local.Z >= -.01) continue;
                    double nx = local.X / (-local.Z * tangent * aspect);
                    double ny = local.Y / (-local.Z * tangent);
                    if (nx < -1 || nx >= 1 || ny <= -1 || ny > 1) continue;
                    int x = (int)((nx + 1) * .5 * width), y = (int)((1 - ny) * .5 * height);
                    for (int sy = Math.Max(0, y - footprintRadius); sy <= Math.Min(height - 1, y + footprintRadius); sy++)
                        for (int sx = Math.Max(0, x - footprintRadius); sx <= Math.Min(width - 1, x + footprintRadius); sx++)
                        {
                            int pixel = sy * width + sx;
                            if (-local.Z >= depth[pixel]) continue;
                            depth[pixel] = -local.Z;
                            next[pixel] = cached;
                        }
                }
                int queries = 0;
                for (int i = 0; i < count && queries < queryBudget; i++)
                    if (!next[i].Valid) SamplePixel(next, i, width, height, world, frame, occluder, ref queries);
                for (int visited = 0; visited < count && queries < queryBudget; visited++)
                {
                    int i = refreshCursor;
                    refreshCursor = (refreshCursor + 1) % count;
                    if (next[i].Updated == frame) continue;
                    SamplePixel(next, i, width, height, world, frame, occluder, ref queries);
                }
                int holes = 0, wrong = 0, falseHot = 0;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        if (!next[i].Valid) { holes++; continue; }
                        Vector3D ray = Vector3D.TransformNormal(ThermalVisionRayLab.Direction(x, y, width, height), world);
/// <summary>Trace operation.</summary>
                        var expected = Trace(ray, occluder && frame >= 20);
                        if (expected.Surface != next[i].Sample.Surface) wrong++;
                        if (expected.Surface == 7 && next[i].Sample.Kelvin > 350) falseHot++;
                    }
                result.MaxQueries = Math.Max(result.MaxQueries, queries);
                result.TotalQueries += queries;
                result.PeakHoles = Math.Max(result.PeakHoles, holes);
                result.PeakWrongSurface = Math.Max(result.PeakWrongSurface, wrong);
                result.PeakFalseHot = Math.Max(result.PeakFalseHot, falseHot);
                result.FinalWrongSurface = wrong;
                csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}\n", frame, queries, holes, wrong, falseHot);
                Cached[] old = current; current = next; next = old;
            }
            result.Csv = csv.ToString();
            return result;
        }

/// <summary>SamplePixel operation.</summary>
        private static void SamplePixel(Cached[] pixels, int i, int width, int height, MatrixD world,
            int frame, bool occluder, ref int queries)
        {
            Vector3D ray = Vector3D.TransformNormal(ThermalVisionRayLab.Direction(i % width, i / width, width, height), world);
/// <summary>Trace operation.</summary>
            var sample = Trace(ray, occluder && frame >= 20);
            pixels[i] = new Cached { Valid = true, Sample = sample, Updated = frame,
                Point = ray * (sample.Surface == 0 ? 150 : sample.Distance) };
            queries++;
        }

/// <summary>WriteReport operation.</summary>
        public static string WriteReport(string directory)
        {
            Directory.CreateDirectory(directory);
/// <summary>StringBuilder operation.</summary>
            var report = new StringBuilder("# Ray sample reuse: feasibility and counterexamples\n\n"
                + "60 analytic updates at 60 Hz; at most 128 fresh scene queries/update. "
                + "Initial full-frame sampling is counted separately. No game timing, GPU or HUD is simulated. "
                + "Cached hit points are reprojected with nearest-depth selection; holes receive priority, then round-robin refresh. "
                + "No-return samples use a 150 m endpoint. Reference rays are used only for scoring and are not charged as renderer queries.\n\n"
                + "| Resolution / scenario | Warmup queries | Update queries | Peak missing pixels | Peak wrong-surface pixels | Peak hot pixels behind new cold occluder | Final wrong-surface pixels |\n"
                + "| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (int width in new[] { 64, 128 })
                for (int scenario = 0; scenario < 5; scenario++)
                {
                    int height = width * 9 / 16;
                    string name = scenario == 0 ? "stationary" : scenario == 1 ? "90deg-pan"
                        : scenario == 2 ? "new-occluder" : scenario == 3 ? "pan-3x3-footprint" : "occluder-3x3-footprint";
/// <summary>Run operation.</summary>
                    Result result = Run(width, height, scenario == 1 || scenario == 3,
                        scenario == 2 || scenario == 4, 128, scenario >= 3 ? 1 : 0);
                    File.WriteAllText(Path.Combine(directory, width + "-" + name + ".csv"), result.Csv);
                    report.AppendFormat(CultureInfo.InvariantCulture, "| {0}×{1} / {2} | {3} | {4} | {5} | {6} | {7} | {8} |\n",
                        width, height, name, result.WarmupQueries, result.TotalQueries, result.PeakHoles,
                        result.PeakWrongSurface, result.PeakFalseHot, result.FinalWrongSurface);
                }
            report.Append("\nWrong-surface counts exclude holes; both must be evaluated. "
                + "The static synthetic scene does not reproduce Havok/render-mesh disagreement or real moving subparts. "
                + "Nearest-depth selection only orders known samples: it cannot detect a newly occluding object without new evidence. "
                + "This tests a simple reuse strategy, not every possible adaptive or mesh-based method. "
                + "A stationary pass alone is not acceptance for integrated moving-camera thermal vision.\n");
            File.WriteAllText(Path.Combine(directory, "report.md"), report.ToString());
            return report.ToString();
        }
    }
}
