using Thermodynamics.Presentation;
using System.Collections.Generic;
using System.Text;

namespace Thermodynamics
{
    [System.Flags]
    public enum ThermalVisionSceneLimit
    {
        None = 0, Discovery = 1, Blocks = 2, Triangles = 4, Time = 8, ModelBuild = 16, ArmourQuota = 32,
    }

    public sealed class ThermalVisionTelemetry
    {
        public const int RowLimit = 128;
        public const int EventLimit = 64;
        public long Frames, SuppressedFrames, Errors, RowsOverflowed, EventsDropped, ImportantEventsDropped;

        public readonly RunningStat CpuMilliseconds = new RunningStat();

        public readonly RunningStat ColdMilliseconds = new RunningStat();

        public readonly RunningStat WarmMilliseconds = new RunningStat();
        private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();

        private readonly Queue<string> events = new Queue<string>();

        private readonly Queue<string> importantEvents = new Queue<string>();

        private readonly object gate = new object();

        private readonly Row overflow = new Row();
        private long sceneFrames, sceneLimited, sceneRefreshes;

        private readonly RunningStat sceneBatchTests = new RunningStat(), sceneBatchRejected = new RunningStat();

        public void SceneCulling(int batches, int rejectedTriangles)
        {
            lock (gate) { sceneBatchTests.Add(batches); sceneBatchRejected.Add(rejectedTriangles); }
        }
        private long scenePendingFrames;

        private readonly RunningStat sceneBuildTriangles = new RunningStat(), sceneBuildMs = new RunningStat();

        public void SceneBuild(int sourceTriangles, bool pending, float milliseconds)
        {
            lock (gate)
            {
                if (pending) scenePendingFrames++;
                sceneBuildTriangles.Add(sourceTriangles); sceneBuildMs.Add(milliseconds);
            }
        }
        private readonly long[] sceneLimitFrames = new long[6];

        private readonly RunningStat sceneArmour = new RunningStat(), sceneDetail = new RunningStat();


        public void SceneBudget(ThermalVisionSceneLimit limits, int armourSubmitted, int detailSubmitted)
        {
            lock (gate)
            {
                for (int i = 0; i < sceneLimitFrames.Length; i++)
                    if (((int)limits & (1 << i)) != 0) sceneLimitFrames[i]++;
                sceneArmour.Add(armourSubmitted); sceneDetail.Add(detailSubmitted);
            }
        }

        public readonly RunningStat SceneDiscoveryMilliseconds = new RunningStat();

        public readonly RunningStat SceneDrawingMilliseconds = new RunningStat();

        private readonly RunningStat sceneScanned = new RunningStat(), sceneCandidates = new RunningStat();

        private readonly RunningStat sceneAttempted = new RunningStat(), sceneSubmitted = new RunningStat();

        private readonly RunningStat sceneMissing = new RunningStat(), sceneLow = new RunningStat(), sceneHigh = new RunningStat();


        public void SceneFrame(int scanned, int candidates, int attempted, int submitted, int missing, bool limited, float low, float high, bool refreshed = false, float discoveryMs = 0, float drawingMs = 0)
        {
            lock (gate)
            {
                sceneFrames++;
                if (refreshed) sceneRefreshes++;
                SceneDiscoveryMilliseconds.Add(discoveryMs);
                SceneDrawingMilliseconds.Add(drawingMs);
                if (limited) sceneLimited++;
                sceneScanned.Add(scanned); sceneCandidates.Add(candidates); sceneAttempted.Add(attempted);
                sceneSubmitted.Add(submitted); sceneMissing.Add(missing); sceneLow.Add(low); sceneHigh.Add(high);
            }
        }

        private sealed class Row
        {
            public long Frames, Empty, Partial, Backfaces, Degenerate;

            public readonly RunningStat Temperature = new RunningStat();

            public readonly RunningStat Submitted = new RunningStat();

            public readonly RunningStat Examined = new RunningStat();

