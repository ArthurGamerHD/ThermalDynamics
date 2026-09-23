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


        public LabelBoxButton(HudParentBase parent) : base(parent)
        {

			FocusHandler = new InputFocusHandler(this);

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


        public LabelBoxButton() : this(null)
        { }


        protected virtual void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                oldColor = Color;
                Color = HighlightColor;
            }
        }


        protected virtual void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                Color = oldColor;
            }
        }
    }
}