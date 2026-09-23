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


        public Button(HudParentBase parent) : base(parent)
        {

            FocusHandler = new InputFocusHandler(this);

            _mouseInput = new MouseInputElement(this);
            MouseInput = _mouseInput;


            HighlightColor = new Color(125, 125, 125, 255);
            HighlightEnabled = true;

			MouseInput.CursorEntered += CursorEnter;
			MouseInput.CursorExited += CursorExit;
        }


        public Button() : this(null)
        { }


		protected virtual void CursorEnter(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                lastBackgroundColor = Color;
                Color = HighlightColor;
            }
        }


		protected virtual void CursorExit(object sender, EventArgs args)
        {
            if (HighlightEnabled)
            {
                Color = lastBackgroundColor;
            }
        }
    }
}