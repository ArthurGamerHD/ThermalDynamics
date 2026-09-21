using System.Text;

namespace Thermodynamics
{
    public class OverlayTelemetry
    {
        public long Frames;

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Considered = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Drawn = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat OffScreen = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat OverBudget = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Radius = new RunningStat();

/// <summary>RunningStat operation.</summary>
        public readonly RunningStat Billboards = new RunningStat();

/// <summary>TimingStat operation.</summary>
        public readonly TimingStat Draw = new TimingStat("overlay draw");

        public long LimitedFrames;

        public string Mode = "";

/// <summary>Frame operation.</summary>
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


/// <summary>Write operation.</summary>
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
