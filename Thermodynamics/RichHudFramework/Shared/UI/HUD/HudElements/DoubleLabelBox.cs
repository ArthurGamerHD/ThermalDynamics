using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
    public class DoubleLabelBox : LabelBoxBase
    {
        public override Vector2 TextSize { get; set; }

        public override Vector2 TextPadding { get { return left.Padding; } set { left.Padding = value; right.Padding = value; } }

        public override bool AutoResize { get { return left.AutoResize; } set { left.AutoResize = value; right.AutoResize = value; } }

        public TextBuilderModes BuilderMode { get { return left.BuilderMode; } set { left.BuilderMode = value; right.BuilderMode = value; } }

        public RichText LeftText { get { return left.TextBoard.GetText(); } set { left.TextBoard.SetText(value); } }

        public RichText RightText { get { return right.TextBoard.GetText(); } set { right.TextBoard.SetText(value); } }

        public ITextBuilder LeftTextBuilder => left.TextBoard;

        public ITextBuilder RightTextBuilder => right.TextBoard;

        protected readonly Label left, right;


        public DoubleLabelBox(HudParentBase parent = null) : base(parent)
        {

            left = new Label(this) { ParentAlignment = ParentAlignments.PaddedInnerLeft };

            right = new Label(this) { ParentAlignment = ParentAlignments.InnerRight };
        }


        protected override void Measure()
        {
            if (AutoResize)
            {
                Vector2 leftSize = left.TextBoard.TextSize,
                    rightSize = right.TextBoard.TextSize,
                    textSize;

                textSize.X = leftSize.X + rightSize.X;
                textSize.Y = (leftSize.Y > rightSize.Y) ? leftSize.Y : rightSize.Y;
                TextSize = textSize;
            }

            base.Measure();
        }


        protected override void Layout()
        {

            left.Size = new Vector2(0.5f * CachedSize.X, CachedSize.Y);

            right.Size = new Vector2(0.5f * CachedSize.X, CachedSize.Y);
        }
    }
}
