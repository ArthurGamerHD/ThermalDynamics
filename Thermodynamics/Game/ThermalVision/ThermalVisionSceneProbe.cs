using Thermodynamics.Presentation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using RichHudFramework.UI;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.Entity;
using VRageMath;

namespace Thermodynamics
{
    public static partial class ThermalVisionProbe
    {
        private const int SceneTriangles = ThermalVisionScenePolicy.TriangleLimit;
        private const int SceneBuildTriangles = ThermalVisionScenePolicy.BuildTriangleLimit;
        private const double SceneMilliseconds = ThermalVisionScenePolicy.MillisecondLimit;
        private static double SceneReach { get { return compositeMode ? ThermalVisionDepthLayers.Reach : ThermalVisionScenePolicy.Reach; } }
        private const int SceneScanLimit = 4096;
        private const int SceneBlockLimit = ThermalVisionScenePolicy.BlockLimit;
        private static bool sceneMode, automaticRange = true;
        private static int sceneBuiltTriangles;
        private static int sceneTriangleLimit;
        private static double sceneDeadline;
        private static ThermalVisionSceneLimit sceneLimits;
        private static int discoveryAge = 10;
        private static bool discoveryLimited;
        private static MatrixD discoveryView;
/// <summary>Stopwatch operation.</summary>
        private static readonly Stopwatch ExposureClock = new Stopwatch();
/// <summary>ThermalVisionAutoRange operation.</summary>
        private static readonly ThermalVisionAutoRange SceneRange = new ThermalVisionAutoRange();
/// <summary>List operation.</summary>
        private static readonly List<SceneCandidate> SceneCandidates = new List<SceneCandidate>();
/// <summary>CandidateComparer operation.</summary>
        private static readonly IComparer<SceneCandidate> NearestFirst = new CandidateComparer();

        private struct SceneCandidate
        {
            public ThermalBlock Block;
            public double Distance;
        }

