namespace RichHudFramework.UI
{
	public class LabelButton : Label, IClickableElement
    {
		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput { get; }

        public override bool IsMousedOver => _mouseInput.IsMousedOver;

		protected MouseInputElement _mouseInput;


        public LabelButton(HudParentBase parent) : base(parent)
        {

			FocusHandler = new InputFocusHandler(this);

			_mouseInput = new MouseInputElement(this);
            MouseInput = _mouseInput;
        }


        public LabelButton() : this(null)
        { }
    }
}