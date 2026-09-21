using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
	public class LabelBox : LabelBoxBase, ILabelElement
	{
		public RichText Text { get { return TextBoard.GetText(); } set { TextBoard.SetText(value); } }

		public GlyphFormat Format { get { return TextBoard.Format; } set { TextBoard.SetFormatting(value); } }

		public override Vector2 TextPadding { get { return textElement.Padding; } set { textElement.Padding = value; } }

		public override Vector2 TextSize { get { return textElement.Size; } set { textElement.Size = value; } }

		public override bool AutoResize { get { return TextBoard.AutoResize; } set { TextBoard.AutoResize = value; } }

		public TextBuilderModes BuilderMode { get { return TextBoard.BuilderMode; } set { TextBoard.BuilderMode = value; } }

		public bool VertCenterText { get { return TextBoard.VertCenterText; } set { TextBoard.VertCenterText = value; } }

		public ITextBoard TextBoard { get; }

		public readonly Label textElement;

/// <summary>LabelBox operation.</summary>
		public LabelBox(HudParentBase parent) : base(parent)
		{
/// <summary>Label operation.</summary>
			textElement = new Label(this);
			TextBoard = textElement.TextBoard;
		}

/// <summary>LabelBox operation.</summary>
		public LabelBox() : this(null)
		{ }
	}
}