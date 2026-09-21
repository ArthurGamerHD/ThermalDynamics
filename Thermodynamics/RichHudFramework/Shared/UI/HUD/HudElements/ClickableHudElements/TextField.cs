using System;
using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
    public class TextField : LabelBoxBase, IClickableElement, IBindInputElement, ILabelElement, IValueControl<ITextBuilder>
    {
        public event EventHandler ValueChanged
        { 
            add { textBox.ValueChanged += value; }
            remove { textBox.ValueChanged -= value; }
        }

		public EventHandler UpdateValueCallback { set { textBox.ValueChanged += value; } }

		public ITextBuilder Value => textBox.TextBoard;

        public RichText Text { get { return textBox.TextBoard.GetText(); } set { textBox.TextBoard.SetText(value); } }

        public ITextBoard TextBoard => textBox.TextBoard;

        public GlyphFormat Format { get { return textBox.Format; } set { textBox.Format = value; } }

        public Color FocusTextColor { get; set; }

        public override Vector2 TextSize { get { return textBox.Size; } set { textBox.Size = value; } }

        public override Vector2 TextPadding { get { return textBox.Padding; } set { textBox.Padding = value; } }

        public override bool AutoResize { get { return textBox.AutoResize; } set { textBox.AutoResize = value; } }

        public bool EnableEditing { get { return textBox.EnableEditing; } set { textBox.EnableEditing = value; } }

        public bool EnableTextHighlighting { get { return textBox.EnableHighlighting; } set { textBox.EnableHighlighting = value; } }

        public bool InputOpen => textBox.InputOpen;

        public Func<char, bool> CharFilterFunc { get { return textBox.CharFilterFunc; } set { textBox.CharFilterFunc = value; } }

        public Vector2I SelectionStart => textBox.SelectionStart;

        public Vector2I SelectionEnd => textBox.SelectionEnd;

        public bool SelectionEmpty => textBox.SelectionEmpty;

        public Color HighlightColor { get; set; }

        public Color FocusColor { get; set; }

        public Color BorderColor { get { return border.Color; } set { border.Color = value; } }

        public float BorderThickness { get { return border.Thickness; } set { border.Thickness = value; } }

        public bool HighlightEnabled { get; set; }

        public bool UseFocusFormatting { get; set; }

        public IFocusHandler FocusHandler => textBox.FocusHandler;

        public IBindInput BindInput => textBox.BindInput;

		public IMouseInput MouseInput => textBox.MouseInput;

        public override bool IsMousedOver => textBox.IsMousedOver;

        public TextBuilderModes BuilderMode { get { return textBox.BuilderMode; } set { textBox.BuilderMode = value; } }

        public bool VertCenterText { get { return textBox.VertCenterText; } set { textBox.VertCenterText = value; } }

        protected readonly TextBox textBox;

        protected readonly BorderBox border;

        protected Color lastColor;
        
        protected Color lastTextColor;

/// <summary>TextField operation.</summary>
        public TextField(HudParentBase parent) : base(parent)
        {
/// <summary>BorderBox operation.</summary>
            border = new BorderBox(Background)
            {
                Thickness = 1f,
                DimAlignment = DimAlignments.Size,
            };

/// <summary>TextBox operation.</summary>
            textBox = new TextBox(Background)
            {
                AutoResize = false,
                DimAlignment = DimAlignments.UnpaddedSize,
/// <summary>Vector2 operation.</summary>
                Padding = new Vector2(24f, 0f),
                MoveToEndOnGainFocus = true,
                ClearSelectionOnLoseFocus = true,
                MouseInput = 
                {
                    CursorEnteredCallback = CursorEnter,
                    CursorExitedCallback = CursorExit
                },
                FocusHandler = 
                {
                    GainedInputFocusCallback = GainFocus,
                    LostInputFocusCallback = LoseFocus
                }
            };
            textBox.FocusHandler.InputOwner = this;

            Format = TerminalFormatting.ControlFormat;
            FocusTextColor = TerminalFormatting.Charcoal;
            Text = "NewTextField";

            Color = TerminalFormatting.OuterSpace;
            HighlightColor = TerminalFormatting.Atomic;
            FocusColor = TerminalFormatting.Mint;
            BorderColor = TerminalFormatting.LimedSpruce;

            UseFocusFormatting = true;
            HighlightEnabled = true;

/// <summary>Vector2 operation.</summary>
            Size = new Vector2(250f, 40);
        }

/// <summary>TextField operation.</summary>
        public TextField() : this(null)
        { }

/// <summary>OpenInput operation.</summary>
		public void OpenInput() =>
            textBox.OpenInput();

/// <summary>CloseInput operation.</summary>
		public void CloseInput() =>
            textBox.CloseInput();

/// <summary>CursorEnter operation.</summary>
        protected virtual void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (!UseFocusFormatting || !FocusHandler.HasFocus)
                {
                    lastColor = Color;
                }

                if (UseFocusFormatting)
                {
					if (!FocusHandler.HasFocus)
						lastTextColor = Format.Color;

					TextBoard.SetFormatting(TextBoard.Format.WithColor(lastTextColor));
				}

				Color = HighlightColor;
            }
        }

/// <summary>CursorExit operation.</summary>
        protected virtual void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (UseFocusFormatting && FocusHandler.HasFocus)
                {
                    Color = FocusColor;
                    TextBoard.SetFormatting(TextBoard.Format.WithColor(FocusTextColor));
                }
                else
                {
                    Color = lastColor;

                    if (UseFocusFormatting)
                        TextBoard.SetFormatting(TextBoard.Format.WithColor(lastTextColor));
                }
            }
        }

/// <summary>GainFocus operation.</summary>
        protected virtual void GainFocus(object sender, EventArgs args)
        {
            if (UseFocusFormatting)
            {
                if (!MouseInput.IsMousedOver)
                {
                    lastColor = Color;
                    lastTextColor = TextBoard.Format.Color;
                }

                Color = FocusColor;
                TextBoard.SetFormatting(TextBoard.Format.WithColor(FocusTextColor));
            }
        }

/// <summary>LoseFocus operation.</summary>
        protected virtual void LoseFocus(object sender, EventArgs args)
        {
            if (UseFocusFormatting)
            {
                Color = lastColor;
                TextBoard.SetFormatting(TextBoard.Format.WithColor(lastTextColor));
            }
        }
    }
}