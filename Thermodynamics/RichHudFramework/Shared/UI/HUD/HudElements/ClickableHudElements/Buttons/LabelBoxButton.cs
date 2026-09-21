using VRageMath;
using System;

namespace RichHudFramework.UI
{
    public class LabelBoxButton : LabelBox, IClickableElement
    {
        public virtual Color HighlightColor { get; set; }

        public virtual bool HighlightEnabled { get; set; }

        public override bool IsMousedOver => _mouseInput.IsMousedOver;

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput { get; }

        protected MouseInputElement _mouseInput;

        protected Color oldColor;

/// <summary>LabelBoxButton operation.</summary>
        public LabelBoxButton(HudParentBase parent) : base(parent)
        {
/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this);
/// <summary>MouseInputElement operation.</summary>
            _mouseInput = new MouseInputElement(this)
            { 
                CursorEnteredCallback = CursorEnter,
                CursorExitedCallback = CursorExit
            };

            MouseInput = _mouseInput;
            Color = Color.DarkGray;
            HighlightColor = Color.Gray;
            HighlightEnabled = true;
        }

/// <summary>LabelBoxButton operation.</summary>
        public LabelBoxButton() : this(null)
        { }

/// <summary>CursorEnter operation.</summary>
        protected virtual void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                oldColor = Color;
                Color = HighlightColor;
            }
        }

/// <summary>CursorExit operation.</summary>
        protected virtual void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                Color = oldColor;
            }
        }
    }
}