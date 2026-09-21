namespace RichHudFramework.UI
{
	public class LabelButton : Label, IClickableElement
    {
		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput { get; }

        public override bool IsMousedOver => _mouseInput.IsMousedOver;

		protected MouseInputElement _mouseInput;

/// <summary>LabelButton operation.</summary>
        public LabelButton(HudParentBase parent) : base(parent)
        {
/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this);
/// <summary>MouseInputElement operation.</summary>
			_mouseInput = new MouseInputElement(this);
            MouseInput = _mouseInput;
        }

/// <summary>LabelButton operation.</summary>
        public LabelButton() : this(null)
        { }
    }
}