using Thermodynamics.Presentation;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Thermodynamics.Core;
using VRageMath;

namespace Thermodynamics.Harness
{
    /// <summary>Analytic sensor-image feasibility study, not a Havok or game-renderer benchmark.</summary>
    public static class ThermalVisionRayLab
    {
        public struct Sample
        {
            public int Surface;
            public double Distance;
            public float Kelvin;
            public bool Estimated;
        }

        public sealed class Frame
        {
            public int Width, Height;
            public Sample[] Pixels;
        }

        // Pinhole camera at the origin, looking down -Z, 60 degree vertical field of view.
        public static Vector3D Direction(int x, int y, int width, int height)
        {
            double tangent = Math.Tan(Math.PI / 6);
            return Vector3D.Normalize(new Vector3D((2 * (x + .5) / width - 1) * width / height * tangent,
                (1 - 2 * (y + .5) / height) * tangent, -1));
        }

        public static Sample Trace(Vector3D direction, bool reverse = false)
        {
            var hit = new Sample { Distance = double.PositiveInfinity, Kelvin = float.NaN };
            if (direction.Y < 0)
                Consider(ref hit, -1.8 / direction.Y, 1, 265, true); // Estimated ground plane.
            if (reverse)
            {
                Plate(ref hit, direction);
                Reactor(ref hit, direction);
            }
            else
            {
                Reactor(ref hit, direction);
                Plate(ref hit, direction);
            }
            Box(ref hit, direction, new Vector3D(1.5, -.5, -8), new Vector3D(.25, .9, .25), 4, 310, true);
            Box(ref hit, direction, new Vector3D(2.2, -.3, -10), new Vector3D(.025, 1.5, .025), 5, 600, false);
            Sphere(ref hit, direction, new Vector3D(-4, .5, -14), 2, 245, true);
            return hit;
        }

        private static void Plate(ref Sample hit, Vector3D ray)
        { Box(ref hit, ray, new Vector3D(-.6, -.3, -7), new Vector3D(.7, 1.5, .2), 2, 280, false); }

        private static void Reactor(ref Sample hit, Vector3D ray)
        { Box(ref hit, ray, new Vector3D(-.4, -.2, -11), new Vector3D(1.6, 1.6, .7), 3, 420, false); }

        private static void Consider(ref Sample hit, double distance, int surface, float kelvin, bool estimated)
        {
            if (distance <= 0 || distance >= hit.Distance) return;
            hit = new Sample { Distance = distance, Surface = surface, Kelvin = kelvin, Estimated = estimated };
        }

        private static void Box(ref Sample hit, Vector3D ray, Vector3D centre, Vector3D half,
            int surface, float kelvin, bool estimated)
        {
            double near = 0, far = double.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                double d = axis == 0 ? ray.X : axis == 1 ? ray.Y : ray.Z;
                double c = axis == 0 ? centre.X : axis == 1 ? centre.Y : centre.Z;
                double h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
                if (Math.Abs(d) < 1e-12) { if (Math.Abs(c) > h) return; continue; }
                double a = (c - h) / d, b = (c + h) / d;
                near = Math.Max(near, Math.Min(a, b));
                far = Math.Min(far, Math.Max(a, b));
                if (near > far) return;
            }
            Consider(ref hit, near, surface, kelvin, estimated);
        }

        private static void Sphere(ref Sample hit, Vector3D ray, Vector3D centre, double radius, float kelvin, bool estimated)
        {
            double b = Vector3D.Dot(ray, centre);
            double discriminant = b * b - centre.LengthSquared() + radius * radius;
            if (discriminant >= 0) Consider(ref hit, b - Math.Sqrt(discriminant), 6, kelvin, estimated);
        }

