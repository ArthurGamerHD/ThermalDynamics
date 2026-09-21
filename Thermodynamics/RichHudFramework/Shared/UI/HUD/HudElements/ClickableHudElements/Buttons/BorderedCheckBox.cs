using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class BorderedCheckBox : Button, IValueControl<bool>
    {
		public event EventHandler ValueChanged;

		public EventHandler UpdateValueCallback { set { ValueChanged += value; } }

		public bool Value { get; set; }

        public Color BorderColor { get { return border.Color; } set { border.Color = value; } }

        public float BorderThickness { get { return border.Thickness; } set { border.Thickness = value; } }

        public Color TickBoxColor { get { return tickBox.Color; } set { tickBox.Color = value; } }

        public Color TickBoxHighlightColor { get; set; }

        public Color TickBoxFocusColor { get; set; }

        public Color FocusColor { get; set; }

        public bool UseFocusFormatting { get; set; }

		protected readonly BorderBox border;

        protected readonly TexturedBox tickBox;

        protected Color lastTickColor;

        protected bool lastValue;

/// <summary>BorderedCheckBox operation.</summary>
        public BorderedCheckBox(HudParentBase parent) : base(parent)
        {
/// <summary>BorderBox operation.</summary>
            border = new BorderBox(this)
            {
                Thickness = 1f,
                DimAlignment = DimAlignments.Size,
            };

/// <summary>TexturedBox operation.</summary>
            tickBox = new TexturedBox(this)
            {
                DimAlignment = DimAlignments.UnpaddedSize,
/// <summary>Vector2 operation.</summary>
                Padding = new Vector2(17f),
            };

            Value = true;
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(37f);

            Color = TerminalFormatting.OuterSpace;
            HighlightColor = TerminalFormatting.Atomic;
            FocusColor = TerminalFormatting.Mint;

            TickBoxColor = TerminalFormatting.StormGrey;
            TickBoxHighlightColor = Color.White;
            TickBoxFocusColor = TerminalFormatting.Cinder;

            BorderColor = TerminalFormatting.LimedSpruce;
            UseFocusFormatting = true;
            lastValue = Value;

            MouseInput.LeftClicked += ToggleValue;
            FocusHandler.GainedInputFocus += GainFocus;
			FocusHandler.LostInputFocus += LoseFocus;
        }

/// <summary>BorderedCheckBox operation.</summary>
        public BorderedCheckBox() : this(null)
        { }

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
        {
            tickBox.Visible = Value;

            if (FocusHandler.HasFocus)
            {
                if (SharedBinds.Space.IsNewPressed)
                {
                    _mouseInput.LeftClick();
                }
            }

            if (lastValue != Value)
            {
                ValueChanged?.Invoke(FocusHandler?.InputOwner, EventArgs.Empty);
                lastValue = Value;
            }
        }

/// <summary>ToggleValue operation.</summary>
		protected virtual void ToggleValue(object sender, EventArgs args)
        {
            Value = !Value;
        }

/// <summary>CursorEnter operation.</summary>
		protected override void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (!(UseFocusFormatting && FocusHandler.HasFocus))
                {
                    lastBackgroundColor = Color;
                    lastTickColor = TickBoxColor;
                }

                Color = HighlightColor;
                TickBoxColor = TickBoxHighlightColor;
            }
        }

/// <summary>CursorExit operation.</summary>
		protected override void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (UseFocusFormatting && FocusHandler.HasFocus)
                {
                    Color = FocusColor;
                    TickBoxColor = TickBoxFocusColor;
                }
                else
                {
                    Color = lastBackgroundColor;
                    TickBoxColor = lastTickColor;
                }
            }
        }

/// <summary>GainFocus operation.</summary>
		protected virtual void GainFocus(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (UseFocusFormatting && !MouseInput.IsMousedOver)
                {
                    Color = FocusColor;
                    TickBoxColor = TickBoxFocusColor;
                }
            }
        }

/// <summary>LoseFocus operation.</summary>
		protected virtual void LoseFocus(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (UseFocusFormatting)
                {
                    Color = lastBackgroundColor;
                    TickBoxColor = lastTickColor;
                }
            }
        }
    }
}