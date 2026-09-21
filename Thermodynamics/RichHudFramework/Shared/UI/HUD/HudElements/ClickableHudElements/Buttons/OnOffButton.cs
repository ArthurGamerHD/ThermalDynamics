using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class OnOffButton : HudElementBase, IClickableElement, IValueControl<bool>
    {
		public event EventHandler ValueChanged;

		public EventHandler UpdateValueCallback { set { ValueChanged += value; } }

		public float ButtonSpacing { get { return buttonChain.Spacing; } set { buttonChain.Spacing = value; } }

		public Color BorderColor
		{
			get { return onBorder.Color; }
			set
			{
				onBorder.Color = value;
				offBorder.Color = value;
				bgBorder.Color = value;
			}
		}

		public Vector2 BackgroundPadding { get { return buttonChain.Padding; } set { buttonChain.Padding = value; } }

		public Color BackgroundColor { get { return _backgroundColor; } set { background.Color = value; _backgroundColor = value; } }

		public Color FocusColor { get; set; }

		public Color HighlightColor { get; set; }

		public Color UnselectedColor { get; set; }

		public Color SelectionColor { get; set; }

		public RichText OnText { get { return on.Text; } set { on.Text = value; } }

		public RichText OffText { get { return off.Text; } set { off.Text = value; } }

		public GlyphFormat Format { get { return on.Format; } set { on.Format = value; off.Format = value; } }

		public bool Value { get; set; }

		public bool UseFocusFormatting { get; set; }

		public virtual bool HighlightEnabled { get; set; }

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput { get; }

		protected readonly LabelBox on, off;

		protected readonly BorderBox onBorder, offBorder;

		protected readonly HudChain buttonChain;

		protected readonly TexturedBox background;

		protected readonly BorderBox bgBorder;

		protected readonly MouseInputElement _mouseInput;
		protected Color _backgroundColor;

		protected bool lastValue;

/// <summary>OnOffButton operation.</summary>
		public OnOffButton(HudParentBase parent) : base(parent)
		{
/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this);
/// <summary>MouseInputElement operation.</summary>
			_mouseInput = new MouseInputElement(this);
			MouseInput = _mouseInput;

/// <summary>TexturedBox operation.</summary>
			background = new TexturedBox(this)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
			};

/// <summary>BorderBox operation.</summary>
			bgBorder = new BorderBox(background)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
			};

/// <summary>LabelBox operation.</summary>
			on = new LabelBox()
			{
				AutoResize = false,
/// <summary>Vector2 operation.</summary>
				Size = new Vector2(71f, 49f),
				Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Center),
				Text = "On"
			};

/// <summary>BorderBox operation.</summary>
			onBorder = new BorderBox(on)
			{
				Thickness = 2f,
				DimAlignment = DimAlignments.UnpaddedSize,
			};

/// <summary>LabelBox operation.</summary>
			off = new LabelBox()
			{
				AutoResize = false,
/// <summary>Vector2 operation.</summary>
				Size = new Vector2(71f, 49f),
				Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Center),
				Text = "Off"
			};

/// <summary>BorderBox operation.</summary>
			offBorder = new BorderBox(off)
			{
				Thickness = 2f,
				DimAlignment = DimAlignments.UnpaddedSize,
			};

/// <summary>HudChain operation.</summary>
			buttonChain = new HudChain(false, bgBorder)
			{
				DimAlignment = DimAlignments.Size,
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
/// <summary>Vector2 operation.</summary>
				Padding = new Vector2(20f, 10f),
				Spacing = 9f,
				CollectionContainer = { { on, 1f }, { off, 1f } }
			};

/// <summary>Vector2 operation.</summary>
			Size = new Vector2(166f, 59f);

			BackgroundColor = TerminalFormatting.Cinder.SetAlphaPct(0.8f);
			HighlightColor = TerminalFormatting.Atomic;
			FocusColor = TerminalFormatting.Mint;
			BorderColor = TerminalFormatting.LimedSpruce;

			UnselectedColor = TerminalFormatting.OuterSpace;
			SelectionColor = TerminalFormatting.DullMint;

			HighlightEnabled = true;
			UseFocusFormatting = true;

			_mouseInput.LeftClicked += LeftClick;
			lastValue = Value;
		}

/// <summary>OnOffButton operation.</summary>
		public OnOffButton() : this(null)
		{ }

/// <summary>LeftClick operation.</summary>
		protected virtual void LeftClick(object sender, EventArgs args) => Value = !Value;

/// <summary>Layout operation.</summary>
		protected override void Layout()
		{
			if (Value)
			{
				on.Color = SelectionColor;
				off.Color = UnselectedColor;
			}
			else
			{
				off.Color = SelectionColor;
				on.Color = UnselectedColor;
			}

			if (HighlightEnabled && _mouseInput.IsMousedOver)
				background.Color = HighlightColor;
/// <summary>if operation.</summary>
			else if (UseFocusFormatting && FocusHandler.HasFocus)
				background.Color = FocusColor;
			else
				background.Color = BackgroundColor;
		}

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			if (lastValue != Value)
			{
				ValueChanged?.Invoke(FocusHandler?.InputOwner, EventArgs.Empty);
				lastValue = Value;
			}

			if (FocusHandler.HasFocus && SharedBinds.Space.IsNewPressed)
			{
				_mouseInput.LeftClick();
			}
		}
	}
}