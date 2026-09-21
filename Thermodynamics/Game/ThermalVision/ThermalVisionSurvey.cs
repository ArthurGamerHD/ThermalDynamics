using Thermodynamics.Presentation;
using System;
using System.Diagnostics;
using System.Globalization;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    public static partial class ThermalVisionProbe
    {
        private static int SurveyWidth = 64, SurveyHeight = 36;
        private static double SurveyDeadline { get { return SurveyWidth == 128 ? 4 : 2; } }
        private const double SurveyReach = 150;
        private static bool surveyMode, surveyScanning, surveyWaitingForUi;
        private static long surveyFrame;
        private static int surveyQueries, surveyMeasured, surveyUnknown;
        private static double surveyQueryMs, surveyMaxQueryMs, surveyCapturedAt;
        private static string surveyStatus;
        private static MatrixD surveyView, surveyProjection;
/// <summary>Stopwatch operation.</summary>
        private static readonly Stopwatch SurveyClock = new Stopwatch();
/// <summary>Stopwatch operation.</summary>
        private static readonly Stopwatch SurveyBudget = new Stopwatch();
        private static ThermalVisionRayScan<SurveySample> SurveyScan =
/// <summary>ThermalVisionRayScan operation.</summary>
            new ThermalVisionRayScan<SurveySample>(SurveyWidth * SurveyHeight, 1, 128);
        private static SurveyImage surveyImage;

        private struct SurveySample
        {
            public bool Hit;
            public float Kelvin;
            public float Facing;
        }

        private sealed class SurveyImage : HudElementBase
        {
            public readonly Color[] Pixels = new Color[128 * 72];
            public bool Ready;
            public float Aspect = 16f / 9f;
/// <summary>MatBoard operation.</summary>
            private readonly MatBoard board = new MatBoard();
/// <summary>SurveyImage operation.</summary>
            public SurveyImage() : base(HudMain.HighDpiRoot) { Visible = false; }

/// <summary>Draw operation.</summary>
            protected override void Draw()
            {
                var box = new CroppedBox { mask = MaskingBox };
/// <summary>BoundingBox2 operation.</summary>
                box.bounds = new BoundingBox2(Position - Size * .5f - new Vector2(5), Position + Size * .5f + new Vector2(5));
/// <summary>Color operation.</summary>
                board.Color = new Color(60, 81, 96);
                board.Draw(ref box, HudSpace.PlaneToWorldRef);
/// <summary>Vector2 operation.</summary>
                Vector2 corner = Position + new Vector2(-Size.X, Size.Y) * .5f;
/// <summary>Vector2 operation.</summary>
                Vector2 cell = new Vector2(Size.X / SurveyWidth, Size.Y / SurveyHeight);
                for (int y = 0; y < SurveyHeight; y++)
                    for (int x = 0; x < SurveyWidth;)
                    {
/// <summary>Color operation.</summary>
                        Color colour = Ready ? Pixels[y * SurveyWidth + x] : new Color(12, 19, 26);
                        int end = x + 1;
                        while (end < SurveyWidth && (!Ready || Pixels[y * SurveyWidth + end] == colour)) end++;
/// <summary>BoundingBox2 operation.</summary>
                        box.bounds = new BoundingBox2(corner + new Vector2(x * cell.X, -(y + 1) * cell.Y),
/// <summary>Vector2 operation.</summary>
                            corner + new Vector2(end * cell.X, -y * cell.Y));
                        board.Color = colour;
                        board.Draw(ref box, HudSpace.PlaneToWorldRef);
                        x = end;
                    }
            }
        }

/// <summary>StartSurvey operation.</summary>
        private static void StartSurvey()
        {
            if (surveyImage == null) surveyImage = new SurveyImage();
            surveyImage.Ready = false;
            surveyImage.Visible = true;
            panel.ParentAlignment = ParentAlignments.Top | ParentAlignments.Right | ParentAlignments.InnerV | ParentAlignments.InnerH;
/// <summary>Vector2 operation.</summary>
            panel.Offset = new Vector2(-24, -28);
            frames = 0;
            SurveyScan.Begin();
            surveyView = MyAPIGateway.Session.Camera.WorldMatrix;
            surveyProjection = MyAPIGateway.Session.Camera.ProjectionMatrix;
            double aspect = Math.Abs(surveyProjection.M22 / surveyProjection.M11);
            if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect < .2 || aspect > 8)
            {
                SurveyScan.Invalidate();
                surveyScanning = false;
                surveyWaitingForUi = false;
                surveyStatus = "UNSUPPORTED CAMERA PROJECTION";
                RecordEvent("survey rejected: unsupported camera projection");
                return;
            }
            surveyImage.Aspect = (float)aspect;
            surveyScanning = true;
            surveyWaitingForUi = true;
            surveyQueries = surveyMeasured = surveyUnknown = 0;
            surveyQueryMs = surveyMaxQueryMs = 0;
            surveyStatus = "ACQUIRING / hold view steady";
            SurveyClock.Restart();
            RecordEvent("survey start: " + SurveyWidth + "x" + SurveyHeight
                + ", 150m, 128 queries/draw, soft 2ms/draw, " + SurveyDeadline + "s deadline");
        }

