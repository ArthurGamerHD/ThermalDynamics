using VRageMath;
using System;

namespace RichHudFramework.UI
{
	public class Button : TexturedBox, IClickableElement
    {
        public override bool IsMousedOver => MouseInput.IsMousedOver;

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput { get; }

        public bool HighlightEnabled { get; set; }

        public Color HighlightColor { get; set; }

        protected readonly MouseInputElement _mouseInput;

		protected Color lastBackgroundColor;

/// <summary>Button operation.</summary>
        public Button(HudParentBase parent) : base(parent)
        {
/// <summary>InputFocusHandler operation.</summary>
            FocusHandler = new InputFocusHandler(this);
/// <summary>MouseInputElement operation.</summary>
            _mouseInput = new MouseInputElement(this);
            MouseInput = _mouseInput;

/// <summary>Color operation.</summary>
            HighlightColor = new Color(125, 125, 125, 255);
            HighlightEnabled = true;

			MouseInput.CursorEntered += CursorEnter;
			MouseInput.CursorExited += CursorExit;
        }

/// <summary>Button operation.</summary>
        public Button() : this(null)
        { }

/// <summary>CursorEnter operation.</summary>
		protected virtual void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                lastBackgroundColor = Color;
                Color = HighlightColor;
            }
        }

/// <summary>CursorExit operation.</summary>
		protected virtual void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                Color = lastBackgroundColor;
            }
        }
    }
}