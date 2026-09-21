using Thermodynamics.Presentation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    public static class ThermalVisionVolumeLab
    {
        private struct Box
        {
            public Vector3D Min, Max;
            public int Owner;
/// <summary>Box operation.</summary>
            public Box(Vector3D min, Vector3D max, int owner) { Min = min; Max = max; Owner = owner; }
/// <summary>Contains operation.</summary>
            public bool Contains(Vector3D p)
            { return p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z; }
        }

        public sealed class Result
        {
            public string Scene, Method;
            public int HotReference, ColdReference, HotCorrect, ColdFalseHot, HotMissing;
            public int HotMisassigned;
            public bool Pass { get { return HotMissing == 0 && ColdFalseHot == 0 && HotMisassigned == 0; } }
        }

/// <summary>Hit operation.</summary>
        private static double Hit(Box box, Vector3D ray)
        {
            double enter = 0, leave = double.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                double d = axis == 0 ? ray.X : axis == 1 ? ray.Y : ray.Z;
                double lo = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
                double hi = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
                if (Math.Abs(d) < 1e-12) { if (lo > 0 || hi < 0) return double.PositiveInfinity; continue; }
                double a = lo / d, b = hi / d;
                enter = Math.Max(enter, Math.Min(a, b));
                leave = Math.Min(leave, Math.Max(a, b));
                if (enter > leave) return double.PositiveInfinity;
            }
            return enter > 0 ? enter : double.PositiveInfinity;
        }

/// <summary>OrderedBoxFaces operation.</summary>
        private static int OrderedBoxFaces(Box cell, Vector3D ray, double sceneDepth)
        {
            var faces = new List<int> { 0, 1, 2, 3, 4, 5 };
            faces.Sort((a, b) => FaceDepth(cell, a).CompareTo(FaceDepth(cell, b)));
            int value = 0;
            foreach (int face in faces)
            {
                int axis = face / 2;
/// <summary>Coordinate operation.</summary>
                double plane = Coordinate(face % 2 == 0 ? cell.Min : cell.Max, axis);
/// <summary>Coordinate operation.</summary>
                double direction = Coordinate(ray, axis);
                if (Math.Abs(direction) < 1e-12) continue;
                double depth = plane / direction;
                if (depth <= 0 || depth >= sceneDepth) continue;
                Vector3D hit = ray * depth;
                bool within = true;
                for (int other = 0; other < 3; other++)
                    if (other != axis && (Coordinate(hit, other) < Coordinate(cell.Min, other)
/// <summary>Coordinate operation.</summary>
                        || Coordinate(hit, other) > Coordinate(cell.Max, other))) within = false;
                if (!within) continue;
                double normal = face % 2 == 0 ? -1 : 1;
                value = -plane * normal > 0 ? 1 : 0;
            }
            return value;
        }

        private struct Boundary
        {
            public Box Cell;
            public int Face;
        }

