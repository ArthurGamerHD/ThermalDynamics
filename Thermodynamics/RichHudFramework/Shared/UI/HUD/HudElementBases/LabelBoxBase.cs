using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		public abstract class LabelBoxBase : HudElementBase
		{
			public abstract Vector2 TextSize { get; set; }

			public abstract Vector2 TextPadding { get; set; }

			public abstract bool AutoResize { get; set; }

			public bool FitToTextElement { get; set; }

			public virtual Color Color { get { return Background.Color; } set { Background.Color = value; } }

			public readonly TexturedBox Background;


			public LabelBoxBase(HudParentBase parent) : base(parent)
			{

				Background = new TexturedBox(this)
				{
					DimAlignment = DimAlignments.UnpaddedSize,
				};

				FitToTextElement = true;
				Color = Color.Gray;

				UnpaddedSize = new Vector2(50f);
			}


			protected override void Measure()
			{
				if (AutoResize)
				{
					if (FitToTextElement)
						UnpaddedSize = TextSize;
					else
						UnpaddedSize = Vector2.Max(UnpaddedSize, TextSize);
				}
			}


			protected override void Layout()
			{
				if (!AutoResize)
				{
					TextSize = UnpaddedSize;
				}
			}
		}
	}
}