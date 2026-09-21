using VRageMath;

namespace RichHudFramework.UI
{
	public class NamedOnOffButton : HudElementBase, IClickableElement, IValueControl<bool>
    {
		public event EventHandler ValueChanged
		{
			add { onOffButton.ValueChanged += value; }
			remove { onOffButton.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback { set { onOffButton.ValueChanged += value; } }

		public RichText Name { get { return name.Text; } set { name.Text = value; } }

		public float ButtonSpacing { get { return onOffButton.ButtonSpacing; } set { onOffButton.ButtonSpacing = value; } }

		public Vector2 ButtonPadding { get { return onOffButton.Padding; } set { onOffButton.Padding = value; } }

		public Color BorderColor { get { return onOffButton.BorderColor; } set { onOffButton.BorderColor = value; } }

		public RichText OnText { get { return onOffButton.OnText; } set { onOffButton.OnText = value; } }

		public RichText OffText { get { return onOffButton.OnText; } set { onOffButton.OnText = value; } }

		public GlyphFormat Format { get { return onOffButton.Format; } set { onOffButton.Format = value; } }

		public bool Value { get { return onOffButton.Value; } set { onOffButton.Value = value; } }

		public IFocusHandler FocusHandler => onOffButton.FocusHandler;

		public IMouseInput MouseInput => onOffButton.MouseInput;

		protected readonly Label name;

		protected readonly OnOffButton onOffButton;

		protected readonly HudChain layout;

/// <summary>NamedOnOffButton operation.</summary>
		public NamedOnOffButton(HudParentBase parent) : base(parent)
		{
/// <summary>Label operation.</summary>
			name = new Label()
			{
				Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Center),
				Text = "NewOnOffButton",
				Height = 22f,
			};

/// <summary>OnOffButton operation.</summary>
			onOffButton = new OnOffButton();

/// <summary>HudChain operation.</summary>
			layout = new HudChain(true, this)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
				Spacing = 2f,
				CollectionContainer = { { name, 0f }, { onOffButton, 0f } }
			};

			FocusHandler.InputOwner = this;
/// <summary>Vector2 operation.</summary>
			Padding = new Vector2(40f, 0f);
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(300f, 84f);
		}

/// <summary>NamedOnOffButton operation.</summary>
		public NamedOnOffButton() : this(null)
		{ }
	}
}