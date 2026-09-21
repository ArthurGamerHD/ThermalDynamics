using VRageMath;
using System;

namespace RichHudFramework.UI
{
	public class SliderBox : HudElementBase, IClickableElement, IValueControl<float>
    {
		public event EventHandler ValueChanged
		{
			add { slide.ValueChanged += value; }
			remove { slide.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback
		{
			set { slide.ValueChanged += value; }
		}

		public float Min { get { return slide.Min; } set { slide.Min = value; } }

		public float Max { get { return slide.Max; } set { slide.Max = value; } }

		public float Value { get { return slide.Value; } set { slide.Value = value; } }

		public float Percent { get { return slide.Percent; } set { slide.Percent = value; } }

		public Color BarColor { get { return slide.BarColor; } set { slide.BarColor = value; lastBarColor = value; } }

		public Color BarHighlight { get { return slide.BarHighlight; } set { slide.BarHighlight = value; } }

		public Color BarFocusColor { get; set; }

		public Color SliderColor { get { return slide.SliderColor; } set { slide.SliderColor = value; lastSliderColor = value; } }

		public Color SliderHighlight { get { return slide.SliderHighlight; } set { slide.SliderHighlight = value; } }

		public Color SliderFocusColor { get; set; }

		public Color BackgroundColor { get { return background.Color; } set { background.Color = value; lastBackgroundColor = value; } }

		public Color BackgroundHighlight { get; set; }

		public Color BackgroundFocusColor { get; set; }

		public Color BorderColor { get { return border.Color; } set { border.Color = value; } }

		public bool HighlightEnabled { get; set; }

		public bool UseFocusFormatting { get; set; }

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput => slide;

		public override bool IsMousedOver => slide.IsMousedOver;

		protected readonly TexturedBox background;

		protected readonly BorderBox border;

		protected readonly SliderBar slide;

		protected Color lastBarColor, lastSliderColor, lastBackgroundColor;

/// <summary>SliderBox operation.</summary>
		public SliderBox(HudParentBase parent) : base(parent)
		{
/// <summary>TexturedBox operation.</summary>
			background = new TexturedBox(this)
			{
				DimAlignment = DimAlignments.Size
			};

/// <summary>BorderBox operation.</summary>
			border = new BorderBox(background)
			{
				Thickness = 1f,
				DimAlignment = DimAlignments.Size,
			};

/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this)
			{
				GainedInputFocusCallback = GainFocus,
				LostInputFocusCallback = LoseFocus
			};
/// <summary>SliderBar operation.</summary>
			slide = new SliderBar(this)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
/// <summary>Vector2 operation.</summary>
				SliderSize = new Vector2(14f, 28f),
				BarHeight = 5f,
				MouseInput =
				{
					CursorEnteredCallback = CursorEnter,
					CursorExitedCallback = CursorExit
				}
			};

			BackgroundColor = TerminalFormatting.OuterSpace;
			BorderColor = TerminalFormatting.LimedSpruce;
			BackgroundHighlight = TerminalFormatting.Atomic;
			BackgroundFocusColor = TerminalFormatting.Mint;

			SliderColor = TerminalFormatting.MistBlue;
			SliderHighlight = Color.White;
			SliderFocusColor = TerminalFormatting.Cinder;

			BarColor = TerminalFormatting.MidGrey;
			BarHighlight = Color.White;
			BarFocusColor = TerminalFormatting.BlackPerl;

			UseFocusFormatting = true;
			HighlightEnabled = true;

/// <summary>Vector2 operation.</summary>
			Padding = new Vector2(18f, 18f);
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(317f, 47f);
		}

/// <summary>SliderBox operation.</summary>
		public SliderBox() : this(null)
		{ }

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			if (FocusHandler.HasFocus)
			{
				if (SharedBinds.LeftArrow.IsNewPressed || SharedBinds.LeftArrow.IsPressedAndHeld)
				{
					Percent -= 0.01f;
				}
/// <summary>if operation.</summary>
				else if (SharedBinds.RightArrow.IsNewPressed || SharedBinds.RightArrow.IsPressedAndHeld)
				{
					Percent += 0.01f;
				}
			}
		}

/// <summary>CursorEnter operation.</summary>
		protected virtual void CursorEnter(object sender, EventArgs args)
		{
			if (HighlightEnabled)
			{
				if (!(UseFocusFormatting && FocusHandler.HasFocus))
				{
					lastBarColor = slide.BarColor;
					lastSliderColor = slide.SliderColor;
					lastBackgroundColor = background.Color;
				}

				slide.SliderColor = SliderHighlight;
				slide.BarColor = BarHighlight;
				background.Color = BackgroundHighlight;
			}
		}

/// <summary>CursorExit operation.</summary>
		protected virtual void CursorExit(object sender, EventArgs args)
		{
			if (HighlightEnabled)
			{
				if (UseFocusFormatting && FocusHandler.HasFocus)
				{
					slide.SliderColor = SliderFocusColor;
					slide.BarColor = BarFocusColor;
					background.Color = BackgroundFocusColor;
				}
				else
				{
					slide.SliderColor = lastSliderColor;
					slide.BarColor = lastBarColor;
					background.Color = lastBackgroundColor;
				}
			}
		}

/// <summary>GainFocus operation.</summary>
		protected virtual void GainFocus(object sender, EventArgs args)
		{
			if (UseFocusFormatting && !MouseInput.IsMousedOver)
			{
				lastBarColor = slide.BarColor;
				lastSliderColor = slide.SliderColor;
				lastBackgroundColor = background.Color;

				slide.SliderColor = SliderFocusColor;
				slide.BarColor = BarFocusColor;
				background.Color = BackgroundFocusColor;
			}
		}

/// <summary>LoseFocus operation.</summary>
		protected virtual void LoseFocus(object sender, EventArgs args)
		{
			if (UseFocusFormatting)
			{
				slide.SliderColor = lastSliderColor;
				slide.BarColor = lastBarColor;
				background.Color = lastBackgroundColor;
			}
		}
	}
}