        private sealed class CandidateComparer : IComparer<SceneCandidate>
        {
/// <summary>Compare operation.</summary>
            public int Compare(SceneCandidate a, SceneCandidate b)
            {
                int distance = a.Distance.CompareTo(b.Distance);
                if (distance != 0) return distance;
                int grid = a.Block.Grid.Grid.EntityId.CompareTo(b.Block.Grid.Grid.EntityId);
                if (grid != 0) return grid;
                return a.Block.Block.Position.GetHashCode().CompareTo(b.Block.Block.Position.GetHashCode());
            }
        }

/// <summary>DrawScene operation.</summary>
        private static void DrawScene()
        {
            sceneBuiltTriangles = 0;
            sceneLimits = ThermalVisionSceneLimit.None;
            rowIdentity = compositeMode ? "composite / measured geometry + neutral context / 5000 m" : "scene / simulated grids within 100 m";
            rowKey = null;
            var camera = MyAPIGateway.Session.Camera;
            MatrixD view = camera.WorldMatrix;
            int scanned = 0, attempted = 0, submittedBlocks = 0, missing = 0;
            bool limited = false;
            double exposureSeconds = Math.Min(.25, ExposureClock.Elapsed.TotalSeconds);
            ExposureClock.Restart();
            bool refreshed = ++discoveryAge >= 10
                || Vector3D.DistanceSquared(discoveryView.Translation, view.Translation) > 25
                || Vector3D.Dot(discoveryView.Forward, view.Forward) < 0.966;
            if (refreshed)
            {
                SceneCandidates.Clear();
                if (automaticRange) SceneRange.BeginSamples();
                var grids = ThermalGrid.LiveGrids;
                for (int g = 0; g < grids.Count; g++)
                {
                    if (g >= 256) { limited = true; break; }
                    var thermal = grids[g];
                    var grid = thermal.Grid;
                    if (grid == null || grid.MarkedForClose || thermal.Simulation == null) continue;
                    var box = grid.PositionComp.WorldAABB;
/// <summary>BoundingSphereD operation.</summary>
                    var reach = new BoundingSphereD(view.Translation, SceneReach);
                    if (!box.Intersects(reach) || !camera.IsInFrustum(ref box)) continue;
                    foreach (var block in thermal.Blocks)
                    {
                        if (scanned >= SceneScanLimit || (scanned % 64 == 0 && ProbeClock.Elapsed.TotalMilliseconds >= 1))
                        { limited = true; break; }
                        scanned++;
                        if (block == null || block.Node == null) continue;
                        Vector3D centre;
                        Vector3 half;
                        block.Block.ComputeWorldCenter(out centre);
                        block.Block.ComputeScaledHalfExtents(out half);
/// <summary>BoundingSphereD operation.</summary>
                        var sphere = new BoundingSphereD(centre, half.Length());
                        double distance = Vector3D.DistanceSquared(centre, view.Translation);
                        double reachWithRadius = SceneReach + sphere.Radius;
                        if (distance > reachWithRadius * reachWithRadius || !camera.IsInFrustum(ref sphere)) continue;
                        SceneCandidates.Add(new SceneCandidate { Block = block, Distance = distance });
                        if (automaticRange) SceneRange.Observe(block.Node.Temperature);
                    }
                    if (limited) break;
                }
                SceneCandidates.Sort(NearestFirst);
                discoveryLimited = limited;
                discoveryView = view;
                discoveryAge = 0;
            }
            limited |= discoveryLimited;
            float discoveryMs = (float)ProbeClock.Elapsed.TotalMilliseconds;
            if (automaticRange) SceneRange.Update(exposureSeconds);
            if (automaticRange && SceneRange.HasSamples)
            {
                lowKelvin = SceneRange.Low;
                highKelvin = SceneRange.High;
                windowIdentity = lowKelvin.ToString("R", CultureInfo.InvariantCulture) + ".."
                    + highKelvin.ToString("R", CultureInfo.InvariantCulture) + " K";

            }
            double buildStarted = ProbeClock.Elapsed.TotalMilliseconds;
            AdvanceGeometryBuild();
            float buildMs = (float)(ProbeClock.Elapsed.TotalMilliseconds - buildStarted);
            int armourAttempted = 0, armourSubmitted = 0, detailSubmitted = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                sceneTriangleLimit = pass == 0 ? SceneTriangles / 4 : SceneTriangles;
                sceneDeadline = pass == 0 ? Math.Min(SceneMilliseconds, discoveryMs + buildMs + 1) : SceneMilliseconds;
                for (int i = 0; i < SceneCandidates.Count; i++)
                {
                    ThermalBlock block = SceneCandidates[i].Block;
                    bool armour = block.Block.FatBlock == null;
                    if (armour != (pass == 0)) continue;
                    if (attempted >= SceneBlockLimit)
                    { sceneLimits |= ThermalVisionSceneLimit.Blocks; break; }
                    if (examined >= sceneTriangleLimit || ProbeClock.Elapsed.TotalMilliseconds >= sceneDeadline
                        || (pass == 0 && armourAttempted >= ThermalVisionScenePolicy.ArmourBlockLimit))
                    {
                        sceneLimits |= pass == 0 ? ThermalVisionSceneLimit.ArmourQuota
                            : examined >= sceneTriangleLimit ? ThermalVisionSceneLimit.Triangles : ThermalVisionSceneLimit.Time;
                        break;
                    }
                    var grid = block.Grid.Grid;
                    if (grid == null || grid.MarkedForClose || block.Node == null
                        || block.Grid.GetAtCell(block.Block.Position) != block) continue;
                    Vector3D centre;
                    Vector3 half;
                    block.Block.ComputeWorldCenter(out centre);
                    block.Block.ComputeScaledHalfExtents(out half);
/// <summary>BoundingSphereD operation.</summary>
                    var sphere = new BoundingSphereD(centre, half.Length());
                    double reachWithRadius = SceneReach + sphere.Radius;
                    if (Vector3D.DistanceSquared(centre, view.Translation) > reachWithRadius * reachWithRadius
                        || !camera.IsInFrustum(ref sphere)) continue;
                    attempted++;
                    if (armour) armourAttempted++;
                    Vector3 srgb;
                    if (!ThermalVisionPalette.TrySample(block.Node.Temperature, State.Current, lowKelvin, highKelvin, out srgb))
                    { missing++; continue; }
                    int before = drawn;
                    parts = 0;
/// <summary>Vector4 operation.</summary>
                    Vector4 colour = new Vector4(compositeMode ? srgb : ThermalVisionPalette.ToLinear(srgb), 1f);
                    var entity = block.Block.FatBlock as MyEntity;
                    if (entity != null) DrawEntity(entity, colour, view.Translation, 0);
                    else DrawArmour(new Crosshair.Target
                    {
                        Block = block, Thermals = block.Grid, Cell = block.Block.Position, Camera = view,
                    }, colour);
                    if (drawn > before) { submittedBlocks++; if (armour) armourSubmitted++; else detailSubmitted++; }
                    else missing++;
                }
            }
            if (discoveryLimited) sceneLimits |= ThermalVisionSceneLimit.Discovery;
            limited |= sceneLimits != ThermalVisionSceneLimit.None;
            incomplete |= limited || missing > 0;
            Outcome(incomplete ? "scene-partial" : SceneCandidates.Count == 0 ? "scene-no-candidates" : "scene-submitted");
            if (capture) Telemetry.Vision.SceneFrame(scanned, SceneCandidates.Count, attempted, submittedBlocks,
                missing, limited, lowKelvin, highKelvin, refreshed, discoveryMs,
                (float)ProbeClock.Elapsed.TotalMilliseconds - discoveryMs - buildMs);
            if (capture) Telemetry.Vision.SceneBuild(sceneBuiltTriangles, pendingGeometry != null, buildMs);
            if (capture) Telemetry.Vision.SceneCulling(batchesTested, batchRejectedTriangles);
            if (capture) Telemetry.Vision.SceneBudget(sceneLimits, armourSubmitted, detailSubmitted);
            if (frames++ % 6 == 0)
            {
                string palette = State.Current == ThermalVisionState.Mode.Cividis ? "CIVIDIS" : "WHITE HOT";
/// <summary>RichText operation.</summary>
                panel.Text = new RichText((compositeMode ? "COMPOSITE EXPERIMENT / " : "SCENE PROBE 8 / ") + palette + " / "
                    + (lowKelvin - 273.15f).ToString("0") + "–" + (highKelvin - 273.15f).ToString("0")
                    + " C " + (automaticRange ? "AUTO" : "LOCK") + "\n"
                    + submittedBlocks + "/" + SceneCandidates.Count + " candidate blocks | " + drawn + " triangles"
                    + (incomplete ? " | PARTIAL" : "")
                    + (compositeMode ? "\n5 km context | dark: UNMEASURED, not cold\nMeasured surfaces only; coverage bounded" : "\nSimulated grids, 100 m; terrain/characters unsupported"));
            }
            panel.Visible = true;
        }
    }
}
