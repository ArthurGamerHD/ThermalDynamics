using System.Text;

namespace Thermodynamics
{
    /// <summary>
    /// What the block overlay costs the client, per frame it drew.
    ///
    /// The overlay is a diagnostic, and a diagnostic that halves the frame rate on the ships worth
    /// diagnosing is one nobody leaves on. It draws no simulation, so nothing else in the report
    /// records it: without this, an overlay-induced stutter appears in the frame histogram as a
    /// simulation cost it is not.
    ///
    /// Free of any Space Engineers type, so what it records is testable outside the game.
    /// </summary>
    public class OverlayTelemetry
    {
        /// <summary>Frames on which the overlay drew something.</summary>
        public long Frames;

        /// <summary>Blocks or cells the view walked.</summary>
        public readonly RunningStat Considered = new RunningStat();

        /// <summary>Of those, the ones drawn.</summary>
        public readonly RunningStat Drawn = new RunningStat();

        /// <summary>Dropped as outside the camera's cone.</summary>
        public readonly RunningStat OffScreen = new RunningStat();

        /// <summary>Dropped by the draw budget.</summary>
        public readonly RunningStat OverBudget = new RunningStat();

        /// <summary>The budget's fitted radius, metres. Absent while it is unbounded.</summary>
        public readonly RunningStat Radius = new RunningStat();

        /// <summary>
        /// Billboards handed to the renderer: six quads and twelve lines per solid box, twelve for
        /// an outline, one per drawn surface. The figure the frame cost tracks against.
        /// </summary>
        public readonly RunningStat Billboards = new RunningStat();

        /// <summary>Wall clock inside the overlay's own draw.</summary>
        public readonly TimingStat Draw = new TimingStat("overlay draw");

        /// <summary>Frames on which the budget held part of a grid back.</summary>
        public long LimitedFrames;

        /// <summary>The view last drawn, so a cost can be attributed to a mode.</summary>
        public string Mode = "";

        public void Frame(
            string mode, int considered, int drawn, int offScreen, int overBudget,
            long billboards, double radius, double milliseconds)
        {
            Frames++;
            Mode = mode;

            Considered.Add(considered);
            Drawn.Add(drawn);
            OffScreen.Add(offScreen);
            OverBudget.Add(overBudget);
            Billboards.Add(billboards);
            Draw.Record(milliseconds);

            if (radius < OverlayBudget.Unbounded)
            {
                LimitedFrames++;
                Radius.Add((float)radius);
            }
        }


        public void Write(StringBuilder sb)
        {
            sb.Append("  frames drawn                  ").Append(Frames.ToString("n0")).Append('\n');
            sb.Append("  view                          ").Append(Mode).Append('\n');
            sb.Append("  ms per frame                  ").Append(Draw.MeanMilliseconds.ToString("n3"))
                .Append(" mean, ").Append(Draw.MaxMilliseconds.ToString("n3")).Append(" worst\n");
            sb.Append("  boxes considered              ").Append(Considered.Format("n0")).Append('\n');
            sb.Append("  boxes drawn                   ").Append(Drawn.Format("n0")).Append('\n');
            sb.Append("  dropped off screen            ").Append(OffScreen.Format("n0")).Append('\n');
            sb.Append("  dropped over budget           ").Append(OverBudget.Format("n0")).Append('\n');
            sb.Append("  billboards                    ").Append(Billboards.Format("n0")).Append('\n');

            if (LimitedFrames == 0)
            {
                sb.Append("  budget                        never reached\n");
                return;
            }

            sb.Append("  frames limited                ").Append(LimitedFrames.ToString("n0")).Append('\n');
            sb.Append("  fitted radius m               ").Append(Radius.Format("n1")).Append('\n');
        }
    }
}