/// <summary>StopSurvey operation.</summary>
        private static void StopSurvey()
        {
            SurveyScan.Invalidate();
            surveyScanning = false;
            surveyWaitingForUi = false;
            SurveyClock.Stop();
            if (surveyImage != null) { surveyImage.Ready = false; surveyImage.Visible = false; }
        }

/// <summary>DrawSurvey operation.</summary>
        private static void DrawSurvey()
        {
            if (surveyImage == null) StartSurvey();
            surveyImage.Visible = true;
            Vector2 size, offset;
            ThermalVisionSensorOptics.Layout(HudMain.ScreenDimHighDPI, surveyImage.Aspect, out size, out offset);
            surveyImage.Size = size;
            surveyImage.Offset = offset;
            var camera = MyAPIGateway.Session.Camera;
            if (surveyWaitingForUi)
            {
                surveyWaitingForUi = false;
                SurveyClock.Restart();
                surveyView = camera.WorldMatrix;
                surveyProjection = camera.ProjectionMatrix;
            }
            if (surveyScanning)
            {
                MatrixD view = camera.WorldMatrix;
                MatrixD projection = camera.ProjectionMatrix;
                bool moved = Vector3D.DistanceSquared(view.Translation, surveyView.Translation) > .0025
                    || Vector3D.Dot(view.Forward, surveyView.Forward) < .99999
                    || Vector3D.Dot(view.Up, surveyView.Up) < .99999
                    || !projection.Equals(surveyProjection);
                if (SurveyClock.Elapsed.TotalSeconds > SurveyDeadline)
                {
                    SurveyScan.Invalidate();
                    surveyScanning = false;
                    surveyStatus = "NO CAPTURE / hold steady and scan again";
                    RecordEvent("survey timeout: queries=" + surveyQueries + " queryMs=" + surveyQueryMs.ToString("0.00")
                        + " maxQueryMs=" + surveyMaxQueryMs.ToString("0.00"));
                }
/// <summary>if operation.</summary>
                else if (moved)
                {
                    SurveyScan.Begin();
                    surveyView = view;
                    surveyProjection = projection;
                    surveyMeasured = surveyUnknown = 0;
                }
                else
                {
                    SurveyScan.AdvanceFrame(++surveyFrame);
                    SurveyBudget.Restart();
                    ThermalVisionRayScan<SurveySample>.Ticket ticket;
                    while (SurveyBudget.Elapsed.TotalMilliseconds < 2 && SurveyScan.TryIssue(out ticket))
                    {
                        int x = ticket.Pixel % SurveyWidth, y = ticket.Pixel / SurveyWidth;
                        double started = SurveyBudget.Elapsed.TotalMilliseconds;
                        SurveySample sample;
                        try
                        {
                            Vector3D ray = ThermalVisionSensorOptics.Ray(x, y, SurveyWidth, SurveyHeight, surveyProjection, surveyView);
/// <summary>QuerySurvey operation.</summary>
                            sample = QuerySurvey(ray);
                        }
                        catch
                        {
                            SurveyScan.Invalidate();
                            SurveyScan.Complete(ticket, new SurveySample());
                            throw;
                        }
                        double elapsed = SurveyBudget.Elapsed.TotalMilliseconds - started;
                        surveyQueryMs += elapsed;
                        surveyMaxQueryMs = Math.Max(surveyMaxQueryMs, elapsed);
                        surveyQueries++;
                        if (!float.IsNaN(sample.Kelvin)) surveyMeasured++;
                        else if (sample.Hit) surveyUnknown++;
                        SurveyScan.Complete(ticket, sample);
                    }
                    if (SurveyScan.HasFrame) PublishSurvey();
                }
            }
            rowIdentity = "survey sensor / collision surfaces within 150 m";
            rowKey = null;
            Outcome(surveyScanning ? "survey-acquiring" : surveyImage.Ready ? "survey-captured" : "survey-no-capture");
            if (frames++ % 6 == 0) panel.Text = new RichText("THERMAL SURVEY / " + (State.Current == ThermalVisionState.Mode.Cividis ? "CIVIDIS" : "WHITE HOT")
                + "\n" + surveyStatus
                + (surveyImage.Ready ? " / age " + (SurveyClock.Elapsed.TotalSeconds - surveyCapturedAt).ToString("0.0") + "s" : "")
                + "\n" + SurveyWidth + " x " + SurveyHeight + " / 150 m / collision approximation"
                + "\nHatched: unknown / dark: no return"
                + "\n" + (lowKelvin - 273.15f).ToString("0") + " to " + (highKelvin - 273.15f).ToString("0")
                + " C / " + (automaticRange ? "AUTO AT CAPTURE" : "LOCK")
                + "\n/thermal vision scan | detail | quick | off");
            panel.Visible = true;
        }