/// <summary>RunBoundaryStress operation.</summary>
        public static Result RunBoundaryStress(string scene, bool partitioned)
        {
/// <summary>List operation.</summary>
            var cells = new List<Box>();
            double surfaceDepth;
            if (scene == "overlap-exit")
            {
                cells.Add(new Box(new Vector3D(-2, -2, -14), new Vector3D(2, 2, -8), 1));
                cells.Add(new Box(new Vector3D(-1, -1, -11), new Vector3D(1, 1, -9), 1));
                surfaceDepth = 12;
            }
/// <summary>if operation.</summary>
            else if (scene == "camera-inside")
            {
                cells.Add(new Box(new Vector3D(-2, -2, -4), new Vector3D(2, 2, 2), 1));
                surfaceDepth = 1;
            }
/// <summary>if operation.</summary>
            else if (scene == "adjacent")
            {
                cells.Add(new Box(new Vector3D(-2, -2, -10), new Vector3D(2, 2, -8), 1));
                cells.Add(new Box(new Vector3D(-2, -2, -12), new Vector3D(2, 2, -10), 1));
                surfaceDepth = 11;
            }
            else throw new ArgumentException("Unknown boundary stress fixture");
            if (partitioned)
            {
/// <summary>ThermalVisionRegionPartition operation.</summary>
                var field = new ThermalVisionRegionPartition(256);
                foreach (Box cell in cells)
                    if (!field.TryAdd(new ThermalVisionRegionPartition.Region(cell.Min, cell.Max, 600)))
                        throw new InvalidOperationException("Synthetic region capacity exceeded");
                cells.Clear();
                for (int i = 0; i < field.Count; i++) cells.Add(new Box(field[i].Min, field[i].Max, 1));
            }
/// <summary>List operation.</summary>
            var boundaries = new List<Boundary>();
            foreach (Box cell in cells)
                for (int i = 0; i < 6; i++) boundaries.Add(new Boundary { Cell = cell, Face = i });
            boundaries.Sort((a, b) =>
            {
/// <summary>FaceDepth operation.</summary>
                int distance = FaceDepth(a.Cell, a.Face).CompareTo(FaceDepth(b.Cell, b.Face));
                if (distance != 0 || !partitioned) return distance;
                return (a.Face == 4 ? 0 : 1).CompareTo(b.Face == 4 ? 0 : 1);
            });
            var result = new Result { Scene = scene, Method = partitioned ? "partition-and-near-seed" : "raw-boundaries" };
            for (int y = 0; y < 90; y++) for (int x = 0; x < 160; x++)
            {
/// <summary>Vector3D operation.</summary>
                var ray = new Vector3D((2 * (x + .5) / 160 - 1) * .25,
                    (1 - 2 * (y + .5) / 90) * .25, -1);
                bool expected = false;
                foreach (Box cell in cells) expected |= cell.Contains(ray * surfaceDepth);
                int actual = 0;
                if (partitioned)
                    foreach (Box cell in cells) if (cell.Contains(ray * .0525)) actual = 1;
                foreach (Boundary boundary in boundaries)
                {
                    int axis = boundary.Face / 2;
/// <summary>Coordinate operation.</summary>
                    double plane = Coordinate(boundary.Face % 2 == 0 ? boundary.Cell.Min : boundary.Cell.Max, axis);
/// <summary>Coordinate operation.</summary>
                    double direction = Coordinate(ray, axis);
                    if (Math.Abs(direction) < 1e-12) continue;
                    double depth = plane / direction;
                    if (depth < .0525 || depth >= surfaceDepth) continue;
                    Vector3D point = ray * depth;
                    bool within = true;
                    for (int other = 0; other < 3; other++)
                        if (other != axis && (Coordinate(point, other) < Coordinate(boundary.Cell.Min, other)
/// <summary>Coordinate operation.</summary>
                            || Coordinate(point, other) > Coordinate(boundary.Cell.Max, other))) within = false;
                    if (within) actual = -plane * (boundary.Face % 2 == 0 ? -1 : 1) > 0 ? 1 : 0;
                }
                if (expected)
                {
                    result.HotReference++;
                    if (actual == 1) result.HotCorrect++; else result.HotMissing++;
                }
                else { result.ColdReference++; if (actual == 1) result.ColdFalseHot++; }
            }
            return result;
        }

/// <summary>Coordinate operation.</summary>
        private static double Coordinate(Vector3D v, int axis)
        { return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z; }

/// <summary>FaceDepth operation.</summary>
        private static double FaceDepth(Box cell, int face)
        { return face == 4 ? -cell.Min.Z : face == 5 ? -cell.Max.Z : -(cell.Min.Z + cell.Max.Z) / 2; }

/// <summary>Run operation.</summary>
        public static Result Run(string scene, string method)
        {
            if (method != "preceding-slice" && method != "expanded-slice" && method != "exact-cell-boundary" && method != "ordered-box-faces")
                throw new ArgumentException("Unknown volume candidate.");
/// <summary>Box operation.</summary>
            var hotCell = new Box(new Vector3D(-1, -1, -12), new Vector3D(1, 1, -10), 1);
/// <summary>List operation.</summary>
            var geometry = new List<Box>();
            if (scene == "isolated")
                geometry.Add(new Box(new Vector3D(-.8, -.8, -11.8), new Vector3D(.8, .8, -10.2), 1));
/// <summary>if operation.</summary>
            else if (scene == "foreground")
            {
                geometry.Add(new Box(new Vector3D(-.8, -.8, -11.8), new Vector3D(.8, .8, -10.2), 1));
                geometry.Add(new Box(new Vector3D(-.4, -.7, -9.95), new Vector3D(.4, .7, -9.9), 2));
            }
/// <summary>if operation.</summary>
            else if (scene == "open-frame-intrusion")
            {
                geometry.Add(new Box(new Vector3D(-.9, -.9, -11.8), new Vector3D(-.6, .9, -10.2), 1));
                geometry.Add(new Box(new Vector3D(.6, -.9, -11.8), new Vector3D(.9, .9, -10.2), 1));
                geometry.Add(new Box(new Vector3D(-.25, -.6, -11.2), new Vector3D(.25, .6, -10.5), 2));
            }
            else throw new ArgumentException("Unknown volume fixture.");

            var result = new Result { Scene = scene, Method = method };
            var layers = new double[ThermalVisionDepthLayers.Count];
            for (int i = 0; i < layers.Length; i++) layers[i] = ThermalVisionDepthLayers.Distance(i, .0525);
            for (int y = 0; y < 180; y++) for (int x = 0; x < 320; x++)
            {
/// <summary>Vector3D operation.</summary>
                Vector3D ray = new Vector3D((2 * (x + .5) / 320 - 1) * .35 * 320 / 180,
                    (1 - 2 * (y + .5) / 180) * .35, -1);
                double depth = double.PositiveInfinity;
                int owner = 0;
                foreach (Box shape in geometry)
                {
/// <summary>Hit operation.</summary>
                    double candidate = Hit(shape, ray);
                    if (candidate < depth) { depth = candidate; owner = shape.Owner; }
                }
                if (owner == 0) continue; // Final background plane, not measured cold.
                int sampledOwner = 0;
                if (method == "ordered-box-faces")
/// <summary>OrderedBoxFaces operation.</summary>
                    sampledOwner = OrderedBoxFaces(hotCell, ray, depth);
/// <summary>if operation.</summary>
                else if (method == "exact-cell-boundary")
                {
                    sampledOwner = hotCell.Contains(ray * (depth + .00001)) ? 1 : 0;
                }
                else
                {
                    int previous = -1;
                    for (int i = 0; i < layers.Length && layers[i] < depth; i++) previous = i;
                    if (previous >= 0)
                    {
                        if (method == "preceding-slice") sampledOwner = hotCell.Contains(ray * layers[previous]) ? 1 : 0;
                        else
                        {
/// <summary>Hit operation.</summary>
                            double entry = Hit(hotCell, ray);
                            double next = previous + 1 < layers.Length ? layers[previous + 1] : layers[previous];
                            sampledOwner = entry >= layers[previous] && entry <= next
                                || hotCell.Contains(ray * layers[previous]) ? 1 : 0;
                        }
                    }
                }
                if (owner == 1)
                {
                    result.HotReference++;
                    if (sampledOwner == 1) result.HotCorrect++;
                    else if (sampledOwner == 0) result.HotMissing++;
                    else result.HotMisassigned++;
                }
                else
                {
                    result.ColdReference++;
                    if (sampledOwner == 1) result.ColdFalseHot++;
                }
            }
            return result;
        }

