using VRageMath;

namespace RichHudFramework.UI
{
	using Rendering;
	using Rendering.Client;
	using Rendering.Server;

	public class Label : LabelElementBase
	{
		public RichText Text { get { return TextBoard.GetText(); } set { TextBoard.SetText(value); } }

		public override ITextBoard TextBoard { get; }

		public GlyphFormat Format { get { return TextBoard.Format; } set { TextBoard.SetFormatting(value); } }

		public TextBuilderModes BuilderMode { get { return TextBoard.BuilderMode; } set { TextBoard.BuilderMode = value; } }

		public bool AutoResize { get { return TextBoard.AutoResize; } set { TextBoard.AutoResize = value; } }

		public bool VertCenterText { get { return TextBoard.VertCenterText; } set { TextBoard.VertCenterText = value; } }

		public float LineWrapWidth { get { return TextBoard.LineWrapWidth; } set { TextBoard.LineWrapWidth = value; } }

/// <summary>Label operation.</summary>
		public Label(HudParentBase parent) : base(parent)
		{
/// <summary>TextBoard operation.</summary>
			TextBoard = new TextBoard();
			TextBoard.SetText("NewLabel", GlyphFormat.White);
/// <summary>Vector2 operation.</summary>
			UnpaddedSize = new Vector2(50f);
		}

/// <summary>Label operation.</summary>
		public Label() : this(null)
		{ }

/// <summary>Measure operation.</summary>
		protected override void Measure()
		{
			if (TextBoard.AutoResize)
				UnpaddedSize = TextBoard.TextSize;
		}

/// <summary>Draw operation.</summary>
		protected override void Draw()
		{
			Vector2 halfSize = .5f * UnpaddedSize;
/// <summary>BoundingBox2 operation.</summary>
			BoundingBox2 box = new BoundingBox2(Position - halfSize, Position + halfSize);

			if (!TextBoard.AutoResize)
				TextBoard.FixedSize = UnpaddedSize;

			if (MaskingBox != null)
				TextBoard.Draw(box, MaskingBox.Value, HudSpace.PlaneToWorldRef);
			else
				TextBoard.Draw(box, CroppedBox.defaultMask, HudSpace.PlaneToWorldRef);
		}
	}
}