            public readonly RunningStat Cpu = new RunningStat();


            public void Add(float kelvin, int submitted, int examined, bool partial, float ms, int backfaces, int degenerate)
            {
                Frames++;
                Backfaces += backfaces;
                Degenerate += degenerate;
                if (submitted == 0) Empty++;
                if (partial) Partial++;
                Temperature.Add(kelvin);
                Submitted.Add(submitted);
                Examined.Add(examined);
                Cpu.Add(ms);
            }


            public void Write(StringBuilder sb, string key)
            {
                sb.Append("  ").Append(key).Append(": frames=").Append(Frames)
                    .Append(" zero-submitted=").Append(Empty).Append(" partial=").Append(Partial).Append(" backfaces-total=").Append(Backfaces)
                        .Append(" degenerate-total=").Append(Degenerate).Append('\n');
                sb.Append("    K min/mean/max: ").Append(Temperature.Format("n2")).Append('\n');
                sb.Append("    submitted: ").Append(Submitted.Format("n0"))
                    .Append("; examined: ").Append(Examined.Format("n0")).Append('\n');
                sb.Append("    CPU ms: ").Append(Cpu.Format("n3")).Append('\n');
            }
        }


        public void Suppress() { lock (gate) SuppressedFrames++; }

        public void Failure() { lock (gate) Errors++; }


        public void Frame(string key, float kelvin, int submitted, int examined, bool partial, bool cold, float ms, int backfaces = 0, int degenerate = 0)
        {
            lock (gate)
            {
                Frames++;
                CpuMilliseconds.Add(ms);
                (cold ? ColdMilliseconds : WarmMilliseconds).Add(ms);
                Row row;
                if (!rows.TryGetValue(key, out row))
                {
                    if (rows.Count >= RowLimit) { row = overflow; RowsOverflowed++; }
                    else { row = new Row(); rows.Add(key, row); }
                }
                row.Add(kelvin, submitted, examined, partial, ms, backfaces, degenerate);
            }
        }


        public void Event(double seconds, string message, bool important = false)
        {
            lock (gate)
            {
                var queue = important ? importantEvents : events;
                if (queue.Count == EventLimit)
                {
                    queue.Dequeue();
                    if (important) ImportantEventsDropped++; else EventsDropped++;
                }
                string clean = (message ?? "").Replace('\r', ' ').Replace('\n', ' ');
                if (clean.Length > 320) clean = clean.Substring(0, 320);
                queue.Enqueue(seconds.ToString("n2") + "s " + clean);
            }
        }


