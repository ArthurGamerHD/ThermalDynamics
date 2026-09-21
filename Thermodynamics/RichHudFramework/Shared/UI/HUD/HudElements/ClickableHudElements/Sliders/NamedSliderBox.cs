using System;
using VRageMath;

namespace RichHudFramework.UI
{
	using UI.Rendering;

	public class NamedSliderBox : HudElementBase, IClickableElement, IValueControl<float>
    {
		public event EventHandler ValueChanged
		{
			add { SliderBox.ValueChanged += value; }
			remove { SliderBox.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback
		{
			set { SliderBox.ValueChanged += value; }
		}

		public RichText Name { get { return name.TextBoard.GetText(); } set { name.TextBoard.SetText(value); } }

		public RichText ValueText { get { return current.TextBoard.GetText(); } set { current.TextBoard.SetText(value); } }

		public ITextBuilder NameBuilder => name.TextBoard;

		public ITextBuilder ValueBuilder => current.TextBoard;

		public float Min { get { return SliderBox.Min; } set { SliderBox.Min = value; } }

		public float Max { get { return SliderBox.Max; } set { SliderBox.Max = value; } }

		public float Value { get { return SliderBox.Value; } set { SliderBox.Value = value; } }

		public float Percent { get { return SliderBox.Percent; } set { SliderBox.Percent = value; } }

		public IFocusHandler FocusHandler => SliderBox.FocusHandler;

		public IMouseInput MouseInput => SliderBox.MouseInput;

		public override bool IsMousedOver => SliderBox.IsMousedOver;

		public readonly SliderBox SliderBox;

		protected readonly Label name, current;

/// <summary>NamedSliderBox operation.</summary>
		public NamedSliderBox(HudParentBase parent) : base(parent)
		{
/// <summary>SliderBox operation.</summary>
			SliderBox = new SliderBox(this)
			{
				DimAlignment = DimAlignments.UnpaddedWidth,
				ParentAlignment = ParentAlignments.InnerBottom,
				UseCursor = true,
			};

/// <summary>Label operation.</summary>
			name = new Label(this)
			{
				AutoResize = false,
				Format = TerminalFormatting.ControlFormat,
				Text = "NewSlideBox",
/// <summary>Vector2 operation.</summary>
				Offset = new Vector2(0f, -18f),
				ParentAlignment = ParentAlignments.PaddedInnerLeft | ParentAlignments.Top
			};

/// <summary>Label operation.</summary>
			current = new Label(this)
			{
				AutoResize = false,
				Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Right),
				Text = "Value",
/// <summary>Vector2 operation.</summary>
				Offset = new Vector2(0f, -18f),
				ParentAlignment = ParentAlignments.PaddedInnerRight | ParentAlignments.Top
			};

			FocusHandler.InputOwner = this;
/// <summary>Vector2 operation.</summary>
			Padding = new Vector2(40f, 0f);
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(317f, 70f);
		}

/// <summary>NamedSliderBox operation.</summary>
		public NamedSliderBox() : this(null)
		{ }

/// <summary>Layout operation.</summary>
		protected override void Layout()
		{
			Vector2 size = UnpaddedSize;
			current.UnpaddedSize = current.TextBoard.TextSize;
			name.UnpaddedSize = name.TextBoard.TextSize;
			SliderBox.Height = size.Y - Math.Max(name.Height, current.Height);
			current.Width = Math.Max(size.X - name.Width - 10f, 0f);
		}
	}
}