/// <summary>WriteReport operation.</summary>
        public static string WriteReport(string directory)
        {
            Directory.CreateDirectory(directory);
/// <summary>StringBuilder operation.</summary>
            var text = new StringBuilder("# Thermal ownership after native depth\n\n"
                + "Synthetic analytic 320×180 fixtures, not game images or GPU timings. Hot cell = 600 K; "
                + "cold foreground object = 280 K. A zero assignment means unknown, not cold. "
                + "All methods receive perfect reference depth; none receives the reference owner.\n\n"
                + "| Scene | Candidate | Hot pixels | Correct hot | Missing hot | Cold pixels | False-hot cold pixels |\n"
                + "| --- | --- | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (string scene in new[] { "isolated", "foreground", "open-frame-intrusion" })
                foreach (string method in new[] { "preceding-slice", "expanded-slice", "exact-cell-boundary", "ordered-box-faces" })
                {
/// <summary>Run operation.</summary>
                    var r = Run(scene, method);
                    text.AppendFormat(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3} | {4} | {5} | {6} |\n",
                        scene, method, r.HotReference, r.HotCorrect, r.HotMissing, r.ColdReference, r.ColdFalseHot);
                }
            text.Append("\n## Boundary stability stress\n\n"
                + "Partitioned fixtures use the shared disjoint-region builder and seed the near plane analytically. "
                + "This tests design requirements, not a native near-cap renderer.\n\n"
                + "| Scene | Candidate | Expected hot | Correct hot | Missing hot | False hot |\n"
                + "| --- | --- | ---: | ---: | ---: | ---: |\n");
            foreach (string scene in new[] { "overlap-exit", "camera-inside", "adjacent" })
                foreach (bool partitioned in new[] { false, true })
                {
/// <summary>RunBoundaryStress operation.</summary>
                    var r = RunBoundaryStress(scene, partitioned);
                    text.AppendFormat(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3} | {4} | {5} |\n",
                        r.Scene, r.Method, r.HotReference, r.HotCorrect, r.HotMissing, r.ColdFalseHot);
                }
            text.Append("\nStrict per-surface ownership gate: FAIL. The optimistic exact-boundary method can fix slice gaps, "
                + "but a coarse block temperature volume still cannot identify a foreign surface inside it. "
                + "The open frame and cold object do not physically intersect. More depth resolution cannot "
/// <summary>heat operation.</summary>
                + "resolve that ownership ambiguity. The user now accepts approximate block-group heat (2026-09-19); the strict ownership failure is no longer by itself a rejection of that product. Ordered box faces are a new six-polygon candidate, not a native render measurement. It still needs rotated/overlapping groups, camera-inside, boundary-coincident surfaces and motion tests before a native request.\n");
            string path = Path.Combine(directory, "report.md");
            File.WriteAllText(path, text.ToString());
            return path;
        }
    }
}