        public void Write(StringBuilder sb)
        {
            lock (gate)
            {
                sb.Append("\nTHERMAL VISION PROBE / telemetry v9 / client only\n")
                    .Append("  Mode and range are in row identity; DISTANCE ONLY rows are not temperature measurements.\n")
                    .Append("  Aimed-model limits only: reach 15 m; model/cache/frame 65536/262144/98304 triangles; 128 cached models; 32 parts.\n")
                    .Append("  Regional rows: boundary triangles (not model geometry); group size in row; field age/count/status in events; neutral context unmeasured and excluded from submitted counts; context-active reported in events.\n")
                    .Append("  Composite rows count measured surface triangles only; exclude four context triangles. Dark context is unmeasured.\n")
                    .Append("  Native-depth diagnostic: 48 planes / 96 submitted triangles / 5000 m; no CPU raycasts or mesh extraction.\n")
                    .Append("  CPU timings cover the selected adapter, including lookup/cache work where applicable; exclude GPU completion.\n")
                    .Append("  Submitted triangles do NOT prove visibility, correct occlusion or visual quality.\n")
                    .Append("  Captured frames=").Append(Frames).Append(" menu-suppressed=").Append(SuppressedFrames)
                    .Append(" render-errors=").Append(Errors).Append('\n')
                    .Append("  CPU ms min/mean/max: ").Append(CpuMilliseconds.Format("n3")).Append('\n')
                    .Append("  Cache-miss CPU ms: ").Append(ColdMilliseconds.Format("n3")).Append('\n')
                    .Append("  No-cache-miss CPU ms: ").Append(WarmMilliseconds.Format("n3")).Append('\n');
                if (sceneFrames > 0)
                {
                    sb.Append("  Scene frames=").Append(sceneFrames).Append(" discovery/draw-limit frames=").Append(sceneLimited)
                        .Append("; scene 100 m / composite 5000 m, 4096 scanned blocks, 512 attempted, 32768 examined triangles, 8192 new model triangles, soft 4 ms/frame.\n")
                        .Append("    Discovery refreshes=").Append(sceneRefreshes).Append("; cached candidates revalidated each draw; refresh every 10 draws or camera movement.\n")
                        .Append("    Discovery/sort CPU ms: ").Append(SceneDiscoveryMilliseconds.Format("n3")).Append('\n')
                        .Append("    Candidate validation/draw CPU ms: ").Append(SceneDrawingMilliseconds.Format("n3")).Append('\n')
                        .Append("    Scanned: ").Append(sceneScanned.Format("n0")).Append("; candidates: ").Append(sceneCandidates.Format("n0")).Append('\n')
                        .Append("    Attempted: ").Append(sceneAttempted.Format("n0")).Append("; submitted blocks: ").Append(sceneSubmitted.Format("n0"))
                        .Append("; no submission/data: ").Append(sceneMissing.Format("n0")).Append('\n')
                        .Append("    Low K: ").Append(sceneLow.Format("n2")).Append("; high K: ").Append(sceneHigh.Format("n2")).Append('\n')
                        .Append("    Candidates are frustum/range filtered, not pixel-visible; hidden blocks may affect AUTO.\n");
                }
                if (sceneFrames > 0)
                {
                    sb.Append("    Batch tests/frame: ").Append(sceneBatchTests.Format("n0"))
                        .Append("; triangles rejected by batch: ").Append(sceneBatchRejected.Format("n0")).Append('\n')
                        .Append("    Examined counts individual triangles after batch rejection; backfaces include batch rejects.\n");
                    sb.Append("    Progressive build pending frames=").Append(scenePendingFrames)
                        .Append("; source triangles/frame: ").Append(sceneBuildTriangles.Format("n0"))
                        .Append("; build CPU ms: ").Append(sceneBuildMs.Format("n3")).Append('\n');
                    sb.Append("    Limit frames (may overlap): discovery=").Append(sceneLimitFrames[0])
                        .Append(" blocks=").Append(sceneLimitFrames[1]).Append(" triangles=").Append(sceneLimitFrames[2])
                        .Append(" time=").Append(sceneLimitFrames[3]).Append(" model-build=").Append(sceneLimitFrames[4])
                        .Append(" armour-quota=").Append(sceneLimitFrames[5]).Append('\n')
                        .Append("    Armour submitted blocks: ").Append(sceneArmour.Format("n0"))
                        .Append("; entity submitted blocks: ").Append(sceneDetail.Format("n0")).Append('\n')
                        .Append("    Armour first pass: 384 blocks, 8192 triangles, soft 1 ms after discovery/build; remainder reserved for entity models.\n");
                }
                foreach (var entry in rows) entry.Value.Write(sb, entry.Key);
                if (RowsOverflowed > 0) overflow.Write(sb, "OTHER (row cap; merged frames=" + RowsOverflowed + ")");
                sb.Append("  Important events (notes, modes, range, errors); older events dropped=").Append(ImportantEventsDropped).Append('\n');
                foreach (string entry in importantEvents) sb.Append("    ").Append(entry).Append('\n');
                sb.Append("  Recent events; older events dropped=").Append(EventsDropped).Append('\n');
                foreach (string entry in events) sb.Append("    ").Append(entry).Append('\n');
            }
        }
    }
}