/// <summary>QuerySurvey operation.</summary>
        private static SurveySample QuerySurvey(Vector3D ray)
        {
            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(surveyView.Translation, surveyView.Translation + ray * SurveyReach, out hit);
            var sample = new SurveySample { Hit = hit != null, Kelvin = float.NaN };
            if (hit == null) return sample;
            sample.Facing = (float)Math.Abs(Vector3D.Dot(ray, new Vector3D(hit.Normal)));
            var grid = hit.HitEntity as MyCubeGrid;
            var thermal = grid == null || grid.GameLogic == null ? null : grid.GameLogic.GetAs<ThermalGrid>();
            if (thermal == null || thermal.Simulation == null) return sample;
            var block = thermal.GetAtCell(grid.WorldToGridInteger(hit.Position - hit.Normal * .005f));
            if (block != null && block.Node != null)
            {
                float value = block.Node.Temperature;
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 100000)
                    sample.Kelvin = value;
            }
            return sample;
        }

/// <summary>Publishes the API table to other mods.</summary>
        private static void PublishSurvey()
        {
/// <summary>ThermalVisionAutoRange operation.</summary>
            var range = new ThermalVisionAutoRange();
            SurveySample sample;
            if (automaticRange)
            {
                for (int i = 0; i < SurveyScan.SampleCount; i++)
                    if (SurveyScan.TryRead(i, out sample)) range.Observe(sample.Kelvin);
                range.Update(0);
                if (range.HasSamples) { lowKelvin = range.Low; highKelvin = range.High; }
            }
            for (int i = 0; i < SurveyScan.SampleCount; i++)
            {
                SurveyScan.TryRead(i, out sample);
                surveyImage.Pixels[i] = ThermalVisionSensorOptics.Shade(sample.Hit, sample.Kelvin, sample.Facing,
                    i % SurveyWidth, i / SurveyWidth, State.Current, lowKelvin, highKelvin);
            }
            surveyImage.Ready = true;
            surveyScanning = false;
            windowIdentity = lowKelvin.ToString("R", CultureInfo.InvariantCulture) + ".."
                + highKelvin.ToString("R", CultureInfo.InvariantCulture) + " K";
            surveyCapturedAt = SurveyClock.Elapsed.TotalSeconds;
            surveyStatus = "SNAPSHOT / " + surveyMeasured + " measured / " + surveyUnknown + " unknown";
            RecordEvent("survey captured: queries=" + surveyQueries + " measured=" + surveyMeasured
                + " unknown=" + surveyUnknown + " durationMs=" + SurveyClock.Elapsed.TotalMilliseconds.ToString("0.0")
                + " queryMs=" + surveyQueryMs.ToString("0.00") + " maxQueryMs=" + surveyMaxQueryMs.ToString("0.00")
                + " range=" + windowIdentity);
        }
    }
}