        public static Frame Render(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 640 || height > 360)
                throw new ArgumentException("Fixture dimensions must be positive and at most 640 by 360.");
            var frame = new Frame { Width = width, Height = height, Pixels = new Sample[width * height] };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) frame.Pixels[y * width + x] = Trace(Direction(x, y, width, height));
            return frame;
        }

        private static string Colour(Sample sample, ThermalVisionState.Mode mode)
        {
            Vector3 colour;
            if (sample.Surface == 0 || !ThermalVisionPalette.TrySample(sample.Kelvin, mode, 225, 625, out colour))
                return "#080c12"; // Background/no return, deliberately not a measured temperature.
            return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}",
                (int)(colour.X * 255), (int)(colour.Y * 255), (int)(colour.Z * 255));
        }

        public static string Svg(Frame frame, ThermalVisionState.Mode mode)
        {
            var text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture,
                "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {0} {1}' shape-rendering='crispEdges'>", frame.Width, frame.Height);
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width;)
                {
                    string colour = Colour(frame.Pixels[y * frame.Width + x], mode);
                    int end = x + 1;
                    while (end < frame.Width && Colour(frame.Pixels[y * frame.Width + end], mode) == colour) end++;
                    text.AppendFormat(CultureInfo.InvariantCulture,
                        "<rect x='{0}' y='{1}' width='{2}' height='1' fill='{3}'/>", x, y, end - x, colour);
                    x = end;
                }
            return text.Append("</svg>").ToString();
        }

        public static string WriteReport(string directory)
        {
            Directory.CreateDirectory(directory);
            var reference = Render(640, 360);
            var report = new StringBuilder("# Reconstructed thermal sensor: feasibility study\n\n"
                + "Synthetic analytic geometry, not game captures or measured engine performance. "
                + "Production Cividis/white-hot palettes with a common locked 225–625 K window. "
                + "Ground, rock and character-shaped box temperatures are explicitly illustrative estimates; "
                + "block/pole temperatures are fixture values. Background is no-return, not cold.\n\n"
                + "| Samples | Queries/image | Queries/s at 10 Hz | Mean query ceiling with 2 ms/frame at 60 FPS | Surface mismatch vs 640×360 | Thin hot pole pixels detected (reference pixels) |\n"
                + "| --- | ---: | ---: | ---: | ---: | ---: |\n");
            var html = new StringBuilder("<!doctype html><meta charset='utf-8'><title>Thermal sensor feasibility</title>"
                + "<style>body{background:#101923;color:#dce7f0;font:16px system-ui;margin:30px auto;max-width:1280px}"
                + "img{width:49%;image-rendering:pixelated}p{max-width:1000px;line-height:1.5}</style>"
                + "<h1>Reconstructed thermal sensor — synthetic study</h1><p>Not an in-game preview. A cold foreground plate hides a hot reactor; "
                + "the character-shaped box, ground and rock have illustrative estimated temperatures. The very thin hot pole exposes missed-detail risk. "
                + "No-return background is not a temperature reading. Same locked range in every panel. Left: Cividis. Right: white-hot.</p>");
            foreach (int width in new[] { 64, 96, 160, 640 })
            {
                int height = width * 9 / 16;
                var frame = width == 640 ? reference : Render(width, height);
                int mismatch = 0, pole = 0, poleDetected = 0;
                for (int y = 0; y < reference.Height; y++)
                    for (int x = 0; x < reference.Width; x++)
                    {
                        var expected = reference.Pixels[y * reference.Width + x];
                        var actual = frame.Pixels[(y * height / reference.Height) * width + x * width / reference.Width];
                        if (expected.Surface != actual.Surface) mismatch++;
                        if (expected.Surface == 5) { pole++; if (actual.Surface == 5) poleDetected++; }
                    }
                int samples = width * height;
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "| {0}×{1} | {2} | {3} | {4:0.00} µs | {5:0.00}% | {6}/{7} |\n",
                    width, height, samples, samples * 10, 120000d / (samples * 10),
                    mismatch * 100d / reference.Pixels.Length, poleDetected, pole);
                string stem = width + "x" + height;
                File.WriteAllText(Path.Combine(directory, stem + "-colour.svg"), Svg(frame, ThermalVisionState.Mode.Cividis));
                File.WriteAllText(Path.Combine(directory, stem + "-grey.svg"), Svg(frame, ThermalVisionState.Mode.WhiteHot));
                html.Append("<h2>" + stem + (width == 640 ? " reference" : " samples") + "</h2><img alt='Cividis sensor fixture' src='"
                    + stem + "-colour.svg'><img alt='White-hot sensor fixture' src='" + stem + "-grey.svg'>");
            }
            report.Append("\nThe query ceiling spends the entire hypothetical 120 ms/second CPU allowance on queries. "
                + "Sampling temperatures, drawing, scheduling and allocation must fit inside it too. This is workload arithmetic, not a benchmark. "
                + "At 10 Hz, a held image can be nearly 100 ms old; turning at 90 degrees/s moves the view by nearly 9 degrees. "
                + "Reprojection cannot recover newly revealed surfaces or unseen moving objects.\n\n"
                + "Closest analytic hits pass occlusion tests here; game collision shapes, glass, ragdolls, deformed armour, non-colliding effects, "
                + "physics streaming range and native HUD composition remain unverified. Full-scene product readiness remains INCOMPLETE.\n");
            var surveyFrame = Render(64, 36);
            File.WriteAllText(Path.Combine(directory, "survey-colour.svg"), SurveySvg(surveyFrame, ThermalVisionState.Mode.Cividis));
            File.WriteAllText(Path.Combine(directory, "survey-grey.svg"), SurveySvg(surveyFrame, ThermalVisionState.Mode.WhiteHot));
            html.Append("<h2>Implemented survey candidate: synthetic presentation preview</h2><p>Uses the candidate's shared pixel presentation. "
                + "Estimated fixture surfaces are shown as unknown, as the game candidate does. This is not a native HUD screenshot.</p>"
                + "<img alt='Survey colour presentation' src='survey-colour.svg'><img alt='Survey greyscale presentation' src='survey-grey.svg'>");
            File.WriteAllText(Path.Combine(directory, "report.md"), report.ToString());
            File.WriteAllText(Path.Combine(directory, "preview.html"), html.ToString());
            return report.ToString();
        }

        private static string SurveySvg(Frame frame, ThermalVisionState.Mode mode)
        {
            var range = new ThermalVisionAutoRange();
            int measured = 0, unknown = 0;
            foreach (var sample in frame.Pixels)
                if (sample.Surface != 0)
                {
                    if (sample.Estimated) unknown++;
                    else { measured++; range.Observe(sample.Kelvin); }
                }
            range.Update(0);
            var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 720 540'>"
                + "<rect width='720' height='540' fill='#101922'/><g font-family='sans-serif' fill='#dcebf2'>"
                + "<text x='32' y='34' font-size='20'>THERMAL SURVEY / "
                + (mode == ThermalVisionState.Mode.Cividis ? "CIVIDIS" : "WHITE HOT") + "</text>"
                + "<text x='32' y='58' font-size='13'>SNAPSHOT • SYNTHETIC FIXTURE • NOT AN IN-GAME CAPTURE</text></g>"
                + "<rect x='35' y='77' width='650' height='370' fill='#3c5160'/><g shape-rendering='crispEdges'>");
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    var sample = frame.Pixels[y * frame.Width + x];
                    Color colour = ThermalVisionSensorOptics.Shade(sample.Surface != 0,
                        sample.Estimated ? float.NaN : sample.Kelvin, .5f, x, y, mode, range.Low, range.High);
                    svg.AppendFormat(CultureInfo.InvariantCulture,
                        "<rect x='{0}' y='{1}' width='10' height='10' fill='#{2:x2}{3:x2}{4:x2}'/>",
                        40 + x * 10, 82 + y * 10, colour.R, colour.G, colour.B);
                }
            svg.AppendFormat(CultureInfo.InvariantCulture,
                "</g><g font-family='sans-serif' fill='#dcebf2' font-size='14'><text x='32' y='473'>64 × 36 / {0} measured samples / {1} unknown</text>"
                + "<text x='32' y='496'>{2:0} to {3:0} °C / AUTO AT CAPTURE</text>"
                + "<text x='32' y='520'>Hatched: unknown temperature     Dark: no return</text></g></svg>",
                measured, unknown, range.Low - 273.15, range.High - 273.15);
            return svg.ToString();
        }
    }
}
