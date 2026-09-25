using System;
using VRageMath;

namespace RichHudFramework.UI
{
    public class BorderedButton : LabelBoxButton
    {
        public Color BorderColor { get { return border.Color; } set { border.Color = value; } }

        public float BorderThickness { get { return border.Thickness; } set { border.Thickness = value; } }

        public override Color HighlightColor { get; set; }

        public Color FocusTextColor { get; set; }

        public Color FocusColor { get; set; } 

        public bool UseFocusFormatting { get; set; }

		protected readonly BorderBox border;

        protected Color lastColor, lastTextColor;


        public BorderedButton(HudParentBase parent) : base(parent)
        {

            border = new BorderBox(this)
            {
                Thickness = 1f,
                DimAlignment = DimAlignments.UnpaddedSize,
            };

            AutoResize = false;
            Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Center);
            FocusTextColor = TerminalFormatting.Charcoal;
            Text = "NewBorderedButton";


            TextPadding = new Vector2(32f, 0f);

            Padding = new Vector2(37f, 0f);

            Size = new Vector2(253f, 50f);
            HighlightEnabled = true;

            Color = TerminalFormatting.OuterSpace;
            HighlightColor = TerminalFormatting.Atomic;
            BorderColor = TerminalFormatting.LimedSpruce;
            FocusColor = TerminalFormatting.Mint;
            UseFocusFormatting = true;

			FocusHandler.GainedInputFocus += GainFocus;
			FocusHandler.LostInputFocus += LoseFocus;
        }


        public BorderedButton() : this(null)
        { }


		protected override void HandleInput(Vector2 cursorPos)
        {
            if (FocusHandler.HasFocus)
            {
                if (SharedBinds.Space.IsNewPressed)
                {
                    _mouseInput.LeftClick();
                }
			}
		}


		protected override void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                if (!UseFocusFormatting || !FocusHandler.HasFocus)
                    lastColor = Color;

				if (UseFocusFormatting)
				{
					if (!FocusHandler.HasFocus)
						lastTextColor = TextBoard.Format.Color;

					TextBoard.SetFormatting(TextBoard.Format.WithColor(lastTextColor));
				}

                Color = HighlightColor;
            }
        }


		protected override void CursorExit(object sender, EventArgs args)
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