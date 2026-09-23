namespace RichHudFramework
{
	namespace UI
	{
		public interface IFocusHandler
		{
			IFocusableElement InputOwner { get; set; }

			event EventHandler GainedInputFocus;

			event EventHandler LostInputFocus;

			EventHandler GainedInputFocusCallback { set; }

			EventHandler LostInputFocusCallback { set; }

			bool HasFocus { get; }


			void GetInputFocus();


			void ReleaseFocus();
		}

		public interface IFocusableElement
		{
			IFocusHandler FocusHandler { get; }
		}
	}
}